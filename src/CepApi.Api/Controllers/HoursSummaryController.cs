using CepApi.Api.Authorization;
using CepApi.Api.Services;
using CepApi.Application;
using CepApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/hours-summary")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
[OrganizationScope]
public sealed class HoursSummaryController(HoursSummaryService summary, IClock clock) : ApiControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(HoursSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HoursSummaryResponse>> Get([FromQuery] DateOnly from,
        [FromQuery] DateOnly to, [FromQuery] Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        if (from == default || to < from || to > TimeAnalysisEngine.LocalDate(now))
            return ApiProblem(400, "Use an ordered period ending today or earlier.", "invalid_history_period");
        if (to.DayNumber - from.DayNumber + 1 > WorkforceHistoryPolicy.RetentionDays)
            return ApiProblem(400, "The period cannot exceed 90 days.", "history_period_too_large");
        return Ok(await summary.ReadAsync(ScopedOrganizationId, from, to, now, cancellationToken));
    }
}
