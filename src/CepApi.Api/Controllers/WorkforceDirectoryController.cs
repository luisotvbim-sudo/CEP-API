using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
[OrganizationScope]
public sealed class WorkforceDirectoryController(
    AppDbContext db,
    IWorkforceDirectorySyncService syncService,
    IAuditService audit) : ApiControllerBase
{
    [HttpGet("external-identities")]
    public async Task<ActionResult<PagedResponse<ExternalWorkforceIdentityResponse>>> ListExternalIdentities(
        [FromQuery] ExternalWorkforceSource? source,
        [FromQuery] bool activeOnly = true,
        [FromQuery] bool? mapped = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationId();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.ExternalWorkforceIdentities.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);
        if (source is not null) query = query.Where(x => x.Source == source);
        if (activeOnly) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(x => x.DisplayName.ToLower().Contains(value) ||
                x.Email != null && x.Email.ToLower().Contains(value) || x.ExternalId.ToLower().Contains(value));
        }
        if (mapped is not null)
        {
            query = mapped.Value
                ? query.Where(identity => db.WorkforcePeople.Any(person =>
                    person.MondayIdentityId == identity.Id || person.VrMaisIdentityId == identity.Id))
                : query.Where(identity => !db.WorkforcePeople.Any(person =>
                    person.MondayIdentityId == identity.Id || person.VrMaisIdentityId == identity.Id));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Source).ThenBy(x => x.DisplayName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(identity => new ExternalWorkforceIdentityResponse(
                identity.Id,
                identity.Source,
                identity.ExternalId,
                identity.DisplayName,
                identity.Email,
                identity.IsActive,
                identity.LastSeenAt,
                db.WorkforcePeople.Where(person => person.MondayIdentityId == identity.Id ||
                    person.VrMaisIdentityId == identity.Id).Select(person => (Guid?)person.Id).SingleOrDefault()))
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<ExternalWorkforceIdentityResponse>(items, page, pageSize, total));
    }

    [HttpPost("synchronizations")]
    public async Task<ActionResult<WorkforceSyncResponse>> Synchronize(
        [FromQuery] bool full = false,
        CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationId();
        try
        {
            var batch = await syncService.SynchronizeAsync(organizationId, CurrentUserId, full, cancellationToken);
            await audit.WriteAsync("time_control.directory_synchronized", organizationId, CurrentUserId,
                details: new
                {
                    batch.Id,
                    status = batch.Status.ToString(),
                    full,
                    sources = batch.Sources.Select(x => new { source = x.Source.ToString(), status = x.Status.ToString() })
                }, ipAddress: IpAddress, cancellationToken: cancellationToken);
            return Ok(ToResponse(batch));
        }
        catch (ExternalDirectoryException exception) when (exception.Code == "sync_already_running")
        {
            return ApiProblem(StatusCodes.Status409Conflict, exception.Message, exception.Code);
        }
    }

    [HttpGet("synchronizations/latest")]
    public async Task<ActionResult<WorkforceSyncResponse>> LatestSynchronization(CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        var batch = await db.WorkforceSyncBatches.AsNoTracking().Include(x => x.Sources)
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return batch is null
            ? ApiProblem(StatusCodes.Status404NotFound, "No workforce synchronization has been requested.", "sync_not_found")
            : Ok(ToResponse(batch));
    }

    [HttpGet("synchronizations/{batchId:guid}")]
    public async Task<ActionResult<WorkforceSyncResponse>> GetSynchronization(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        var batch = await db.WorkforceSyncBatches.AsNoTracking().Include(x => x.Sources)
            .SingleOrDefaultAsync(x => x.Id == batchId && x.OrganizationId == organizationId, cancellationToken);
        return batch is null
            ? ApiProblem(StatusCodes.Status404NotFound, "Workforce synchronization not found.", "sync_not_found")
            : Ok(ToResponse(batch));
    }

    private Guid RequireOrganizationId()
        => CurrentOrganizationId ?? throw new InvalidOperationException("An organization claim is required.");

    private static WorkforceSyncResponse ToResponse(WorkforceSyncBatch batch)
        => new(batch.Id, batch.Status, batch.StartedAt, batch.CompletedAt,
            batch.Sources.OrderBy(x => x.Source).Select(x => new WorkforceSyncSourceResponse(
                x.Source, x.Status, x.ReceivedCount, x.CreatedCount, x.UpdatedCount, x.DeactivatedCount,
                x.TimeRecordReceivedCount, x.TimeRecordCreatedCount, x.TimeRecordUpdatedCount,
                x.TimeRecordRemovedCount, x.CompleteSnapshot, x.CoverageFrom, x.CoverageTo,
                x.ErrorCode, x.ErrorMessage, x.StartedAt, x.CompletedAt)).ToArray());
}
