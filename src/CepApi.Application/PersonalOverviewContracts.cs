using CepApi.Domain;

namespace CepApi.Application;

public enum PersonalOverviewStatus { Regular, Difference, Incomplete, NotAssociated, InactiveIdentity }
public sealed record PersonalOverviewPeriod(AnalysisPeriod Period, DateOnly From, DateOnly To);
public sealed record PersonalOverviewDay(DateOnly Day, PersonalOverviewStatus Status, bool Partial,
    IReadOnlyList<string> Issues);
public sealed record PersonalSourceDay(DateOnly Day, long? VrSeconds, long? MondaySeconds);
public sealed record PersonalOverviewResponse(AnalysisPeriod Period, DateTimeOffset Cutoff,
    IReadOnlyList<PersonalOverviewPeriod> Periods, PersonalOverviewStatus Status,
    TimeAnalysisResponse? Analysis, IReadOnlyList<PersonalOverviewDay> AttentionDays,
    IReadOnlyList<PersonalSourceDay> AvailableSourceDays);

/// <summary>Projects the shared engine's evidence without inventing totals or a second tolerance rule.</summary>
public static class PersonalOverviewProjection
{
    public static PersonalOverviewStatus DayStatus(TimeAnalysisDay day)
        => day.DeltaSeconds is null || day.Issues.Any(issue => issue != "above_tolerance")
            ? PersonalOverviewStatus.Incomplete
            : day.Issues.Contains("above_tolerance") ? PersonalOverviewStatus.Difference : PersonalOverviewStatus.Regular;

    public static PersonalOverviewResponse FromAnalysis(AnalysisPeriod period, TimeAnalysisResponse analysis,
        IReadOnlyList<PersonalSourceDay> availableSourceDays)
    {
        var days = analysis.Days.Select(day => new PersonalOverviewDay(day.Day, DayStatus(day), day.Partial, day.Issues)).ToArray();
        var complete = analysis.Days.Count > 0 && analysis.Sources.Count == 2 &&
            analysis.Sources.All(source => source.Status == "complete") &&
            analysis.DeltaSeconds.HasValue && days.All(day => day.Status != PersonalOverviewStatus.Incomplete);
        var status = !complete ? PersonalOverviewStatus.Incomplete :
            days.Any(day => day.Status == PersonalOverviewStatus.Difference) ? PersonalOverviewStatus.Difference : PersonalOverviewStatus.Regular;
        return new(period, analysis.Cutoff, Periods(analysis.Cutoff), status, analysis,
            days.Where(day => day.Status != PersonalOverviewStatus.Regular).ToArray(), availableSourceDays);
    }

    public static IReadOnlyList<PersonalOverviewPeriod> Periods(DateTimeOffset cutoff)
        => Enum.GetValues<AnalysisPeriod>().Select(period =>
        {
            var window = TimeAnalysisEngine.ResolvePeriod(period, cutoff);
            return new PersonalOverviewPeriod(period, window.From, window.To);
        }).ToArray();
}
