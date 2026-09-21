using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Services;

public sealed class WorkforceDirectorySyncService(
    AppDbContext db,
    IEnumerable<IExternalWorkforceDirectorySource> sources,
    IEnumerable<IExternalWorkforceTimeSource> timeSources,
    IClock clock) : IWorkforceDirectorySyncService
{
    private readonly IReadOnlyDictionary<ExternalWorkforceSource, IExternalWorkforceDirectorySource> sourcesByType =
        sources.ToDictionary(x => x.Source);
    private readonly IReadOnlyDictionary<ExternalWorkforceSource, IExternalWorkforceTimeSource> timeSourcesByType =
        timeSources.ToDictionary(x => x.Source);

    public async Task<WorkforceSyncBatch> SynchronizeAsync(
        Guid organizationId,
        Guid requestedByUserId,
        bool fullRefresh,
        CancellationToken cancellationToken)
    {
        var staleBefore = clock.UtcNow.AddHours(-2);
        var staleBatches = await db.WorkforceSyncBatches.Include(x => x.Sources).Where(x =>
            x.OrganizationId == organizationId && x.Status == WorkforceSyncStatus.Running &&
            x.StartedAt < staleBefore).ToListAsync(cancellationToken);
        foreach (var stale in staleBatches)
        {
            stale.Status = WorkforceSyncStatus.Failed;
            stale.CompletedAt = clock.UtcNow;
            foreach (var source in stale.Sources.Where(x => x.Status == WorkforceSyncStatus.Running))
            {
                source.Status = WorkforceSyncStatus.Failed;
                source.ErrorCode = "sync_interrupted";
                source.ErrorMessage = "The synchronization was interrupted before completion.";
                source.CompletedAt = clock.UtcNow;
            }
        }
        if (staleBatches.Count > 0) await db.SaveChangesAsync(cancellationToken);

        if (await db.WorkforceSyncBatches.AnyAsync(
            x => x.OrganizationId == organizationId && x.Status == WorkforceSyncStatus.Running,
            cancellationToken))
            throw new ExternalDirectoryException("sync_already_running", "A workforce synchronization is already running.");

        var now = clock.UtcNow;
        var batch = new WorkforceSyncBatch
        {
            OrganizationId = organizationId,
            RequestedByUserId = requestedByUserId,
            StartedAt = now,
            Sources = Enum.GetValues<ExternalWorkforceSource>().Select(source => new WorkforceSyncSourceRun
            {
                OrganizationId = organizationId,
                Source = source,
                StartedAt = now
            }).ToList()
        };
        db.WorkforceSyncBatches.Add(batch);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            if (await HasRunningBatchAsync(organizationId, cancellationToken))
                throw new ExternalDirectoryException("sync_already_running", "A workforce synchronization is already running.", exception);
            throw;
        }

        var fetchTasks = batch.Sources.ToDictionary(
            run => run.Source,
            run => FetchAsync(run.Source, cancellationToken));
        await Task.WhenAll(fetchTasks.Values);

        foreach (var run in batch.Sources.OrderBy(x => x.Source))
        {
            var outcome = await fetchTasks[run.Source];
            if (outcome.Error is not null)
            {
                run.Status = WorkforceSyncStatus.Failed;
                run.ErrorCode = outcome.Error.Code;
                run.ErrorMessage = Truncate(outcome.Error.Message, 500);
            }
            else
            {
                try
                {
                    await ApplySnapshotAsync(organizationId, run, outcome.Snapshot!, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    if (!timeSourcesByType.TryGetValue(run.Source, out var timeSource))
                        throw new ExternalDirectoryException("time_source_not_registered", $"{run.Source} time integration is not registered.");
                    var (from, to) = await GetSynchronizationPeriodAsync(
                        organizationId, run.Source, batch.Id, fullRefresh, cancellationToken);
                    var activeIds = outcome.Snapshot!.Identities.Where(x => x.IsActive)
                        .Select(x => x.ExternalId.Trim()).Distinct(StringComparer.Ordinal).ToArray();
                    var timeSnapshot = await timeSource.FetchAsync(from, to, activeIds, cancellationToken);
                    await ApplyTimeSnapshotAsync(organizationId, run, timeSnapshot, activeIds, cancellationToken);
                    run.Status = WorkforceSyncStatus.Succeeded;
                }
                catch (ExternalDirectoryException exception)
                {
                    run.Status = run.ReceivedCount > 0
                        ? WorkforceSyncStatus.PartiallySucceeded
                        : WorkforceSyncStatus.Failed;
                    run.ErrorCode = exception.Code;
                    run.ErrorMessage = Truncate(exception.Message, 500);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    run.Status = run.ReceivedCount > 0
                        ? WorkforceSyncStatus.PartiallySucceeded
                        : WorkforceSyncStatus.Failed;
                    run.ErrorCode = "time_sync_failed";
                    run.ErrorMessage = Truncate($"{run.Source} time synchronization failed.", 500);
                }
            }
            run.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var successes = batch.Sources.Count(x => x.Status == WorkforceSyncStatus.Succeeded);
        var useful = batch.Sources.Count(x => x.Status is WorkforceSyncStatus.Succeeded or WorkforceSyncStatus.PartiallySucceeded);
        batch.Status = successes == batch.Sources.Count
            ? WorkforceSyncStatus.Succeeded
            : useful == 0 ? WorkforceSyncStatus.Failed : WorkforceSyncStatus.PartiallySucceeded;
        batch.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return batch;
    }

    private async Task ApplySnapshotAsync(
        Guid organizationId,
        WorkforceSyncSourceRun run,
        ExternalWorkforceDirectorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Identities.Count > 10_000)
            throw new ExternalDirectoryException("directory_too_large", "The external directory exceeded the supported limit.");

        var normalizedItems = snapshot.Identities.Select(item => new NormalizedIdentity(
            Truncate(item.ExternalId.Trim(), 200),
            Truncate(item.DisplayName.Trim(), 200),
            NormalizeEmail(item.Email),
            item.IsActive,
            item.SourceUpdatedAt)).ToArray();
        if (normalizedItems.Any(x => x.ExternalId.Length == 0 || x.DisplayName.Length == 0))
            throw new ExternalDirectoryException("directory_invalid_identity", "The external directory returned an invalid identity.");
        var duplicates = normalizedItems.GroupBy(x => x.ExternalId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null)
            throw new ExternalDirectoryException("directory_duplicate_id", "The external directory returned duplicate identifiers.");

        var existing = await db.ExternalWorkforceIdentities
            .Where(x => x.OrganizationId == organizationId && x.Source == run.Source)
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.Ordinal, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = clock.UtcNow;

        foreach (var item in normalizedItems)
        {
            seen.Add(item.ExternalId);
            if (!existing.TryGetValue(item.ExternalId, out var identity))
            {
                identity = new ExternalWorkforceIdentity
                {
                    OrganizationId = organizationId,
                    Source = run.Source,
                    ExternalId = item.ExternalId,
                    DisplayName = item.DisplayName,
                    Email = item.Email,
                    IsActive = item.IsActive,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    SourceUpdatedAt = item.SourceUpdatedAt
                };
                db.ExternalWorkforceIdentities.Add(identity);
                existing.Add(item.ExternalId, identity);
                run.CreatedCount++;
                continue;
            }

            var changed = identity.DisplayName != item.DisplayName || identity.Email != item.Email ||
                identity.IsActive != item.IsActive || identity.SourceUpdatedAt != item.SourceUpdatedAt;
            identity.DisplayName = item.DisplayName;
            identity.Email = item.Email;
            identity.IsActive = item.IsActive;
            identity.LastSeenAt = now;
            identity.SourceUpdatedAt = item.SourceUpdatedAt;
            if (changed) run.UpdatedCount++;
        }

        if (snapshot.Complete)
        {
            foreach (var missing in existing.Values.Where(x => x.IsActive && !seen.Contains(x.ExternalId)))
            {
                missing.IsActive = false;
                run.DeactivatedCount++;
            }
        }

        run.ReceivedCount = snapshot.Identities.Count;
        run.CompleteSnapshot = snapshot.Complete;
    }

    private async Task ApplyTimeSnapshotAsync(
        Guid organizationId,
        WorkforceSyncSourceRun run,
        ExternalWorkforceTimeSnapshot snapshot,
        IReadOnlyCollection<string> activeExternalIdentityIds,
        CancellationToken cancellationToken)
    {
        if (snapshot.To < snapshot.From || snapshot.Records.Count > 500_000)
            throw new ExternalDirectoryException("time_snapshot_invalid", "The external source returned an invalid time snapshot.");
        var normalized = snapshot.Records.Select(NormalizeTimeRecord).ToArray();
        var duplicate = normalized.GroupBy(x => x.ExternalKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ExternalDirectoryException("time_snapshot_duplicate", "The external source returned duplicate time records.");
        if (normalized.Any(x => x.WorkDate < snapshot.From || x.WorkDate > snapshot.To))
            throw new ExternalDirectoryException("time_snapshot_out_of_range", "The external source returned time records outside the requested period.");

        var externalIds = activeExternalIdentityIds.Select(x => Truncate(x.Trim(), 200))
            .Distinct(StringComparer.Ordinal).ToArray();
        var identities = await db.ExternalWorkforceIdentities.Where(x => x.OrganizationId == organizationId &&
                x.Source == run.Source && externalIds.Contains(x.ExternalId))
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.Ordinal, cancellationToken);
        if (identities.Count != externalIds.Length)
            throw new ExternalDirectoryException("time_snapshot_unknown_identity", "The external source returned time data for an unknown identity.");
        if (normalized.Any(x => !identities.ContainsKey(x.ExternalIdentityId)))
            throw new ExternalDirectoryException("time_snapshot_unknown_identity", "The external source returned time data for an unknown identity.");

        var participatingIdentityIds = identities.Values.Select(x => x.Id).ToArray();
        var existing = await db.WorkforceTimeRecords.Where(x => x.OrganizationId == organizationId &&
                x.Source == run.Source && x.WorkDate >= snapshot.From && x.WorkDate <= snapshot.To &&
                participatingIdentityIds.Contains(x.ExternalIdentityId))
            .ToDictionaryAsync(x => x.ExternalKey, StringComparer.Ordinal, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = clock.UtcNow;
        foreach (var item in normalized)
        {
            seen.Add(item.ExternalKey);
            var identityId = identities[item.ExternalIdentityId].Id;
            if (!existing.TryGetValue(item.ExternalKey, out var record))
            {
                record = new WorkforceTimeRecord
                {
                    OrganizationId = organizationId,
                    Source = run.Source,
                    ExternalIdentityId = identityId,
                    ExternalKey = item.ExternalKey,
                    WorkDate = item.WorkDate,
                    StartedAt = item.StartedAt,
                    EndedAt = item.EndedAt,
                    DurationSeconds = item.DurationSeconds,
                    State = item.State,
                    Title = item.Title,
                    Url = item.Url,
                    DetailsJson = item.DetailsJson,
                    LastSyncedAt = now
                };
                db.WorkforceTimeRecords.Add(record);
                existing.Add(item.ExternalKey, record);
                run.TimeRecordCreatedCount++;
                continue;
            }

            var changed = record.ExternalIdentityId != identityId || record.WorkDate != item.WorkDate ||
                record.StartedAt != item.StartedAt || record.EndedAt != item.EndedAt ||
                record.DurationSeconds != item.DurationSeconds || record.State != item.State ||
                record.Title != item.Title || record.Url != item.Url || record.DetailsJson != item.DetailsJson ||
                record.IsRemoved;
            record.ExternalIdentityId = identityId;
            record.WorkDate = item.WorkDate;
            record.StartedAt = item.StartedAt;
            record.EndedAt = item.EndedAt;
            record.DurationSeconds = item.DurationSeconds;
            record.State = item.State;
            record.Title = item.Title;
            record.Url = item.Url;
            record.DetailsJson = item.DetailsJson;
            record.IsRemoved = false;
            record.LastSyncedAt = now;
            if (changed) run.TimeRecordUpdatedCount++;
        }

        if (snapshot.Complete)
        {
            foreach (var removed in existing.Values.Where(x => !x.IsRemoved && !seen.Contains(x.ExternalKey)))
            {
                removed.IsRemoved = true;
                removed.LastSyncedAt = now;
                run.TimeRecordRemovedCount++;
            }
        }

        var retentionCutoff = LocalToday(clock.UtcNow).AddDays(-89);
        await db.WorkforceTimeRecords.Where(x => x.OrganizationId == organizationId && x.Source == run.Source &&
            x.WorkDate < retentionCutoff).ExecuteDeleteAsync(cancellationToken);
        run.TimeRecordReceivedCount = normalized.Length;
        run.CoverageFrom = snapshot.From;
        run.CoverageTo = snapshot.To;
        run.CompleteSnapshot &= snapshot.Complete;
    }

    private async Task<(DateOnly From, DateOnly To)> GetSynchronizationPeriodAsync(
        Guid organizationId,
        ExternalWorkforceSource source,
        Guid currentBatchId,
        bool fullRefresh,
        CancellationToken cancellationToken)
    {
        var today = LocalToday(clock.UtcNow);
        var previousCoverage = fullRefresh ? null : await db.WorkforceSyncSourceRuns.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Source == source && x.BatchId != currentBatchId &&
                x.Status == WorkforceSyncStatus.Succeeded && x.CoverageTo != null)
            .OrderByDescending(x => x.CompletedAt).Select(x => x.CoverageTo).FirstOrDefaultAsync(cancellationToken);
        var earliest = today.AddDays(-59);
        var from = previousCoverage?.AddDays(-1) ?? earliest;
        if (from < earliest) from = earliest;
        if (from > today) from = today;
        return (from, today);
    }

    private static NormalizedTimeRecord NormalizeTimeRecord(ExternalWorkforceTimeRecordSnapshot item)
    {
        var externalIdentityId = Truncate(item.ExternalIdentityId.Trim(), 200);
        var externalKey = Truncate(item.ExternalKey.Trim(), 500);
        var state = Truncate(item.State.Trim().ToLowerInvariant(), 32);
        if (externalIdentityId.Length == 0 || externalKey.Length == 0 || state.Length == 0 || item.DurationSeconds < 0)
            throw new ExternalDirectoryException("time_snapshot_invalid_record", "The external source returned an invalid time record.");
        var details = item.DetailsJson;
        if (details is { Length: > 20_000 })
            throw new ExternalDirectoryException("time_snapshot_details_too_large", "The external source returned oversized time record details.");
        if (details is not null)
        {
            try { using var _ = System.Text.Json.JsonDocument.Parse(details); }
            catch (System.Text.Json.JsonException exception)
            {
                throw new ExternalDirectoryException("time_snapshot_invalid_details", "The external source returned invalid time record details.", exception);
            }
        }
        return new NormalizedTimeRecord(externalIdentityId, externalKey, item.WorkDate, item.StartedAt, item.EndedAt,
            item.DurationSeconds, state, TruncateNullable(item.Title, 500), NormalizeUrl(item.Url), details);
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return uri.ToString();
    }

    private static DateOnly LocalToday(DateTimeOffset value)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime);
    }

    private async Task<FetchOutcome> FetchAsync(ExternalWorkforceSource source, CancellationToken cancellationToken)
    {
        if (!sourcesByType.TryGetValue(source, out var provider))
            return new(null, new ExternalDirectoryException("source_not_registered", $"{source} integration is not registered."));
        try
        {
            return new(await provider.FetchAsync(cancellationToken), null);
        }
        catch (ExternalDirectoryException exception)
        {
            return new(null, exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(null, new ExternalDirectoryException("source_sync_failed", $"{source} synchronization failed.", exception));
        }
    }

    private Task<bool> HasRunningBatchAsync(Guid organizationId, CancellationToken cancellationToken)
        => db.WorkforceSyncBatches.AsNoTracking().AnyAsync(
            x => x.OrganizationId == organizationId && x.Status == WorkforceSyncStatus.Running,
            cancellationToken);

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim().ToLowerInvariant(), 320);

    private static string Truncate(string value, int maxLength)
        => value[..Math.Min(value.Length, maxLength)];

    private static string? TruncateNullable(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    private sealed record FetchOutcome(
        ExternalWorkforceDirectorySnapshot? Snapshot,
        ExternalDirectoryException? Error);

    private sealed record NormalizedIdentity(
        string ExternalId,
        string DisplayName,
        string? Email,
        bool IsActive,
        DateTimeOffset? SourceUpdatedAt);

    private sealed record NormalizedTimeRecord(
        string ExternalIdentityId,
        string ExternalKey,
        DateOnly WorkDate,
        DateTimeOffset? StartedAt,
        DateTimeOffset? EndedAt,
        int? DurationSeconds,
        string State,
        string? Title,
        string? Url,
        string? DetailsJson);
}
