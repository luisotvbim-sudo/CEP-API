using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Services;

/// <summary>Enqueues due slots while the processor holds its PostgreSQL session lock.</summary>
public sealed class TimeNotificationScheduler(AppDbContext db, IClock clock)
{
    public async Task EnqueueDueAsync(CancellationToken cancellationToken)
    {
        var settings = await db.Set<TimeControlSettings>().AsNoTracking().SingleAsync(cancellationToken);
        var now = clock.UtcNow;
        var today = TimeAnalysisEngine.LocalDate(now);
        var midnight = TimeAnalysisEngine.StartOfDay(today);
        var schedules = settings.AutomaticEnabled && today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            ? await db.Set<TimeNotificationSchedule>().AsNoTracking().Where(x => !x.IsDeleted && x.IsEnabled).ToListAsync(cancellationToken) : [];
        var organizations = await db.Organizations.AsNoTracking().Where(x => x.Status == OrganizationStatus.Active &&
            db.WorkforcePeople.Any(p => p.OrganizationId == x.Id && p.UserId != null)).Select(x => x.Id).ToArrayAsync(cancellationToken);
        // Compute due slots once. Enabling/changing settings never replays earlier slots.
        // The report has no popup and runs every civil day, including weekends.
        var dueSlots = new List<(string Key, TimeNotificationSchedule? Schedule, DateTimeOffset At)>
        {
            ($"report:{today:yyyy-MM-dd}", null, midnight)
        };
        foreach (var schedule in schedules)
        {
            var due = midnight.Add(schedule.LocalTime.ToTimeSpan());
            if (due <= now && due >= settings.UpdatedAt)
                dueSlots.Add(($"schedule:{schedule.Id}:{today:yyyy-MM-dd}", schedule, due));
        }
        var candidateKeys = dueSlots.Select(slot => slot.Key).ToArray();
        var existing = await db.Set<TimeNotificationDispatch>().AsNoTracking()
            .Where(x => organizations.Contains(x.OrganizationId) && candidateKeys.Contains(x.DeduplicationKey))
            .Select(x => new { x.OrganizationId, x.DeduplicationKey }).ToListAsync(cancellationToken);
        var keys = existing.Select(x => (x.OrganizationId, x.DeduplicationKey)).ToHashSet();
        foreach (var organizationId in organizations)
            foreach (var (key, schedule, due) in dueSlots)
            {
                if (!keys.Add((organizationId, key))) continue;
                db.Add(new TimeNotificationDispatch
                {
                    OrganizationId = organizationId,
                    DeduplicationKey = key,
                    RequestHash = "scheduled",
                    Message = schedule?.Message ?? "Relatório do dia anterior",
                    ScheduleId = schedule?.Id,
                    Kind = schedule?.Kind,
                    Period = schedule is null || schedule.Kind == NotificationScheduleKind.PreviousDay ? AnalysisPeriod.PreviousDay : AnalysisPeriod.Daily,
                    ReportOnly = schedule is null,
                    CreatedAt = due,
                    ToleranceMinutes = settings.ToleranceMinutes,
                    SettingsVersion = settings.Version
                });
            }
        await db.SaveChangesAsync(cancellationToken);
    }

}
