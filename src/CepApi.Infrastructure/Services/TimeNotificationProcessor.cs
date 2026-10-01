using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Services;

public sealed class TimeNotificationProcessor(AppDbContext db, IClock clock, IEnumerable<IExternalWorkforceTimeSource> sources)
{
    private readonly FreshTimeAnalysisService freshAnalysis = new(clock, sources);

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        // Session lock survives source calls without holding a long database transaction.
        // Closing the connection releases the lock after a crash. All writes are committed together.
        await db.Database.OpenConnectionAsync(cancellationToken);
        var acquired = false;
        try
        {
            acquired = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(718930112) AS \"Value\"").SingleAsync(cancellationToken);
            if (!acquired) return;
            await EnqueueDueAsync(cancellationToken);
            var pending = await db.Set<TimeNotificationDispatch>().Where(x => x.Status == NotificationDispatchStatus.Pending)
                .OrderBy(x => x.CreatedAt).Take(20).ToListAsync(cancellationToken);
            foreach (var dispatch in pending) await ProcessAsync(dispatch, cancellationToken);
        }
        finally
        {
            if (acquired) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(718930112)", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task EnqueueDueAsync(CancellationToken cancellationToken)
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

    private async Task ProcessAsync(TimeNotificationDispatch dispatch, CancellationToken cancellationToken)
    {
        if (!await db.Organizations.AnyAsync(x => x.Id == dispatch.OrganizationId && x.Status == OrganizationStatus.Active, cancellationToken) ||
            dispatch.ActorUserId.HasValue && !await db.Users.AnyAsync(u => u.Id == dispatch.ActorUserId && u.Status == UserStatus.Active &&
                (u.Role == UserRole.SystemAdmin || u.Role == UserRole.OrganizationAdmin && u.OrganizationId == dispatch.OrganizationId), cancellationToken))
        {
            await FailAsync(dispatch, "dispatch_scope_revoked", cancellationToken);
            return;
        }
        if (dispatch.ScheduleId.HasValue && (!await db.Set<TimeControlSettings>().AnyAsync(s => s.AutomaticEnabled, cancellationToken) ||
            !await db.Set<TimeNotificationSchedule>().AnyAsync(s => s.Id == dispatch.ScheduleId && s.IsEnabled && !s.IsDeleted, cancellationToken)))
        {
            await FailAsync(dispatch, "schedule_disabled", cancellationToken);
            return;
        }
        var people = await db.ActiveNotificationRecipients(dispatch.OrganizationId, dispatch.UserId).AsNoTracking()
            .Include(x => x.MondayIdentity).Include(x => x.VrMaisIdentity).ToListAsync(cancellationToken);
        // Captured at enqueue: every recipient and both sources share the same cutoff.
        var window = TimeAnalysisEngine.ResolvePeriod(dispatch.Period, dispatch.CreatedAt);
        var (snapshots, sourceStates) = await freshAnalysis.FetchAsync(people, window, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (dispatch.ScheduleId.HasValue)
        {
            // Serialize final publication with disabling/deleting a schedule. Source
            // calls happened outside this short transaction and may have taken time.
            var settings = await db.Set<TimeControlSettings>().FromSqlRaw("SELECT * FROM time_control.app_settings WHERE \"Id\" = 1 FOR SHARE")
                .AsNoTracking().SingleAsync(cancellationToken);
            var schedule = await db.Set<TimeNotificationSchedule>().FromSqlInterpolated($"SELECT * FROM time_control.notification_schedules WHERE \"Id\" = {dispatch.ScheduleId.Value} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (!settings.AutomaticEnabled || schedule is null || !schedule.IsEnabled || schedule.IsDeleted)
            {
                await FailAsync(dispatch, "schedule_disabled", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }
        CreateReports(dispatch, people, window, snapshots, sourceStates);
        dispatch.Status = NotificationDispatchStatus.Completed;
        dispatch.CompletedAt = clock.UtcNow;
        if (snapshots.Count != 2 || snapshots.Values.Any(x => !x.Complete)) dispatch.ErrorCode = "analysis_sources_incomplete";
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void CreateReports(TimeNotificationDispatch dispatch, IReadOnlyCollection<WorkforcePerson> people,
        AnalysisWindow window, Dictionary<ExternalWorkforceSource, ExternalWorkforceTimeSnapshot> snapshots,
        List<TimeAnalysisSourceResponse> sourceStates)
    {
        foreach (var person in people)
        {
            var response = freshAnalysis.Analyze(person, window, dispatch.ToleranceMinutes, dispatch.SettingsVersion, snapshots, sourceStates);
            var analysis = response;
            var report = new TimeAnalysisReport
            {
                OrganizationId = person.OrganizationId,
                DispatchId = dispatch.Id,
                WorkforcePersonId = person.Id,
                UserId = person.UserId!.Value,
                DisplayName = person.DisplayName,
                Period = dispatch.Period,
                CreatedAt = clock.UtcNow,
                AnalysisJson = TimeAnalysisJson.Write(response)
            };
            db.Add(report);
            var hasConfirmedError = analysis.Days.Any(x => x.Issues.Any(i => i is "odd_punches" or "running_timer" or "above_tolerance"));
            // The 10:00 reminder is only for an identified error yesterday, not
            // a missing weekend record or an unavailable upstream service.
            if (dispatch.ReportOnly || dispatch.Kind == NotificationScheduleKind.PreviousDay && !hasConfirmedError) continue;
            var outcome = hasConfirmedError ? "Há registros que precisam de ajuste."
                : analysis.DeltaSeconds is null ? "Não foi possível conferir todas as horas. Consulte a qualidade das fontes."
                : "Suas horas estão dentro da tolerância até o momento analisado.";
            db.Add(new TimeNotification
            {
                OrganizationId = person.OrganizationId,
                UserId = person.UserId.Value,
                Report = report,
                ReportId = report.Id,
                Message = $"{dispatch.Message}\n{outcome}",
                CreatedAt = clock.UtcNow
            });
            dispatch.RecipientCount++;
        }
    }

    private async Task FailAsync(TimeNotificationDispatch dispatch, string code, CancellationToken cancellationToken)
    {
        dispatch.Status = NotificationDispatchStatus.Failed;
        dispatch.ErrorCode = code;
        dispatch.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
