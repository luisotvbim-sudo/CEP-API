namespace CepApi.Application;

public static class TimeControlCalendar
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateOnly Today(DateTimeOffset instant)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}
