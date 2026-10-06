using CepApi.Application;
using CepApi.Api.Authorization;
using CepApi.Domain;
using CepApi.Api.Services;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/people")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)},{nameof(UserRole.User)}")]
[OrganizationScope]
public sealed class WorkforcePeopleController(
    AppDbContext db,
    InvitationService invitations,
    IClock clock,
    IAuditService audit,
    TimeControlAccessService accessService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<WorkforcePersonResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = ScopedOrganizationId;
        var access = await accessService.ResolveAsync(
            scopedOrganizationId, CurrentUserId, CurrentRole, cancellationToken);
        (page, pageSize) = NormalizePage(page, pageSize, 200);
        var query = db.WorkforcePeople.AsNoTracking()
            .Include(x => x.MondayIdentity).Include(x => x.VrMaisIdentity)
            .Where(x => x.OrganizationId == scopedOrganizationId);
        if (!access.HasFullAccess)
        {
            // A person's own association remains readable without a current team assignment.
            var visibleUserIds = access.VisibleUserIds.Append(CurrentUserId).Distinct().ToArray();
            query = query.Where(x => x.UserId != null && visibleUserIds.Contains(x.UserId.Value));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(x => x.DisplayName.ToLower().Contains(value) || x.Email.ToLower().Contains(value));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var people = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Page(page, pageSize)
            .ToListAsync(cancellationToken);
        var personIds = people.Select(x => x.Id).ToArray();
        var invitations = await db.Invitations.AsNoTracking().Where(x => x.OrganizationId == scopedOrganizationId && x.WorkforcePersonId != null &&
                personIds.Contains(x.WorkforcePersonId.Value))
            .GroupBy(x => x.WorkforcePersonId).Select(group => group.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First()).ToListAsync(cancellationToken);
        var latestInvitations = invitations.ToDictionary(x => x.WorkforcePersonId!.Value);

        return Ok(new PagedResponse<WorkforcePersonResponse>(people.Select(person =>
            ToResponse(person, latestInvitations.GetValueOrDefault(person.Id))).ToArray(), page, pageSize, total));
    }

    [HttpPost("invitations")]
    [Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
    public async Task<ActionResult<InviteWorkforcePersonResponse>> Invite(
        InviteWorkforcePersonRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (request.MondayIdentityId == Guid.Empty || request.VrMaisIdentityId == Guid.Empty)
            return ApiProblem(StatusCodes.Status400BadRequest, "Both external identities are required.", "external_identities_required");
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return ApiProblem(StatusCodes.Status400BadRequest, "Display name cannot be empty.", "invalid_display_name");
        await invitations.EnsureAllowedEmailAsync(request.Email, cancellationToken);

        var scopedOrganizationId = ScopedOrganizationId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockOrganizationAsync(scopedOrganizationId, cancellationToken);
        var organization = await db.Organizations.SingleAsync(x => x.Id == scopedOrganizationId, cancellationToken);
        if (organization.Status != OrganizationStatus.Active)
            return ApiProblem(StatusCodes.Status403Forbidden, "Organization is not active.", "organization_inactive");

        var identities = await db.ExternalWorkforceIdentities.Where(x => x.OrganizationId == scopedOrganizationId &&
                (x.Id == request.MondayIdentityId || x.Id == request.VrMaisIdentityId))
            .ToListAsync(cancellationToken);
        var monday = identities.SingleOrDefault(x => x.Id == request.MondayIdentityId &&
            x.Source == ExternalWorkforceSource.Monday);
        var vrMais = identities.SingleOrDefault(x => x.Id == request.VrMaisIdentityId &&
            x.Source == ExternalWorkforceSource.VrMais);
        if (monday is null || vrMais is null)
            return ApiProblem(StatusCodes.Status404NotFound, "An external identity was not found in this organization.", "external_identity_not_found");
        if (!monday.IsActive || !vrMais.IsActive)
            return ApiProblem(StatusCodes.Status409Conflict, "Inactive external identities cannot be invited.", "external_identity_inactive");
        if (await db.WorkforcePeople.AnyAsync(x => x.MondayIdentityId == monday.Id ||
            x.VrMaisIdentityId == vrMais.Id, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "An external identity is already associated.", "external_identity_already_mapped");

        var normalizedEmail = await invitations.ReserveEmailAsync(request.Email, cancellationToken);

        var now = clock.UtcNow;
        var person = new WorkforcePerson
        {
            OrganizationId = scopedOrganizationId,
            DisplayName = request.DisplayName.Trim(),
            Email = normalizedEmail,
            MondayIdentityId = monday.Id,
            VrMaisIdentityId = vrMais.Id,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = CurrentUserId
        };
        db.WorkforcePeople.Add(person);
        var invitation = invitations.Create(organization, request.Email, normalizedEmail, UserRole.User,
            [], CurrentUserId, person.Id);
        await audit.WriteAsync("time_control.workforce_person_invited", scopedOrganizationId, CurrentUserId,
            details: new
            {
                workforcePersonId = person.Id,
                invitationId = invitation.Id,
                mondayIdentityId = monday.Id,
                vrMaisIdentityId = vrMais.Id,
                person.Email
            }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var invitationResponse = invitation.ToInvitationResponse();
        return CreatedAtAction(nameof(List), new { organizationId = scopedOrganizationId }, new InviteWorkforcePersonResponse(
            ToResponse(person, invitation, monday, vrMais), invitationResponse));
    }

    private static WorkforcePersonResponse ToResponse(WorkforcePerson person, Invitation? invitation)
        => ToResponse(person, invitation, person.MondayIdentity, person.VrMaisIdentity);

    private static WorkforcePersonResponse ToResponse(
        WorkforcePerson person,
        Invitation? invitation,
        ExternalWorkforceIdentity monday,
        ExternalWorkforceIdentity vrMais)
        => new(person.Id, person.UserId, person.DisplayName, person.Email,
            ToIdentityResponse(monday, person.Id), ToIdentityResponse(vrMais, person.Id),
            invitation?.Id, invitation?.ExpiresAt, invitation?.AcceptedAt, person.CreatedAt, person.UpdatedAt);

    private static ExternalWorkforceIdentityResponse ToIdentityResponse(
        ExternalWorkforceIdentity identity,
        Guid workforcePersonId)
        => new(identity.Id, identity.Source, identity.ExternalId, identity.DisplayName, identity.Email,
            identity.IsActive, identity.LastSeenAt, workforcePersonId);

}
