using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Services;

public sealed class PowerActionUnlockService(AppDbContext db, IClock clock, IAuditService audit, PowerPinHasher hasher)
{
    public async Task<PowerActionUnlockResponse> UnlockAsync(Guid organizationId, Guid userId, string pin,
        string? ipAddress, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Also serializes attempts across all API instances and PIN rotation.
        var configuration = await db.Set<PowerPinConfiguration>()
            .FromSqlRaw("SELECT * FROM time_control.power_pin_configuration WHERE \"Id\" = 1 FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (configuration is null)
            throw new ApiProblemException(503, "O PIN administrativo ainda não foi configurado.", "power_pin_not_configured");
        await db.LockOrganizationAsync(organizationId, cancellationToken);
        await db.LockUserAsync(userId, cancellationToken);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId &&
            x.OrganizationId == organizationId && x.Status == UserStatus.Active, cancellationToken);
        if (user is null || !await db.Organizations.AnyAsync(x => x.Id == organizationId && x.Status == OrganizationStatus.Active, cancellationToken))
            throw new ApiProblemException(403, "An operational organization is required.", "forbidden");

        var since = clock.UtcNow.AddMinutes(-15);
        var attempts = db.AuditEvents.Where(x => x.CreatedAt > since &&
            (x.Action == "power.override_granted" || x.Action == "power.override_denied"));
        if (await attempts.CountAsync(x => x.OrganizationId == organizationId && x.ActorUserId == userId, cancellationToken) >= 5 ||
            await attempts.CountAsync(x => x.OrganizationId == organizationId, cancellationToken) >= 20 ||
            await attempts.CountAsync(cancellationToken) >= 50)
        {
            await audit.WriteAsync("power.override_rate_limited", organizationId, userId, userId,
                ipAddress: ipAddress, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new ApiProblemException(429, "Limite de tentativas atingido. Aguarde antes de tentar novamente.", "power_unlock_rate_limited");
        }
        if (!hasher.Verify(configuration, pin))
        {
            await audit.WriteAsync("power.override_denied", organizationId, userId, userId,
                ipAddress: ipAddress, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new ApiProblemException(403, "PIN administrativo inválido.", "invalid_admin_pin");
        }

        var now = clock.UtcNow;
        var grant = await db.Set<PowerActionOverride>().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (grant is null)
        {
            grant = new PowerActionOverride { UserId = userId, RecipientSecurityVersion = "" };
            db.Add(grant);
        }
        grant.OrganizationId = organizationId;
        grant.PinVersion = configuration.Version;
        grant.RecipientSecurityVersion = JwtTokenService.SecurityVersion(user.SecurityStamp!);
        grant.GrantedAt = now;
        grant.ExpiresAt = now.AddMinutes(5);
        await audit.WriteAsync("power.override_granted", organizationId, userId, userId,
            new { grant.ExpiresAt, grant.PinVersion }, ipAddress, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, grant.ExpiresAt, now);
    }

    public async Task<DateTimeOffset?> GetActiveUntilAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var grant = await db.Set<PowerActionOverride>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.UserId == userId && x.OrganizationId == organizationId && x.ExpiresAt > now &&
            db.Set<PowerPinConfiguration>().Any(p => p.Id == 1 && p.Version == x.PinVersion), cancellationToken);
        if (grant is null) return null;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId &&
            x.OrganizationId == organizationId && x.Status == UserStatus.Active &&
            x.Organization!.Status == OrganizationStatus.Active, cancellationToken);
        return user?.SecurityStamp is { } stamp && grant.RecipientSecurityVersion == JwtTokenService.SecurityVersion(stamp) &&
            grant.ExpiresAt > clock.UtcNow ? grant.ExpiresAt : null;
    }
}
