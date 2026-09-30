namespace CepApi.Application;

public static class TimeControlCalendar
{


    public static DateOnly Today(DateTimeOffset instant)
        => CepApi.Domain.TimeAnalysisEngine.LocalDate(instant);
}
