using CepApi.Domain;

namespace CepApi.UnitTests;

public sealed class TimeAnalysisTests
{
    [Theory]
    [InlineData("2026-02-14T15:00:00Z", "2026-02-01", "2026-02-14")]
    [InlineData("2026-02-15T15:00:00Z", "2026-02-15", "2026-02-15")]
    [InlineData("2028-02-29T15:00:00Z", "2028-02-15", "2028-02-29")]
    [InlineData("2026-09-22T15:00:00Z", "2026-09-15", "2026-09-22")]
    public void Sprint_uses_nonoverlapping_calendar_halves(string instant, string from, string to)
    {
        var result = TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.Sprint, DateTimeOffset.Parse(instant));
        Assert.Equal(DateOnly.Parse(from), result.From);
        Assert.Equal(DateOnly.Parse(to), result.To);
    }

    [Fact]
    public void Periods_use_Sao_Paulo_and_week_can_cross_month_and_year()
    {
        var cutoff = DateTimeOffset.Parse("2026-01-01T02:00:00Z");
        var daily = TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.Daily, cutoff);
        Assert.Equal(new DateOnly(2025, 12, 31), daily.From);
        Assert.Equal(new DateOnly(2025, 12, 29), TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.Weekly, cutoff).From);
        Assert.Equal(new DateOnly(2025, 12, 30), TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.PreviousDay, cutoff).From);
    }

    [Fact]
    public void Open_punch_and_timer_use_identical_cutoff_during_day()
    {
        var vr = Vr("[\"08:00\"]", 0);
        var monday = Monday("08:00", null);
        var result = Run([vr, monday], "2026-09-22T11:50:00-03:00");
        var day = Assert.Single(result.Days);
        Assert.Equal(13800L, day.VrSeconds);
        Assert.Equal(day.VrSeconds, day.MondaySeconds);
        Assert.Equal(0L, day.DeltaSeconds);
        Assert.True(day.Partial);
        Assert.Empty(day.Issues);
    }

    [Fact]
    public void Past_odd_punch_and_running_timer_are_errors_not_estimated_difference()
    {
        var result = Run([Vr("[\"08:00\"]", 0), Monday("08:00", null)], "2026-09-23T10:00:00-03:00");
        var day = Assert.Single(result.Days);
        Assert.Contains("odd_punches", day.Issues);
        Assert.Contains("running_timer", day.Issues);
        Assert.Null(day.DeltaSeconds);
        Assert.Null(result.DeltaSeconds);
    }

    [Theory]
    [InlineData(1800, false)]
    [InlineData(1801, true)]
    [InlineData(-1800, false)]
    [InlineData(-1801, true)]
    public void Tolerance_is_symmetric_and_boundary_is_inclusive(int delta, bool exceeded)
    {
        var result = Run([Vr("[\"08:00\",\"09:00\"]", 3600 - delta), Monday("08:00", "09:00")]);
        Assert.Equal(exceeded, result.Days[0].Issues.Contains("above_tolerance"));
        Assert.Equal((long)delta, result.DeltaSeconds);
    }

    [Fact]
    public void Missing_source_is_not_zero_or_definitive_success()
    {
        var result = Run([Vr("[]", 0)], complete: false);
        Assert.Null(result.VrSeconds);
        Assert.Null(result.MondaySeconds);
        Assert.Null(result.DeltaSeconds);
        Assert.Contains("incomplete", result.Days[0].Issues);
    }

    [Fact]
    public void Retries_deduplicate_sessions_and_clip_across_midnight()
    {
        var session = Monday("00:00", "01:00");
        session.StartedAt = DateTimeOffset.Parse("2026-09-21T23:00:00-03:00");
        var result = Run([Vr("[\"00:00\",\"01:00\"]", 3600), session, session]);
        Assert.Equal(3600L, result.MondaySeconds);
        Assert.Null(result.DeltaSeconds);
        Assert.Contains("running_timer", result.Days[0].Issues);
    }

    [Fact]
    public void Clock_closed_after_midnight_is_reported_even_if_no_longer_running()
    {
        var row = Monday("08:00", "09:00");
        row.EndedAt = DateTimeOffset.Parse("2026-09-23T08:00:00-03:00");
        var result = Run([Vr("[\"08:00\",\"17:00\"]", 32400), row]);
        Assert.Contains("running_timer", result.Days[0].Issues);
        Assert.Null(result.DeltaSeconds);
    }

    [Fact]
    public void Absolute_divergence_does_not_cancel_opposite_daily_differences()
    {
        var firstVr = Vr("[\"08:00\",\"09:00\"]", 1800);
        var secondVr = Vr("[\"08:00\",\"09:00\"]", 5400);
        secondVr.ExternalKey = "secondVr";
        secondVr.WorkDate = new DateOnly(2026, 9, 23);
        var firstMonday = Monday("08:00", "09:00");
        var secondMonday = Monday("08:00", "09:00");
        secondMonday.ExternalKey = "secondMonday";
        secondMonday.WorkDate = new DateOnly(2026, 9, 23);
        secondMonday.StartedAt = secondMonday.StartedAt!.Value.AddDays(1);
        secondMonday.EndedAt = secondMonday.EndedAt!.Value.AddDays(1);
        var result = TimeAnalysisEngine.Analyze(new(2026, 9, 22), new(2026, 9, 23),
            DateTimeOffset.Parse("2026-09-24T10:00:00-03:00"), 30,
            [firstVr, secondVr, firstMonday, secondMonday], true);
        Assert.Equal(0L, result.DeltaSeconds);
        Assert.Equal(3600L, result.AbsoluteDivergenceSeconds);
    }

    [Fact]
    public void Invalid_punches_do_not_produce_healthy_analysis()
    {
        var result = Run([Vr("[\"bad\"]", 0)]);
        Assert.Null(result.DeltaSeconds);
        Assert.Contains("incomplete", result.Days[0].Issues);
    }

    [Fact]
    public void Current_day_uses_punches_even_if_official_total_is_not_ready()
    {
        var row = Vr("[\"08:00\",\"09:00\",\"10:00\"]", 0);
        row.State = "unrecognized";
        row.DurationSeconds = null;
        var result = Run([row], "2026-09-22T11:00:00-03:00");
        Assert.Equal(7200L, result.VrSeconds);
    }

    [Fact]
    public void Timer_left_open_yesterday_is_still_a_problem_in_current_day()
    {
        var session = Monday("08:00", null);
        session.StartedAt = DateTimeOffset.Parse("2026-09-21T08:00:00-03:00");
        var result = Run([Vr("[\"08:00\"]", 0), session], "2026-09-22T11:00:00-03:00");
        Assert.Contains("running_timer", result.Days[0].Issues);
        Assert.Null(result.DeltaSeconds);
    }

    [Fact]
    public void Odd_punches_are_reported_even_when_VR_total_is_unrecognized()
    {
        var row = Vr("[\"08:00\"]", 0);
        row.State = "unrecognized";
        row.DurationSeconds = null;
        var result = Run([row]);
        Assert.Contains("odd_punches", result.Days[0].Issues);
        Assert.Null(result.DeltaSeconds);
    }

    private static TimeAnalysisResult Run(WorkforceTimeRecord[] records,
        string cutoff = "2026-09-23T10:00:00-03:00", bool complete = true) =>
        TimeAnalysisEngine.Analyze(new(2026, 9, 22), new(2026, 9, 22), DateTimeOffset.Parse(cutoff), 30, records, complete);

    private static WorkforceTimeRecord Vr(string punches, int seconds) => new()
    {
        Source = ExternalWorkforceSource.VrMais, ExternalKey = "vr:day", WorkDate = new(2026, 9, 22),
        State = "reported", DurationSeconds = seconds, DetailsJson = "{\"timeCards\":" + punches + "}"
    };

    private static WorkforceTimeRecord Monday(string start, string? end) => new()
    {
        Source = ExternalWorkforceSource.Monday, ExternalKey = "monday:session", WorkDate = new(2026, 9, 22),
        State = end is null ? "running" : "closed", StartedAt = DateTimeOffset.Parse($"2026-09-22T{start}:00-03:00"),
        EndedAt = end is null ? null : DateTimeOffset.Parse($"2026-09-22T{end}:00-03:00")
    };
}
