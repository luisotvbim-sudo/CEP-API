using CepApi.Application;

namespace CepApi.UnitTests;

public sealed class TimeControlCalendarTests
{
    [Fact]
    public void Today_uses_sao_paulo_business_date()
    {
        var instant = new DateTimeOffset(2026, 9, 24, 0, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 9, 23), TimeControlCalendar.Today(instant));
    }

    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 90)]
    public void Synchronization_period_includes_today_in_sao_paulo(bool fullRefresh, int days)
    {
        var period = WorkforceHistoryPolicy.SyncPeriod(new DateTimeOffset(2026, 9, 24, 0, 30, 0, TimeSpan.Zero), fullRefresh);
        Assert.Equal(new DateOnly(2026, 9, 23), period.To);
        Assert.Equal(days, period.To.DayNumber - period.From.DayNumber + 1);
    }
}
