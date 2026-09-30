using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CepApi.Api.Controllers;

[Route("api/v1/time-control")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
[EnableRateLimiting("account")]
public sealed class TimeSettingsController(AppDbContext db, IClock clock, IAuditService audit) : ApiControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<TimeSettingsResponse>> Settings(CancellationToken cancellationToken)
        => Ok(ToResponse(await db.Set<TimeControlSettings>().AsNoTracking().SingleAsync(cancellationToken)));

    [HttpPatch("settings")]
    public async Task<ActionResult<TimeSettingsResponse>> Update(UpdateTimeSettingsRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var settings = await db.Set<TimeControlSettings>().SingleAsync(cancellationToken);
        if (settings.Version != request.Version) return ConflictResult();
        var before = ToResponse(settings);
        settings.ToleranceMinutes = request.ToleranceMinutes;
        settings.AutomaticEnabled = request.AutomaticEnabled;
        settings.Version = Guid.NewGuid();
        settings.UpdatedAt = clock.UtcNow;
        settings.UpdatedByUserId = CurrentUserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync("time_control.global_settings_changed", actorUserId: CurrentUserId,
                details: new { before, after = ToResponse(settings) }, ipAddress: IpAddress, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return ConflictResult(); }
        return Ok(ToResponse(settings));
    }

    [HttpGet("notification-schedules")]
    public async Task<ActionResult<IReadOnlyCollection<TimeScheduleResponse>>> Schedules(CancellationToken cancellationToken)
        => Ok((await db.Set<TimeNotificationSchedule>().AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.LocalTime).ToListAsync(cancellationToken)).Select(ToResponse).ToArray());

    [HttpPost("notification-schedules")]
    public Task<ActionResult<TimeScheduleResponse>> Create(TimeScheduleRequest request, CancellationToken cancellationToken)
        => Save(null, request, cancellationToken);

    [HttpPatch("notification-schedules/{id:guid}")]
    public Task<ActionResult<TimeScheduleResponse>> Edit(Guid id, TimeScheduleRequest request, CancellationToken cancellationToken)
        => Save(id, request, cancellationToken);

    private async Task<ActionResult<TimeScheduleResponse>> Save(Guid? id, TimeScheduleRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Kind) || request.LocalTime.Ticks % TimeSpan.TicksPerMinute != 0 || string.IsNullOrWhiteSpace(request.Message))
            return ApiProblem(400, "Invalid schedule. Use minute precision and a message.", "invalid_schedule");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = id.HasValue ? await db.Set<TimeNotificationSchedule>().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken) : new TimeNotificationSchedule { Message = request.Message.Trim() };
        if (schedule is null) return ApiProblem(404, "Schedule not found.", "schedule_not_found");
        if (id.HasValue && schedule.Version != request.Version) return ConflictResult();
        var before = id.HasValue ? ToResponse(schedule) : null;
        if (!id.HasValue) db.Add(schedule);
        schedule.LocalTime = request.LocalTime;
        schedule.Message = request.Message.Trim();
        schedule.Kind = request.Kind;
        schedule.IsEnabled = request.IsEnabled;
        schedule.Version = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync("time_control.global_schedule_saved", actorUserId: CurrentUserId,
                details: new { before, after = ToResponse(schedule) }, ipAddress: IpAddress, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return ConflictResult(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return ApiProblem(409, "A schedule already exists at that time.", "schedule_time_conflict"); }
        return Ok(ToResponse(schedule));
    }

    [HttpDelete("notification-schedules/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid version, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await db.Set<TimeNotificationSchedule>().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (schedule is null) return ApiProblem(404, "Schedule not found.", "schedule_not_found");
        if (version != schedule.Version) return ConflictResult();
        schedule.IsDeleted = true;
        schedule.IsEnabled = false;
        schedule.Version = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync("time_control.global_schedule_deleted", actorUserId: CurrentUserId,
                details: new { scheduleId = id }, ipAddress: IpAddress, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return ConflictResult(); }
        return NoContent();
    }

    private ObjectResult ConflictResult() => ApiProblem(409, "Configuration changed. Reload before saving.", "configuration_conflict");
    private static TimeSettingsResponse ToResponse(TimeControlSettings s) => new(s.ToleranceMinutes, "America/Sao_Paulo", s.AutomaticEnabled, s.Version, s.UpdatedAt, s.UpdatedByUserId);
    private static TimeScheduleResponse ToResponse(TimeNotificationSchedule s) => new(s.Id, s.LocalTime, s.Message, s.Kind, s.IsEnabled, s.Version);
}
