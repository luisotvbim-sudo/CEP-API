using CepApi.Domain;

namespace CepApi.Application;

public sealed record HoursPersonResponse(Guid Id, string Name, HoursTotals Totals,
    IReadOnlyList<TimeAnalysisDay> Days);
public sealed record HoursTeamMemberResponse(Guid PersonId, HoursTotals Totals, IReadOnlyList<DateOnly> Days);
public sealed record HoursTeamResponse(Guid Id, string Name, HoursTotals Totals,
    IReadOnlyList<HoursTeamMemberResponse> Members);
public sealed record HoursSummaryResponse(DateOnly From, DateOnly To, DateTimeOffset GeneratedAt,
    int ToleranceMinutes, HoursTotals Organization, IReadOnlyList<HoursPersonResponse> People,
    IReadOnlyList<HoursTeamResponse> Teams);
public sealed record HoursTeamSummaryResponse(DateOnly From, DateOnly To, DateTimeOffset GeneratedAt,
    int ToleranceMinutes, HoursTeamResponse Team, IReadOnlyList<HoursPersonResponse> People);
