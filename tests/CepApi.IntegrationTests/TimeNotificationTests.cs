using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class TimeNotificationTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string DispatchRoute = "/api/v1/organization/time-control/notification-dispatches";

    [Fact]
    public async Task Global_configuration_allows_org_admins_but_not_members_and_rejects_stale_writes()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var otherAdmin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var member = await fixture.CreateUserAsync();
        using var client = await Client(admin);
        using var other = await Client(otherAdmin);
        using var user = await Client(member);
        const string path = "/api/v1/time-control/settings";
        var settings = (await client.GetFromJsonAsync<TimeSettingsResponse>(path, SecurityFixture.Json, Ct))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(path, Ct)).StatusCode);
        var changed = await client.PatchAsJsonAsync(path, new UpdateTimeSettingsRequest(settings.Version, 47, false), Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var global = (await other.GetFromJsonAsync<TimeSettingsResponse>(path, SecurityFixture.Json, Ct))!;
        Assert.Equal(47, global.ToleranceMinutes);
        Assert.NotEqual(settings.Version, global.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PatchAsJsonAsync(path, new UpdateTimeSettingsRequest(settings.Version, 12, false), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PatchAsJsonAsync(path, new UpdateTimeSettingsRequest(global.Version, 12, false), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path, new UpdateTimeSettingsRequest(global.Version, -1, false), Ct)).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(x => x.ActorUserId == admin.Id && x.Action == "time_control.global_settings_changed", Ct));
    }

    [Fact]
    public async Task Manual_send_is_durable_idempotent_rate_limited_and_scoped()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var outsider = await fixture.CreateUserAsync();
        await Associate(admin);
        await Associate(outsider);
        using var client = await Client(admin);
        var request = new SendTimeNotificationRequest(Guid.NewGuid(), null, "Confira suas horas", AnalysisPeriod.Sprint);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(DispatchRoute, request with { UserId = outsider.Id }, SecurityFixture.Json, Ct)).StatusCode);
        var first = await client.PostAsJsonAsync(DispatchRoute, request, SecurityFixture.Json, Ct);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var saved = (await first.Content.ReadFromJsonAsync<TimeDispatchResponse>(SecurityFixture.Json, Ct))!;
        var retry = await client.PostAsJsonAsync(DispatchRoute, request, SecurityFixture.Json, Ct);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(saved.Id, (await retry.Content.ReadFromJsonAsync<TimeDispatchResponse>(SecurityFixture.Json, Ct))!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(DispatchRoute, request with { Message = "Changed" }, SecurityFixture.Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(DispatchRoute, request with { RequestId = Guid.NewGuid() }, SecurityFixture.Json, Ct)).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Set<TimeNotificationDispatch>().CountAsync(x => x.OrganizationId == admin.OrganizationId && x.ActorUserId != null, Ct));
    }

    [Fact]
    public async Task Inbox_receipt_is_not_read_and_foreign_identifiers_cannot_be_modified()
    {
        var owner = await fixture.CreateUserAsync();
        var other = await fixture.CreateUserAsync();
        var ownId = await SeedNotification(owner);
        var foreignId = await SeedNotification(other);
        using var client = await Client(owner);
        var pending = (await client.GetFromJsonAsync<PagedResponse<TimeNotificationResponse>>("/api/v1/me/notifications?pendingOnly=true", SecurityFixture.Json, Ct))!;
        Assert.Contains(pending.Items, x => x.Id == ownId);
        Assert.DoesNotContain(pending.Items, x => x.Id == foreignId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/me/notifications/received", new ReceiveTimeNotificationsRequest([ownId, foreignId]), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/me/notifications/{foreignId}/read", new { }, Ct)).StatusCode);
        var unread = (await client.GetFromJsonAsync<PagedResponse<TimeNotificationResponse>>("/api/v1/me/notifications?unreadOnly=true", SecurityFixture.Json, Ct))!;
        Assert.Contains(unread.Items, x => x.Id == ownId && x.DeliveredAt != null && x.ReadAt == null);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var foreign = await db.Set<TimeNotification>().SingleAsync(x => x.Id == foreignId, Ct);
        Assert.Null(foreign.DeliveredAt);
        Assert.Null(foreign.ReadAt);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/me/notifications/{ownId}/read", new { }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Processor_persists_individual_results_and_retries_do_not_duplicate_delivery()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var person = await Associate(admin);
        var now = DateTimeOffset.Parse("2026-09-29T14:50:00Z");
        var dispatch = new TimeNotificationDispatch
        {
            OrganizationId = admin.OrganizationId!.Value, ActorUserId = admin.Id, UserId = admin.Id,
            DeduplicationKey = Guid.NewGuid().ToString(), RequestHash = "test", Message = "Lunch",
            Period = AnalysisPeriod.Daily, CreatedAt = now, ToleranceMinutes = 30
        };
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(dispatch);
        await db.SaveChangesAsync(Ct);
        var processor = new TimeNotificationProcessor(db, new FixedClock(now), [new FakeSource(ExternalWorkforceSource.Monday), new FakeSource(ExternalWorkforceSource.VrMais)]);
        await processor.TickAsync(Ct);
        await processor.TickAsync(Ct);
        Assert.Equal(NotificationDispatchStatus.Completed, dispatch.Status);
        Assert.Equal(1, dispatch.RecipientCount);
        var report = Assert.Single(await db.Set<TimeAnalysisReport>().Where(x => x.DispatchId == dispatch.Id).ToListAsync(Ct));
        Assert.Equal(person.Id, report.WorkforcePersonId);
        Assert.Equal(1, await db.Set<TimeNotification>().CountAsync(x => x.ReportId == report.Id, Ct));
        var analysis = System.Text.Json.JsonSerializer.Deserialize<TimeAnalysisResponse>(report.AnalysisJson, SecurityFixture.Json)!;
        Assert.Equal(0L, analysis.DeltaSeconds);
        Assert.Equal(13800L, analysis.VrSeconds);
        Assert.False(analysis.HasIssues);
    }

    private async Task<HttpClient> Client(ApplicationUser user)
    {
        var client = fixture.Client();
        var login = await fixture.LoginAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    [Fact]
    public async Task Source_failure_sends_inconclusive_result_and_never_claims_hours_are_correct()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await Associate(admin);
        var now = DateTimeOffset.Parse("2026-09-29T14:50:00Z");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatch = new TimeNotificationDispatch { OrganizationId = admin.OrganizationId!.Value, ActorUserId = admin.Id,
            UserId = admin.Id, DeduplicationKey = Guid.NewGuid().ToString(), RequestHash = "test", Message = "Conferência",
            Period = AnalysisPeriod.Daily, CreatedAt = now, ToleranceMinutes = 30 };
        db.Add(dispatch);
        await db.SaveChangesAsync(Ct);
        await new TimeNotificationProcessor(db, new FixedClock(now),
            [new FakeSource(ExternalWorkforceSource.Monday, fail: true), new FakeSource(ExternalWorkforceSource.VrMais)]).TickAsync(Ct);
        Assert.Equal("analysis_sources_incomplete", dispatch.ErrorCode);
        var report = await db.Set<TimeAnalysisReport>().SingleAsync(x => x.DispatchId == dispatch.Id, Ct);
        var result = System.Text.Json.JsonSerializer.Deserialize<TimeAnalysisResponse>(report.AnalysisJson, SecurityFixture.Json)!;
        Assert.Null(result.DeltaSeconds);
        Assert.Contains(result.Sources, x => x.Source == ExternalWorkforceSource.Monday && x.Status == "incomplete");
        var notification = await db.Set<TimeNotification>().SingleAsync(x => x.ReportId == report.Id, Ct);
        Assert.Contains("Não foi possível", notification.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("dentro da tolerância", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Weekend_creates_midnight_report_without_automatic_popups_and_deduplicates_it()
    {
        var user = await fixture.CreateUserAsync();
        await Associate(user);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Set<TimeControlSettings>().SingleAsync(Ct);
        var previous = settings.AutomaticEnabled;
        settings.AutomaticEnabled = true;
        await db.SaveChangesAsync(Ct);
        try
        {
            var processor = new TimeNotificationProcessor(db, new FixedClock(DateTimeOffset.Parse("2026-09-26T14:50:00Z")),
                [new FakeSource(ExternalWorkforceSource.Monday), new FakeSource(ExternalWorkforceSource.VrMais)]);
            await processor.TickAsync(Ct);
            await processor.TickAsync(Ct);
            var dispatches = await db.Set<TimeNotificationDispatch>().Where(x => x.OrganizationId == user.OrganizationId).ToArrayAsync(Ct);
            Assert.True(Assert.Single(dispatches).ReportOnly);
            Assert.Equal(1, await db.Set<TimeAnalysisReport>().CountAsync(x => x.OrganizationId == user.OrganizationId, Ct));
            Assert.Equal(0, await db.Set<TimeNotification>().CountAsync(x => x.OrganizationId == user.OrganizationId, Ct));
        }
        finally { settings.AutomaticEnabled = previous; await db.SaveChangesAsync(Ct); }
    }

    private async Task<WorkforcePerson> Associate(ApplicationUser user)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var monday = new ExternalWorkforceIdentity { OrganizationId = user.OrganizationId!.Value, Source = ExternalWorkforceSource.Monday, ExternalId = Guid.NewGuid().ToString(), DisplayName = "Monday" };
        var vr = new ExternalWorkforceIdentity { OrganizationId = user.OrganizationId.Value, Source = ExternalWorkforceSource.VrMais, ExternalId = Guid.NewGuid().ToString(), DisplayName = "VR" };
        var person = new WorkforcePerson { OrganizationId = user.OrganizationId.Value, UserId = user.Id, DisplayName = user.DisplayName,
            Email = user.Email!, MondayIdentity = monday, VrMaisIdentity = vr, CreatedByUserId = user.Id };
        db.Add(person);
        await db.SaveChangesAsync(Ct);
        return person;
    }

    [Fact]
    public async Task Concurrent_identical_manual_requests_create_one_dispatch()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await Associate(admin);
        using var client = await Client(admin);
        var request = new SendTimeNotificationRequest(Guid.NewGuid(), admin.Id, "Uma entrega", AnalysisPeriod.Weekly);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            client.PostAsJsonAsync(DispatchRoute, request, SecurityFixture.Json, Ct)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Accepted);
        Assert.Equal(5, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        var ids = new HashSet<Guid>();
        foreach (var response in responses)
        {
            ids.Add((await response.Content.ReadFromJsonAsync<TimeDispatchResponse>(SecurityFixture.Json, Ct))!.Id);
            response.Dispose();
        }
        Assert.Single(ids);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Morning_schedule_considers_only_yesterday_and_only_notifies_confirmed_errors(bool yesterdayError, int expected)
    {
        var user = await fixture.CreateUserAsync();
        var person = await Associate(user);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Set<TimeControlSettings>().SingleAsync(Ct);
        var oldEnabled = settings.AutomaticEnabled;
        settings.AutomaticEnabled = true;
        await db.SaveChangesAsync(Ct);
        try
        {
            var now = DateTimeOffset.Parse("2026-09-29T13:00:00Z");
            var processor = new TimeNotificationProcessor(db, new FixedClock(now),
                [new MorningSource(ExternalWorkforceSource.Monday, yesterdayError), new MorningSource(ExternalWorkforceSource.VrMais, yesterdayError)]);
            await processor.TickAsync(Ct);
            await processor.TickAsync(Ct);
            var dispatch = await db.Set<TimeNotificationDispatch>().SingleAsync(x => x.OrganizationId == user.OrganizationId && x.Kind == NotificationScheduleKind.PreviousDay, Ct);
            Assert.Equal(expected, dispatch.RecipientCount);
            var report = await db.Set<TimeAnalysisReport>().SingleAsync(x => x.DispatchId == dispatch.Id && x.WorkforcePersonId == person.Id, Ct);
            var result = System.Text.Json.JsonSerializer.Deserialize<TimeAnalysisResponse>(report.AnalysisJson, SecurityFixture.Json)!;
            Assert.Equal(new DateOnly(2026, 9, 28), result.From);
            Assert.Equal(result.From, result.To);
            Assert.Single(result.Days);
            Assert.Equal(expected, await db.Set<TimeNotification>().CountAsync(x => x.ReportId == report.Id, Ct));
        }
        finally { settings.AutomaticEnabled = oldEnabled; await db.SaveChangesAsync(Ct); }
    }

    private async Task<Guid> SeedNotification(ApplicationUser user)
    {
        var person = await Associate(user);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatch = new TimeNotificationDispatch { OrganizationId = user.OrganizationId!.Value, DeduplicationKey = Guid.NewGuid().ToString(), RequestHash = "test", Message = "test", Status = NotificationDispatchStatus.Completed, CreatedAt = DateTimeOffset.UtcNow };
        var report = new TimeAnalysisReport { OrganizationId = user.OrganizationId.Value, DispatchId = dispatch.Id, WorkforcePersonId = person.Id, UserId = user.Id, DisplayName = user.DisplayName, AnalysisJson = "{}", CreatedAt = DateTimeOffset.UtcNow };
        var notification = new TimeNotification { OrganizationId = user.OrganizationId.Value, UserId = user.Id, Report = report, Message = "test", CreatedAt = DateTimeOffset.UtcNow };
        db.Add(dispatch); db.Add(notification);
        await db.SaveChangesAsync(Ct);
        return notification.Id;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    private sealed class MorningSource(ExternalWorkforceSource source, bool error) : IExternalWorkforceTimeSource
    {
        public ExternalWorkforceSource Source => source;
        public Task<ExternalWorkforceTimeSnapshot> FetchAsync(DateOnly from, DateOnly to, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
        {
            var records = new List<ExternalWorkforceTimeRecordSnapshot>();
            foreach (var id in ids)
            {
                if (source == ExternalWorkforceSource.Monday)
                    records.Add(new(id, $"{id}:yesterday", from, TimeAnalysisEngine.StartOfDay(from).AddHours(8), TimeAnalysisEngine.StartOfDay(from).AddHours(9), 3600, "closed", null, null, null));
                else
                {
                    records.Add(new(id, $"{id}:yesterday", from, null, null, 3600, "reported", null, null,
                        error ? "{\"timeCards\":[\"08:00\"]}" : "{\"timeCards\":[\"08:00\",\"09:00\"]}"));
                    // Old errors must not leak into yesterday's reminder even if an adapter returns them.
                    records.Add(new(id, $"{id}:old", from.AddDays(-1), null, null, null, "unrecognized", null, null, "{\"timeCards\":[\"08:00\"]}"));
                }
            }
            return Task.FromResult(new ExternalWorkforceTimeSnapshot(from, to, records, true));
        }
    }
    private sealed class FakeSource(ExternalWorkforceSource source, bool fail = false) : IExternalWorkforceTimeSource
    {
        public ExternalWorkforceSource Source => source;
        public Task<ExternalWorkforceTimeSnapshot> FetchAsync(DateOnly from, DateOnly to, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
        {
            if (fail) throw new ExternalDirectoryException("source_unavailable", "Test failure");
            var records = new List<ExternalWorkforceTimeRecordSnapshot>();
            foreach (var id in ids)
                for (var day = from; day <= to; day = day.AddDays(1))
                    records.Add(source == ExternalWorkforceSource.Monday
                        ? new(id, $"{id}:{day}", day, TimeAnalysisEngine.StartOfDay(day).AddHours(8), null, 0, "running", null, null, null)
                        : new(id, $"{id}:{day}", day, null, null, 0, "reported", null, null, "{\"timeCards\":[\"08:00\"]}"));
            return Task.FromResult(new ExternalWorkforceTimeSnapshot(from, to, records, true));
        }
    }
}
