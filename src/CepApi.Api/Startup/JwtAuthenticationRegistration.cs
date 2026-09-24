using System.Security.Claims;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CepApi.Api.Startup;

internal static class JwtAuthenticationRegistration
{
    public static void Add(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtKeyRing, IOptions<JwtOptions>>(ConfigureBearer);
        services.AddAuthorization();
    }

    private static void ConfigureBearer(
        JwtBearerOptions bearer,
        JwtKeyRing keyRing,
        IOptions<JwtOptions> jwtOptions)
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keyRing.ValidationKeys,
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.ApiAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidTypes = ["at+jwt"],
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        bearer.Events = new JwtBearerEvents { OnTokenValidated = ValidateSessionAsync };
    }

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var userId))
        {
            context.Fail("Invalid subject.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);
        if (user is null || user.Status != UserStatus.Active ||
            user.Organization is { Status: not OrganizationStatus.Active })
        {
            context.Fail("Account is not active.");
            return;
        }

        var now = services.GetRequiredService<IClock>().UtcNow;
        if (!await HasActiveSession(context, user, db, now))
        {
            context.Fail("Session has been revoked.");
            return;
        }

        var tokenRole = context.Principal?.FindFirstValue("role");
        var tokenOrganization = context.Principal?.FindFirstValue("org_id");
        if (!string.Equals(tokenRole, user.Role.ToString(), StringComparison.Ordinal) ||
            !string.Equals(tokenOrganization, user.OrganizationId?.ToString(), StringComparison.Ordinal))
            context.Fail("Token authorization claims are stale.");
    }

    private static async Task<bool> HasActiveSession(
        TokenValidatedContext context,
        ApplicationUser user,
        AppDbContext db,
        DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(user.SecurityStamp) ||
            context.Principal?.FindFirstValue("security_version") != JwtTokenService.SecurityVersion(user.SecurityStamp) ||
            !Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var familyId))
            return false;

        return await db.RefreshSessions.AsNoTracking().AnyAsync(x => x.UserId == user.Id &&
            x.FamilyId == familyId && x.RevokedAt == null && x.ExpiresAt > now,
            context.HttpContext.RequestAborted);
    }
}
