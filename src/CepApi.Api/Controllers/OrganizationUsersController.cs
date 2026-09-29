using CepApi.Application;
using CepApi.Domain;
using CepApi.Api.Services;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
[OrganizationScope]
public sealed class OrganizationUsersController(
    AppDbContext db,
    InvitationService invitations,
    IClock clock,
    IAuditService audit) : ApiControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<PagedResponse<UserResponse>>> ListUsers(
        [FromQuery] string? search, [FromQuery] UserStatus? status, [FromQuery] UserRole? role,
        [FromQuery] Product? product, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = ScopedOrganizationId;
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.Users.AsNoTracking().Include(x => x.ProductAccesses).Where(x => x.OrganizationId == scopedOrganizationId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(x => x.DisplayName.ToLower().Contains(value) || x.Email!.ToLower().Contains(value));
        }
        if (status is not null) query = query.Where(x => x.Status == status);
        if (role is not null) query = query.Where(x => x.Role == role);
        if (product is not null) query = query.Where(x => x.ProductAccesses.Any(access => access.Product == product));

        var total = await query.LongCountAsync(cancellationToken);
        var users = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Page(page, pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<UserResponse>(users.Select(x => x.ToUserResponse()).ToArray(), page, pageSize, total));
    }

    [HttpPost("invitations")]
    public async Task<ActionResult<InvitationResponse>> Invite(
        InviteUserRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        await invitations.EnsureAllowedEmailAsync(request.Email, cancellationToken);
        if (request.Role is UserRole.SystemAdmin)
            return ApiProblem(StatusCodes.Status400BadRequest, "SystemAdmin cannot be assigned inside an organization.", "invalid_role");
        var scopedOrganizationId = ScopedOrganizationId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var organization = await db.Organizations.SingleAsync(x => x.Id == scopedOrganizationId, cancellationToken);
        if (organization.Status != OrganizationStatus.Active)
            return ApiProblem(StatusCodes.Status403Forbidden, "Organization is not active.", "organization_inactive");

        var normalizedEmail = await invitations.ReserveEmailAsync(request.Email, cancellationToken);

        var invitation = invitations.Create(organization, request.Email, normalizedEmail, request.Role,
            request.Products ?? [], CurrentUserId);
        await audit.WriteAsync("invitation.created", scopedOrganizationId, CurrentUserId, details: new { invitation.Id, invitation.Email, role = request.Role.ToString() }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CreatedAtAction(nameof(ListInvitations),
            new { organizationId = scopedOrganizationId }, invitation.ToInvitationResponse());
    }

    [HttpGet("invitations")]
    public async Task<ActionResult<IReadOnlyCollection<InvitationResponse>>> ListInvitations(
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = ScopedOrganizationId;
        var invitations = await db.Invitations.AsNoTracking().Where(x => x.OrganizationId == scopedOrganizationId)
            .OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return Ok(invitations.Select(x => x.ToInvitationResponse()).ToArray());
    }

    [HttpPost("invitations/{invitationId:guid}/resend")]
    public async Task<IActionResult> Resend(
        Guid invitationId,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        await invitations.ResendAsync(ScopedOrganizationId, invitationId, CurrentUserId,
            "invitation.resent", IpAddress, cancellationToken);
        return NoContent();
    }

    [HttpDelete("invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeInvitation(
        Guid invitationId,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        await invitations.RevokeAsync(ScopedOrganizationId, invitationId, CurrentUserId, IpAddress, cancellationToken);
        return NoContent();
    }

    [HttpPatch("users/{userId:guid}")]
    public async Task<ActionResult<UserResponse>> UpdateUser(
        Guid userId,
        UpdateUserRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (request.Role is UserRole.SystemAdmin)
            return ApiProblem(StatusCodes.Status400BadRequest, "SystemAdmin cannot be assigned inside an organization.", "invalid_role");
        var scopedOrganizationId = ScopedOrganizationId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize the last-administrator check for the organization, then account changes.
        await db.LockOrganizationAsync(scopedOrganizationId, cancellationToken);
        await db.LockUserAsync(userId, cancellationToken);
        var user = await db.Users.Include(x => x.ProductAccesses)
            .SingleOrDefaultAsync(x => x.Id == userId && x.OrganizationId == scopedOrganizationId, cancellationToken);
        if (user is null) return ApiProblem(StatusCodes.Status404NotFound, "User not found.", "user_not_found");

        var hasOtherAdmin = await db.Users.AnyAsync(x => x.OrganizationId == scopedOrganizationId && x.Id != userId &&
            x.Role == UserRole.OrganizationAdmin && x.Status == UserStatus.Active, cancellationToken);
        if (DomainRules.WouldRemoveLastAdministrator(user.Role, user.Status, request.Role, request.Status, hasOtherAdmin))
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
            user.SecurityStamp = Guid.NewGuid().ToString();
            await db.RevokeUserSessionsAsync(user.Id, clock.UtcNow, "user_access_changed", cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(user).Collection(x => x.ProductAccesses).LoadAsync(cancellationToken);
        await audit.WriteAsync("user.updated", scopedOrganizationId, CurrentUserId, user.Id,
            new { role = user.Role.ToString(), status = user.Status.ToString(), products = user.ProductAccesses.Select(x => x.Product.ToString()) },
            IpAddress, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(user.ToUserResponse());
    }

}
