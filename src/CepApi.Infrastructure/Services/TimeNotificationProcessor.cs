using System.Text.Json;
using System.Text.Json.Serialization;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CepApi.Infrastructure.Services;

public sealed class TimeNotificationProcessor(AppDbContext db, IClock clock, IEnumerable<IExternalWorkforceTimeSource> sources)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public async Task TickAsync(CancellationToken ct)
    {
        // Session lock survives source calls without holding a long database transaction.
        // Closing the connection releases the lock after a crash. All writes are committed together.
        await db.Database.OpenConnectionAsync(ct);
        var acquired = false;
        try
        {
            acquired = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(718930112) AS \"Value\"").SingleAsync(ct);
            if (!acquired) return;
            await EnqueueDueAsync(ct);
            var pending = await db.Set<TimeNotificationDispatch>().Where(x => x.Status == NotificationDispatchStatus.Pending)
                .OrderBy(x => x.CreatedAt).Take(20).ToListAsync(ct);
            foreach (var dispatch in pending) await ProcessAsync(dispatch, ct);
        }
        finally
        {
            if (acquired) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(718930112)", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task EnqueueDueAsync(CancellationToken ct)
    {
        var settings = await db.Set<TimeControlSettings>().AsNoTracking().SingleAsync(ct);
        var now = clock.UtcNow;
        var today = TimeAnalysisEngine.LocalDate(now);
        var midnight = TimeAnalysisEngine.StartOfDay(today);
        var schedules = settings.AutomaticEnabled && today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            ? await db.Set<TimeNotificationSchedule>().AsNoTracking().Where(x => !x.IsDeleted && x.IsEnabled).ToListAsync(ct) : [];
        var organizations = await db.Organizations.AsNoTracking().Where(x => x.Status == OrganizationStatus.Active &&
            db.WorkforcePeople.Any(p => p.OrganizationId == x.Id && p.UserId != null)).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var org in organizations)
        {
            // The report is generated once per civil day, also on weekends. No popup.
            await AddAsync(org, $"report:{today:yyyy-MM-dd}", null, midnight, true, settings, ct);
            foreach (var schedule in schedules)
            {
                var due = midnight.Add(schedule.LocalTime.ToTimeSpan());
                // Do not replay hours-old lunch reminders after a server outage.
                if (now < due || now - due > TimeSpan.FromMinutes(2)) continue;
                await AddAsync(org, $"schedule:{schedule.Id}:{today:yyyy-MM-dd}", schedule, due, false, settings, ct);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task AddAsync(Guid org, string key, TimeNotificationSchedule? schedule, DateTimeOffset at,
        bool reportOnly, TimeControlSettings settings, CancellationToken ct)
    {
        if (await db.Set<TimeNotificationDispatch>().AnyAsync(x => x.OrganizationId == org && x.DeduplicationKey == key, ct)) return;
        db.Add(new TimeNotificationDispatch { OrganizationId = org, DeduplicationKey = key, RequestHash = "scheduled",
            Message = schedule?.Message ?? "Relatório do dia anterior", ScheduleId = schedule?.Id, Kind = schedule?.Kind,
            Period = reportOnly || schedule?.Kind == NotificationScheduleKind.PreviousDay ? AnalysisPeriod.PreviousDay : AnalysisPeriod.Daily,
            ReportOnly = reportOnly, CreatedAt = at, ToleranceMinutes = settings.ToleranceMinutes, SettingsVersion = settings.Version });
    }

    private async Task ProcessAsync(TimeNotificationDispatch dispatch, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(x => x.Id == dispatch.OrganizationId && x.Status == OrganizationStatus.Active, ct) ||
            dispatch.ActorUserId.HasValue && !await db.Users.AnyAsync(u => u.Id == dispatch.ActorUserId && u.Status == UserStatus.Active &&
                (u.Role == UserRole.SystemAdmin || u.Role == UserRole.OrganizationAdmin && u.OrganizationId == dispatch.OrganizationId), ct))
        {
            dispatch.Status = NotificationDispatchStatus.Failed; dispatch.ErrorCode = "dispatch_scope_revoked";
            dispatch.CompletedAt = clock.UtcNow; await db.SaveChangesAsync(ct); return;
        }
        if (dispatch.ScheduleId.HasValue && (!await db.Set<TimeControlSettings>().AnyAsync(s => s.AutomaticEnabled, ct) ||
            !await db.Set<TimeNotificationSchedule>().AnyAsync(s => s.Id == dispatch.ScheduleId && s.IsEnabled && !s.IsDeleted, ct)))
        {
            dispatch.Status = NotificationDispatchStatus.Failed; dispatch.ErrorCode = "schedule_disabled";
            dispatch.CompletedAt = clock.UtcNow; await db.SaveChangesAsync(ct); return;
        }
        var people = await db.WorkforcePeople.AsNoTracking().Include(x => x.MondayIdentity).Include(x => x.VrMaisIdentity)
            .Where(p => p.OrganizationId == dispatch.OrganizationId && p.UserId != null && (!dispatch.UserId.HasValue || p.UserId == dispatch.UserId) &&
                db.Users.Any(u => u.Id == p.UserId && u.OrganizationId == dispatch.OrganizationId && u.Status == UserStatus.Active))
            .ToListAsync(ct);
        // Captured at enqueue: every recipient and both sources share the same cutoff.
        var window = TimeAnalysisEngine.ResolvePeriod(dispatch.Period, dispatch.CreatedAt);
        var snapshots = new Dictionary<ExternalWorkforceSource, ExternalWorkforceTimeSnapshot>();
        var sourceStates = new List<TimeAnalysisSourceResponse>();
        foreach (var source in sources)
        {
            var ids = people.Select(p => source.Source == ExternalWorkforceSource.Monday ? p.MondayIdentity : p.VrMaisIdentity)
                .Where(x => x.IsActive).Select(x => x.ExternalId).Distinct().ToArray();
            if (ids.Length == 0) { sourceStates.Add(new(source.Source, "incomplete", "source_scope_empty", clock.UtcNow)); continue; }
            try
            {
                var snapshot = source is IExternalWorkforceOverlapTimeSource overlap
                    ? await overlap.FetchIncludingOverlapAsync(window.From, window.To, ids, ct)
                    : await source.FetchAsync(window.From, window.To, ids, ct);
                snapshots[source.Source] = snapshot;
                sourceStates.Add(new(source.Source, snapshot.Complete ? "complete" : "incomplete", null, clock.UtcNow));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is ExternalDirectoryException or HttpRequestException or TaskCanceledException or JsonException)
            { sourceStates.Add(new(source.Source, "incomplete", e is ExternalDirectoryException external ? external.Code : "source_unavailable", clock.UtcNow)); }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (dispatch.ScheduleId.HasValue)
        {
            // Serialize final publication with disabling/deleting a schedule. Source
            // calls happened outside this short transaction and may have taken time.
            var settings = await db.Set<TimeControlSettings>().FromSqlRaw("SELECT * FROM time_control.app_settings WHERE \"Id\" = 1 FOR SHARE")
                .AsNoTracking().SingleAsync(ct);
            var schedule = await db.Set<TimeNotificationSchedule>().FromSqlInterpolated($"SELECT * FROM time_control.notification_schedules WHERE \"Id\" = {dispatch.ScheduleId.Value} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (!settings.AutomaticEnabled || schedule is null || !schedule.IsEnabled || schedule.IsDeleted)
            {
                dispatch.Status = NotificationDispatchStatus.Failed; dispatch.ErrorCode = "schedule_disabled";
                dispatch.CompletedAt = clock.UtcNow; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return;
            }
        }
        foreach (var person in people)
        {
            var records = new List<WorkforceTimeRecord>();
            foreach (var (source, snapshot) in snapshots)
            {
                var identity = source == ExternalWorkforceSource.Monday ? person.MondayIdentity : person.VrMaisIdentity;
                records.AddRange(snapshot.Records.Where(r => r.ExternalIdentityId == identity.ExternalId).Select(r => new WorkforceTimeRecord
                {
                    OrganizationId = person.OrganizationId, ExternalIdentityId = identity.Id, Source = source,
                    ExternalKey = r.ExternalKey, WorkDate = r.WorkDate, StartedAt = r.StartedAt, EndedAt = r.EndedAt,
                    DurationSeconds = r.DurationSeconds, State = r.State, Title = r.Title, DetailsJson = r.DetailsJson, LastSyncedAt = clock.UtcNow
                }));
            }
            var complete = snapshots.Count == 2 && snapshots.Values.All(x => x.Complete && x.From <= window.From && x.To >= window.To) &&
                person.MondayIdentity.IsActive && person.VrMaisIdentity.IsActive;
            var analysis = TimeAnalysisEngine.Analyze(window.From, window.To, window.Cutoff, dispatch.ToleranceMinutes, records, complete);
            var response = new TimeAnalysisResponse(analysis.From, analysis.To, analysis.Cutoff, analysis.ToleranceMinutes, analysis.Days,
                analysis.VrSeconds, analysis.MondaySeconds, analysis.DeltaSeconds, analysis.AbsoluteDivergenceSeconds, analysis.HasIssues, dispatch.SettingsVersion, sourceStates);
            var report = new TimeAnalysisReport { OrganizationId = person.OrganizationId, DispatchId = dispatch.Id,
                WorkforcePersonId = person.Id, UserId = person.UserId!.Value, DisplayName = person.DisplayName,
                Period = dispatch.Period, CreatedAt = clock.UtcNow, AnalysisJson = JsonSerializer.Serialize(response, Json) };
            db.Add(report);
            var hasConfirmedError = analysis.Days.Any(x => x.Issues.Any(i => i is "odd_punches" or "running_timer" or "above_tolerance"));
            // The 10:00 reminder is only for an identified error yesterday, not
            // a missing weekend record or an unavailable upstream service.
            if (dispatch.ReportOnly || dispatch.Kind == NotificationScheduleKind.PreviousDay && !hasConfirmedError) continue;
            var outcome = hasConfirmedError ? "Há registros que precisam de ajuste."
                : analysis.DeltaSeconds is null ? "Não foi possível conferir todas as horas. Consulte a qualidade das fontes."
                : "Suas horas estão dentro da tolerância até o momento analisado.";
            db.Add(new TimeNotification { OrganizationId = person.OrganizationId, UserId = person.UserId.Value, Report = report,
                ReportId = report.Id, Message = $"{dispatch.Message}\n{outcome}", CreatedAt = clock.UtcNow });
            dispatch.RecipientCount++;
        }
        dispatch.Status = NotificationDispatchStatus.Completed; dispatch.CompletedAt = clock.UtcNow;
        if (snapshots.Count != 2 || snapshots.Values.Any(x => !x.Complete)) dispatch.ErrorCode = "analysis_sources_incomplete";
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

internal sealed class TimeNotificationWorker(IServiceScopeFactory scopes, ILogger<TimeNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TimeNotificationProcessor>().TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Do not log external responses, personnel data, messages or credentials.
                logger.LogWarning("Time notification processing failed; pending work will be retried.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
