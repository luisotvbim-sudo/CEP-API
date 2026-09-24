using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CepApi.Api.Controllers;

public sealed class RefreshTokenSessionService(
    AppDbContext db,
    ITokenService tokenService,
    IClock clock,
    IAuditService audit,
    IOptions<JwtOptions> jwtOptions)
{
    public async Task<TokenResponse> RotateAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(refreshToken);
        var userId = await db.RefreshSessions.AsNoTracking().Where(x => x.TokenHash == hash)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(cancellationToken);
        if (userId is null)
            throw InvalidRefreshToken();

        await using var transaction = await db.BeginForUserAsync(userId.Value, cancellationToken);
        var session = await db.RefreshSessions.SingleAsync(x => x.TokenHash == hash, cancellationToken);
        if (session.RevokedAt is not null)
        {
            await RevokeReusedFamilyAsync(session, ipAddress, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw InvalidRefreshToken();
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
            await transaction.CommitAsync(cancellationToken);
            throw InvalidRefreshToken();
        }

        var replacementToken = tokenService.CreateRefreshToken();
        var replacement = CreateReplacement(session, user.Id, replacementToken, ipAddress, now);
        session.LastUsedAt = now;
        session.RevokedAt = now;
        session.RevocationReason = "rotated";
        session.ReplacedBySessionId = replacement.Id;
        db.RefreshSessions.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);

        var access = tokenService.CreateAccessToken(ToTokenUser(user), replacement.FamilyId,
            user.SecurityStamp!, now);
        await transaction.CommitAsync(cancellationToken);
        return new TokenResponse(access.Token, access.ExpiresAt, replacementToken,
            replacement.ExpiresAt, user.ToUserResponse());
    }

    public async Task LogoutAsync(
        string refreshToken,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(refreshToken);
        var session = await db.RefreshSessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (session is null)
            return;

        await using var transaction = await db.BeginForUserAsync(session.UserId, cancellationToken);
        await db.RevokeFamilyAsync(session.UserId, session.FamilyId, clock.UtcNow, "logout", cancellationToken);
        await audit.WriteAsync("auth.logout", actorUserId: session.UserId, targetUserId: session.UserId,
            ipAddress: ipAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private RefreshSession CreateReplacement(
        RefreshSession session,
        Guid userId,
        string refreshToken,
        string? ipAddress,
        DateTimeOffset now)
        => new()
        {
            FamilyId = session.FamilyId,
            UserId = userId,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            ClientType = session.ClientType,
            ClientVersion = session.ClientVersion,
            InstallationId = session.InstallationId,
            IpAddress = ipAddress,
            CreatedAt = now,
            ExpiresAt = now.AddDays(jwtOptions.Value.RefreshTokenDays)
        };

    private async Task RevokeReusedFamilyAsync(
        RefreshSession session,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (session.ReplacedBySessionId is null)
            return;

        var family = await db.RefreshSessions
            .Where(x => x.FamilyId == session.FamilyId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var item in family)
        {
            item.RevokedAt = clock.UtcNow;
            item.RevocationReason = "refresh_token_reuse";
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("auth.refresh_reuse_detected", actorUserId: session.UserId,
            targetUserId: session.UserId, ipAddress: ipAddress, cancellationToken: cancellationToken);
    }

    private static TokenUser ToTokenUser(ApplicationUser user)
        => new(user.Id, user.DisplayName, user.Email!, user.OrganizationId, user.Role);

    private static ApiProblemException InvalidRefreshToken()
        => new(StatusCodes.Status401Unauthorized, "Refresh failed.", "invalid_refresh_token");
}
