using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CepApi.Api.Controllers;

public sealed class AuthenticationSessionService(
    AppDbContext db,
    ITokenService tokenService,
    IClock clock,
    IOptions<JwtOptions> jwtOptions)
{
    public async Task<TokenResponse> CreateAsync(
        ApplicationUser user,
        ClientInfo? client,
        string? ipAddress,
        CancellationToken cancellationToken,
        bool web = false)
    {
        var now = clock.UtcNow;
        var refreshToken = tokenService.CreateRefreshToken();
        var session = new RefreshSession
        {
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            ClientType = web ? "cep-horas-browser" : Truncate(client?.Type ?? "unknown", 50)!,
            ClientVersion = Truncate(client?.Version, 50),
            InstallationId = Truncate(client?.InstallationId, 200),
            IpAddress = ipAddress,
            CreatedAt = now,
            ExpiresAt = now.AddDays(web ? Math.Min(7, jwtOptions.Value.RefreshTokenDays) : jwtOptions.Value.RefreshTokenDays)
        };
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        var tokenUser = new TokenUser(user.Id, user.DisplayName, user.Email!, user.OrganizationId, user.Role);
        var access = tokenService.CreateAccessToken(tokenUser, session.FamilyId, user.SecurityStamp!, now);
        return new TokenResponse(access.Token, access.ExpiresAt, refreshToken, session.ExpiresAt,
            user.ToUserResponse());
    }

    public async Task RevokeAllAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var sessions = await db.RefreshSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = clock.UtcNow;
            session.RevocationReason = reason;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, maxLength)];
    }
}
