using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CepApi.Infrastructure.Services;

public sealed record AutomaticNotificationExecutionClaim(
    Guid ExecutionId,
    Guid OrganizationId,
    Guid ScheduleId,
    DateOnly LocalDate,
    DateTimeOffset ScheduledFor,
    int Attempt);

public sealed class AutomaticNotificationExecutionStore(AppDbContext db)
{
    private static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyCollection<AutomaticNotificationExecutionClaim>> ClaimDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var schedules = await db.AutomaticNotificationSchedules.AsNoTracking()
            .Where(x => x.IsEnabled && x.Organization.Status == OrganizationStatus.Active)
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.LocalTime)
            .ToListAsync(cancellationToken);
        var claims = new List<AutomaticNotificationExecutionClaim>();

        foreach (var schedule in schedules)
        {
            var due = ResolveDueOccurrence(schedule, now);
            if (due is null)
                continue;
            var claim = await TryClaimAsync(schedule, due.Value.LocalDate, due.Value.ScheduledFor, now,
                cancellationToken);
            if (claim is not null)
                claims.Add(claim);
        }

        return claims;
    }

    public async Task CompleteAsync(
        Guid executionId,
        int createdNotificationCount,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        var execution = await db.AutomaticNotificationExecutions.SingleOrDefaultAsync(
            x => x.Id == executionId, cancellationToken)
            ?? throw new InvalidOperationException("Notification execution was not found.");
        if (execution.Status != AutomaticNotificationExecutionStatus.Running)
            return;
        execution.Status = AutomaticNotificationExecutionStatus.Succeeded;
        execution.CreatedNotificationCount = createdNotificationCount;
        execution.CompletedAt = completedAt;
        execution.ErrorCode = null;
        execution.ErrorMessage = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid executionId,
        string errorCode,
        string? errorMessage,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        var execution = await db.AutomaticNotificationExecutions.SingleOrDefaultAsync(
            x => x.Id == executionId, cancellationToken)
            ?? throw new InvalidOperationException("Notification execution was not found.");
        if (execution.Status != AutomaticNotificationExecutionStatus.Running)
            return;
        execution.Status = AutomaticNotificationExecutionStatus.Failed;
        execution.CompletedAt = completedAt;
        execution.ErrorCode = errorCode[..Math.Min(errorCode.Length, 100)];
        execution.ErrorMessage = errorMessage?[..Math.Min(errorMessage.Length, 500)];
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AutomaticNotificationExecutionClaim?> TryClaimAsync(
        AutomaticNotificationSchedule schedule,
        DateOnly localDate,
        DateTimeOffset scheduledFor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.AutomaticNotificationExecutions
            .FromSqlInterpolated($"SELECT * FROM automatic_notification_executions WHERE \"ScheduleId\" = {schedule.Id} AND \"LocalDate\" = {localDate} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != AutomaticNotificationExecutionStatus.Running || existing.LeaseExpiresAt > now)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            existing.Attempts++;
            existing.StartedAt = now;
            existing.LeaseExpiresAt = now.Add(DefaultLease);
            existing.CompletedAt = null;
            existing.ErrorCode = null;
            existing.ErrorMessage = null;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToClaim(existing);
        }

        var execution = new AutomaticNotificationExecution
        {
            OrganizationId = schedule.OrganizationId,
            ScheduleId = schedule.Id,
            LocalDate = localDate,
            ScheduledFor = scheduledFor,
            Status = AutomaticNotificationExecutionStatus.Running,
            Attempts = 1,
            StartedAt = now,
            LeaseExpiresAt = now.Add(DefaultLease)
        };
        db.AutomaticNotificationExecutions.Add(execution);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToClaim(execution);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            db.Entry(execution).State = EntityState.Detached;
            return null;
        }
    }

    private static (DateOnly LocalDate, DateTimeOffset ScheduledFor)? ResolveDueOccurrence(
        AutomaticNotificationSchedule schedule,
        DateTimeOffset now)
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        if (TimeOnly.FromDateTime(localNow.DateTime) < schedule.LocalTime)
            return null;
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var localDateTime = DateTime.SpecifyKind(localDate.ToDateTime(schedule.LocalTime), DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localDateTime))
            return null;
        var scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone);
        return (localDate, new DateTimeOffset(scheduledUtc));
    }

    private static AutomaticNotificationExecutionClaim ToClaim(AutomaticNotificationExecution execution)
        => new(execution.Id, execution.OrganizationId, execution.ScheduleId, execution.LocalDate,
            execution.ScheduledFor, execution.Attempts);
}
