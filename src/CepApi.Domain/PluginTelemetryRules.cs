namespace CepApi.Domain;

public static class PluginTelemetryRules
{
    public const int MaxBatchSize = 200;
    public const int MaxDurationMs = 86_400_000;
    public static readonly TimeSpan MaxEventAge = TimeSpan.FromDays(7);
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    public static bool IsTimestampAllowed(DateTimeOffset occurredAt, DateTimeOffset receivedAt)
        => occurredAt >= receivedAt.Subtract(MaxEventAge) &&
           occurredAt <= receivedAt.Add(MaxFutureSkew);

    public static bool IsDurationAllowed(int? durationMs)
        => durationMs is null or >= 0 and <= MaxDurationMs;
}
