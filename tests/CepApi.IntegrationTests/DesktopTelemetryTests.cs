using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Api.Controllers;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class DesktopTelemetryTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private const string Route = "/api/v1/desktop-telemetry/events";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Concurrent_replay_is_idempotent_and_authentication_assigns_scope()
    {
        var owner = await fixture.CreateUserAsync();
        var outsider = await fixture.CreateUserAsync();
        var item = Event();
        using var ownerClient = await Client(owner);
        var posts = Enumerable.Range(0, 2).Select(_ => ownerClient.PostAsJsonAsync(Route,
            new DesktopTelemetryBatchRequest { Events = [item] }, Ct)).ToArray();
        var replies = await Task.WhenAll(posts);
        Assert.All(replies, reply => Assert.Equal(HttpStatusCode.OK, reply.StatusCode));
        foreach (var reply in replies)
            Assert.Equal([item.EventId], (await reply.Content.ReadFromJsonAsync<DesktopTelemetryBatchResponse>(SecurityFixture.Json, Ct))!.AcceptedEventIds);

        using var outsiderClient = await Client(outsider);
        Assert.Equal(HttpStatusCode.OK, (await outsiderClient.PostAsJsonAsync(Route,
            new DesktopTelemetryBatchRequest { Events = [item] }, Ct)).StatusCode);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DesktopTelemetryEvents
            .Where(x => x.EventId == item.EventId).ToArrayAsync(Ct);
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row.OrganizationId == owner.OrganizationId && row.UserId == owner.Id);
        Assert.Contains(rows, row => row.OrganizationId == outsider.OrganizationId && row.UserId == outsider.Id);
        Assert.All(rows, row => Assert.Equal(item.InstallationId, row.InstallationId));
    }

    [Fact]
    public async Task Invalid_or_stale_items_are_rejected_without_poisoning_newer_events()
    {
        var owner = await fixture.CreateUserAsync();
        var good = Event();
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Route,
            new DesktopTelemetryBatchRequest { Events = [good] }, Ct)).StatusCode);
        var systemAdmin = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        using var systemClient = await Client(systemAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await systemClient.PostAsJsonAsync(Route,
            new DesktopTelemetryBatchRequest { Events = [good] }, Ct)).StatusCode);
        using var client = await Client(owner);
        var unknownId = Guid.NewGuid();
        var malformed = new
        {
            events = new object[] { good, new { eventId = unknownId, installationId = Guid.NewGuid(),
                occurredAt = DateTimeOffset.UtcNow, code = "desktop_started", phase = "startup",
                outcome = "success", errorCode = "none", appVersion = "0.4.20", machineName = "should-reject" } }
        };
        var mixed = await client.PostAsJsonAsync(Route, malformed, Ct);
        Assert.Equal(HttpStatusCode.OK, mixed.StatusCode);
        var firstAck = (await mixed.Content.ReadFromJsonAsync<DesktopTelemetryBatchResponse>(SecurityFixture.Json, Ct))!;
        Assert.Equal([good.EventId], firstAck.AcceptedEventIds);
        Assert.Equal([unknownId], firstAck.RejectedEventIds);
        var fresh = Event();
        var stale = Event() with { OccurredAt = DateTimeOffset.UtcNow.AddDays(-31) };
        var future = Event() with { OccurredAt = DateTimeOffset.UtcNow.AddHours(1) };
        var invalidCode = Event() with { Code = "arbitrary_error" };
        var second = await client.PostAsJsonAsync(Route,
            new DesktopTelemetryBatchRequest { Events = [stale, future, invalidCode, fresh] }, Ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondAck = (await second.Content.ReadFromJsonAsync<DesktopTelemetryBatchResponse>(SecurityFixture.Json, Ct))!;
        Assert.Equal([fresh.EventId], secondAck.AcceptedEventIds);
        Assert.Equal([stale.EventId, future.EventId, invalidCode.EventId], secondAck.RejectedEventIds);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var ids = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DesktopTelemetryEvents
            .Where(x => x.EventId == good.EventId || x.EventId == fresh.EventId || x.EventId == unknownId ||
                x.EventId == stale.EventId || x.EventId == future.EventId || x.EventId == invalidCode.EventId)
            .Select(x => x.EventId).ToArrayAsync(Ct);
        Assert.Equal(2, ids.Length);
        Assert.Contains(good.EventId, ids);
        Assert.Contains(fresh.EventId, ids);
    }

    [Fact]
    public async Task Administrator_read_is_paginated_and_organization_isolated()
    {
        var owner = await fixture.CreateUserAsync();
        var localAdmin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin, owner.OrganizationId);
        var foreignAdmin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var item = Event();
        var second = Event() with { InstallationId = item.InstallationId };
        using (var ownerClient = await Client(owner))
            Assert.Equal(HttpStatusCode.OK, (await ownerClient.PostAsJsonAsync(Route,
                new DesktopTelemetryBatchRequest { Events = [item, second] }, Ct)).StatusCode);
        using var localClient = await Client(localAdmin);
        var path = "/api/v1/organization/desktop-telemetry/events?pageSize=1";
        var local = (await localClient.GetFromJsonAsync<DesktopTelemetryEventResponse[]>(path, SecurityFixture.Json, Ct))!;
        Assert.Single(local);
        Assert.Contains(local, row => row.EventId == item.EventId || row.EventId == second.EventId);
        var nextPath = path + $"&before={Uri.EscapeDataString(local[0].ReceivedAt.ToString("O"))}&beforeEventId={local[0].EventId}&beforeUserId={local[0].UserId}";
        var next = (await localClient.GetFromJsonAsync<DesktopTelemetryEventResponse[]>(nextPath, SecurityFixture.Json, Ct))!;
        Assert.Single(next);
        Assert.NotEqual(local[0].EventId, next[0].EventId);
        Assert.Equal(HttpStatusCode.BadRequest, (await localClient.GetAsync("/api/v1/organization/desktop-telemetry/events?pageSize=101", Ct)).StatusCode);
        using var foreignClient = await Client(foreignAdmin);
        var foreign = await foreignClient.GetFromJsonAsync<DesktopTelemetryEventResponse[]>(path, SecurityFixture.Json, Ct);
        Assert.DoesNotContain(foreign!, row => row.EventId == item.EventId);
        using var memberClient = await Client(owner);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync(path, Ct)).StatusCode);
    }

    [Fact]
    public async Task Recovery_required_accepts_a_classified_service_error_without_free_text()
    {
        var owner = await fixture.CreateUserAsync();
        var item = Event() with
        {
            Code = "power_recovery_required", Phase = "reconcile", Outcome = "uncertain",
            Action = "shutdown", OperationId = Guid.NewGuid(), ErrorCode = "service_unavailable"
        };
        using var client = await Client(owner);
        var reply = await client.PostAsJsonAsync(Route,
            new DesktopTelemetryBatchRequest { Events = [item] }, Ct);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var ack = (await reply.Content.ReadFromJsonAsync<DesktopTelemetryBatchResponse>(SecurityFixture.Json, Ct))!;
        Assert.Equal([item.EventId], ack.AcceptedEventIds);
        Assert.Empty(ack.RejectedEventIds);
    }

    private async Task<HttpClient> Client(ApplicationUser user)
    {
        var client = fixture.Client();
        var token = await fixture.LoginAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private static DesktopTelemetryItem Event() => new()
    {
        EventId = Guid.NewGuid(), InstallationId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
        Code = "desktop_started", Phase = "startup", Outcome = "success", ErrorCode = "none",
        AppVersion = "0.4.20"
    };
}
