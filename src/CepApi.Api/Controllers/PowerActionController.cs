using System.ComponentModel.DataAnnotations;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Api.Services;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/me/time-control")]
[Authorize]
[EnableRateLimiting("account")]
[ProducesResponseType<PowerActionCheckResponse>(StatusCodes.Status200OK)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public sealed class PowerActionController(AppDbContext db, IClock clock, FreshTimeAnalysisService analysis,
    PowerActionUnlockService unlock) : ApiControllerBase
{
    [HttpPost("power-action-unlock")]
    [EnableRateLimiting("power-unlock")]
    [ProducesResponseType<PowerActionUnlockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PowerActionUnlockResponse>> Unlock([FromBody, Required] PowerActionUnlockRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (CurrentOrganizationId is not { } organizationId)
            return ApiProblem(403, "An operational organization is required.", "forbidden");
        return Ok(await unlock.UnlockAsync(organizationId, CurrentUserId, request.Pin, IpAddress, cancellationToken));
    }

    [HttpPost("power-action-check")]
    public Task<ActionResult<PowerActionCheckResponse>> Check([FromBody, Required] PowerActionCheckRequest request, CancellationToken cancellationToken)
        => DecideAsync(request.Action, cancellationToken);

    [HttpGet("power-action-status")]
    public Task<ActionResult<PowerActionCheckResponse>> Status([FromQuery] string action = "shutdown", CancellationToken cancellationToken = default)
        => DecideAsync(action, cancellationToken);

    private async Task<ActionResult<PowerActionCheckResponse>> DecideAsync(string action, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (action is not ("shutdown" or "restart" or "hibernate"))
            return ApiProblem(400, "Action must be shutdown, restart or hibernate.", "invalid_power_action");
        if (CurrentOrganizationId is not { } organizationId)
            return ApiProblem(403, "An operational organization is required.", "forbidden");

        if (await unlock.GetActiveUntilAsync(organizationId, CurrentUserId, cancellationToken) is { } unlockedUntil)
            return Ok(new PowerActionCheckResponse(action, "allowed", "administrative_override",
                "Ação liberada temporariamente pelo PIN administrativo.", null, true, unlockedUntil));

        var person = await db.ActiveNotificationRecipients(organizationId, CurrentUserId).AsNoTracking()
            .Include(x => x.MondayIdentity).Include(x => x.VrMaisIdentity).SingleOrDefaultAsync(cancellationToken);
        if (person is null)
            return Ok(new PowerActionCheckResponse(action, "indeterminate", "workforce_person_not_associated",
                "Sua conta ainda não está associada aos cadastros Monday e VR Mais.", null));
        if (!person.MondayIdentity.IsActive || !person.VrMaisIdentity.IsActive)
            return Ok(new PowerActionCheckResponse(action, "indeterminate", "external_identity_inactive",
                "Seu cadastro em uma das fontes está inativo. Solicite a revisão da associação.", null));

        var settings = await db.Set<TimeControlSettings>().AsNoTracking().SingleAsync(cancellationToken);
        var window = TimeAnalysisEngine.ResolvePeriod(AnalysisPeriod.Daily, clock.UtcNow);
        var (snapshots, states) = await analysis.FetchAsync([person], window, cancellationToken);
        var result = analysis.Analyze(person, window, settings.ToleranceMinutes, settings.Version, snapshots, states);
        return Ok(PowerActionDecision.FromAnalysis(action, result));
    }
}
