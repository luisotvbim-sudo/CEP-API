using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class InvitationAcceptanceController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISecurityCodeService codeService,
    IRegistrationEmailPolicy registrationEmailPolicy,
    AuthenticationSessionService sessionService,
    IClock clock,
    IAuditService audit) : ApiControllerBase
{
    [HttpPost("invitations/accept")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> AcceptInvitation(
        AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!await registrationEmailPolicy.IsAllowedAsync(request.Email, cancellationToken))
            return ApiProblem(StatusCodes.Status400BadRequest, "Email domain is not allowed for registration.", "email_domain_not_allowed");

        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM invitations WHERE \"Email\" = {normalizedEmail} ORDER BY \"Id\" FOR UPDATE",
            cancellationToken);
        var invitation = await db.Invitations.Include(x => x.Organization)
            .Where(x => x.Email == normalizedEmail && x.AcceptedAt == null && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var now = clock.UtcNow;
        if (invitation is null || invitation.ExpiresAt <= now || invitation.FailedAttempts >= 5 ||
            !codeService.Verify(request.Code, invitation.CodeHash))
        {
            if (invitation is not null)
            {
                invitation.FailedAttempts++;
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return ApiProblem(StatusCodes.Status400BadRequest, "Invitation is invalid or expired.", "invalid_invitation");
        }
        if (invitation.Organization.Status != OrganizationStatus.Active)
            return ApiProblem(StatusCodes.Status403Forbidden, "Organization is not active.", "organization_inactive");
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "The email is already in use.", "email_already_exists");

        WorkforcePerson? workforcePerson = null;
        if (invitation.WorkforcePersonId is { } workforcePersonId)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM workforce_people WHERE \"Id\" = {workforcePersonId} FOR UPDATE", cancellationToken);
            workforcePerson = await db.WorkforcePeople.SingleOrDefaultAsync(x => x.Id == workforcePersonId &&
                x.OrganizationId == invitation.OrganizationId, cancellationToken);
            if (workforcePerson is null || workforcePerson.UserId is not null)
                return ApiProblem(StatusCodes.Status409Conflict,
                    "The workforce identity association is no longer available.", "workforce_person_unavailable");
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            OrganizationId = invitation.OrganizationId,
            Role = invitation.Role,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["password"] = result.Errors.Select(x => x.Description).ToArray()
            })
            { Extensions = { ["code"] = "invalid_password" } });
        }

        AddProductAccesses(invitation, user.Id, now);
        if (workforcePerson is not null)
        {
            workforcePerson.UserId = user.Id;
            workforcePerson.DisplayName = user.DisplayName;
            workforcePerson.Email = normalizedEmail!;
            workforcePerson.UpdatedAt = now;
        }
        invitation.AcceptedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await db.Entry(user).Collection(x => x.ProductAccesses).LoadAsync(cancellationToken);

        var response = await sessionService.CreateAsync(user, request.Client, IpAddress, cancellationToken);
        await audit.WriteAsync("invitation.accepted", user.OrganizationId, user.Id, user.Id,
            ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(response);
    }

    private void AddProductAccesses(Invitation invitation, Guid userId, DateTimeOffset grantedAt)
    {
        if (invitation.CanUseRevit)
            db.ProductAccesses.Add(new ProductAccess { UserId = userId, Product = Product.Revit, GrantedAt = grantedAt });
        if (invitation.CanUseZwcad)
            db.ProductAccesses.Add(new ProductAccess { UserId = userId, Product = Product.Zwcad, GrantedAt = grantedAt });
    }
}
