using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Services;

internal sealed class WorkforceSnapshotWriter(AppDbContext db, IClock clock)
{
    public async Task ApplyDirectoryAsync(
        Guid organizationId,
        WorkforceSyncSourceRun run,
        ExternalWorkforceDirectorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var normalized = WorkforceSnapshotNormalizer.NormalizeDirectory(snapshot);
        var existing = await db.ExternalWorkforceIdentities
            .Where(x => x.OrganizationId == organizationId && x.Source == run.Source)
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.Ordinal, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = clock.UtcNow;

        foreach (var item in normalized)
        {
            seen.Add(item.ExternalId);
            if (!existing.TryGetValue(item.ExternalId, out var identity))
            {
                identity = CreateIdentity(organizationId, run.Source, item, now);
                db.ExternalWorkforceIdentities.Add(identity);
                existing.Add(item.ExternalId, identity);
                run.CreatedCount++;
                continue;
            }

            if (UpdateIdentity(identity, item, now))
                run.UpdatedCount++;
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

    public async Task ApplyTimeAsync(
        Guid organizationId,
        WorkforceSyncSourceRun run,
        ExternalWorkforceTimeSnapshot snapshot,
        IReadOnlyCollection<string> activeExternalIdentityIds,
        CancellationToken cancellationToken)
    {
        var normalized = WorkforceSnapshotNormalizer.NormalizeTime(snapshot);
        var externalIds = WorkforceSnapshotNormalizer.NormalizeExternalIds(activeExternalIdentityIds);
        var identities = await LoadIdentitiesAsync(organizationId, run.Source, externalIds, cancellationToken);
        EnsureKnownIdentities(normalized, externalIds, identities);

        var identityIds = identities.Values.Select(x => x.Id).ToArray();
        var snapshotKeys = normalized.Select(x => x.ExternalKey).ToArray();
        var existing = await db.WorkforceTimeRecords.Where(x => x.OrganizationId == organizationId &&
                x.Source == run.Source &&
                (x.WorkDate >= snapshot.From && x.WorkDate <= snapshot.To && identityIds.Contains(x.ExternalIdentityId) ||
                 snapshotKeys.Contains(x.ExternalKey)))
            .ToDictionaryAsync(x => x.ExternalKey, StringComparer.Ordinal, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var now = clock.UtcNow;

        foreach (var item in normalized)
        {
            seen.Add(item.ExternalKey);
            var identityId = identities[item.ExternalIdentityId].Id;
            if (!existing.TryGetValue(item.ExternalKey, out var record))
            {
                record = CreateTimeRecord(organizationId, run.Source, identityId, item, now);
                db.WorkforceTimeRecords.Add(record);
                existing.Add(item.ExternalKey, record);
                run.TimeRecordCreatedCount++;
                continue;
            }

            if (UpdateTimeRecord(record, identityId, item, now))
                run.TimeRecordUpdatedCount++;
        }

        if (snapshot.Complete)
        {
            foreach (var removed in existing.Values.Where(x => identityIds.Contains(x.ExternalIdentityId) &&
                !x.IsRemoved && !seen.Contains(x.ExternalKey)))
            {
                removed.IsRemoved = true;
                removed.LastSyncedAt = now;
                run.TimeRecordRemovedCount++;
            }
        }

        await DeleteExpiredRecordsAsync(organizationId, run.Source, cancellationToken);
        run.TimeRecordReceivedCount = normalized.Count;
        run.CoverageFrom = snapshot.From;
        run.CoverageTo = snapshot.To;
        run.CompleteSnapshot &= snapshot.Complete;
    }

    private async Task<Dictionary<string, ExternalWorkforceIdentity>> LoadIdentitiesAsync(
        Guid organizationId,
        ExternalWorkforceSource source,
        string[] externalIds,
        CancellationToken cancellationToken)
        => await db.ExternalWorkforceIdentities.Where(x => x.OrganizationId == organizationId &&
                x.Source == source && externalIds.Contains(x.ExternalId))
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.Ordinal, cancellationToken);

    private static void EnsureKnownIdentities(
        IReadOnlyCollection<NormalizedWorkforceTimeRecord> records,
        IReadOnlyCollection<string> externalIds,
        IReadOnlyDictionary<string, ExternalWorkforceIdentity> identities)
    {
        if (identities.Count != externalIds.Count || records.Any(x => !identities.ContainsKey(x.ExternalIdentityId)))
            throw new ExternalDirectoryException(
                "time_snapshot_unknown_identity", "The external source returned time data for an unknown identity.");
    }

    private async Task DeleteExpiredRecordsAsync(
        Guid organizationId,
        ExternalWorkforceSource source,
        CancellationToken cancellationToken)
    {
        var retentionCutoff = TimeControlCalendar.Today(clock.UtcNow)
            .AddDays(-(WorkforceHistoryPolicy.RetentionDays - 1));
        await db.WorkforceTimeRecords.Where(x => x.OrganizationId == organizationId && x.Source == source &&
            x.WorkDate < retentionCutoff).ExecuteDeleteAsync(cancellationToken);
    }

    private static ExternalWorkforceIdentity CreateIdentity(
        Guid organizationId,
        ExternalWorkforceSource source,
        NormalizedWorkforceIdentity item,
        DateTimeOffset now)
        => new()
        {
            OrganizationId = organizationId,
            Source = source,
            ExternalId = item.ExternalId,
            DisplayName = item.DisplayName,
            Email = item.Email,
            IsActive = item.IsActive,
            FirstSeenAt = now,
            LastSeenAt = now,
            SourceUpdatedAt = item.SourceUpdatedAt
        };

    private static bool UpdateIdentity(
        ExternalWorkforceIdentity identity,
        NormalizedWorkforceIdentity item,
        DateTimeOffset now)
    {
        var changed = identity.DisplayName != item.DisplayName || identity.Email != item.Email ||
            identity.IsActive != item.IsActive || identity.SourceUpdatedAt != item.SourceUpdatedAt;
        identity.DisplayName = item.DisplayName;
        identity.Email = item.Email;
        identity.IsActive = item.IsActive;
        identity.LastSeenAt = now;
        identity.SourceUpdatedAt = item.SourceUpdatedAt;
        return changed;
    }

    private static WorkforceTimeRecord CreateTimeRecord(
        Guid organizationId,
        ExternalWorkforceSource source,
        Guid identityId,
        NormalizedWorkforceTimeRecord item,
        DateTimeOffset now)
        => new()
        {
            OrganizationId = organizationId,
            Source = source,
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

    private static bool UpdateTimeRecord(
        WorkforceTimeRecord record,
        Guid identityId,
        NormalizedWorkforceTimeRecord item,
        DateTimeOffset now)
    {
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
        return changed;
    }
}
