using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Api.Controllers;
using CepApi.Api.Services;
using CepApi.Infrastructure.Services;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CepApi.IntegrationTests;

public sealed class PowerActionEndpointTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private const string Route = "/api/v1/me/time-control/power-action-check";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("shutdown", 0, false, "allowed")]
    [InlineData("restart", 2000, false, "blocked")]
    [InlineData("hibernate", 0, true, "indeterminate")]
    public async Task Decision_uses_live_own_identities_and_status_has_the_same_rules(string action, int delta, bool incomplete, string expected)
    {
        var owner = await fixture.CreateUserAsync();
        var outsider = await fixture.CreateUserAsync();
        var ownPerson = await Associate(owner);
        await Associate(outsider);
        var monday = new LiveSource(ExternalWorkforceSource.Monday, delta, incomplete);
        var vr = new LiveSource(ExternalWorkforceSource.VrMais, delta, incomplete);
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IExternalWorkforceTimeSource>();
            services.AddSingleton<IExternalWorkforceTimeSource>(monday);
            services.AddSingleton<IExternalWorkforceTimeSource>(vr);
            // Fix only the business cutoff; authentication retains its real clock.
            services.AddControllers().AddControllersAsServices();
            services.AddTransient(provider => new PowerActionController(provider.GetRequiredService<AppDbContext>(),
                new BusinessClock(), new FreshTimeAnalysisService(new BusinessClock(), provider.GetServices<IExternalWorkforceTimeSource>()),
                provider.GetRequiredService<PowerActionUnlockService>()));
        }));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var tokens = await fixture.LoginAsync(client, owner.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var post = await client.PostAsJsonAsync(Route + $"?userId={outsider.Id}&organizationId={outsider.OrganizationId}", new { action, userId = outsider.Id }, Ct);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var decision = (await post.Content.ReadFromJsonAsync<PowerActionCheckResponse>(SecurityFixture.Json, Ct))!;
        Assert.Equal(expected, decision.Decision);
        Assert.Equal(action, decision.Action);
        Assert.Equal(new[] { ownPerson.MondayIdentity.ExternalId }, monday.Ids);
        Assert.Equal(new[] { ownPerson.VrMaisIdentity.ExternalId }, vr.Ids);
        Assert.Contains("no-store", post.Headers.CacheControl!.ToString());
        var status = (await client.GetFromJsonAsync<PowerActionCheckResponse>("/api/v1/me/time-control/power-action-status?action=" + action, SecurityFixture.Json, Ct))!;
        Assert.Equal(expected, status.Decision);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Set<TimeControlSettings>().SingleAsync(Ct);
        Assert.Equal(settings.Version, decision.Analysis!.SettingsVersion);
        Assert.Equal(settings.ToleranceMinutes, decision.Analysis.ToleranceMinutes);
        Assert.False(await db.Set<TimeNotification>().AnyAsync(x => x.UserId == owner.Id, Ct));
    }

    [Fact]
    public async Task Missing_association_is_explicit_and_invalid_actions_are_validation_errors()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        var response = await client.PostAsJsonAsync(Route, new { action = "shutdown" }, Ct);
        var decision = (await response.Content.ReadFromJsonAsync<PowerActionCheckResponse>(SecurityFixture.Json, Ct))!;
        Assert.Equal("indeterminate", decision.Decision);
        Assert.Equal("workforce_person_not_associated", decision.Code);
        Assert.Null(decision.Analysis);
        await AdministrationRegressionTests.AssertProblemAsync(await client.PostAsJsonAsync(Route, new { action = "sleep" }, Ct), HttpStatusCode.BadRequest, "invalid_power_action");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route, new { }, Ct)).StatusCode);
    }

    private async Task<WorkforcePerson> Associate(ApplicationUser user)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = user.OrganizationId!.Value;
        var person = new WorkforcePerson
        {
            OrganizationId = organizationId, UserId = user.Id, DisplayName = user.DisplayName, Email = user.Email!, CreatedByUserId = user.Id,
            MondayIdentity = new() { OrganizationId = organizationId, Source = ExternalWorkforceSource.Monday, ExternalId = Guid.NewGuid().ToString(), DisplayName = "Monday" },
            VrMaisIdentity = new() { OrganizationId = organizationId, Source = ExternalWorkforceSource.VrMais, ExternalId = Guid.NewGuid().ToString(), DisplayName = "VR" }
        };
        db.Add(person);
        await db.SaveChangesAsync(Ct);
        return person;
    }

    private sealed class BusinessClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-10-05T15:00:00Z");
    }

    private sealed class LiveSource(ExternalWorkforceSource source, int delta, bool incomplete) : IExternalWorkforceTimeSource
    {
        public ExternalWorkforceSource Source => source;
        public string[] Ids { get; private set; } = [];
        public Task<ExternalWorkforceTimeSnapshot> FetchAsync(DateOnly from, DateOnly to, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
        {
            Ids = ids.ToArray();
            var start = TimeAnalysisEngine.StartOfDay(from);
            var rows = new List<ExternalWorkforceTimeRecordSnapshot>();
            if (source == ExternalWorkforceSource.Monday)
                rows.Add(new(ids.Single(), "session", from, start, null, 0, "running", null, null, null));
            else
                rows.Add(new(ids.Single(), "vr", from, null, null, 0, "reported", null, null,
                    "{\"timeCards\":[\"" + TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(delta)).ToString("HH:mm:ss") + "\"]}"));
            return Task.FromResult(new ExternalWorkforceTimeSnapshot(from, to, rows, !incomplete));
        }
    }
}
