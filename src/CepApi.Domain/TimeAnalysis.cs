using System.Globalization;
using System.Text.Json;

namespace CepApi.Domain;

public enum AnalysisPeriod { Daily, Weekly, Sprint, PreviousDay }
public sealed record AnalysisWindow(DateOnly From, DateOnly To, DateTimeOffset Cutoff);
public sealed record TimeAnalysisDay(DateOnly Day, long? VrSeconds, long? MondaySeconds,
    long? DeltaSeconds, bool Partial, IReadOnlyList<string> Issues);
public sealed record TimeAnalysisResult(DateOnly From, DateOnly To, DateTimeOffset Cutoff,
    int ToleranceMinutes, IReadOnlyList<TimeAnalysisDay> Days, long? VrSeconds,
    long? MondaySeconds, long? DeltaSeconds, long? AbsoluteDivergenceSeconds, bool HasIssues);

/// <summary>One calculation path for reports, interactive queries and notifications.
/// The caller supplies records for exactly one person and verifies source coverage/freshness.</summary>
public static class TimeAnalysisEngine
{
    /// <summary>Summarizes stored history, without treating absent imports as zero or
    /// extending a stored running timer up to the time of the query. This is not a
    /// fresh source consultation and does not certify synchronization coverage.</summary>
    public static TimeAnalysisDay SummarizeImportedDay(DateOnly day, DateTimeOffset now,
        int toleranceMinutes, IEnumerable<WorkforceTimeRecord> records)
    {
        var input = records.Where(x => !x.IsRemoved).ToArray();
        if (day > LocalDate(now))
            return new(day, null, null, null, true, ["incomplete"]);
        var result = Analyze(day, day, now, toleranceMinutes, input, true).Days[0];
        var mondayRows = input.Where(x => x.Source == ExternalWorkforceSource.Monday && x.WorkDate == day).ToArray();
        var vrRows = input.Where(x => x.Source == ExternalWorkforceSource.VrMais && x.WorkDate == day).ToArray();
        var monday = mondayRows.Length == 0 || mondayRows.Any(x => x.DurationSeconds is null or < 0 ||
            x.EndedAt is null || x.State == "running") ? null : result.MondaySeconds;
        // Today's punches are a stored snapshot: never infer work after their last import.
        var vr = result.Partial ? null : result.VrSeconds;
        if (vrRows.Any(x => x.DurationSeconds is null or < 0)) vr = null;
        var delta = monday.HasValue && vr.HasValue ? result.DeltaSeconds : null;
        var issues = result.Issues.Where(x => delta.HasValue || x != "above_tolerance").ToHashSet();
        if (monday is null || vr is null) issues.Add("incomplete");
        return result with
        {
            MondaySeconds = monday,
            VrSeconds = vr,
            DeltaSeconds = delta,
            Issues = issues.Order(StringComparer.Ordinal).ToArray()
        };
    }

    public static TimeZoneInfo SaoPaulo { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public static DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, SaoPaulo).DateTime);
    public static DateTimeOffset StartOfDay(DateOnly day) => AtTime(day, TimeOnly.MinValue);
    private static DateTimeOffset AtTime(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, SaoPaulo));
    }

    public static AnalysisWindow ResolvePeriod(AnalysisPeriod period, DateTimeOffset cutoff)
    {
        var today = LocalDate(cutoff);
        return period switch
        {
            AnalysisPeriod.Daily => new(today, today, cutoff),
            AnalysisPeriod.Weekly => new(today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today, cutoff),
            AnalysisPeriod.Sprint => new(new DateOnly(today.Year, today.Month, today.Day < 15 ? 1 : 15), today, cutoff),
            AnalysisPeriod.PreviousDay => new(today.AddDays(-1), today.AddDays(-1), cutoff),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
    }

    public static TimeAnalysisResult Analyze(DateOnly from, DateOnly to, DateTimeOffset cutoff,
        int toleranceMinutes, IEnumerable<WorkforceTimeRecord> records, bool sourcesComplete)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (from > to || to > LocalDate(cutoff) || to.DayNumber - from.DayNumber > 365)
            throw new ArgumentOutOfRangeException(nameof(to));
        if (toleranceMinutes < 0) throw new ArgumentOutOfRangeException(nameof(toleranceMinutes));
        // Snapshot retries must never count the same source session/day twice.
        var input = records.GroupBy(x => (x.Source, x.ExternalIdentityId, x.ExternalKey))
            .Select(group => group.MaxBy(x => x.LastSyncedAt)!)
            .Where(x => !x.IsRemoved).ToArray();
        var vrByDay = input.Where(x => x.Source == ExternalWorkforceSource.VrMais).ToLookup(x => x.WorkDate);
        var mondayRows = input.Where(x => x.Source == ExternalWorkforceSource.Monday).ToArray();
        var today = LocalDate(cutoff);
        var days = new List<TimeAnalysisDay>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var issues = new HashSet<string>(StringComparer.Ordinal);
            var partial = day == today;
            var start = StartOfDay(day);
            var end = StartOfDay(day.AddDays(1));
            if (end > cutoff) end = cutoff;
            var vrRows = vrByDay[day].ToArray();
            long? vr = vrRows.Length == 1 ? VrSeconds(vrRows[0], day, end, partial, issues) : null;
            long? monday = 0;
            foreach (var row in mondayRows)
            {
                if (row.StartedAt is not { } began)
                {
                    if (row.WorkDate == day) { issues.Add("incomplete"); monday = null; }
                    continue;
                }
                var stopped = row.EndedAt ?? cutoff;
                if (stopped < began)
                {
                    if (row.WorkDate == day) { issues.Add("incomplete"); monday = null; }
                    continue;
                }
                if (began >= end || stopped <= start) continue;
                if (row.EndedAt is null && row.State != "running")
                { issues.Add("incomplete"); monday = null; continue; }
                // Overnight work is disallowed. A session closed later still crossed the day's boundary.
                if (began < start || !partial && (row.EndedAt is null || stopped > end)) issues.Add("running_timer");
                var clippedStart = began > start ? began : start;
                var clippedEnd = stopped < end ? stopped : end;
                if (monday.HasValue) monday += Math.Max(0, (long)(clippedEnd - clippedStart).TotalSeconds);
            }
            if (!sourcesComplete) { vr = null; monday = null; }
            if (vr is null || monday is null) issues.Add("incomplete");
            var delta = vr.HasValue && monday.HasValue && !issues.Contains("odd_punches") &&
                !issues.Contains("running_timer") && !issues.Contains("incomplete") ? monday - vr : null;
            if (delta.HasValue && Math.Abs(delta.Value) > (long)toleranceMinutes * 60) issues.Add("above_tolerance");
            days.Add(new(day, vr, monday, delta, partial, issues.Order(StringComparer.Ordinal).ToArray()));
        }
        var vrTotal = days.All(x => x.VrSeconds.HasValue) ? days.Sum(x => x.VrSeconds) : null;
        var mondayTotal = days.All(x => x.MondaySeconds.HasValue) ? days.Sum(x => x.MondaySeconds) : null;
        var deltaTotal = days.All(x => x.DeltaSeconds.HasValue) ? days.Sum(x => x.DeltaSeconds) : null;
        long? absoluteDivergence = days.All(x => x.DeltaSeconds.HasValue) ? days.Sum(x => Math.Abs(x.DeltaSeconds!.Value)) : null;
        return new(from, to, cutoff, toleranceMinutes, days, vrTotal, mondayTotal, deltaTotal, absoluteDivergence, days.Any(x => x.Issues.Count > 0));
    }

    private static long? VrSeconds(WorkforceTimeRecord row, DateOnly day, DateTimeOffset cutoff,
        bool partial, HashSet<string> issues)
    {
        if (row.State == "missing") return null;
        if (row.DetailsJson is null) return partial ? null : row.DurationSeconds;
        try
        {
            using var document = JsonDocument.Parse(row.DetailsJson);
            if (!document.RootElement.TryGetProperty("timeCards", out var values) || values.ValueKind != JsonValueKind.Array)
                return partial ? null : row.DurationSeconds;
            var punches = new List<DateTimeOffset>();
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String || !TimeOnly.TryParseExact(value.GetString(),
                    ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
                var instant = AtTime(day, time);
                if (punches.Count > 0 && instant <= punches[^1]) return null;
                punches.Add(instant);
            }
            if (!partial && punches.Count % 2 != 0) { issues.Add("odd_punches"); return null; }
            // A closed day's official VR total includes approved adjustments; punches validate its integrity.
            if (!partial) return row.DurationSeconds is >= 0 ? row.DurationSeconds : null;
            if (punches.Count == 0 && row.DurationSeconds != 0) return null;
            long seconds = 0;
            for (var index = 0; index < punches.Count; index += 2)
            {
                if (punches[index] >= cutoff) break;
                var finish = index + 1 < punches.Count ? punches[index + 1] : cutoff;
                if (finish > cutoff) finish = cutoff;
                seconds += (long)(finish - punches[index]).TotalSeconds;
            }
            return seconds;
        }
        catch (JsonException) { return null; }
    }
}
