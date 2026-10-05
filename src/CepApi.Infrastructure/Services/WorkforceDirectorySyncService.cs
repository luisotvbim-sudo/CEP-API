using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CepApi.Infrastructure.Services;

internal sealed class WorkforceDirectorySyncService(
    AppDbContext db,
    IEnumerable<IExternalWorkforceDirectorySource> directorySources,
    IEnumerable<IExternalWorkforceTimeSource> timeSources,
    WorkforceSnapshotWriter snapshotWriter,
    IClock clock,
    IOptions<WorkforceIntegrationOptions>? options = null,
    ILogger<WorkforceDirectorySyncService>? logger = null) : IWorkforceDirectorySyncService
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
            await db.ExternalWorkforceIdentities.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId).Select(x => x.Source).Distinct()
                .CountAsync(cancellationToken) < Enum.GetValues<ExternalWorkforceSource>().Length;
        var refreshDirectory = fullRefresh || bootstrap;
        var targetIds = refreshDirectory
            ? null
            : await LoadTargetExternalIdsAsync(organizationId, visibleUserIds, cancellationToken);
        if (targetIds is not null && targetIds.Values.Any(ids => ids.Length == 0))
            throw new ExternalDirectoryException("sync_scope_empty",
                "Associated active workforce identities from both sources are required for this synchronization.");

        await CompleteInterruptedBatchesAsync(organizationId, cancellationToken);
        var batch = await StartBatchAsync(organizationId, requestedByUserId, cancellationToken);

        var period = WorkforceHistoryPolicy.SyncPeriod(clock.UtcNow, refreshDirectory);
        using var fetchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var budgets = batch.Sources.ToDictionary(run => run.Source, _ =>
        {
            var budget = CancellationTokenSource.CreateLinkedTokenSource(fetchCancellation.Token);
            budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(
                options?.Value.SynchronizationSourceTimeoutSeconds ?? 120, 1, 120)));
            return budget;
        });
        var pending = new Dictionary<WorkforceSyncSourceRun, Task<SourceFetchOutcome>>();
        var allFetches = new List<Task<SourceFetchOutcome>>();
        var idsBySource = new Dictionary<ExternalWorkforceSource, string[]>();
        try
        {
            foreach (var run in batch.Sources.OrderBy(x => x.Source))
            {
                if (!refreshDirectory)
                {
                    idsBySource[run.Source] = targetIds![run.Source];
                    run.CompleteSnapshot = true;
                }
                StartFetch(run, refreshDirectory);
            }
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending.Values);
                var run = pending.First(item => item.Value == completed).Key;
                pending.Remove(run);
                var outcome = await completed;
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (outcome.Error is { } error) throw error;
                    if (outcome.Directory is { } directory)
                    {
                        // Only the coordinator accesses this scoped DbContext; network fetches run independently.
                        await snapshotWriter.ApplyDirectoryAsync(organizationId, run, directory, cancellationToken);
                        await db.SaveChangesAsync(cancellationToken);
                        idsBySource[run.Source] = directory.Identities.Where(x => x.IsActive)
                            .Select(x => x.ExternalId.Trim()).Distinct(StringComparer.Ordinal).ToArray();
                        StartFetch(run, directory: false);
                        continue;
                    }
                    await snapshotWriter.ApplyTimeAsync(organizationId, run, outcome.Time!,
                        idsBySource[run.Source], cancellationToken);
                    run.Status = WorkforceSyncStatus.Succeeded;
                }
                catch (ExternalDirectoryException error)
                {
                    MarkFailed(run, error.Code, error.Message, run.ReceivedCount > 0);
                    logger?.LogWarning("Workforce source {Source} failed with {Code}", run.Source, error.Code);
                }
                run.CompletedAt = clock.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            batch.Status = CalculateBatchStatus(batch.Sources);
            batch.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return batch;
        }
        catch
        {
            await fetchCancellation.CancelAsync();
            try { await Task.WhenAll(allFetches); }
            catch (Exception) { /* Observe every fetch before disposing its budget. */ }
            await FinalizeInterruptedBatchAsync(batch.Id, cancellationToken.IsCancellationRequested
                ? "sync_cancelled" : "sync_interrupted");
            throw;
        }
        finally
        {
            foreach (var budget in budgets.Values) budget.Dispose();
        }

        void StartFetch(WorkforceSyncSourceRun run, bool directory)
        {
            var task = FetchSourceAsync(run.Source, directory, period.From, period.To,
                directory ? [] : idsBySource[run.Source], budgets[run.Source].Token, fetchCancellation.Token);
            pending.Add(run, task);
            allFetches.Add(task);
        }
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

    private async Task<SourceFetchOutcome> FetchSourceAsync(
        ExternalWorkforceSource source,
        bool directory,
        DateOnly from,
        DateOnly to,
        string[] activeExternalIds,
        CancellationToken sourceToken,
        CancellationToken requestToken)
    {
        try
        {
            if (directory)
            {
                if (!directorySourcesByType.TryGetValue(source, out var provider))
                    throw new ExternalDirectoryException("source_not_registered", $"{source} integration is not registered.");
                return new(await provider.FetchAsync(sourceToken), null, null);
            }
            if (!timeSourcesByType.TryGetValue(source, out var timeSource))
                throw new ExternalDirectoryException("time_source_not_registered", $"{source} time integration is not registered.");
            return new(null, await timeSource.FetchAsync(from, to, activeExternalIds, sourceToken), null);
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested && sourceToken.IsCancellationRequested)
        {
            return new(null, null, new ExternalDirectoryException("source_sync_timeout",
                $"{source} synchronization exceeded its time limit. Previously imported records were preserved."));
        }
        catch (ExternalDirectoryException exception)
        {
            return new(null, null, exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(null, null, new ExternalDirectoryException(directory ? "source_sync_failed" : "time_sync_failed",
                $"{source} synchronization failed.", exception));
        }
    }

    private async Task FinalizeInterruptedBatchAsync(Guid batchId, string code)
    {
        // Discard changes that were not committed. The aborted HTTP token must not prevent cleanup.
        db.ChangeTracker.Clear();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var stored = await db.WorkforceSyncBatches.Include(x => x.Sources)
                .SingleAsync(x => x.Id == batchId, cleanup.Token);
            foreach (var run in stored.Sources.Where(x => x.Status == WorkforceSyncStatus.Running))
            {
                MarkFailed(run, code, "The synchronization was interrupted before completion.", run.ReceivedCount > 0);
                run.CompletedAt = clock.UtcNow;
            }
            stored.Status = CalculateBatchStatus(stored.Sources);
            stored.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(cleanup.Token);
        }
        catch (Exception)
        {
            logger?.LogError("Workforce synchronization cleanup failed; interrupted-run recovery remains necessary");
        }
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

        var mondayIds = people.Select(x => x.MondayIdentityId);
        var vrMaisIds = people.Select(x => x.VrMaisIdentityId);
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

    private sealed record SourceFetchOutcome(
        ExternalWorkforceDirectorySnapshot? Directory,
        ExternalWorkforceTimeSnapshot? Time,
        ExternalDirectoryException? Error);
}
