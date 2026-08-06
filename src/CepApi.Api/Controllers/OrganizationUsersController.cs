using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization")]
[Authorize(Roles = nameof(UserRole.OrganizationAdmin))]
public sealed class OrganizationUsersController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISecurityCodeService codeService,
    IEmailSender emailSender,
    IClock clock,
    IAuditService audit) : ApiControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<PagedResponse<UserResponse>>> ListUsers(
        [FromQuery] string? search, [FromQuery] UserStatus? status, [FromQuery] UserRole? role,
        [FromQuery] Product? product, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationId();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Users.AsNoTracking().Include(x => x.ProductAccesses).Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(x => x.DisplayName.ToLower().Contains(value) || x.Email!.ToLower().Contains(value));
        }
        if (status is not null) query = query.Where(x => x.Status == status);
        if (role is not null) query = query.Where(x => x.Role == role);
        if (product is not null) query = query.Where(x => x.ProductAccesses.Any(access => access.Product == product));

        var total = await query.LongCountAsync(cancellationToken);
        var users = await query.OrderBy(x => x.DisplayName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<UserResponse>(users.Select(ToResponse).ToArray(), page, pageSize, total));
    }

    [HttpPost("invitations")]
    public async Task<ActionResult<InvitationResponse>> Invite(InviteUserRequest request, CancellationToken cancellationToken)
    {
        if (request.Role is UserRole.SystemAdmin)
            return ApiProblem(StatusCodes.Status400BadRequest, "SystemAdmin cannot be assigned inside an organization.", "invalid_role");
        var organizationId = RequireOrganizationId();
        var organization = await db.Organizations.SingleAsync(x => x.Id == organizationId, cancellationToken);
        if (organization.Status != OrganizationStatus.Active)
            return ApiProblem(StatusCodes.Status403Forbidden, "Organization is not active.", "organization_inactive");

        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken) ||
            await db.Invitations.AnyAsync(x => x.Email == normalizedEmail && x.AcceptedAt == null && x.RevokedAt == null && x.ExpiresAt > clock.UtcNow, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "Email already exists or has a pending invitation.", "email_unavailable");

        var now = clock.UtcNow;
        var code = codeService.GenerateInvitationCode();
        var products = request.Products?.Distinct().ToHashSet() ?? [];
        var invitation = new Invitation
        {
            OrganizationId = organizationId,
            Email = normalizedEmail!,
            Role = request.Role,
            CanUseRevit = products.Contains(Product.Revit),
            CanUseZwcad = products.Contains(Product.Zwcad),
            CodeHash = codeService.Hash(code),
            CreatedAt = now,
            ExpiresAt = now.AddHours(48),
            CreatedByUserId = CurrentUserId
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        await emailSender.SendInvitationAsync(request.Email.Trim(), organization.Name, code, invitation.ExpiresAt, cancellationToken);
        await audit.WriteAsync("invitation.created", organizationId, CurrentUserId, details: new { invitation.Id, invitation.Email, role = request.Role.ToString() }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        return CreatedAtAction(nameof(ListInvitations), ToResponse(invitation));
    }

    [HttpGet("invitations")]
    public async Task<ActionResult<IReadOnlyCollection<InvitationResponse>>> ListInvitations(CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        var invitations = await db.Invitations.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return Ok(invitations.Select(ToResponse).ToArray());
    }

    [HttpPost("invitations/{invitationId:guid}/resend")]
    public async Task<IActionResult> Resend(Guid invitationId, CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        var invitation = await db.Invitations.Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == invitationId && x.OrganizationId == organizationId, cancellationToken);
        if (invitation is null) return ApiProblem(StatusCodes.Status404NotFound, "Invitation not found.", "invitation_not_found");
        if (invitation.AcceptedAt is not null || invitation.RevokedAt is not null)
            return ApiProblem(StatusCodes.Status409Conflict, "Invitation is no longer pending.", "invitation_not_pending");

        var code = codeService.GenerateInvitationCode();
        invitation.CodeHash = codeService.Hash(code);
        invitation.ExpiresAt = clock.UtcNow.AddHours(48);
        invitation.FailedAttempts = 0;
        await db.SaveChangesAsync(cancellationToken);
        await emailSender.SendInvitationAsync(invitation.Email, invitation.Organization.Name, code, invitation.ExpiresAt, cancellationToken);
        await audit.WriteAsync("invitation.resent", organizationId, CurrentUserId,
            details: new { invitation.Id }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        return NoContent();
    }

    [HttpDelete("invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid invitationId, CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        var invitation = await db.Invitations.SingleOrDefaultAsync(
            x => x.Id == invitationId && x.OrganizationId == organizationId, cancellationToken);
        if (invitation is null) return ApiProblem(StatusCodes.Status404NotFound, "Invitation not found.", "invitation_not_found");
        if (invitation.AcceptedAt is not null)
            return ApiProblem(StatusCodes.Status409Conflict, "Accepted invitations cannot be revoked.", "invitation_already_accepted");
        invitation.RevokedAt ??= clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("invitation.revoked", organizationId, CurrentUserId,
            details: new { invitation.Id }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        return NoContent();
    }

    [HttpPatch("users/{userId:guid}")]
    public async Task<ActionResult<UserResponse>> UpdateUser(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (request.Role is UserRole.SystemAdmin)
            return ApiProblem(StatusCodes.Status400BadRequest, "SystemAdmin cannot be assigned inside an organization.", "invalid_role");
        var organizationId = RequireOrganizationId();
        var user = await db.Users.Include(x => x.ProductAccesses)
            .SingleOrDefaultAsync(x => x.Id == userId && x.OrganizationId == organizationId, cancellationToken);
        if (user is null) return ApiProblem(StatusCodes.Status404NotFound, "User not found.", "user_not_found");

        var otherAdminIds = await db.Users.Where(x => x.OrganizationId == organizationId && x.Id != userId &&
                x.Role == UserRole.OrganizationAdmin && x.Status == UserStatus.Active)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        if (DomainRules.WouldRemoveLastAdministrator(userId, user.Role, user.Status, request.Role, request.Status, otherAdminIds))
            return ApiProblem(StatusCodes.Status409Conflict, "The last active organization administrator cannot be removed.", "last_organization_admin");

        var securityChanged = user.Role != request.Role || user.Status != request.Status;
        user.Role = request.Role;
        user.Status = request.Status;
        if (request.DisplayName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return ApiProblem(StatusCodes.Status400BadRequest, "Display name cannot be empty.", "invalid_display_name");
            user.DisplayName = request.DisplayName.Trim();
        }
        user.UpdatedAt = clock.UtcNow;

        if (request.Products is not null)
        {
            var requestedProducts = request.Products.Distinct().ToHashSet();
            var existingProducts = user.ProductAccesses.Select(x => x.Product).ToHashSet();
            securityChanged |= !requestedProducts.SetEquals(existingProducts);
            foreach (var removed in user.ProductAccesses.Where(x => !requestedProducts.Contains(x.Product)).ToList())
                db.ProductAccesses.Remove(removed);
            foreach (var added in requestedProducts.Except(existingProducts))
                db.ProductAccesses.Add(new ProductAccess { UserId = user.Id, Product = added, GrantedAt = clock.UtcNow });
        }

        if (securityChanged)
        {
            var sessions = await db.RefreshSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.RevokedAt = clock.UtcNow;
                session.RevocationReason = "user_access_changed";
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(user).Collection(x => x.ProductAccesses).LoadAsync(cancellationToken);
        await audit.WriteAsync("user.updated", organizationId, CurrentUserId, user.Id,
            new { role = user.Role.ToString(), status = user.Status.ToString(), products = user.ProductAccesses.Select(x => x.Product.ToString()) },
            IpAddress, cancellationToken);
        return Ok(ToResponse(user));
    }

    private Guid RequireOrganizationId()
        => CurrentOrganizationId ?? throw new InvalidOperationException("An organization claim is required.");

    private static UserResponse ToResponse(ApplicationUser user)
        => new(user.Id, user.DisplayName, user.Email!, user.OrganizationId, user.Role, user.Status,
            user.ProductAccesses.Select(x => x.Product).Order().ToArray());

    private static InvitationResponse ToResponse(Invitation invitation)
        => new(invitation.Id, invitation.Email, invitation.Role, invitation.CanUseRevit, invitation.CanUseZwcad,
            invitation.ExpiresAt, invitation.AcceptedAt, invitation.RevokedAt);
}
