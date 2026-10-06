using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/admin/audit")]
[Authorize(Roles = nameof(UserRole.SystemAdmin))]
public sealed class SystemAuditController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<AuditEventResponse>>> List(
        [FromQuery] Guid? organizationId, [FromQuery] DateTimeOffset? before,
        [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default)
    {
        var query = db.AuditEvents.AsNoTracking().AsQueryable();
        if (organizationId is not null) query = query.Where(x => x.OrganizationId == organizationId);
        return Ok(await query.ReadAuditEventsAsync(before, pageSize, cancellationToken));
    }
}
