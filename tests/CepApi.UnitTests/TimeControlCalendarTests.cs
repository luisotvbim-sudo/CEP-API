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
}
