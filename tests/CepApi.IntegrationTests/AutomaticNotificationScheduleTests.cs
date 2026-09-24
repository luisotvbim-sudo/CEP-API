using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class AutomaticNotificationScheduleTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    [Fact]
    public async Task Organization_starts_with_default_schedule_and_admin_can_add_and_disable_times()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var systemAdmin = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, systemAdmin.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var slug = Guid.NewGuid().ToString("N");
        var createOrganization = await client.PostAsJsonAsync("/api/v1/admin/organizations",
            new CreateOrganizationRequest("Notification Test", slug, $"admin-{slug}@example.test", [Product.Revit]),
            cancellationToken);
        createOrganization.EnsureSuccessStatusCode();
        var organization = (await createOrganization.Content.ReadFromJsonAsync<OrganizationResponse>(
            SecurityFixture.Json, cancellationToken))!;

        var list = await client.GetAsync(
            $"/api/v1/organization/time-control/notification-schedules?organizationId={organization.Id}",
            cancellationToken);
        list.EnsureSuccessStatusCode();
        var defaultSchedule = Assert.Single((await list.Content.ReadFromJsonAsync<AutomaticNotificationScheduleResponse[]>(
            SecurityFixture.Json, cancellationToken))!);
        Assert.Equal(new TimeOnly(11, 50), defaultSchedule.LocalTime);
        Assert.Equal(AutomaticNotificationDefaults.TimeZoneId, defaultSchedule.TimeZoneId);
        Assert.True(defaultSchedule.IsEnabled);

        var createSecond = await client.PostAsJsonAsync(
            $"/api/v1/organization/time-control/notification-schedules?organizationId={organization.Id}",
            new CreateAutomaticNotificationScheduleRequest(new TimeOnly(14, 30)), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createSecond.StatusCode);
        var second = (await createSecond.Content.ReadFromJsonAsync<AutomaticNotificationScheduleResponse>(
            SecurityFixture.Json, cancellationToken))!;
        Assert.Equal(AutomaticNotificationDefaults.TimeZoneId, second.TimeZoneId);

        var disable = await client.PatchAsJsonAsync(
            $"/api/v1/organization/time-control/notification-schedules/{second.Id}?organizationId={organization.Id}",
            new UpdateAutomaticNotificationScheduleRequest(null, null, false), cancellationToken);
        disable.EnsureSuccessStatusCode();
        var disabled = (await disable.Content.ReadFromJsonAsync<AutomaticNotificationScheduleResponse>(
            SecurityFixture.Json, cancellationToken))!;
        Assert.False(disabled.IsEnabled);

        var duplicate = await client.PostAsJsonAsync(
            $"/api/v1/organization/time-control/notification-schedules?organizationId={organization.Id}",
            new CreateAutomaticNotificationScheduleRequest(new TimeOnly(11, 50)), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var auditActions = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents
            .Where(x => x.OrganizationId == organization.Id)
            .Select(x => x.Action)
            .ToListAsync(cancellationToken);
        Assert.Contains("time_control.notification_schedule_created", auditActions);
        Assert.Contains("time_control.notification_schedule_updated", auditActions);
    }

    [Fact]
    public async Task Due_execution_is_idempotent_and_stale_lease_can_be_reclaimed_after_restart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 9, 24, 15, 0, 0, TimeSpan.Zero);
        Guid organizationId;
        Guid scheduleId;

        await using (var seedScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organization = new Organization
            {
                Name = "Execution Test",
                Slug = Guid.NewGuid().ToString("N"),
                CreatedAt = now,
                UpdatedAt = now
            };
            var schedule = new AutomaticNotificationSchedule
            {
                OrganizationId = organization.Id,
                Organization = organization,
                LocalTime = new TimeOnly(11, 50),
                TimeZoneId = AutomaticNotificationDefaults.TimeZoneId,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.AddRange(organization, schedule);
            await db.SaveChangesAsync(cancellationToken);
            organizationId = organization.Id;
            scheduleId = schedule.Id;
        }

        AutomaticNotificationExecutionClaim first;
        await using (var firstScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = firstScope.ServiceProvider.GetRequiredService<AutomaticNotificationExecutionStore>();
            first = Assert.Single(await store.ClaimDueAsync(now, cancellationToken));
            Assert.Equal(organizationId, first.OrganizationId);
            Assert.Equal(scheduleId, first.ScheduleId);
            Assert.Equal(1, first.Attempt);
        }

        await using (var restartScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = restartScope.ServiceProvider.GetRequiredService<AutomaticNotificationExecutionStore>();
            Assert.Empty(await store.ClaimDueAsync(now.AddMinutes(1), cancellationToken));
        }

        await using (var expireScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = expireScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var execution = await db.AutomaticNotificationExecutions.SingleAsync(
                x => x.Id == first.ExecutionId, cancellationToken);
            execution.LeaseExpiresAt = now.AddMinutes(-1);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using (var retryScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = retryScope.ServiceProvider.GetRequiredService<AutomaticNotificationExecutionStore>();
            var retry = Assert.Single(await store.ClaimDueAsync(now.AddMinutes(2), cancellationToken));
            Assert.Equal(first.ExecutionId, retry.ExecutionId);
            Assert.Equal(2, retry.Attempt);
            await store.CompleteAsync(retry.ExecutionId, 3, now.AddMinutes(3), cancellationToken);
        }

        await using (var completedScope = fixture.Factory.Services.CreateAsyncScope())
        {
            var store = completedScope.ServiceProvider.GetRequiredService<AutomaticNotificationExecutionStore>();
            Assert.Empty(await store.ClaimDueAsync(now.AddHours(1), cancellationToken));
            var execution = await completedScope.ServiceProvider.GetRequiredService<AppDbContext>()
                .AutomaticNotificationExecutions.SingleAsync(x => x.Id == first.ExecutionId, cancellationToken);
            Assert.Equal(AutomaticNotificationExecutionStatus.Succeeded, execution.Status);
            Assert.Equal(3, execution.CreatedNotificationCount);
        }
    }
}
