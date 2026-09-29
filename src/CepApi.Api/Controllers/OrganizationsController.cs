using System.Text.RegularExpressions;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Api.Services;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/admin/organizations")]
[Authorize(Roles = nameof(UserRole.SystemAdmin))]
public sealed partial class OrganizationsController(
    AppDbContext db,
    InvitationService invitations,
    IClock clock,
    IAuditService audit) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<OrganizationResponse>>> List(
        [FromQuery] string? search, [FromQuery] OrganizationStatus? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.Organizations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(value) || x.Slug.ToLower().Contains(value));
        }
        if (status is not null) query = query.Where(x => x.Status == status);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Page(page, pageSize)
            .Select(x => new OrganizationResponse(x.Id, x.Name, x.Slug, x.Status, x.CreatedAt)).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<OrganizationResponse>(items, page, pageSize, total));
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationResponse>> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        await invitations.EnsureAllowedEmailAsync(request.InitialAdminEmail, cancellationToken);
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.Name) || !SlugPattern().IsMatch(slug))
            return ApiProblem(StatusCodes.Status400BadRequest, "Name or slug is invalid.", "invalid_organization");
        if (await db.Organizations.AnyAsync(x => x.Slug == slug, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "Organization slug already exists.", "slug_already_exists");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var normalizedEmail = await invitations.ReserveEmailAsync(request.InitialAdminEmail, cancellationToken);

        var now = clock.UtcNow;
        var organization = new Organization { Name = request.Name.Trim(), Slug = slug, CreatedAt = now, UpdatedAt = now };
        db.Organizations.Add(organization);
        invitations.Create(organization, request.InitialAdminEmail, normalizedEmail, UserRole.OrganizationAdmin,
            request.Products ?? [Product.Revit, Product.Zwcad], CurrentUserId);
        await audit.WriteAsync("organization.created", organization.Id, CurrentUserId, details: new { organization.Id, organization.Slug }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { organizationId = organization.Id },
            new OrganizationResponse(organization.Id, organization.Name, organization.Slug, organization.Status, organization.CreatedAt));
    }

    [HttpGet("{organizationId:guid}")]
    public async Task<ActionResult<OrganizationResponse>> Get(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == organizationId, cancellationToken);
        return organization is null
            ? ApiProblem(StatusCodes.Status404NotFound, "Organization not found.", "organization_not_found")
            : Ok(new OrganizationResponse(organization.Id, organization.Name, organization.Slug, organization.Status, organization.CreatedAt));
    }

    [HttpPatch("{organizationId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid organizationId, ChangeOrganizationStatusRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockOrganizationAsync(organizationId, cancellationToken);
        var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Id == organizationId, cancellationToken);
        if (organization is null) return ApiProblem(StatusCodes.Status404NotFound, "Organization not found.", "organization_not_found");
        if (organization.Status == OrganizationStatus.Archived && request.Status != OrganizationStatus.Archived)
            return ApiProblem(StatusCodes.Status409Conflict, "Archived organizations cannot be reactivated.", "organization_archived");

        organization.Status = request.Status;
        organization.UpdatedAt = clock.UtcNow;
        if (request.Status != OrganizationStatus.Active)
        {
            await db.LockOrganizationUsersAsync(organizationId, cancellationToken);
            await db.RefreshSessions.Where(session => db.Users.Any(user =>
                    user.Id == session.UserId && user.OrganizationId == organizationId))
                .RevokeSessionsAsync(clock.UtcNow, "organization_inactive", cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("organization.status_changed", organizationId, CurrentUserId,
            details: new { status = request.Status.ToString() }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/invitations")]
    public async Task<ActionResult<IReadOnlyCollection<InvitationResponse>>> Invitations(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!await db.Organizations.AnyAsync(x => x.Id == organizationId, cancellationToken))
            return ApiProblem(StatusCodes.Status404NotFound, "Organization not found.", "organization_not_found");
        var invitations = await db.Invitations.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt).Take(200)
            .Select(x => new InvitationResponse(x.Id, x.Email, x.Role, x.CanUseRevit, x.CanUseZwcad,
                x.ExpiresAt, x.AcceptedAt, x.RevokedAt)).ToListAsync(cancellationToken);
        return Ok(invitations);
    }

    [HttpPost("{organizationId:guid}/invitations/{invitationId:guid}/resend")]
    public async Task<IActionResult> ResendInvitation(Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        await invitations.ResendAsync(organizationId, invitationId, CurrentUserId,
            "invitation.resent_by_system_admin", IpAddress, cancellationToken);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/users")]
    public async Task<ActionResult<PagedResponse<UserResponse>>> Users(Guid organizationId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.Users.AsNoTracking().Include(x => x.ProductAccesses).Where(x => x.OrganizationId == organizationId);
        var total = await query.LongCountAsync(cancellationToken);
        var users = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Page(page, pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<UserResponse>(users.Select(x => x.ToUserResponse()).ToArray(), page, pageSize, total));
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
