using CepApi.Api.Authorization;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/notification-schedules")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
[OrganizationScope]
public sealed class AutomaticNotificationSchedulesController(
    AppDbContext db,
    IClock clock,
    IAuditService audit,
    OrganizationScopeService organizationScope) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<AutomaticNotificationScheduleResponse>>> List(
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var items = await db.AutomaticNotificationSchedules.AsNoTracking()
            .Where(x => x.OrganizationId == scopedOrganizationId)
            .OrderBy(x => x.LocalTime)
            .ThenBy(x => x.Id)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<AutomaticNotificationScheduleResponse>> Create(
        CreateAutomaticNotificationScheduleRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var timeZoneId = NormalizeTimeZone(request.TimeZoneId);
        if (!IsMinutePrecision(request.LocalTime))
            return InvalidTimePrecision();
        if (!IsValidTimeZone(timeZoneId))
            return InvalidTimeZone();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockOrganizationAsync(scopedOrganizationId, cancellationToken);
        if (await db.AutomaticNotificationSchedules.AnyAsync(x =>
                x.OrganizationId == scopedOrganizationId && x.LocalTime == request.LocalTime &&
                x.TimeZoneId == timeZoneId, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "A schedule already exists for this time and time zone.",
                "notification_schedule_already_exists");

        var now = clock.UtcNow;
        var schedule = new AutomaticNotificationSchedule
        {
            OrganizationId = scopedOrganizationId,
            LocalTime = request.LocalTime,
            TimeZoneId = timeZoneId,
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = CurrentUserId,
            UpdatedByUserId = CurrentUserId
        };
        db.AutomaticNotificationSchedules.Add(schedule);
        await audit.WriteAsync("time_control.notification_schedule_created", scopedOrganizationId, CurrentUserId,
            details: new { schedule.Id, schedule.LocalTime, schedule.TimeZoneId, schedule.IsEnabled },
            ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CreatedAtAction(nameof(List), new { organizationId = scopedOrganizationId }, ToResponse(schedule));
    }

    [HttpPatch("{scheduleId:guid}")]
    public async Task<ActionResult<AutomaticNotificationScheduleResponse>> Update(
        Guid scheduleId,
        UpdateAutomaticNotificationScheduleRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        if (request.LocalTime is null && request.TimeZoneId is null && request.IsEnabled is null)
            return ApiProblem(StatusCodes.Status400BadRequest, "At least one field must be provided.",
                "notification_schedule_update_empty");

        var timeZoneId = request.TimeZoneId is null ? null : NormalizeTimeZone(request.TimeZoneId);
        if (request.LocalTime is { } localTime && !IsMinutePrecision(localTime))
            return InvalidTimePrecision();
        if (timeZoneId is not null && !IsValidTimeZone(timeZoneId))
            return InvalidTimeZone();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockOrganizationAsync(scopedOrganizationId, cancellationToken);
        var schedule = await db.AutomaticNotificationSchedules.SingleOrDefaultAsync(
            x => x.Id == scheduleId && x.OrganizationId == scopedOrganizationId, cancellationToken);
        if (schedule is null)
            return ApiProblem(StatusCodes.Status404NotFound, "Notification schedule not found.",
                "notification_schedule_not_found");

        var updatedLocalTime = request.LocalTime ?? schedule.LocalTime;
        var updatedTimeZoneId = timeZoneId ?? schedule.TimeZoneId;
        if (await db.AutomaticNotificationSchedules.AnyAsync(x =>
                x.OrganizationId == scopedOrganizationId && x.Id != schedule.Id &&
                x.LocalTime == updatedLocalTime && x.TimeZoneId == updatedTimeZoneId, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "A schedule already exists for this time and time zone.",
                "notification_schedule_already_exists");

        var before = new { schedule.LocalTime, schedule.TimeZoneId, schedule.IsEnabled };
        schedule.LocalTime = updatedLocalTime;
        schedule.TimeZoneId = updatedTimeZoneId;
        schedule.IsEnabled = request.IsEnabled ?? schedule.IsEnabled;
        schedule.UpdatedAt = clock.UtcNow;
        schedule.UpdatedByUserId = CurrentUserId;
        await audit.WriteAsync("time_control.notification_schedule_updated", scopedOrganizationId, CurrentUserId,
            details: new
            {
                schedule.Id,
                before,
                after = new { schedule.LocalTime, schedule.TimeZoneId, schedule.IsEnabled }
            }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(schedule));
    }

    private Task LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM organizations WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);

    private ActionResult InvalidTimePrecision()
        => ApiProblem(StatusCodes.Status400BadRequest, "Schedule time must use minute precision.",
            "notification_schedule_time_precision_invalid");

    private ActionResult InvalidTimeZone()
        => ApiProblem(StatusCodes.Status400BadRequest, "Time zone is invalid.",
            "notification_schedule_time_zone_invalid");

    private static string NormalizeTimeZone(string? timeZoneId)
        => string.IsNullOrWhiteSpace(timeZoneId) ? AutomaticNotificationDefaults.TimeZoneId : timeZoneId.Trim();

    private static bool IsMinutePrecision(TimeOnly value)
        => value.Second == 0 && value.Millisecond == 0 && value.Microsecond == 0 && value.Nanosecond == 0;

    private static bool IsValidTimeZone(string timeZoneId)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static AutomaticNotificationScheduleResponse ToResponse(AutomaticNotificationSchedule schedule)
        => new(schedule.Id, schedule.LocalTime, schedule.TimeZoneId, schedule.IsEnabled,
            schedule.CreatedAt, schedule.UpdatedAt);
}
