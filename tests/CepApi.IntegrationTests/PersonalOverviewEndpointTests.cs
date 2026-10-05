using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CepApi.IntegrationTests;

public sealed class PersonalOverviewEndpointTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private const string Route = "/api/v1/me/time-control/overview";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-10-05T15:00:00Z");

    [Theory]
    [InlineData(-1800, false, PersonalOverviewStatus.Regular)]
    [InlineData(1801, false, PersonalOverviewStatus.Difference)]
    [InlineData(0, true, PersonalOverviewStatus.Incomplete)]
    public async Task Only_session_owner_is_read_once_per_source_without_persistence(int delta, bool failedVr, PersonalOverviewStatus expected)
    {
        var owner = await fixture.CreateUserAsync();
        var outsider = await fixture.CreateUserAsync();
        var person = await Associate(owner);
        await Associate(outsider);
        var monday = new LiveSource(ExternalWorkforceSource.Monday, delta, false);
        var vr = new LiveSource(ExternalWorkforceSource.VrMais, delta, failedVr);
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>(); services.AddSingleton<IClock>(new Clock());
            services.RemoveAll<IExternalWorkforceTimeSource>();
            services.AddSingleton<IExternalWorkforceTimeSource>(monday);
            services.AddSingleton<IExternalWorkforceTimeSource>(vr);
        }));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var tokens = await fixture.LoginAsync(client, owner.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await client.GetAsync(Route + $"?userId={outsider.Id}&organizationId={outsider.OrganizationId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = (await response.Content.ReadFromJsonAsync<PersonalOverviewResponse>(SecurityFixture.Json, Ct))!;
        Assert.Equal(expected, value.Status);
        Assert.Equal(Cutoff, value.Cutoff);
        Assert.Equal(1, monday.Calls); Assert.Equal(1, vr.Calls);
        Assert.Equal(new[] { person.MondayIdentity.ExternalId }, monday.Ids);
        Assert.Equal(new[] { person.VrMaisIdentity.ExternalId }, vr.Ids);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(4, value.Periods.Count);
        if (failedVr)
        {
            Assert.Null(value.Analysis!.MondaySeconds);
            Assert.Null(value.Analysis.VrSeconds);
            Assert.Null(value.Analysis.DeltaSeconds);
            Assert.Equal(3600, Assert.Single(value.AvailableSourceDays).MondaySeconds);
            Assert.Null(Assert.Single(value.AvailableSourceDays).VrSeconds);
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<TimeNotification>().AnyAsync(item => item.UserId == owner.Id, Ct));
        Assert.False(await db.Set<TimeAnalysisReport>().AnyAsync(item => item.UserId == owner.Id, Ct));
        Assert.False(await db.Set<PowerActionOverride>().AnyAsync(item => item.UserId == owner.Id, Ct));
        Assert.False(await db.WorkforceTimeRecords.AnyAsync(item => item.OrganizationId == owner.OrganizationId, Ct));
    }

    [Fact]
    public async Task Missing_association_inactive_identity_and_invalid_period_are_explicit()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        var value = (await client.GetFromJsonAsync<PersonalOverviewResponse>(Route, SecurityFixture.Json, Ct))!;
        Assert.Equal(PersonalOverviewStatus.NotAssociated, value.Status); Assert.Null(value.Analysis);
        await Associate(user, active: false);
        value = (await client.GetFromJsonAsync<PersonalOverviewResponse>(Route, SecurityFixture.Json, Ct))!;
        Assert.Equal(PersonalOverviewStatus.InactiveIdentity, value.Status); Assert.Null(value.Analysis);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Route + "?period=999", Ct)).StatusCode);
        var system = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        tokens = await fixture.LoginAsync(client, system.Email!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route + "?organizationId=" + user.OrganizationId, Ct)).StatusCode);
    }

    private async Task<WorkforcePerson> Associate(ApplicationUser user, bool active = true)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var org = user.OrganizationId!.Value;
        var person = new WorkforcePerson
        {
            OrganizationId = org, UserId = user.Id, DisplayName = "Fixture", Email = user.Email!, CreatedByUserId = user.Id,
            MondayIdentity = new() { OrganizationId = org, Source = ExternalWorkforceSource.Monday, ExternalId = Guid.NewGuid().ToString(), DisplayName = "Fixture", IsActive = active },
            VrMaisIdentity = new() { OrganizationId = org, Source = ExternalWorkforceSource.VrMais, ExternalId = Guid.NewGuid().ToString(), DisplayName = "Fixture" }
        };
        db.Add(person); await db.SaveChangesAsync(Ct); return person;
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Cutoff; }
    private sealed class LiveSource(ExternalWorkforceSource source, int delta, bool failed) : IExternalWorkforceTimeSource
    {
        public ExternalWorkforceSource Source => source;
        public int Calls { get; private set; }
        public string[] Ids { get; private set; } = [];
        public Task<ExternalWorkforceTimeSnapshot> FetchAsync(DateOnly from, DateOnly to, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
        {
            Calls++; Ids = ids.ToArray();
            if (failed) throw new ExternalDirectoryException("source_unavailable", "fixture");
            var rows = new List<ExternalWorkforceTimeRecordSnapshot>();
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var start = TimeAnalysisEngine.StartOfDay(day).AddHours(8);
                rows.Add(source == ExternalWorkforceSource.Monday
                    ? new(ids.Single(), "m-" + day, day, start, start.AddSeconds(3600 + delta), 3600 + delta, "closed", null, null, null)
                    : new(ids.Single(), "v-" + day, day, null, null, 3600, "reported", null, null, "{\"timeCards\":[\"08:00\",\"09:00\"]}"));
                rows.Add(new("foreign", "foreign-" + day, day, start, start.AddHours(20), 99999, "closed", null, null, null));
            }
            return Task.FromResult(new ExternalWorkforceTimeSnapshot(from, to, rows, true));
        }
    }
}
