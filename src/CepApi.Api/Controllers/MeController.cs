using CepApi.Application;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/me")]
[Authorize]
public sealed class MeController(AppDbContext db, UserManager<ApplicationUser> userManager, IClock clock, IAuditService audit) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserResponse>> Get(CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.ProductAccesses)
            .SingleAsync(x => x.Id == CurrentUserId, cancellationToken);
        return Ok(ToResponse(user));
    }

    [HttpPatch]
    public async Task<ActionResult<UserResponse>> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.Include(x => x.ProductAccesses)
            .SingleAsync(x => x.Id == CurrentUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return ApiProblem(StatusCodes.Status400BadRequest, "Display name cannot be empty.", "invalid_display_name");

        user.DisplayName = request.DisplayName.Trim();
        user.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("user.profile_updated", user.OrganizationId, user.Id, user.Id,
            ipAddress: IpAddress, cancellationToken: cancellationToken);
        return Ok(ToResponse(user));
    }

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(CurrentUserId.ToString());
        if (user is null) return ApiProblem(StatusCodes.Status404NotFound, "User not found.", "user_not_found");

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["newPassword"] = result.Errors.Select(x => x.Description).ToArray()
            })
            { Extensions = { ["code"] = "password_change_failed" } });

        var sessions = await db.RefreshSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = clock.UtcNow;
            session.RevocationReason = "password_changed";
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("auth.password_changed", user.OrganizationId, user.Id, user.Id, ipAddress: IpAddress, cancellationToken: cancellationToken);
        return NoContent();
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyCollection<SessionResponse>>> Sessions(CancellationToken cancellationToken)
    {
        var sessions = await db.RefreshSessions.AsNoTracking()
            .Where(x => x.UserId == CurrentUserId && x.RevokedAt == null && x.ExpiresAt > clock.UtcNow)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new SessionResponse(x.Id, x.ClientType, x.ClientVersion, x.InstallationId, x.IpAddress,
                x.CreatedAt, x.ExpiresAt, x.LastUsedAt)).ToListAsync(cancellationToken);
        return Ok(sessions);
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.RefreshSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == CurrentUserId, cancellationToken);
        if (session is null) return ApiProblem(StatusCodes.Status404NotFound, "Session not found.", "session_not_found");
        if (session.RevokedAt is null)
        {
            session.RevokedAt = clock.UtcNow;
            session.RevocationReason = "user_revoked";
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync("session.revoked", CurrentOrganizationId, CurrentUserId, CurrentUserId,
                new { sessionId }, IpAddress, cancellationToken);
        }
        return NoContent();
    }

    private static UserResponse ToResponse(ApplicationUser user)
        => new(user.Id, user.DisplayName, user.Email!, user.OrganizationId, user.Role, user.Status,
            user.ProductAccesses.Select(x => x.Product).Order().ToArray());
}
