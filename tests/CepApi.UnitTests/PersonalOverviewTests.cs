using CepApi.Application;
using CepApi.Domain;

namespace CepApi.UnitTests;

public sealed class PersonalOverviewTests
{
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-10-05T15:00:00Z");

    [Theory]
    [InlineData(-1801, PersonalOverviewStatus.Difference)]
    [InlineData(-1800, PersonalOverviewStatus.Regular)]
    [InlineData(0, PersonalOverviewStatus.Regular)]
    [InlineData(1800, PersonalOverviewStatus.Regular)]
    [InlineData(1801, PersonalOverviewStatus.Difference)]
    public void Exact_symmetric_tolerance_is_projected_from_shared_engine(int delta, PersonalOverviewStatus expected)
    {
        var window = TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.PreviousDay, Cutoff);
        var day = window.From;
        var start = TimeAnalysisEngine.StartOfDay(day).AddHours(8);
        var result = TimeAnalysisEngine.Analyze(day, day, Cutoff, 30,
            [new() { Source = ExternalWorkforceSource.Monday, ExternalKey = "m", WorkDate = day, StartedAt = start, EndedAt = start.AddSeconds(3600 + delta), State = "closed" },
             new() { Source = ExternalWorkforceSource.VrMais, ExternalKey = "v", WorkDate = day, DurationSeconds = 3600, State = "reported" }], true);
        var response = Wrap(result);
        var overview = PersonalOverviewProjection.FromAnalysis(AnalysisPeriod.PreviousDay, response, []);
        Assert.Equal(expected, overview.Status);
        Assert.Equal(delta, overview.Analysis!.DeltaSeconds);
        Assert.Equal(Cutoff, overview.Cutoff);
        Assert.Equal(4, overview.Periods.Count);
        Assert.All(overview.Periods, period => Assert.True(period.To <= TimeAnalysisEngine.LocalDate(Cutoff)));
    }

    [Fact]
    public void Cancelling_daily_differences_does_not_make_the_period_regular()
    {
        var first = new DateOnly(2026, 10, 3);
        var result = new TimeAnalysisResponse(first, first.AddDays(1), Cutoff, 30,
            [new(first, 7200, 3600, -3600, false, ["above_tolerance"]),
             new(first.AddDays(1), 3600, 7200, 3600, false, ["above_tolerance"])],
            10800, 10800, 0, 7200, true, Guid.NewGuid(), Sources());
        var overview = PersonalOverviewProjection.FromAnalysis(AnalysisPeriod.Sprint, result, []);
        Assert.Equal(PersonalOverviewStatus.Difference, overview.Status);
        Assert.Equal(2, overview.AttentionDays.Count);
        Assert.Equal(7200, overview.Analysis!.AbsoluteDivergenceSeconds);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("odd_punches")]
    [InlineData("running_timer")]
    public void Unknown_and_integrity_issues_never_produce_regularity(string issue)
    {
        var day = TimeAnalysisEngine.LocalDate(Cutoff);
        var result = new TimeAnalysisResponse(day, day, Cutoff, 30,
            [new(day, null, null, null, true, [issue])], null, null, null, null, true, Guid.NewGuid(), Sources());
        var overview = PersonalOverviewProjection.FromAnalysis(AnalysisPeriod.Daily, result,
            [new(day, null, 3600)]);
        Assert.Equal(PersonalOverviewStatus.Incomplete, overview.Status);
        Assert.True(Assert.Single(overview.AttentionDays).Partial);
        Assert.Null(overview.Analysis!.DeltaSeconds);
        Assert.Equal(3600, Assert.Single(overview.AvailableSourceDays).MondaySeconds);
    }

    [Fact]
    public void A_partial_current_day_can_be_known_only_at_its_cutoff()
    {
        var day = TimeAnalysisEngine.LocalDate(Cutoff);
        var result = new TimeAnalysisResponse(day, day, Cutoff, 30,
            [new(day, 3600, 3600, 0, true, [])], 3600, 3600, 0, 0, false, Guid.NewGuid(), Sources());
        Assert.Equal(PersonalOverviewStatus.Regular, PersonalOverviewProjection.FromAnalysis(AnalysisPeriod.Daily, result, []).Status);
        var incomplete = result with { Sources = [new(ExternalWorkforceSource.Monday, "incomplete", null, Cutoff)] };
        Assert.Equal(PersonalOverviewStatus.Incomplete, PersonalOverviewProjection.FromAnalysis(AnalysisPeriod.Daily, incomplete, []).Status);
    }

    private static TimeAnalysisResponse Wrap(TimeAnalysisResult value)
        => new(value.From, value.To, value.Cutoff, value.ToleranceMinutes, value.Days, value.VrSeconds, value.MondaySeconds,
            value.DeltaSeconds, value.AbsoluteDivergenceSeconds, value.HasIssues, Guid.NewGuid(), Sources());
    private static TimeAnalysisSourceResponse[] Sources()
        => [new(ExternalWorkforceSource.Monday, "complete", null, Cutoff), new(ExternalWorkforceSource.VrMais, "complete", null, Cutoff)];
}
