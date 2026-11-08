using CepApi.Api.Authorization;
using CepApi.Api.Services;
using CepApi.Application;
using CepApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/hours-summary")]
[Authorize]
[OrganizationScope]
public sealed class HoursSummaryController(HoursSummaryService summary, IClock clock) : ApiControllerBase
{
    [HttpGet]
    [Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(HoursSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HoursSummaryResponse>> Get([FromQuery] DateOnly from,
        [FromQuery] DateOnly to, [FromQuery] Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        if (InvalidPeriod(from, to, now) is { } invalid) return invalid;
        return Ok(await summary.ReadAsync(ScopedOrganizationId, from, to, now, cancellationToken));
    }

    [HttpGet("/api/v1/organization/time-control/teams/{teamId:guid}/hours-summary")]
    [Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)},{nameof(UserRole.User)}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(HoursTeamSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HoursTeamSummaryResponse>> GetTeam(Guid teamId, [FromQuery] DateOnly from,
        [FromQuery] DateOnly to, [FromQuery] Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        if (InvalidPeriod(from, to, now) is { } invalid) return invalid;
        var team = await summary.ReadTeamAsync(ScopedOrganizationId, teamId,
            CurrentRole == UserRole.User ? CurrentUserId : null, from, to, now, cancellationToken);
        if (team is null) return ApiProblem(404, "Team not found.", "team_not_found");
        return Ok(team);
    }

    private ObjectResult? InvalidPeriod(DateOnly from, DateOnly to, DateTimeOffset now)
    {
        if (from == default || to < from || to > TimeAnalysisEngine.LocalDate(now))
            return ApiProblem(400, "Use an ordered period ending today or earlier.", "invalid_history_period");
        if (to.DayNumber - from.DayNumber + 1 > WorkforceHistoryPolicy.RetentionDays)
            return ApiProblem(400, "The period cannot exceed 90 days.", "history_period_too_large");
        return null;
    }
}
