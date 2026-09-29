using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Services;

public sealed class InvitationService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISecurityCodeService codes,
    IEmailQueue emailQueue,
    IRegistrationEmailPolicy emailPolicy,
    IClock clock,
    IAuditService audit)
{
    public async Task EnsureAllowedEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (!await emailPolicy.IsAllowedAsync(email, cancellationToken))
            throw new ApiProblemException(StatusCodes.Status400BadRequest,
                "Email domain is not allowed for registration.", "email_domain_not_allowed");
    }

    // The caller owns the transaction, including its organization/person and audit.
    public async Task<string> ReserveEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(email)!;
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Invitation creation requires a transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({normalizedEmail}, 372))", cancellationToken);
        var now = clock.UtcNow;
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken) ||
            await db.Invitations.AnyAsync(x => x.Email == normalizedEmail && x.AcceptedAt == null &&
                x.RevokedAt == null && x.ExpiresAt > now, cancellationToken))
            throw new ApiProblemException(StatusCodes.Status409Conflict,
                "Email already exists or has a pending invitation.", "email_unavailable");
        return normalizedEmail;
    }

    public Invitation Create(Organization organization, string email, string normalizedEmail, UserRole role,
        IReadOnlyCollection<Product> products, Guid actorUserId, Guid? workforcePersonId = null)
    {
        var now = clock.UtcNow;
        var code = codes.GenerateInvitationCode();
        var invitation = new Invitation
        {
            OrganizationId = organization.Id,
            Organization = organization,
            WorkforcePersonId = workforcePersonId,
            Email = normalizedEmail,
            Role = role,
            CanUseRevit = products.Contains(Product.Revit),
            CanUseZwcad = products.Contains(Product.Zwcad),
            CodeHash = codes.Hash(code),
            CreatedAt = now,
            ExpiresAt = now.Add(SecurityCodePolicy.InvitationLifetime),
            CreatedByUserId = actorUserId
        };
        db.Invitations.Add(invitation);
        emailQueue.Invitation(email.Trim(), organization.Name, code, invitation.ExpiresAt);
        return invitation;
    }

    public async Task ResendAsync(Guid organizationId, Guid invitationId, Guid actorUserId,
        string action, string? ipAddress, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invitation = await LockInvitationAsync(organizationId, invitationId, cancellationToken);
        if (invitation.AcceptedAt is not null || invitation.RevokedAt is not null)
            throw new ApiProblemException(StatusCodes.Status409Conflict,
                "Invitation is no longer pending.", "invitation_not_pending");
        await EnsureAllowedEmailAsync(invitation.Email, cancellationToken);

        var code = codes.GenerateInvitationCode();
        invitation.CodeHash = codes.Hash(code);
        invitation.ExpiresAt = clock.UtcNow.Add(SecurityCodePolicy.InvitationLifetime);
        invitation.FailedAttempts = 0;
        emailQueue.Invitation(invitation.Email, invitation.Organization.Name, code, invitation.ExpiresAt);
        await audit.WriteAsync(action, organizationId, actorUserId, details: new { invitation.Id },
            ipAddress: ipAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid organizationId, Guid invitationId, Guid actorUserId,
        string? ipAddress, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invitation = await LockInvitationAsync(organizationId, invitationId, cancellationToken);
        if (invitation.AcceptedAt is not null)
            throw new ApiProblemException(StatusCodes.Status409Conflict,
                "Accepted invitations cannot be revoked.", "invitation_already_accepted");
        invitation.RevokedAt ??= clock.UtcNow;
        await audit.WriteAsync("invitation.revoked", organizationId, actorUserId, details: new { invitation.Id },
            ipAddress: ipAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Invitation> LockInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM invitations WHERE \"Id\" = {invitationId} AND \"OrganizationId\" = {organizationId} FOR UPDATE",
            cancellationToken);
        return await db.Invitations.Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == invitationId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new ApiProblemException(StatusCodes.Status404NotFound, "Invitation not found.", "invitation_not_found");
    }
}
