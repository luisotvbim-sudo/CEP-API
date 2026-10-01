using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Services;

namespace CepApi.UnitTests;

public sealed class PowerActionTests
{
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-09-30T15:00:00Z");
    private static readonly Guid Version = Guid.NewGuid();

    [Theory]
    [InlineData(-1801, "blocked")]
    [InlineData(-1800, "allowed")]
    [InlineData(0, "allowed")]
    [InlineData(1800, "allowed")]
    [InlineData(1801, "blocked")]
    public async Task Uses_daily_symmetric_tolerance_and_a_single_cutoff(int delta, string expected)
    {
        var person = Person();
        var monday = new FakeTimeSource(ExternalWorkforceSource.Monday, delta);
        var vr = new FakeTimeSource(ExternalWorkforceSource.VrMais, delta);
        var service = new FreshTimeAnalysisService(new Clock(), [monday, vr]);
        var window = TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.Daily, Cutoff);
        var (snapshots, states) = await service.FetchAsync([person], window, TestContext.Current.CancellationToken);
        var result = service.Analyze(person, window, 30, Version, snapshots, states);
        foreach (var action in new[] { "shutdown", "restart", "hibernate" })
        {
            var decision = PowerActionDecision.FromAnalysis(action, result);
            Assert.Equal(expected, decision.Decision);
            Assert.Equal(action, decision.Action);
        }
        Assert.Equal(delta, result.DeltaSeconds);
        Assert.Equal(Cutoff, result.Cutoff);
        Assert.Equal(Version, result.SettingsVersion);
        Assert.True(Assert.Single(result.Days).Partial);
        Assert.Equal(new[] { "own-monday" }, monday.RequestedIds);
        Assert.Equal(new[] { "own-vr" }, vr.RequestedIds);
    }

    [Theory]
    [InlineData("missing_adapter")]
    [InlineData("unavailable")]
    [InlineData("partial")]
    [InlineData("coverage")]
    [InlineData("missing_vr")]
    [InlineData("bad_punches")]
    public async Task Unknown_data_never_allows_an_action(string failure)
    {
        var person = Person();
        var sources = new List<IExternalWorkforceTimeSource> { new FakeTimeSource(ExternalWorkforceSource.Monday, 0) };
        if (failure != "missing_adapter") sources.Add(new FakeTimeSource(ExternalWorkforceSource.VrMais, 0, failure));
        var service = new FreshTimeAnalysisService(new Clock(), sources);
        var window = TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.Daily, Cutoff);
        var (snapshots, states) = await service.FetchAsync([person], window, TestContext.Current.CancellationToken);
        var result = service.Analyze(person, window, 30, Version, snapshots, states);
        Assert.Equal("indeterminate", PowerActionDecision.FromAnalysis("shutdown", result).Decision);
        Assert.Null(result.DeltaSeconds);
        Assert.Equal(2, result.Sources.Count);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_to_a_business_decision()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new FreshTimeAnalysisService(new Clock(), [new FakeTimeSource(ExternalWorkforceSource.Monday, 0)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FetchAsync([Person()],
            TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.Daily, Cutoff), cancellation.Token));
    }

    private static WorkforcePerson Person() => new()
    {
        OrganizationId = Guid.NewGuid(), DisplayName = "Test", Email = "test@example.test",
        MondayIdentity = new() { Id = Guid.NewGuid(), ExternalId = "own-monday", DisplayName = "Monday", IsActive = true },
        VrMaisIdentity = new() { Id = Guid.NewGuid(), ExternalId = "own-vr", DisplayName = "VR", IsActive = true }
    };

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Cutoff; }
    private sealed class FakeTimeSource(ExternalWorkforceSource source, int delta, string? failure = null) : IExternalWorkforceTimeSource
    {
        public ExternalWorkforceSource Source => source;
        public string[] RequestedIds { get; private set; } = [];
        public Task<ExternalWorkforceTimeSnapshot> FetchAsync(DateOnly from, DateOnly to, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedIds = ids.ToArray();
            if (failure == "unavailable") throw new ExternalDirectoryException("source_unavailable", "test");
            var start = TimeAnalysisEngine.StartOfDay(from).AddHours(8);
            var rows = new List<ExternalWorkforceTimeRecordSnapshot>();
            if (failure != "missing_vr")
                rows.Add(source == ExternalWorkforceSource.Monday
                    ? new(ids.Single(), "session", from, start, Cutoff.AddSeconds(delta), 0, "closed", null, null, null)
                    : new(ids.Single(), "vr-day", from, null, null, 0, "reported", null, null,
                        failure == "bad_punches" ? "{\"timeCards\":[\"bad\"]}" : "{\"timeCards\":[\"08:00\"]}"));
            // Positive delta uses an earlier VR entry rather than future Monday time.
            if (delta > 0)
            {
                rows.Clear();
                rows.Add(source == ExternalWorkforceSource.Monday
                    ? new(ids.Single(), "session", from, start.AddSeconds(-delta), null, 0, "running", null, null, null)
                    : new(ids.Single(), "vr-day", from, null, null, 0, "reported", null, null, "{\"timeCards\":[\"08:00\"]}"));
            }
            // Foreign identity rows from a source must never enter the person's analysis.
            rows.Add(new("foreign", "foreign", from, start, Cutoff, 99999, "closed", null, null, null));
            return Task.FromResult(new ExternalWorkforceTimeSnapshot(failure == "coverage" ? from.AddDays(1) : from, to, rows, failure != "partial"));
        }
    }
}
