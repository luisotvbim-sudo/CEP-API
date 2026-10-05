using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;

namespace CepApi.Infrastructure.Services;

/// <summary>Fresh source reads and the shared engine for both notices and interactive decisions.</summary>
public sealed class FreshTimeAnalysisService(IClock clock, IEnumerable<IExternalWorkforceTimeSource> sources)
{
    public async Task<(Dictionary<ExternalWorkforceSource, ExternalWorkforceTimeSnapshot> Snapshots,
        List<TimeAnalysisSourceResponse> States)> FetchAsync(
        IReadOnlyCollection<WorkforcePerson> people, AnalysisWindow window, CancellationToken cancellationToken)
    {
        var snapshots = new Dictionary<ExternalWorkforceSource, ExternalWorkforceTimeSnapshot>();
        var sourceStates = new List<TimeAnalysisSourceResponse>();
        foreach (var source in sources)
        {
            var ids = people.Select(p => source.Source == ExternalWorkforceSource.Monday ? p.MondayIdentity : p.VrMaisIdentity)
                .Where(x => x.IsActive).Select(x => x.ExternalId).Distinct().ToArray();
            if (ids.Length == 0)
            {
                sourceStates.Add(new(source.Source, "incomplete", "source_scope_empty", clock.UtcNow));
                continue;
            }
            try
            {
                var snapshot = source is IExternalWorkforceOverlapTimeSource overlap
                    ? await overlap.FetchIncludingOverlapAsync(window.From, window.To, ids, cancellationToken)
                    : await source.FetchAsync(window.From, window.To, ids, cancellationToken);
                snapshots[source.Source] = snapshot;
                sourceStates.Add(new(source.Source, snapshot.Complete && snapshot.From <= window.From && snapshot.To >= window.To ? "complete" : "incomplete", null, clock.UtcNow));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is ExternalDirectoryException or HttpRequestException or TaskCanceledException or JsonException)
            {
                var errorCode = exception is ExternalDirectoryException external ? external.Code : "source_unavailable";
                sourceStates.Add(new(source.Source, "incomplete", errorCode, clock.UtcNow));
            }
        }
        foreach (var source in Enum.GetValues<ExternalWorkforceSource>())
            if (!sourceStates.Any(x => x.Source == source))
                sourceStates.Add(new(source, "incomplete", "source_unavailable", clock.UtcNow));
        return (snapshots, sourceStates);
    }

    public TimeAnalysisResponse Analyze(WorkforcePerson person, AnalysisWindow window, int toleranceMinutes, Guid settingsVersion,
        Dictionary<ExternalWorkforceSource, ExternalWorkforceTimeSnapshot> snapshots, List<TimeAnalysisSourceResponse> sourceStates)
        => AnalyzeCore(person, window, toleranceMinutes, settingsVersion, snapshots, sourceStates, false);

    internal TimeAnalysisResponse ProjectAvailableSource(WorkforcePerson person, AnalysisWindow window, int toleranceMinutes, Guid settingsVersion,
        ExternalWorkforceSource source, ExternalWorkforceTimeSnapshot snapshot, List<TimeAnalysisSourceResponse> sourceStates)
        => AnalyzeCore(person, window, toleranceMinutes, settingsVersion, new() { [source] = snapshot }, sourceStates, true);

    private TimeAnalysisResponse AnalyzeCore(WorkforcePerson person, AnalysisWindow window, int toleranceMinutes, Guid settingsVersion,
        Dictionary<ExternalWorkforceSource, ExternalWorkforceTimeSnapshot> snapshots, List<TimeAnalysisSourceResponse> sourceStates,
        bool preserveAvailableSourceValues = false)
    {
        var records = new List<WorkforceTimeRecord>();
        foreach (var (source, sourceRecords) in snapshots.ToDictionary(pair => pair.Key, pair => pair.Value.Records.ToLookup(record => record.ExternalIdentityId, StringComparer.Ordinal)))
        {
            var identity = source == ExternalWorkforceSource.Monday ? person.MondayIdentity : person.VrMaisIdentity;
            records.AddRange(sourceRecords[identity.ExternalId].Select(r => new WorkforceTimeRecord
            {
                OrganizationId = person.OrganizationId,
                ExternalIdentityId = identity.Id,
                Source = source,
                ExternalKey = r.ExternalKey,
                WorkDate = r.WorkDate,
                StartedAt = r.StartedAt,
                EndedAt = r.EndedAt,
                DurationSeconds = r.DurationSeconds,
                State = r.State,
                Title = r.Title,
                DetailsJson = r.DetailsJson,
                LastSyncedAt = clock.UtcNow
            }));
        }
        var complete = snapshots.Count == 2 && snapshots.Values.All(x => x.Complete && x.From <= window.From && x.To >= window.To) &&
            person.MondayIdentity.IsActive && person.VrMaisIdentity.IsActive;
        var analysis = TimeAnalysisEngine.Analyze(window.From, window.To, window.Cutoff, toleranceMinutes, records, complete || preserveAvailableSourceValues);
        return new TimeAnalysisResponse(analysis.From, analysis.To, analysis.Cutoff, analysis.ToleranceMinutes, analysis.Days,
            analysis.VrSeconds, analysis.MondaySeconds, analysis.DeltaSeconds, analysis.AbsoluteDivergenceSeconds, analysis.HasIssues, settingsVersion, sourceStates);
    }
}
