using CepApi.Domain;

namespace CepApi.UnitTests;

public sealed class PluginTelemetryRulesTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Events_from_the_offline_window_are_accepted()
    {
        Assert.True(PluginTelemetryRules.IsTimestampAllowed(ReceivedAt.AddDays(-7), ReceivedAt));
        Assert.True(PluginTelemetryRules.IsTimestampAllowed(ReceivedAt.AddMinutes(5), ReceivedAt));
    }

    [Fact]
    public void Events_outside_the_offline_window_are_rejected()
    {
        Assert.False(PluginTelemetryRules.IsTimestampAllowed(ReceivedAt.AddDays(-7).AddTicks(-1), ReceivedAt));
        Assert.False(PluginTelemetryRules.IsTimestampAllowed(ReceivedAt.AddMinutes(5).AddTicks(1), ReceivedAt));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(86_400_000, true)]
    [InlineData(-1, false)]
    [InlineData(86_400_001, false)]
    public void Duration_is_bounded(int? durationMs, bool expected)
    {
        Assert.Equal(expected, PluginTelemetryRules.IsDurationAllowed(durationMs));
    }
}
