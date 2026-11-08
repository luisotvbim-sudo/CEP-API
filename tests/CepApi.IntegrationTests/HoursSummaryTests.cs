using System.Net;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class HoursSummaryTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    [Fact]
    public async Task Current_team_uses_only_assignment_days_and_organization_does_not_duplicate_people()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var org = admin.OrganizationId!.Value;
        var member = await fixture.CreateUserAsync(organizationId: org);
        var unassigned = await fixture.CreateUserAsync(organizationId: org);
        var outsider = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var today = TimeAnalysisEngine.LocalDate(DateTimeOffset.UtcNow);
        var first = today.AddDays(-2);
        var second = today.AddDays(-1);
        Guid personId, teamId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            WorkforcePerson Person(Guid userId, string name, Guid organizationId)
            {
                var person = new WorkforcePerson
                {
                    OrganizationId = organizationId, UserId = userId, DisplayName = name,
                    Email = name + "@example.test", CreatedByUserId = userId,
                    MondayIdentity = new() { OrganizationId = organizationId, Source = ExternalWorkforceSource.Monday,
                        DisplayName = name, ExternalId = Guid.NewGuid().ToString("N") },
                    VrMaisIdentity = new() { OrganizationId = organizationId, Source = ExternalWorkforceSource.VrMais,
                        DisplayName = name, ExternalId = Guid.NewGuid().ToString("N") }
                };
                db.Add(person);
                return person;
            }
            void AddDay(WorkforcePerson person, DateOnly day, int mondayHours, int? vrHours)
            {
                var start = TimeAnalysisEngine.StartOfDay(day).AddHours(8);
                db.Add(new WorkforceTimeRecord { OrganizationId = person.OrganizationId,
                    Source = ExternalWorkforceSource.Monday, ExternalIdentity = person.MondayIdentity,
                    ExternalKey = Guid.NewGuid().ToString("N"), WorkDate = day, State = "closed",
                    StartedAt = start, EndedAt = start.AddHours(mondayHours), DurationSeconds = mondayHours * 3600 });
                if (vrHours is { } vr)
                    db.Add(new WorkforceTimeRecord { OrganizationId = person.OrganizationId,
                        Source = ExternalWorkforceSource.VrMais, ExternalIdentity = person.VrMaisIdentity,
                        ExternalKey = Guid.NewGuid().ToString("N"), WorkDate = day, State = "reported",
                        DurationSeconds = vr * 3600 });
            }
            var person = Person(member.Id, "member", org);
            personId = person.Id;
            AddDay(person, first, 8, 7);
            AddDay(person, second, 6, 7);
            AddDay(Person(unassigned.Id, "unassigned", org), first, 2, null);
            AddDay(Person(outsider.Id, "outside", outsider.OrganizationId!.Value), first, 8, 8);

            var team = new WorkforceTeam { OrganizationId = org, Name = "New", NormalizedName = "NEW" };
            var secondTeam = new WorkforceTeam { OrganizationId = org, Name = "Also", NormalizedName = "ALSO" };
            var previous = new WorkforceTeam { OrganizationId = org, Name = "Previous", NormalizedName = "PREVIOUS" };
            db.AddRange(team, secondTeam, previous);
            teamId = team.Id;
            foreach (var t in new[] { team, secondTeam, previous })
                db.Add(new TeamAssignment
                {
                    Team = t, UserId = member.Id, Role = TeamAssignmentRole.Member,
                    EffectiveFrom = t == previous ? first : second,
                    EffectiveTo = t == previous ? first : null, CreatedByUserId = admin.Id
                });
            // Two roles in the same team must not add the same day twice.
            db.Add(new TeamAssignment { Team = team, UserId = member.Id, Role = TeamAssignmentRole.Manager,
                EffectiveFrom = second, CreatedByUserId = admin.Id });
            await db.SaveChangesAsync(ct);
        }

        using var client = fixture.Client();
        var route = $"/api/v1/organization/time-control/hours-summary?from={first:yyyy-MM-dd}&to={second:yyyy-MM-dd}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route, ct)).StatusCode);
        var tokens = await fixture.LoginAsync(client, member.Email!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route, ct)).StatusCode);
        tokens = await fixture.LoginAsync(client, admin.Email!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        var result = (await client.GetFromJsonAsync<HoursSummaryResponse>(route, SecurityFixture.Json, ct))!;
        Assert.Equal(2, result.People.Count);
        var summary = Assert.Single(result.People, x => x.Id == personId);
        Assert.Equal(14 * 3600, summary.Totals.MondaySeconds);
        Assert.Equal(0, summary.Totals.DeltaSeconds);
        Assert.Equal(2 * 3600, summary.Totals.AbsoluteDivergenceSeconds);
        Assert.Equal(16 * 3600, result.Organization.MondaySeconds);
        Assert.Equal(14 * 3600, result.Organization.VrSeconds);
        Assert.Equal(0, result.Organization.DeltaSeconds);
        Assert.Equal(2, result.Organization.ComparableDays);
        Assert.Equal(4, result.Organization.PersonDays);
        Assert.All(result.Teams.Where(t => t.Name != "Previous"), t => Assert.Equal(6 * 3600, t.Totals.MondaySeconds));
        Assert.Equal(second, Assert.Single(Assert.Single(result.Teams.Single(x => x.Id == teamId).Members).Days));
        Assert.Empty(result.Teams.Single(x => x.Name == "Previous").Members);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route + "&organizationId=" + outsider.OrganizationId, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(
            $"/api/v1/organization/time-control/hours-summary?from={today.AddDays(-90):yyyy-MM-dd}&to={today:yyyy-MM-dd}", ct)).StatusCode);
    }
}
