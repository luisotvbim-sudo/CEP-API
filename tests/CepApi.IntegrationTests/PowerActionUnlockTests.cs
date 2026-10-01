using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CepApi.IntegrationTests;

public sealed class PowerActionUnlockTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private const string UnlockRoute = "/api/v1/me/time-control/power-action-unlock";
    private const string CheckRoute = "/api/v1/me/time-control/power-action-check";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string RandomPin() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");

    [Fact]
    public async Task Correct_pin_grants_all_three_actions_for_exactly_five_minutes_only_to_requester()
    {
        var pin = await ConfigurePin();
        var owner = await fixture.CreateUserAsync();
        var colleague = await fixture.CreateUserAsync(organizationId: owner.OrganizationId);
        var outsider = await fixture.CreateUserAsync();
        var clock = new MutableClock();
        await using var factory = Factory(clock);
        using var client = await Client(factory, owner);
        using var other = await Client(factory, colleague);
        using var foreign = await Client(factory, outsider);
        var reply = await client.PostAsJsonAsync(UnlockRoute + $"?userId={outsider.Id}", new { pin, userId = outsider.Id }, Ct);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var result = (await reply.Content.ReadFromJsonAsync<PowerActionUnlockResponse>(SecurityFixture.Json, Ct))!;
        Assert.True(result.Override);
        Assert.Equal(TimeSpan.FromMinutes(5), result.UnlockedUntil - result.ServerTime);
        Assert.DoesNotContain(pin, await reply.Content.ReadAsStringAsync(Ct));
        foreach (var action in new[] { "shutdown", "restart", "hibernate" })
        {
            var decision = await Check(client, action);
            Assert.Equal("allowed", decision.Decision);
            Assert.Equal("administrative_override", decision.Code);
            Assert.True(decision.Override);
            Assert.Equal(result.UnlockedUntil, decision.UnlockedUntil);
            Assert.Null(decision.Analysis);
        }
        Assert.False((await Check(other, "shutdown")).Override);
        Assert.False((await Check(foreign, "shutdown")).Override);
        clock.UtcNow = result.UnlockedUntil.AddTicks(-10);
        var status = (await client.GetFromJsonAsync<PowerActionCheckResponse>("/api/v1/me/time-control/power-action-status?action=hibernate", SecurityFixture.Json, Ct))!;
        Assert.True(status.Override);
        clock.UtcNow = result.UnlockedUntil;
        var expired = await Check(client, "shutdown");
        Assert.False(expired.Override);
        Assert.Null(expired.UnlockedUntil);
        Assert.Equal("indeterminate", expired.Decision); // normal rule, no person association

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grant = await db.Set<PowerActionOverride>().SingleAsync(Ct);
        Assert.Equal(owner.Id, grant.UserId);
        Assert.Equal(owner.OrganizationId, grant.OrganizationId);
        var audit = await db.AuditEvents.Where(x => x.Action == "power.override_granted").SingleAsync(Ct);
        Assert.Equal(owner.Id, audit.ActorUserId);
        Assert.DoesNotContain(pin, audit.DetailsJson!);
    }

    [Fact]
    public async Task Wrong_pin_is_denied_and_cannot_extend_an_existing_window_or_use_login_password()
    {
        var pin = await ConfigurePin();
        var user = await fixture.CreateUserAsync();
        var clock = new MutableClock();
        await using var factory = Factory(clock);
        using var client = await Client(factory, user);
        await AdministrationRegressionTests.AssertProblemAsync(await client.PostAsJsonAsync(UnlockRoute, new { pin = DifferentPin(pin) }, Ct),
            HttpStatusCode.Forbidden, "invalid_admin_pin");
        var granted = await client.PostAsJsonAsync(UnlockRoute, new { pin }, Ct);
        var until = (await granted.Content.ReadFromJsonAsync<PowerActionUnlockResponse>(SecurityFixture.Json, Ct))!.UnlockedUntil;
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(UnlockRoute, new { pin = DifferentPin(pin) }, Ct)).StatusCode);
        Assert.Equal(until, (await Check(client, "restart")).UnlockedUntil);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(UnlockRoute, new { pin = SecurityFixture.Password }, Ct)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var failed = await db.AuditEvents.Where(x => x.Action == "power.override_denied" && x.ActorUserId == user.Id).ToListAsync(Ct);
        Assert.Equal(2, failed.Count);
        Assert.All(failed, e => Assert.Null(e.DetailsJson));
    }

    [Fact]
    public async Task Rotation_and_password_security_stamp_change_invalidate_existing_grants()
    {
        var pin = await ConfigurePin();
        var user = await fixture.CreateUserAsync();
        await using var factory = Factory(new MutableClock());
        using var client = await Client(factory, user);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(UnlockRoute, new { pin }, Ct)).StatusCode);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var configuration = await db.Set<PowerPinConfiguration>().SingleAsync(Ct);
            configuration.Version = Guid.NewGuid();
            await db.SaveChangesAsync(Ct);
        }
        Assert.False((await Check(client, "shutdown")).Override);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(UnlockRoute, new { pin }, Ct)).StatusCode);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SecurityStamp, Guid.NewGuid().ToString()), Ct);
        }
        // A fresh session cannot inherit a grant approved against the old security stamp.
        using var renewed = await Client(factory, user);
        Assert.False((await Check(renewed, "shutdown")).Override);
    }

    [Fact]
    public async Task Persisted_rate_limit_survives_another_host_instance_and_expires_after_fifteen_minutes()
    {
        var pin = await ConfigurePin();
        var user = await fixture.CreateUserAsync();
        var clock = new MutableClock();
        await using var first = Factory(clock);
        using var client = await Client(first, user);
        var wrong = DifferentPin(pin);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(UnlockRoute, new { pin = wrong }, Ct)).StatusCode);
        await using var second = Factory(clock);
        using var fresh = await Client(second, user);
        await AdministrationRegressionTests.AssertProblemAsync(await fresh.PostAsJsonAsync(UnlockRoute, new { pin }, Ct),
            HttpStatusCode.TooManyRequests, "power_unlock_rate_limited");
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsJsonAsync(UnlockRoute, new { pin }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Http_rate_limit_and_missing_configuration_are_explicit()
    {
        await ConfigurePin();
        var user = await fixture.CreateUserAsync();
        await using var factory = Factory(new MutableClock(), 1);
        using var client = await Client(factory, user);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(UnlockRoute, new { }, Ct)).StatusCode);
        await AdministrationRegressionTests.AssertProblemAsync(await client.PostAsJsonAsync(UnlockRoute, new { }, Ct),
            HttpStatusCode.TooManyRequests, "rate_limit_exceeded");
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<PowerPinConfiguration>().ExecuteDeleteAsync(Ct);
        await using var otherFactory = Factory(new MutableClock());
        using var otherClient = await Client(otherFactory, user);
        await AdministrationRegressionTests.AssertProblemAsync(await otherClient.PostAsJsonAsync(UnlockRoute, new { pin = RandomPin() }, Ct),
            HttpStatusCode.ServiceUnavailable, "power_pin_not_configured");
        Assert.False((await Check(otherClient, "shutdown")).Override);
    }

    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 50)]
    public async Task Organization_and_global_limits_prevent_distributed_guessing(bool global, int count)
    {
        var pin = await ConfigurePin();
        var user = await fixture.CreateUserAsync();
        var clock = new MutableClock();
        await using var factory = Factory(clock);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < count; i++)
                db.Add(new AuditEvent { Action = "power.override_denied", ActorUserId = Guid.NewGuid(),
                    OrganizationId = global ? Guid.NewGuid() : user.OrganizationId, CreatedAt = clock.UtcNow });
            await db.SaveChangesAsync(Ct);
        }
        using var client = await Client(factory, user);
        await AdministrationRegressionTests.AssertProblemAsync(await client.PostAsJsonAsync(UnlockRoute, new { pin }, Ct),
            HttpStatusCode.TooManyRequests, "power_unlock_rate_limited");
    }

    private async Task<string> ConfigurePin()
    {
        var pin = RandomPin();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AuditEvents.Where(x => x.Action.StartsWith("power.")).ExecuteDeleteAsync(Ct);
        await db.Set<PowerActionOverride>().ExecuteDeleteAsync(Ct);
        var config = await db.Set<PowerPinConfiguration>().SingleOrDefaultAsync(Ct);
        if (config is null) { config = new() { PinHash = "" }; db.Add(config); }
        config.PinHash = new PowerPinHasher().Hash(config, pin);
        config.Version = Guid.NewGuid();
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(Ct);
        Assert.NotEqual(pin, config.PinHash);
        return pin;
    }

    private WebApplicationFactory<Program> Factory(MutableClock clock, int permits = 100)
        => fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:PowerUnlockPerMinute", permits.ToString());
            builder.ConfigureServices(services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        });

    private async Task<HttpClient> Client(WebApplicationFactory<Program> factory, ApplicationUser user)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var tokens = await fixture.LoginAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task<PowerActionCheckResponse> Check(HttpClient client, string action)
    {
        var reply = await client.PostAsJsonAsync(CheckRoute, new { action }, Ct);
        reply.EnsureSuccessStatusCode();
        return (await reply.Content.ReadFromJsonAsync<PowerActionCheckResponse>(SecurityFixture.Json, Ct))!;
    }
    private static string DifferentPin(string pin) => (pin[0] == '9' ? "0" : "9") + pin[1..];
    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
}
