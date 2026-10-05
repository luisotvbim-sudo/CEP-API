using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CepApi.Api.Controllers;

[Route("api/v1/me/time-control/overview")]
[Authorize]
[EnableRateLimiting("account")]
public sealed class PersonalOverviewController(PersonalOverviewService overview) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PersonalOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PersonalOverviewResponse>> Get(
        [FromQuery] AnalysisPeriod period = AnalysisPeriod.Daily, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Enum.IsDefined(period)) return ApiProblem(400, "Choose an official analysis period.", "invalid_analysis_period");
        if (CurrentOrganizationId is not { } organizationId)
            return ApiProblem(403, "An operational organization is required.", "forbidden");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            return Ok(await overview.ReadAsync(organizationId, CurrentUserId, period, deadline.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiProblem(503, "The source consultation did not finish in time.", "overview_timeout");
        }
    }
}
