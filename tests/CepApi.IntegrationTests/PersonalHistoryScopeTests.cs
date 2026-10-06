using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class PersonalHistoryScopeTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string PeopleRoute = "/api/v1/organization/time-control/people";
    private const string HistoryRoute = "/api/v1/organization/time-control/history?from=2026-10-01&to=2026-10-01";

    [Fact]
    public async Task Associated_user_without_team_assignment_reads_only_own_person_and_history()
    {
        var owner = await fixture.CreateUserAsync();
        var colleague = await fixture.CreateUserAsync(organizationId: owner.OrganizationId);
        var outsider = await fixture.CreateUserAsync();
        var ownPerson = await Associate(owner, withRecords: true);
        var colleaguePerson = await Associate(colleague, withRecords: true);
        var outsiderPerson = await Associate(outsider, withRecords: true);

        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, owner.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var people = (await client.GetFromJsonAsync<PagedResponse<WorkforcePersonResponse>>(
            PeopleRoute, SecurityFixture.Json, Ct))!;
        Assert.Equal(1, people.Total);
        Assert.Equal(ownPerson.Id, Assert.Single(people.Items).Id);
        Assert.Equal(owner.Id, Assert.Single(people.Items).UserId);
        Assert.Equal(1, (await client.GetFromJsonAsync<PagedResponse<WorkforcePersonResponse>>(
            PeopleRoute + "?search=" + Uri.EscapeDataString(ownPerson.DisplayName), SecurityFixture.Json, Ct))!.Total);

        var history = (await client.GetFromJsonAsync<WorkforceAdminHistoryResponse>(
            HistoryRoute, SecurityFixture.Json, Ct))!;
        var ownHistory = Assert.Single(history.People);
        Assert.Equal(ownPerson.Id, ownHistory.WorkforcePersonId);
        Assert.Equal(2, ownHistory.Records.Count);
        Assert.Null(Assert.Single(ownHistory.Records, x => x.Source == ExternalWorkforceSource.Monday).DurationSeconds);
        Assert.Null(Assert.Single(ownHistory.Days).MondaySeconds);

        foreach (var hiddenId in new[] { colleaguePerson.Id, outsiderPerson.Id })
        {
            var filtered = (await client.GetFromJsonAsync<WorkforceAdminHistoryResponse>(
                HistoryRoute + "&workforcePersonId=" + hiddenId, SecurityFixture.Json, Ct))!;
            Assert.Empty(filtered.People);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            HistoryRoute + "&organizationId=" + outsider.OrganizationId, Ct)).StatusCode);
    }

    private async Task<WorkforcePerson> Associate(ApplicationUser user, bool withRecords)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = user.OrganizationId!.Value;
        var person = new WorkforcePerson
        {
            OrganizationId = organizationId,
            UserId = user.Id,
            DisplayName = "Person " + user.Id,
            Email = user.Email!,
            CreatedByUserId = user.Id,
            MondayIdentity = new() { OrganizationId = organizationId, Source = ExternalWorkforceSource.Monday,
                ExternalId = Guid.NewGuid().ToString("N"), DisplayName = "Monday", IsActive = true },
            VrMaisIdentity = new() { OrganizationId = organizationId, Source = ExternalWorkforceSource.VrMais,
                ExternalId = Guid.NewGuid().ToString("N"), DisplayName = "VR Mais", IsActive = true }
        };
        db.WorkforcePeople.Add(person);
        if (withRecords)
        {
            db.WorkforceTimeRecords.AddRange(
                new WorkforceTimeRecord { OrganizationId = organizationId, Source = ExternalWorkforceSource.Monday,
                    ExternalIdentity = person.MondayIdentity, ExternalKey = "m-" + user.Id,
                    WorkDate = new DateOnly(2026, 10, 1), State = "closed", DurationSeconds = null },
                new WorkforceTimeRecord { OrganizationId = organizationId, Source = ExternalWorkforceSource.VrMais,
                    ExternalIdentity = person.VrMaisIdentity, ExternalKey = "v-" + user.Id,
                    WorkDate = new DateOnly(2026, 10, 1), State = "reported", DurationSeconds = 3600 });
        }
        await db.SaveChangesAsync(Ct);
        return person;
    }
}
