using System.Diagnostics;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CepApi.Infrastructure.Services;

public sealed class PersonalOverviewService(AppDbContext db, IClock clock, FreshTimeAnalysisService analysis,
    ILogger<PersonalOverviewService>? logger = null)
{
    public async Task<PersonalOverviewResponse> ReadAsync(Guid organizationId, Guid userId,
        AnalysisPeriod period, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var cutoff = clock.UtcNow;
        long? lookupMs = null, sourcesMs = null, projectionMs = null;
        var sourceTimings = new Dictionary<ExternalWorkforceSource, (long Milliseconds, string Outcome)>();
        var outcome = "failed";
        try
        {
            var lookupStarted = Stopwatch.GetTimestamp();
            var person = await db.ActiveNotificationRecipients(organizationId, userId).AsNoTracking()
                .Include(person => person.MondayIdentity).Include(person => person.VrMaisIdentity)
                .SingleOrDefaultAsync(cancellationToken);
            if (person is null)
            {
                lookupMs = (long)Stopwatch.GetElapsedTime(lookupStarted).TotalMilliseconds;
                outcome = "not_associated";
                return Unavailable(PersonalOverviewStatus.NotAssociated);
            }
            if (!person.MondayIdentity.IsActive || !person.VrMaisIdentity.IsActive)
            {
                lookupMs = (long)Stopwatch.GetElapsedTime(lookupStarted).TotalMilliseconds;
                outcome = "inactive_identity";
                return Unavailable(PersonalOverviewStatus.InactiveIdentity);
            }

            var settings = await db.Set<TimeControlSettings>().AsNoTracking().SingleAsync(cancellationToken);
            lookupMs = (long)Stopwatch.GetElapsedTime(lookupStarted).TotalMilliseconds;
            var window = TimeAnalysisEngine.ResolvePeriod(period, cutoff);
            var sourcesStarted = Stopwatch.GetTimestamp();
            var (snapshots, states) = await analysis.FetchAsync([person], window, cancellationToken,
                (source, milliseconds, status) => sourceTimings[source] = (milliseconds, status));
            sourcesMs = (long)Stopwatch.GetElapsedTime(sourcesStarted).TotalMilliseconds;

            var projectionStarted = Stopwatch.GetTimestamp();
            var result = analysis.Analyze(person, window, settings.ToleranceMinutes, settings.Version, snapshots, states);

            // Keep the existing all-source aggregate semantics. Separate daily source values only
            // use a complete targeted read for this person, never an organization's import timestamp.
            var available = new Dictionary<ExternalWorkforceSource, IReadOnlyList<TimeAnalysisDay>>();
            foreach (var (source, snapshot) in snapshots)
            {
                if (!snapshot.Complete || snapshot.From > window.From || snapshot.To < window.To) continue;
                var projected = analysis.ProjectAvailableSource(person, window, settings.ToleranceMinutes, settings.Version,
                    source, snapshot, states);
                available[source] = projected.Days;
            }
            var sourceDays = result.Days.Select((day, index) => new PersonalSourceDay(day.Day,
                available.TryGetValue(ExternalWorkforceSource.VrMais, out var vr) ? vr[index].VrSeconds : null,
                available.TryGetValue(ExternalWorkforceSource.Monday, out var monday) ? monday[index].MondaySeconds : null)).ToArray();
            var response = PersonalOverviewProjection.FromAnalysis(period, result, sourceDays);
            projectionMs = (long)Stopwatch.GetElapsedTime(projectionStarted).TotalMilliseconds;
            outcome = "complete";
            return response;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            var hasMonday = sourceTimings.TryGetValue(ExternalWorkforceSource.Monday, out var monday);
            var hasVr = sourceTimings.TryGetValue(ExternalWorkforceSource.VrMais, out var vr);
            logger?.LogInformation(
                "Personal overview timing TraceId={TraceId} Period={Period} Outcome={Outcome} TotalMs={TotalMs} LookupMs={LookupMs} SourcesMs={SourcesMs} MondayMs={MondayMs} MondayOutcome={MondayOutcome} VrMaisMs={VrMaisMs} VrMaisOutcome={VrMaisOutcome} ProjectionMs={ProjectionMs}",
                Activity.Current?.TraceId.ToString(), period, outcome,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, lookupMs, sourcesMs,
                hasMonday ? monday.Milliseconds : null,
                monday.Outcome,
                hasVr ? vr.Milliseconds : null,
                vr.Outcome, projectionMs);
        }

        PersonalOverviewResponse Unavailable(PersonalOverviewStatus status)
            => new(period, cutoff, PersonalOverviewProjection.Periods(cutoff), status, null, [], []);
    }
}
