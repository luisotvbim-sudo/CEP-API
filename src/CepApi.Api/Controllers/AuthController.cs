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
using Microsoft.Extensions.Options;

namespace CepApi.Api.Controllers;

[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokenService,
    ISecurityCodeService codeService,
    IEmailSender emailSender,
    IClock clock,
    IAuditService audit,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthController> logger) : ApiControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        var user = await db.Users.Include(x => x.Organization).Include(x => x.ProductAccesses)
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            _ = userManager.PasswordHasher.HashPassword(new ApplicationUser { DisplayName = "dummy" }, request.Password);
            await audit.WriteAsync("auth.login_failed", details: new { reason = "invalid_credentials" }, ipAddress: IpAddress, cancellationToken: cancellationToken);
            return ApiProblem(StatusCodes.Status401Unauthorized, "Authentication failed.", "invalid_credentials");
        }

        var passwordResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!passwordResult.Succeeded || user.Status != UserStatus.Active ||
            user.Organization is { Status: not OrganizationStatus.Active })
        {
            await audit.WriteAsync("auth.login_failed", user.OrganizationId, user.Id, user.Id,
                new { reason = passwordResult.IsLockedOut ? "locked_out" : "invalid_or_inactive" }, IpAddress, cancellationToken);
            return ApiProblem(StatusCodes.Status401Unauthorized, "Authentication failed.", "invalid_credentials");
        }

        var response = await CreateSessionAsync(user, request.Client, cancellationToken);
        await audit.WriteAsync("auth.login_succeeded", user.OrganizationId, user.Id, user.Id,
            new { client = request.Client?.Type ?? "unknown" }, IpAddress, cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var session = await db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (session is null)
            return ApiProblem(StatusCodes.Status401Unauthorized, "Refresh failed.", "invalid_refresh_token");

        if (session.RevokedAt is not null)
        {
            if (session.ReplacedBySessionId is not null)
            {
                var family = await db.RefreshSessions
                    .Where(x => x.FamilyId == session.FamilyId && x.RevokedAt == null).ToListAsync(cancellationToken);
                foreach (var item in family)
                {
                    item.RevokedAt = clock.UtcNow;
                    item.RevocationReason = "refresh_token_reuse";
                }
                await db.SaveChangesAsync(cancellationToken);
                await audit.WriteAsync("auth.refresh_reuse_detected", actorUserId: session.UserId,
                    targetUserId: session.UserId, ipAddress: IpAddress, cancellationToken: cancellationToken);
            }
            return ApiProblem(StatusCodes.Status401Unauthorized, "Refresh failed.", "invalid_refresh_token");
        }

        var user = await db.Users.Include(x => x.Organization).Include(x => x.ProductAccesses)
            .SingleOrDefaultAsync(x => x.Id == session.UserId, cancellationToken);
        var now = clock.UtcNow;
        if (user is null || session.ExpiresAt <= now || user.Status != UserStatus.Active ||
            user.Organization is { Status: not OrganizationStatus.Active })
        {
            session.RevokedAt = now;
            session.RevocationReason = "expired_or_inactive";
            await db.SaveChangesAsync(cancellationToken);
            return ApiProblem(StatusCodes.Status401Unauthorized, "Refresh failed.", "invalid_refresh_token");
        }

        var refreshToken = tokenService.CreateRefreshToken();
        var replacement = new RefreshSession
        {
            FamilyId = session.FamilyId,
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            ClientType = session.ClientType,
            ClientVersion = session.ClientVersion,
            InstallationId = session.InstallationId,
            IpAddress = IpAddress,
            CreatedAt = now,
            ExpiresAt = now.AddDays(jwtOptions.Value.RefreshTokenDays)
        };
        session.LastUsedAt = now;
        session.RevokedAt = now;
        session.RevocationReason = "rotated";
        session.ReplacedBySessionId = replacement.Id;
        db.RefreshSessions.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);

        var access = tokenService.CreateAccessToken(ToTokenUser(user), now);
        return Ok(new TokenResponse(access.Token, access.ExpiresAt, refreshToken, replacement.ExpiresAt, ToResponse(user)));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var session = await db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (session is { RevokedAt: null })
        {
            session.RevokedAt = clock.UtcNow;
            session.RevocationReason = "logout";
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync("auth.logout", actorUserId: session.UserId, targetUserId: session.UserId,
                ipAddress: IpAddress, cancellationToken: cancellationToken);
        }
        return NoContent();
    }

    [HttpPost("invitations/accept")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> AcceptInvitation(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email);
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
            return ApiProblem(StatusCodes.Status400BadRequest, "Invitation is invalid or expired.", "invalid_invitation");
        }
        if (invitation.Organization.Status != OrganizationStatus.Active)
            return ApiProblem(StatusCodes.Status403Forbidden, "Organization is not active.", "organization_inactive");
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "The email is already in use.", "email_already_exists");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
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
        if (invitation.CanUseRevit) db.ProductAccesses.Add(new ProductAccess { UserId = user.Id, Product = Product.Revit, GrantedAt = now });
        if (invitation.CanUseZwcad) db.ProductAccesses.Add(new ProductAccess { UserId = user.Id, Product = Product.Zwcad, GrantedAt = now });
        invitation.AcceptedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await db.Entry(user).Collection(x => x.ProductAccesses).LoadAsync(cancellationToken);
        var response = await CreateSessionAsync(user, request.Client, cancellationToken);
        await audit.WriteAsync("invitation.accepted", user.OrganizationId, user.Id, user.Id, ipAddress: IpAddress, cancellationToken: cancellationToken);
        return Ok(response);
    }

    [HttpPost("password/forgot")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is { Status: UserStatus.Active })
        {
            var now = clock.UtcNow;
            var code = codeService.GeneratePasswordResetCode();
            db.PasswordResets.Add(new PasswordReset
            {
                UserId = user.Id,
                CodeHash = codeService.Hash(code),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(15)
            });
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                await emailSender.SendPasswordResetAsync(user.Email!, code, now.AddMinutes(15), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Password reset email delivery failed for user {UserId}.", user.Id);
            }
        }
        return Accepted();
    }

    [HttpPost("password/reset")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null)
            return ApiProblem(StatusCodes.Status400BadRequest, "Reset code is invalid or expired.", "invalid_reset_code");

        var reset = await db.PasswordResets.Where(x => x.UserId == user.Id && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var now = clock.UtcNow;
        if (reset is null || reset.ExpiresAt <= now || reset.FailedAttempts >= 5 || !codeService.Verify(request.Code, reset.CodeHash))
        {
            if (reset is not null)
            {
                reset.FailedAttempts++;
                await db.SaveChangesAsync(cancellationToken);
            }
            return ApiProblem(StatusCodes.Status400BadRequest, "Reset code is invalid or expired.", "invalid_reset_code");
        }

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, request.NewPassword);
        if (!result.Succeeded)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["newPassword"] = result.Errors.Select(x => x.Description).ToArray()
            })
            { Extensions = { ["code"] = "invalid_password" } });

        reset.UsedAt = now;
        await RevokeSessionsAsync(user.Id, "password_reset", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("auth.password_reset", user.OrganizationId, user.Id, user.Id, ipAddress: IpAddress, cancellationToken: cancellationToken);
        return NoContent();
    }

    private async Task<TokenResponse> CreateSessionAsync(ApplicationUser user, ClientInfo? client, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var refreshToken = tokenService.CreateRefreshToken();
        var session = new RefreshSession
        {
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            ClientType = Truncate(client?.Type ?? "unknown", 50)!,
            ClientVersion = Truncate(client?.Version, 50),
            InstallationId = Truncate(client?.InstallationId, 200),
            IpAddress = IpAddress,
            CreatedAt = now,
            ExpiresAt = now.AddDays(jwtOptions.Value.RefreshTokenDays)
        };
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        var access = tokenService.CreateAccessToken(ToTokenUser(user), now);
        return new TokenResponse(access.Token, access.ExpiresAt, refreshToken, session.ExpiresAt, ToResponse(user));
    }

    private async Task RevokeSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var sessions = await db.RefreshSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = clock.UtcNow;
            session.RevocationReason = reason;
        }
    }

    private static TokenUser ToTokenUser(ApplicationUser user)
        => new(user.Id, user.DisplayName, user.Email!, user.OrganizationId, user.Role);

    private static UserResponse ToResponse(ApplicationUser user)
        => new(user.Id, user.DisplayName, user.Email!, user.OrganizationId, user.Role, user.Status,
            user.ProductAccesses.Select(x => x.Product).Order().ToArray());

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
