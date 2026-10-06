namespace CepApi.Application;

public static class WorkforceHistoryPolicy
{
    public const int RetentionDays = 90;
    public const int ManualSyncDays = 17;

    public static (DateOnly From, DateOnly To) SyncPeriod(DateTimeOffset now, bool fullRefresh)
    {
        var today = TimeControlCalendar.Today(now);
        return (today.AddDays(1 - (fullRefresh ? RetentionDays : ManualSyncDays)), today);
    }
}
