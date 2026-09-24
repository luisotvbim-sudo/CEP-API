using System.Diagnostics;
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
public sealed class PasswordRecoveryController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISecurityCodeService codeService,
    IEmailQueue emailQueue,
    AuthenticationSessionService sessionService,
    IClock clock,
    IAuditService audit) : ApiControllerBase
{
    private static readonly TimeSpan MinimumResponseTime = TimeSpan.FromMilliseconds(250);

    [HttpPost("password/forgot")]
    [EnableRateLimiting("recovery-request")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is { Status: UserStatus.Active })
        {
            await RequestResetAsync(user, cancellationToken);
        }
        else
        {
            await audit.WriteAsync("auth.password_reset_request_suppressed",
                details: new { reason = "unknown_or_inactive" }, ipAddress: IpAddress,
                cancellationToken: cancellationToken);
        }

        await PadResponseAsync(started, cancellationToken);
        return Accepted();
    }

    [HttpPost("password/reset")]
    [EnableRateLimiting("recovery-verify")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        var userId = await db.Users.AsNoTracking().Where(x => x.NormalizedEmail == normalizedEmail)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (userId is null)
            return await InvalidResetCodeAsync(started, cancellationToken);

        await using var transaction = await db.BeginForUserAsync(userId.Value, cancellationToken);
        var user = await db.Users.SingleAsync(x => x.Id == userId, cancellationToken);
        var reset = await db.PasswordResets.AsNoTracking().Where(x => x.UserId == user.Id && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var now = clock.UtcNow;
        if (reset is null || reset.ExpiresAt <= now || reset.FailedAttempts >= 5 ||
            !codeService.Verify(request.Code, reset.CodeHash))
        {
            if (reset is not null && reset.ExpiresAt > now && reset.FailedAttempts < 5)
            {
                await db.PasswordResets.Where(x => x.Id == reset.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1), cancellationToken);
            }
            await audit.WriteAsync("auth.password_reset_failed", user.OrganizationId, targetUserId: user.Id,
                details: new { reason = "invalid_or_expired" }, ipAddress: IpAddress,
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await PadResponseAsync(started, cancellationToken);
            return ApiProblem(StatusCodes.Status400BadRequest,
                "Reset code is invalid or expired.", "invalid_reset_code");
        }

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, request.NewPassword);
        if (!result.Succeeded)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["newPassword"] = result.Errors.Select(x => x.Description).ToArray()
            })
            { Extensions = { ["code"] = "invalid_password" } });
        }

        await db.InvalidateResetCodesAsync(user.Id, now, cancellationToken);
        await sessionService.RevokeAllAsync(user.Id, "password_reset", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("auth.password_reset", user.OrganizationId, user.Id, user.Id,
            ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task RequestResetAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        await using var transaction = await db.BeginForUserAsync(user.Id, cancellationToken);
        if (await db.Users.AnyAsync(x => x.Id == user.Id && x.Status == UserStatus.Active, cancellationToken))
        {
            var now = clock.UtcNow;
            var activeResetExists = await db.PasswordResets.AsNoTracking().AnyAsync(x =>
                x.UserId == user.Id && x.UsedAt == null && x.ExpiresAt > now, cancellationToken);
            if (!activeResetExists)
            {
                await db.InvalidateResetCodesAsync(user.Id, now, cancellationToken);
                var code = codeService.GeneratePasswordResetCode();
                var expiresAt = now.AddMinutes(15);
                db.PasswordResets.Add(new PasswordReset
                {
                    UserId = user.Id,
                    CodeHash = codeService.Hash(code),
                    CreatedAt = now,
                    ExpiresAt = expiresAt
                });
                emailQueue.PasswordReset(user.Email!, code, expiresAt);
                await audit.WriteAsync("auth.password_reset_requested", user.OrganizationId, targetUserId: user.Id,
                    ipAddress: IpAddress, cancellationToken: cancellationToken);
            }
            else
            {
                await audit.WriteAsync("auth.password_reset_request_suppressed", user.OrganizationId,
                    targetUserId: user.Id, ipAddress: IpAddress, cancellationToken: cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<IActionResult> InvalidResetCodeAsync(long started, CancellationToken cancellationToken)
    {
        await audit.WriteAsync("auth.password_reset_failed", details: new { reason = "invalid_or_expired" },
            ipAddress: IpAddress, cancellationToken: cancellationToken);
        await PadResponseAsync(started, cancellationToken);
        return ApiProblem(StatusCodes.Status400BadRequest,
            "Reset code is invalid or expired.", "invalid_reset_code");
    }

    private static async Task PadResponseAsync(long started, CancellationToken cancellationToken)
    {
        var remaining = MinimumResponseTime - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken);
    }
}
