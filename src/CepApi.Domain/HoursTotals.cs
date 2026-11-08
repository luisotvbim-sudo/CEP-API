namespace CepApi.Domain;

public sealed record HoursTotals(long? MondaySeconds, long? VrSeconds, long? DeltaSeconds,
    long? AbsoluteDivergenceSeconds, int PersonDays, int MondayDays, int VrDays,
    int ComparableDays, int AboveToleranceDays)
{
    public static HoursTotals Sum(IEnumerable<TimeAnalysisDay> days)
    {
        long monday = 0, vr = 0, delta = 0, divergence = 0;
        int count = 0, mondayDays = 0, vrDays = 0, comparable = 0, above = 0;
        foreach (var day in days)
        {
            count++;
            if (day.MondaySeconds is { } m) { monday += m; mondayDays++; }
            if (day.VrSeconds is { } v) { vr += v; vrDays++; }
            if (day.DeltaSeconds is not { } d) continue;
            delta += d;
            divergence += Math.Abs(d);
            comparable++;
            if (day.Issues.Contains("above_tolerance")) above++;
        }
        return new(mondayDays == 0 ? null : monday, vrDays == 0 ? null : vr,
            comparable == 0 ? null : delta, comparable == 0 ? null : divergence,
            count, mondayDays, vrDays, comparable, above);
    }
}
