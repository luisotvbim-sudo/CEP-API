using CepApi.Application;
namespace CepApi.Infrastructure.Services;

internal sealed class WorkforceSyncPeriodResolver(IClock clock)
{
    public (DateOnly From, DateOnly To) Resolve(bool fullRefresh)
    {
        var today = TimeControlCalendar.Today(clock.UtcNow);
        var days = fullRefresh ? WorkforceHistoryPolicy.RetentionDays : WorkforceHistoryPolicy.ManualSyncDays;
        return (today.AddDays(-(days - 1)), today);
    }

}
