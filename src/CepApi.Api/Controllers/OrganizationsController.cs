using System.Text.RegularExpressions;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/admin/organizations")]
[Authorize(Roles = nameof(UserRole.SystemAdmin))]
public sealed partial class OrganizationsController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISecurityCodeService codeService,
    IEmailQueue emailQueue,
    IRegistrationEmailPolicy registrationEmailPolicy,
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
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OrganizationResponse(x.Id, x.Name, x.Slug, x.Status, x.CreatedAt)).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<OrganizationResponse>(items, page, pageSize, total));
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationResponse>> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (!await registrationEmailPolicy.IsAllowedAsync(request.InitialAdminEmail, cancellationToken))
            return ApiProblem(StatusCodes.Status400BadRequest, "Email domain is not allowed for registration.", "email_domain_not_allowed");
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.Name) || !SlugPattern().IsMatch(slug))
            return ApiProblem(StatusCodes.Status400BadRequest, "Name or slug is invalid.", "invalid_organization");
        if (await db.Organizations.AnyAsync(x => x.Slug == slug, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "Organization slug already exists.", "slug_already_exists");

        var normalizedEmail = userManager.NormalizeEmail(request.InitialAdminEmail);
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken) ||
            await db.Invitations.AnyAsync(x => x.Email == normalizedEmail && x.AcceptedAt == null && x.RevokedAt == null && x.ExpiresAt > clock.UtcNow, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "Email already exists or has a pending invitation.", "email_unavailable");

        var now = clock.UtcNow;
        var organization = new Organization { Name = request.Name.Trim(), Slug = slug, CreatedAt = now, UpdatedAt = now };
        var code = codeService.GenerateInvitationCode();
        var products = request.Products?.Distinct().ToHashSet() ?? [Product.Revit, Product.Zwcad];
        var invitation = new Invitation
        {
            OrganizationId = organization.Id,
            Organization = organization,
            Email = normalizedEmail!,
            Role = UserRole.OrganizationAdmin,
            CanUseRevit = products.Contains(Product.Revit),
            CanUseZwcad = products.Contains(Product.Zwcad),
            CodeHash = codeService.Hash(code),
            CreatedAt = now,
            ExpiresAt = now.AddHours(48),
            CreatedByUserId = CurrentUserId
        };
        db.Organizations.Add(organization);
        db.Invitations.Add(invitation);
        emailQueue.Invitation(request.InitialAdminEmail.Trim(), organization.Name, code, invitation.ExpiresAt);
        await audit.WriteAsync("organization.created", organization.Id, CurrentUserId, details: new { organization.Id, organization.Slug }, ipAddress: IpAddress, cancellationToken: cancellationToken);
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
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM organizations WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);
        var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Id == organizationId, cancellationToken);
        if (organization is null) return ApiProblem(StatusCodes.Status404NotFound, "Organization not found.", "organization_not_found");
        if (organization.Status == OrganizationStatus.Archived && request.Status != OrganizationStatus.Archived)
            return ApiProblem(StatusCodes.Status409Conflict, "Archived organizations cannot be reactivated.", "organization_archived");

        organization.Status = request.Status;
        organization.UpdatedAt = clock.UtcNow;
        if (request.Status != OrganizationStatus.Active)
        {
            var userIds = await db.Users.Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(cancellationToken);
            foreach (var userId in userIds) await db.LockUserAsync(userId, cancellationToken);
            var sessions = await db.RefreshSessions.Where(x => userIds.Contains(x.UserId) && x.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.RevokedAt = clock.UtcNow;
                session.RevocationReason = "organization_inactive";
            }
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
        var invitation = await db.Invitations.Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == invitationId && x.OrganizationId == organizationId, cancellationToken);
        if (invitation is null)
            return ApiProblem(StatusCodes.Status404NotFound, "Invitation not found.", "invitation_not_found");
        if (invitation.AcceptedAt is not null || invitation.RevokedAt is not null)
            return ApiProblem(StatusCodes.Status409Conflict, "Invitation is no longer pending.", "invitation_not_pending");

        if (!await registrationEmailPolicy.IsAllowedAsync(invitation.Email, cancellationToken))
            return ApiProblem(StatusCodes.Status400BadRequest, "Email domain is not allowed for registration.", "email_domain_not_allowed");

        var code = codeService.GenerateInvitationCode();
        invitation.CodeHash = codeService.Hash(code);
        invitation.ExpiresAt = clock.UtcNow.AddHours(48);
        invitation.FailedAttempts = 0;
        emailQueue.Invitation(invitation.Email, invitation.Organization.Name, code, invitation.ExpiresAt);
        await audit.WriteAsync("invitation.resent_by_system_admin", organizationId, CurrentUserId,
            details: new { invitation.Id }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/users")]
    public async Task<ActionResult<PagedResponse<UserResponse>>> Users(Guid organizationId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.Users.AsNoTracking().Include(x => x.ProductAccesses).Where(x => x.OrganizationId == organizationId);
        var total = await query.LongCountAsync(cancellationToken);
        var users = await query.OrderBy(x => x.DisplayName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<UserResponse>(users.Select(x => x.ToUserResponse()).ToArray(), page, pageSize, total));
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
