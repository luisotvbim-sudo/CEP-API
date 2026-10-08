using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

public sealed record DesktopTelemetryEventResponse(Guid EventId, Guid UserId, Guid InstallationId, Guid? OperationId,
    DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt, string Code, string Phase, string Outcome,
    string? Action, string ErrorCode, string AppVersion);

[Route("api/v1/organization/desktop-telemetry")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
[OrganizationScope]
[EnableRateLimiting("account")]
public sealed class OrganizationDesktopTelemetryController(AppDbContext db, IAuditService audit) : ApiControllerBase
{
    [HttpGet("events")]
    [ProducesResponseType<IReadOnlyList<DesktopTelemetryEventResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DesktopTelemetryEventResponse>>> List(
        [FromQuery] Guid? installationId = null, [FromQuery] DateTimeOffset? before = null,
        [FromQuery] Guid? beforeEventId = null, [FromQuery] Guid? beforeUserId = null,
        [FromQuery] int pageSize = 50, [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (pageSize is < 1 or > 100 || installationId == Guid.Empty || beforeEventId == Guid.Empty || beforeUserId == Guid.Empty ||
            (before is null) != (beforeEventId is null) || (before is null) != (beforeUserId is null) ||
            (before is { } cursor && cursor.Offset != TimeSpan.Zero))
            return ApiProblem(400, "Invalid telemetry query.", "invalid_telemetry_query");
        var query = db.DesktopTelemetryEvents.AsNoTracking()
            .Where(x => x.OrganizationId == ScopedOrganizationId);
        if (installationId is { } selected) query = query.Where(x => x.InstallationId == selected);
        if (before is { } point && beforeEventId is { } lastId && beforeUserId is { } lastUser)
            query = query.Where(x => x.ReceivedAt < point || (x.ReceivedAt == point &&
                (x.EventId.CompareTo(lastId) < 0 || (x.EventId == lastId && x.UserId.CompareTo(lastUser) < 0))));
        var rows = await query.OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.EventId)
            .ThenByDescending(x => x.UserId)
            .Take(pageSize).Select(x => new DesktopTelemetryEventResponse(x.EventId, x.UserId, x.InstallationId,
                x.OperationId, x.OccurredAt, x.ReceivedAt, x.Code, x.Phase, x.Outcome,
                x.Action, x.ErrorCode, x.AppVersion)).ToArrayAsync(cancellationToken);
        await audit.WriteAsync("desktop_telemetry.read", ScopedOrganizationId, CurrentUserId,
            details: new { count = rows.Length }, cancellationToken: cancellationToken);
        return Ok(rows);
    }
}
