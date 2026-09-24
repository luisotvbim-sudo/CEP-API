using System.Text.Json;
using CepApi.Api.Authorization;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

public sealed record AuditEventResponse(Guid Id, Guid? OrganizationId, Guid? ActorUserId, Guid? TargetUserId,
    string Action, JsonElement? Details, string? IpAddress, DateTimeOffset CreatedAt);

[Route("api/v1/organization/audit")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
[OrganizationScope]
public sealed class AuditController(
    AppDbContext db,
    OrganizationScopeService organizationScope) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<AuditEventResponse>>> List(
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int pageSize = 100,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var query = db.AuditEvents.AsNoTracking().Where(x => x.OrganizationId == scopedOrganizationId);
        if (before is not null) query = query.Where(x => x.CreatedAt < before);
        var events = await query.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(pageSize, 1, 200)).ToListAsync(cancellationToken);
        return Ok(events.Select(x => new AuditEventResponse(x.Id, x.OrganizationId, x.ActorUserId, x.TargetUserId,
            x.Action, ParseDetails(x.DetailsJson), x.IpAddress, x.CreatedAt)).ToArray());
    }

    private static JsonElement? ParseDetails(string? json)
        => json is null ? null : JsonSerializer.Deserialize<JsonElement>(json);
}
