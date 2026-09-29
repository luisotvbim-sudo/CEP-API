using CepApi.Domain;

namespace CepApi.UnitTests;

public sealed class ImportedHistoryTests
{
    private static readonly DateOnly Day = new(2026, 9, 22);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-23T12:00:00-03:00");

    [Theory]
    [InlineData(10800, -3600)]
    [InlineData(7200, 0)]
    [InlineData(3600, 3600)]
    public void Multiple_sessions_are_summed_once_and_difference_preserves_sign(int vrSeconds, long delta)
    {
        var first = Monday("08:00", "09:00");
        var second = Monday("10:00", "11:00");
        var result = TimeAnalysisEngine.SummarizeImportedDay(Day, Now, 30, [first, first, second, Vr(vrSeconds)]);
        Assert.Equal(7200, result.MondaySeconds);
        Assert.Equal(vrSeconds, result.VrSeconds);
        Assert.Equal(delta, result.DeltaSeconds);
    }

    [Fact]
    public void Missing_Monday_is_unknown_even_when_VR_explicitly_reports_zero()
    {
        var result = TimeAnalysisEngine.SummarizeImportedDay(Day, Now, 30, [Vr(0)]);
        Assert.Null(result.MondaySeconds);
        Assert.Equal(0, result.VrSeconds);
        Assert.Null(result.DeltaSeconds);
        Assert.Contains("incomplete", result.Issues);
    }

    [Fact]
    public void Null_duration_does_not_become_zero_or_get_reconstructed_from_timestamps()
    {
        var monday = Monday("08:00", "09:00");
        monday.DurationSeconds = null;
        var result = TimeAnalysisEngine.SummarizeImportedDay(Day, Now, 30, [monday, Vr(3600)]);
        Assert.Null(result.MondaySeconds);
        Assert.Null(result.DeltaSeconds);
    }

    [Fact]
    public void Imported_running_timer_never_advances_to_query_time()
    {
        var monday = Monday("08:00", "09:00");
        monday.EndedAt = null;
        monday.State = "running";
        var result = TimeAnalysisEngine.SummarizeImportedDay(Day, Now, 30, [monday, Vr(3600)]);
        Assert.Null(result.MondaySeconds);
        Assert.Null(result.DeltaSeconds);
        Assert.Contains("running_timer", result.Issues);
    }

    [Fact]
    public void Current_and_future_days_are_partial_and_do_not_estimate_VR_work()
    {
        foreach (var now in new[] { Now.AddDays(-1), Now.AddDays(-2) })
        {
            var result = TimeAnalysisEngine.SummarizeImportedDay(Day, now, 30, [Monday("08:00", "09:00"), Vr(3600)]);
            Assert.True(result.Partial);
            Assert.Null(result.VrSeconds);
            Assert.Null(result.DeltaSeconds);
        }
    }

    private static WorkforceTimeRecord Vr(int seconds) => new()
    {
        Source = ExternalWorkforceSource.VrMais, ExternalKey = "vr", WorkDate = Day,
        State = "reported", DurationSeconds = seconds
    };
    private static WorkforceTimeRecord Monday(string start, string end) => new()
    {
        Source = ExternalWorkforceSource.Monday, ExternalKey = start, WorkDate = Day, State = "closed",
        DurationSeconds = 3600, StartedAt = DateTimeOffset.Parse($"2026-09-22T{start}:00-03:00"),
        EndedAt = DateTimeOffset.Parse($"2026-09-22T{end}:00-03:00")
    };
}
