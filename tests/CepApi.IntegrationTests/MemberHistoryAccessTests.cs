using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class MemberHistoryAccessTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    [Fact]
    public async Task Associated_member_has_personal_overview_but_history_requires_current_team_assignment()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await fixture.CreateUserAsync();
        var cutoff = DateTimeOffset.Parse("2026-10-05T15:00:00Z");
        var day = new DateOnly(2026, 10, 4);
        var person = new WorkforcePerson
        {
            OrganizationId = user.OrganizationId!.Value, UserId = user.Id,
            DisplayName = "History fixture", Email = user.Email!, CreatedByUserId = user.Id,
            MondayIdentity = new() { OrganizationId = user.OrganizationId.Value,
                Source = ExternalWorkforceSource.Monday, ExternalId = Guid.NewGuid().ToString(), DisplayName = "Monday fixture" },
            VrMaisIdentity = new() { OrganizationId = user.OrganizationId.Value,
                Source = ExternalWorkforceSource.VrMais, ExternalId = Guid.NewGuid().ToString(), DisplayName = "VR fixture" }
        };
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(person);
            db.Add(new WorkforceTimeRecord
            {
                OrganizationId = person.OrganizationId, ExternalIdentity = person.MondayIdentity,
                Source = ExternalWorkforceSource.Monday, ExternalKey = Guid.NewGuid().ToString(),
                WorkDate = day, StartedAt = TimeAnalysisEngine.StartOfDay(day).AddHours(8),
                EndedAt = TimeAnalysisEngine.StartOfDay(day).AddHours(9), DurationSeconds = 3600,
                State = "closed", LastSyncedAt = cutoff
            });
            await db.SaveChangesAsync(ct);
            var clock = new FixedClock(cutoff);
            var overview = await new PersonalOverviewService(db, clock, new FreshTimeAnalysisService(clock, []))
                .ReadAsync(person.OrganizationId, user.Id, AnalysisPeriod.PreviousDay, ct);
            Assert.Equal(PersonalOverviewStatus.Incomplete, overview.Status);
            Assert.NotNull(overview.Analysis);
        }
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var people = (await client.GetFromJsonAsync<PagedResponse<WorkforcePersonResponse>>(
            "/api/v1/organization/time-control/people", SecurityFixture.Json, ct))!;
        Assert.Empty(people.Items);
        var historyRoute = $"/api/v1/organization/time-control/history?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&workforcePersonId={person.Id}";
        var before = (await client.GetFromJsonAsync<WorkforceAdminHistoryResponse>(historyRoute, SecurityFixture.Json, ct))!;
        Assert.Empty(before.People);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.WorkforceTimeRecords.AnyAsync(r => r.ExternalIdentityId == person.MondayIdentityId, ct));
            var team = new WorkforceTeam { OrganizationId = person.OrganizationId, Name = "Fixture team", NormalizedName = "FIXTURE TEAM" };
            db.Add(team);
            db.Add(new TeamAssignment { Team = team, UserId = user.Id, Role = TeamAssignmentRole.Member,
                EffectiveFrom = day, CreatedByUserId = user.Id });
            await db.SaveChangesAsync(ct);
        }
        people = (await client.GetFromJsonAsync<PagedResponse<WorkforcePersonResponse>>(
            "/api/v1/organization/time-control/people", SecurityFixture.Json, ct))!;
        Assert.Equal(person.Id, Assert.Single(people.Items).Id);
        var after = (await client.GetFromJsonAsync<WorkforceAdminHistoryResponse>(historyRoute, SecurityFixture.Json, ct))!;
        Assert.Equal(person.Id, Assert.Single(after.People).WorkforcePersonId);
        Assert.Single(after.People.Single().Records);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
}
