using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/people")]
[Authorize(Roles = nameof(UserRole.OrganizationAdmin))]
public sealed class WorkforcePeopleController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISecurityCodeService codeService,
    IEmailQueue emailQueue,
    IRegistrationEmailPolicy registrationEmailPolicy,
    IClock clock,
    IAuditService audit) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<WorkforcePersonResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationId();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.WorkforcePeople.AsNoTracking()
            .Include(x => x.MondayIdentity).Include(x => x.VrMaisIdentity)
            .Where(x => x.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(x => x.DisplayName.ToLower().Contains(value) || x.Email.ToLower().Contains(value));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var people = await query.OrderBy(x => x.DisplayName).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        var personIds = people.Select(x => x.Id).ToArray();
        var invitations = await db.Invitations.AsNoTracking().Where(x => x.WorkforcePersonId != null &&
                personIds.Contains(x.WorkforcePersonId.Value))
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        var latestInvitations = invitations.GroupBy(x => x.WorkforcePersonId!.Value)
            .ToDictionary(x => x.Key, x => x.First());

        return Ok(new PagedResponse<WorkforcePersonResponse>(people.Select(person =>
            ToResponse(person, latestInvitations.GetValueOrDefault(person.Id))).ToArray(), page, pageSize, total));
    }

    [HttpPost("invitations")]
    public async Task<ActionResult<InviteWorkforcePersonResponse>> Invite(
        InviteWorkforcePersonRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MondayIdentityId == Guid.Empty || request.VrMaisIdentityId == Guid.Empty)
            return ApiProblem(StatusCodes.Status400BadRequest, "Both external identities are required.", "external_identities_required");
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return ApiProblem(StatusCodes.Status400BadRequest, "Display name cannot be empty.", "invalid_display_name");
        if (!await registrationEmailPolicy.IsAllowedAsync(request.Email, cancellationToken))
            return ApiProblem(StatusCodes.Status400BadRequest, "Email domain is not allowed for registration.", "email_domain_not_allowed");

        var organizationId = RequireOrganizationId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM organizations WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);
        var organization = await db.Organizations.SingleAsync(x => x.Id == organizationId, cancellationToken);
        if (organization.Status != OrganizationStatus.Active)
            return ApiProblem(StatusCodes.Status403Forbidden, "Organization is not active.", "organization_inactive");

        var identities = await db.ExternalWorkforceIdentities.Where(x => x.OrganizationId == organizationId &&
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

        var normalizedEmail = userManager.NormalizeEmail(request.Email)!;
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken) ||
            await db.Invitations.AnyAsync(x => x.Email == normalizedEmail && x.AcceptedAt == null &&
                x.RevokedAt == null && x.ExpiresAt > clock.UtcNow, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "Email already exists or has a pending invitation.", "email_unavailable");

        var now = clock.UtcNow;
        var person = new WorkforcePerson
        {
            OrganizationId = organizationId,
            DisplayName = request.DisplayName.Trim(),
            Email = normalizedEmail,
            MondayIdentityId = monday.Id,
            VrMaisIdentityId = vrMais.Id,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = CurrentUserId
        };
        db.WorkforcePeople.Add(person);
        var code = codeService.GenerateInvitationCode();
        var invitation = new Invitation
        {
            OrganizationId = organizationId,
            WorkforcePersonId = person.Id,
            Email = normalizedEmail,
            Role = UserRole.User,
            CodeHash = codeService.Hash(code),
            CreatedAt = now,
            ExpiresAt = now.AddHours(48),
            CreatedByUserId = CurrentUserId
        };
        db.Invitations.Add(invitation);
        emailQueue.Invitation(request.Email.Trim(), organization.Name, code, invitation.ExpiresAt);
        await audit.WriteAsync("time_control.workforce_person_invited", organizationId, CurrentUserId,
            details: new
            {
                workforcePersonId = person.Id,
                invitationId = invitation.Id,
                mondayIdentityId = monday.Id,
                vrMaisIdentityId = vrMais.Id,
                person.Email
            }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var invitationResponse = ToInvitationResponse(invitation);
        return CreatedAtAction(nameof(List), new InviteWorkforcePersonResponse(
            ToResponse(person, invitation, monday, vrMais), invitationResponse));
    }

    private Guid RequireOrganizationId()
        => CurrentOrganizationId ?? throw new InvalidOperationException("An organization claim is required.");

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

    private static InvitationResponse ToInvitationResponse(Invitation invitation)
        => new(invitation.Id, invitation.Email, invitation.Role, invitation.CanUseRevit, invitation.CanUseZwcad,
            invitation.ExpiresAt, invitation.AcceptedAt, invitation.RevokedAt);
}
