using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CepApi.IntegrationTests;

public sealed class WorkforceSynchronizationTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Repeated_snapshots_count_only_changes_and_restore_removed_rows_without_affecting_another_organization()
    {
        var owner = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var foreign = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new TestClock();
        var sources = Sources();
        var service = Service(db, clock, sources);
        Task<WorkforceSyncBatch> Sync() => service.SynchronizeAsync(owner.OrganizationId!.Value, owner.Id, true, null, Ct);
        await service.SynchronizeAsync(foreign.OrganizationId!.Value, foreign.Id, true, null, Ct);
        var created = await Sync();
        Assert.All(created.Sources, run => { Assert.Equal(1, run.CreatedCount); Assert.Equal(1, run.TimeRecordCreatedCount); });
        var ids = await db.WorkforceTimeRecords.Where(x => x.OrganizationId == owner.OrganizationId)
            .OrderBy(x => x.Source).Select(x => x.Id).ToArrayAsync(Ct);
        var firstSeen = clock.UtcNow;

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var unchanged = await Sync();
        Assert.All(unchanged.Sources, run =>
        {
            Assert.Equal(0, run.CreatedCount + run.UpdatedCount + run.TimeRecordCreatedCount + run.TimeRecordUpdatedCount);
            Assert.Equal(WorkforceSyncStatus.Succeeded, run.Status);
        });
        var identity = await db.ExternalWorkforceIdentities.FirstAsync(x => x.OrganizationId == owner.OrganizationId, Ct);
        Assert.Equal(firstSeen, identity.FirstSeenAt);
        Assert.Equal(clock.UtcNow, identity.LastSeenAt);

        foreach (var source in sources)
        {
            source.Identities = [new("42", "Changed", "new@example.test", true)];
            source.Title = "Changed activity";
        }
        var changed = await Sync();
        Assert.All(changed.Sources, run => { Assert.Equal(1, run.UpdatedCount); Assert.Equal(1, run.TimeRecordUpdatedCount); });
        foreach (var source in sources) { source.EmitRecords = false; source.TimeComplete = false; }
        var incomplete = await Sync();
        Assert.All(incomplete.Sources, run => { Assert.False(run.CompleteSnapshot); Assert.Equal(0, run.TimeRecordRemovedCount); });
        foreach (var source in sources) source.TimeComplete = true;
        var removed = await Sync();
        Assert.All(removed.Sources, run => Assert.Equal(1, run.TimeRecordRemovedCount));
        foreach (var source in sources) source.EmitRecords = true;
        var restored = await Sync();
        Assert.All(restored.Sources, run => { Assert.Equal(0, run.TimeRecordCreatedCount); Assert.Equal(1, run.TimeRecordUpdatedCount); });
        db.ChangeTracker.Clear();
        var records = await db.WorkforceTimeRecords.Where(x => x.OrganizationId == owner.OrganizationId).OrderBy(x => x.Source).ToArrayAsync(Ct);
        Assert.Equal(ids, records.Select(x => x.Id));
        Assert.All(records, record => { Assert.False(record.IsRemoved); Assert.Equal("Changed activity", record.Title); });
        var foreignRecords = await db.WorkforceTimeRecords.Where(x => x.OrganizationId == foreign.OrganizationId).ToArrayAsync(Ct);
        Assert.Equal(2, foreignRecords.Length);
        Assert.All(foreignRecords, record => { Assert.False(record.IsRemoved); Assert.Equal("Original activity", record.Title); });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task One_source_failure_preserves_the_other_source_and_reports_partial_progress(bool directoryFails)
    {
        var owner = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sources = Sources();
        sources[0].DirectoryFails = directoryFails;
        sources[0].TimeFails = !directoryFails;
        var batch = await Service(db, new TestClock(), sources)
            .SynchronizeAsync(owner.OrganizationId!.Value, owner.Id, true, null, Ct);
        Assert.Equal(WorkforceSyncStatus.PartiallySucceeded, batch.Status);
        var monday = Assert.Single(batch.Sources, x => x.Source == ExternalWorkforceSource.Monday);
        Assert.Equal(directoryFails ? WorkforceSyncStatus.Failed : WorkforceSyncStatus.PartiallySucceeded, monday.Status);
        Assert.Equal(directoryFails ? "test_directory_failure" : "test_time_failure", monday.ErrorCode);
        Assert.Equal(directoryFails ? 0 : 1, monday.ReceivedCount);
        Assert.NotNull(monday.CompletedAt);
        Assert.Equal(WorkforceSyncStatus.Succeeded, Assert.Single(batch.Sources, x => x.Source == ExternalWorkforceSource.VrMais).Status);
        Assert.Equal(1, await db.WorkforceTimeRecords.CountAsync(x => x.OrganizationId == owner.OrganizationId, Ct));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Only_complete_directories_deactivate_missing_identities(bool complete, int expectedDeactivations)
    {
        var owner = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sources = Sources();
        foreach (var source in sources) source.Identities = [new("42", "Kept", null, true), new("84", "Missing later", null, true)];
        var service = Service(db, new TestClock(), sources);
        await service.SynchronizeAsync(owner.OrganizationId!.Value, owner.Id, true, null, Ct);
        foreach (var source in sources) { source.Identities = [new("42", "Kept", null, true)]; source.DirectoryComplete = complete; }
        var batch = await service.SynchronizeAsync(owner.OrganizationId.Value, owner.Id, true, null, Ct);
        Assert.All(batch.Sources, run => Assert.Equal(expectedDeactivations, run.DeactivatedCount));
        var missing = await db.ExternalWorkforceIdentities.Where(x => x.OrganizationId == owner.OrganizationId && x.ExternalId == "84").ToArrayAsync(Ct);
        Assert.All(missing, identity => Assert.Equal(!complete, identity.IsActive));
    }

    [Fact]
    public async Task Active_batch_blocks_a_second_run_and_stale_batch_is_finalized_before_retry()
    {
        var owner = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new TestClock();
        var batch = new WorkforceSyncBatch
        {
            OrganizationId = owner.OrganizationId!.Value,
            RequestedByUserId = owner.Id,
            StartedAt = clock.UtcNow,
            Sources = [new() { OrganizationId = owner.OrganizationId.Value, Source = ExternalWorkforceSource.Monday, StartedAt = clock.UtcNow }]
        };
        db.Add(batch);
        await db.SaveChangesAsync(Ct);
        var service = Service(db, clock, Sources());
        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() =>
            service.SynchronizeAsync(owner.OrganizationId.Value, owner.Id, true, null, Ct));
        Assert.Equal("sync_already_running", exception.Code);
        Assert.False(await db.ExternalWorkforceIdentities.AnyAsync(x => x.OrganizationId == owner.OrganizationId, Ct));
        clock.UtcNow = clock.UtcNow.AddHours(3);
        var retried = await service.SynchronizeAsync(owner.OrganizationId.Value, owner.Id, true, null, Ct);
        Assert.Equal(WorkforceSyncStatus.Succeeded, retried.Status);
        Assert.Equal(WorkforceSyncStatus.Failed, batch.Status);
        Assert.Equal("sync_interrupted", Assert.Single(batch.Sources).ErrorCode);
        Assert.Equal(clock.UtcNow, batch.CompletedAt);
    }

    private static WorkforceDirectorySyncService Service(AppDbContext db, IClock clock, TestSource[] sources)
        => new(db, sources, sources, new WorkforceSnapshotWriter(db, clock), clock);

    [Fact]
    public async Task Normal_admin_retry_bootstraps_a_directory_missing_after_a_partial_first_run()
    {
        var owner = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new TestClock();
        var sources = Sources();
        sources[1].DirectoryFails = true;
        var service = Service(db, clock, sources);
        var partial = await service.SynchronizeAsync(owner.OrganizationId!.Value, owner.Id, false, null, Ct);
        Assert.Equal(WorkforceSyncStatus.PartiallySucceeded, partial.Status);
        Assert.Equal(1, await db.ExternalWorkforceIdentities.CountAsync(x => x.OrganizationId == owner.OrganizationId, Ct));
        sources[1].DirectoryFails = false;
        var retried = await service.SynchronizeAsync(owner.OrganizationId.Value, owner.Id, false, null, Ct);
        Assert.Equal(WorkforceSyncStatus.Succeeded, retried.Status);
        Assert.Equal(2, await db.ExternalWorkforceIdentities.CountAsync(x => x.OrganizationId == owner.OrganizationId, Ct));
        Assert.All(retried.Sources, run => Assert.Equal(1, run.ReceivedCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_source_deadline_finishes_the_batch_without_discarding_the_other_source(bool directoryWaits)
    {
        var owner = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new TestClock();
        var sources = Sources();
        await Service(db, clock, sources).SynchronizeAsync(owner.OrganizationId!.Value, owner.Id, true, null, Ct);
        if (directoryWaits) sources[0].WaitForDirectory = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
        else sources[0].WaitForTime = token => Task.Delay(Timeout.InfiniteTimeSpan, token);
        var service = new WorkforceDirectorySyncService(db, sources, sources, new WorkforceSnapshotWriter(db, clock), clock,
            Options.Create(new WorkforceIntegrationOptions { SynchronizationSourceTimeoutSeconds = 1 }));
        var result = await service.SynchronizeAsync(owner.OrganizationId.Value, owner.Id, true, null, Ct)
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(WorkforceSyncStatus.PartiallySucceeded, result.Status);
        Assert.Equal("source_sync_timeout", Assert.Single(result.Sources,
            source => source.Source == ExternalWorkforceSource.Monday).ErrorCode);
        Assert.Equal(WorkforceSyncStatus.Succeeded, Assert.Single(result.Sources,
            source => source.Source == ExternalWorkforceSource.VrMais).Status);
        Assert.Equal(2, await db.WorkforceTimeRecords.CountAsync(x => x.OrganizationId == owner.OrganizationId, Ct));
    }

    [Fact]
    public async Task Vr_directory_and_records_are_persisted_while_monday_is_waiting()
    {
        var owner = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sources = Sources();
        var mondayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMonday = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sources[0].WaitForTime = async token =>
        {
            mondayStarted.TrySetResult();
            await releaseMonday.Task.WaitAsync(token);
        };
        var synchronization = Service(db, new TestClock(), sources)
            .SynchronizeAsync(owner.OrganizationId!.Value, owner.Id, true, null, Ct);
        try
        {
            await mondayStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            await using var check = fixture.Factory.Services.CreateAsyncScope();
            var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            while (!await checkDb.WorkforceSyncSourceRuns.AsNoTracking().AnyAsync(x =>
                x.OrganizationId == owner.OrganizationId && x.Source == ExternalWorkforceSource.VrMais &&
                x.Status == WorkforceSyncStatus.Succeeded, deadline.Token))
                await Task.Delay(20, deadline.Token);
            Assert.Equal(1, await checkDb.WorkforceTimeRecords.CountAsync(x =>
                x.OrganizationId == owner.OrganizationId && x.Source == ExternalWorkforceSource.VrMais, Ct));
        }
        finally
        {
            releaseMonday.TrySetResult();
            await synchronization;
        }
    }

    [Fact]
    public async Task Cancelled_request_finalizes_running_sources_and_allows_an_immediate_retry()
    {
        var owner = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sources = Sources();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sources[0].WaitForTime = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var synchronization = Service(db, new TestClock(), sources)
            .SynchronizeAsync(owner.OrganizationId!.Value, owner.Id, true, null, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => synchronization);
        await using var check = fixture.Factory.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await checkDb.WorkforceSyncBatches.AsNoTracking().Include(x => x.Sources)
            .SingleAsync(x => x.OrganizationId == owner.OrganizationId, Ct);
        Assert.NotEqual(WorkforceSyncStatus.Running, batch.Status);
        Assert.NotNull(batch.CompletedAt);
        Assert.All(batch.Sources, source => Assert.NotEqual(WorkforceSyncStatus.Running, source.Status));
        Assert.Equal("sync_cancelled", Assert.Single(batch.Sources, x => x.Source == ExternalWorkforceSource.Monday).ErrorCode);
        sources[0].WaitForTime = null;
        var retry = await Service(db, new TestClock(), sources)
            .SynchronizeAsync(owner.OrganizationId.Value, owner.Id, true, null, Ct);
        Assert.Equal(WorkforceSyncStatus.Succeeded, retry.Status);
    }

    private static TestSource[] Sources() => [new(ExternalWorkforceSource.Monday), new(ExternalWorkforceSource.VrMais)];

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class TestSource(ExternalWorkforceSource source) : IExternalWorkforceDirectorySource, IExternalWorkforceTimeSource
    {
        public ExternalWorkforceSource Source => source;
        public ExternalWorkforceIdentitySnapshot[] Identities { get; set; } = [new("42", "Original", null, true)];
        public bool DirectoryComplete { get; set; } = true;
        public bool TimeComplete { get; set; } = true;
        public bool DirectoryFails { get; set; }
        public bool TimeFails { get; set; }
        public bool EmitRecords { get; set; } = true;
        public string Title { get; set; } = "Original activity";
        public Func<CancellationToken, Task>? WaitForTime { get; set; }
        public Func<CancellationToken, Task>? WaitForDirectory { get; set; }

        public async Task<ExternalWorkforceDirectorySnapshot> FetchAsync(CancellationToken cancellationToken)
        {
            if (WaitForDirectory is not null) await WaitForDirectory(cancellationToken);
            if (DirectoryFails) throw new ExternalDirectoryException("test_directory_failure", "Directory failed");
            return new ExternalWorkforceDirectorySnapshot(Identities, DirectoryComplete);
        }

        public async Task<ExternalWorkforceTimeSnapshot> FetchAsync(DateOnly from, DateOnly to, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
        {
            if (WaitForTime is not null) await WaitForTime(cancellationToken);
            if (TimeFails) throw new ExternalDirectoryException("test_time_failure", "Time source failed");
            return new ExternalWorkforceTimeSnapshot(from, to, EmitRecords
                    ? ids.Select(id => new ExternalWorkforceTimeRecordSnapshot(id, $"record:{id}", to,
                        TimeAnalysisEngine.StartOfDay(to).AddHours(9), TimeAnalysisEngine.StartOfDay(to).AddHours(10),
                        3600, "closed", Title, null, null)).ToArray() : [], TimeComplete);
        }
    }
}
