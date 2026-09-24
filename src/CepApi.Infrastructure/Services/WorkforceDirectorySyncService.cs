using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Services;

internal sealed class WorkforceDirectorySyncService(
    AppDbContext db,
    IEnumerable<IExternalWorkforceDirectorySource> directorySources,
    IEnumerable<IExternalWorkforceTimeSource> timeSources,
    WorkforceSnapshotWriter snapshotWriter,
    WorkforceSyncPeriodResolver periodResolver,
    IClock clock) : IWorkforceDirectorySyncService
{
    private readonly IReadOnlyDictionary<ExternalWorkforceSource, IExternalWorkforceDirectorySource> directorySourcesByType =
        directorySources.ToDictionary(x => x.Source);
    private readonly IReadOnlyDictionary<ExternalWorkforceSource, IExternalWorkforceTimeSource> timeSourcesByType =
        timeSources.ToDictionary(x => x.Source);

    public async Task<WorkforceSyncBatch> SynchronizeAsync(
        Guid organizationId,
        Guid requestedByUserId,
        bool fullRefresh,
        IReadOnlyCollection<Guid>? visibleUserIds,
        CancellationToken cancellationToken)
    {
        var bootstrap = !fullRefresh && visibleUserIds is null &&
            !await db.ExternalWorkforceIdentities.AsNoTracking()
                .AnyAsync(x => x.OrganizationId == organizationId, cancellationToken);
        var refreshDirectory = fullRefresh || bootstrap;
        var targetIds = refreshDirectory
            ? null
            : await LoadTargetExternalIdsAsync(organizationId, visibleUserIds, cancellationToken);
        if (targetIds is not null && targetIds.Values.Any(ids => ids.Length == 0))
            throw new ExternalDirectoryException("sync_scope_empty",
                "Associated active workforce identities from both sources are required for this synchronization.");

        await CompleteInterruptedBatchesAsync(organizationId, cancellationToken);
        var batch = await StartBatchAsync(organizationId, requestedByUserId, cancellationToken);

        var fetchTasks = refreshDirectory
            ? batch.Sources.ToDictionary(run => run.Source,
                run => FetchDirectoryAsync(run.Source, cancellationToken))
            : null;
        if (fetchTasks is not null)
            await Task.WhenAll(fetchTasks.Values);

        foreach (var run in batch.Sources.OrderBy(x => x.Source))
        {
            await SynchronizeSourceAsync(
                organizationId,
                run,
                fetchTasks is null ? null : await fetchTasks[run.Source],
                targetIds is null ? null : targetIds[run.Source],
                refreshDirectory,
                cancellationToken);
        }

        batch.Status = CalculateBatchStatus(batch.Sources);
        batch.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return batch;
    }

    private async Task CompleteInterruptedBatchesAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var staleBefore = clock.UtcNow.AddHours(-2);
        var staleBatches = await db.WorkforceSyncBatches.Include(x => x.Sources).Where(x =>
            x.OrganizationId == organizationId && x.Status == WorkforceSyncStatus.Running &&
            x.StartedAt < staleBefore).ToListAsync(cancellationToken);

        foreach (var batch in staleBatches)
        {
            batch.Status = WorkforceSyncStatus.Failed;
            batch.CompletedAt = clock.UtcNow;
            foreach (var source in batch.Sources.Where(x => x.Status == WorkforceSyncStatus.Running))
            {
                source.Status = WorkforceSyncStatus.Failed;
                source.ErrorCode = "sync_interrupted";
                source.ErrorMessage = "The synchronization was interrupted before completion.";
                source.CompletedAt = clock.UtcNow;
            }
        }

        if (staleBatches.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        if (await HasRunningBatchAsync(organizationId, cancellationToken))
            throw AlreadyRunning();
    }

    private async Task<WorkforceSyncBatch> StartBatchAsync(
        Guid organizationId,
        Guid requestedByUserId,
        CancellationToken cancellationToken)
    {
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
            return batch;
        }
        catch (DbUpdateException exception)
        {
            if (await HasRunningBatchAsync(organizationId, cancellationToken))
                throw AlreadyRunning(exception);
            throw;
        }
    }

    private async Task SynchronizeSourceAsync(
        Guid organizationId,
        WorkforceSyncSourceRun run,
        DirectoryFetchOutcome? directoryOutcome,
        string[]? targetedExternalIds,
        bool fullRefresh,
        CancellationToken cancellationToken)
    {
        if (directoryOutcome?.Error is { } directoryError)
        {
            MarkFailed(run, directoryError.Code, directoryError.Message);
        }
        else
        {
            try
            {
                var activeExternalIds = targetedExternalIds;
                if (directoryOutcome is not null)
                {
                    var snapshot = directoryOutcome.Snapshot!;
                    await snapshotWriter.ApplyDirectoryAsync(organizationId, run, snapshot, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    activeExternalIds = snapshot.Identities.Where(x => x.IsActive)
                        .Select(x => x.ExternalId.Trim()).Distinct(StringComparer.Ordinal).ToArray();
                }
                else
                {
                    run.CompleteSnapshot = true;
                }

                if (!timeSourcesByType.TryGetValue(run.Source, out var timeSource))
                    throw new ExternalDirectoryException(
                        "time_source_not_registered",
                        $"{run.Source} time integration is not registered.");

                var period = periodResolver.Resolve(fullRefresh);
                var timeSnapshot = await timeSource.FetchAsync(
                    period.From, period.To, activeExternalIds!, cancellationToken);
                await snapshotWriter.ApplyTimeAsync(
                    organizationId, run, timeSnapshot, activeExternalIds!, cancellationToken);
                run.Status = WorkforceSyncStatus.Succeeded;
            }
            catch (ExternalDirectoryException exception)
            {
                MarkFailed(run, exception.Code, exception.Message, partiallySucceeded: run.ReceivedCount > 0);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                MarkFailed(run, "time_sync_failed", $"{run.Source} time synchronization failed.",
                    partiallySucceeded: run.ReceivedCount > 0);
            }
        }

        run.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<ExternalWorkforceSource, string[]>> LoadTargetExternalIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid>? visibleUserIds,
        CancellationToken cancellationToken)
    {
        var people = db.WorkforcePeople.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (visibleUserIds is not null)
        {
            var userIds = visibleUserIds.ToArray();
            people = people.Where(x => x.UserId != null && userIds.Contains(x.UserId.Value));
        }

        var identityIds = await people.Select(x => new { x.MondayIdentityId, x.VrMaisIdentityId })
            .ToArrayAsync(cancellationToken);
        var mondayIds = identityIds.Select(x => x.MondayIdentityId).ToArray();
        var vrMaisIds = identityIds.Select(x => x.VrMaisIdentityId).ToArray();
        var activeIdentities = await db.ExternalWorkforceIdentities.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive &&
                (x.Source == ExternalWorkforceSource.Monday && mondayIds.Contains(x.Id) ||
                 x.Source == ExternalWorkforceSource.VrMais && vrMaisIds.Contains(x.Id)))
            .Select(x => new { x.Source, x.ExternalId })
            .ToArrayAsync(cancellationToken);

        return Enum.GetValues<ExternalWorkforceSource>().ToDictionary(
            source => source,
            source => activeIdentities.Where(x => x.Source == source).Select(x => x.ExternalId)
                .Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<DirectoryFetchOutcome> FetchDirectoryAsync(
        ExternalWorkforceSource source,
        CancellationToken cancellationToken)
    {
        if (!directorySourcesByType.TryGetValue(source, out var provider))
            return DirectoryFetchOutcome.Failure(new ExternalDirectoryException(
                "source_not_registered", $"{source} integration is not registered."));

        try
        {
            return DirectoryFetchOutcome.Success(await provider.FetchAsync(cancellationToken));
        }
        catch (ExternalDirectoryException exception)
        {
            return DirectoryFetchOutcome.Failure(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return DirectoryFetchOutcome.Failure(new ExternalDirectoryException(
                "source_sync_failed", $"{source} synchronization failed.", exception));
        }
    }

    private Task<bool> HasRunningBatchAsync(Guid organizationId, CancellationToken cancellationToken)
        => db.WorkforceSyncBatches.AsNoTracking().AnyAsync(
            x => x.OrganizationId == organizationId && x.Status == WorkforceSyncStatus.Running,
            cancellationToken);

    private static void MarkFailed(
        WorkforceSyncSourceRun run,
        string code,
        string message,
        bool partiallySucceeded = false)
    {
        run.Status = partiallySucceeded ? WorkforceSyncStatus.PartiallySucceeded : WorkforceSyncStatus.Failed;
        run.ErrorCode = code;
        run.ErrorMessage = Truncate(message, 500);
    }

    private static WorkforceSyncStatus CalculateBatchStatus(ICollection<WorkforceSyncSourceRun> sources)
    {
        var successful = sources.Count(x => x.Status == WorkforceSyncStatus.Succeeded);
        if (successful == sources.Count) return WorkforceSyncStatus.Succeeded;

        var useful = sources.Any(x =>
            x.Status is WorkforceSyncStatus.Succeeded or WorkforceSyncStatus.PartiallySucceeded);
        return useful ? WorkforceSyncStatus.PartiallySucceeded : WorkforceSyncStatus.Failed;
    }

    private static ExternalDirectoryException AlreadyRunning(Exception? innerException = null)
        => new("sync_already_running", "A workforce synchronization is already running.", innerException);

    private static string Truncate(string value, int maxLength)
        => value[..Math.Min(value.Length, maxLength)];

    private sealed record DirectoryFetchOutcome(
        ExternalWorkforceDirectorySnapshot? Snapshot,
        ExternalDirectoryException? Error)
    {
        public static DirectoryFetchOutcome Success(ExternalWorkforceDirectorySnapshot snapshot)
            => new(snapshot, null);

        public static DirectoryFetchOutcome Failure(ExternalDirectoryException error)
            => new(null, error);
    }
}
