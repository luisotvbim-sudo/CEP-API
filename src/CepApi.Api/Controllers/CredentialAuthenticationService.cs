using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

public sealed class CredentialAuthenticationService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AuthenticationSessionService sessionService,
    IAuditService audit)
{
    public async Task<TokenResponse> AuthenticateAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.Email);
        var userId = await db.Users.AsNoTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            _ = userManager.PasswordHasher.HashPassword(
                new ApplicationUser { DisplayName = "dummy" }, request.Password);
            await audit.WriteAsync("auth.login_failed", details: new { reason = "invalid_credentials" },
                ipAddress: ipAddress, cancellationToken: cancellationToken);
            throw InvalidCredentials();
        }

        await using var transaction = await db.BeginForUserAsync(userId.Value, cancellationToken);
        var user = await db.Users.Include(x => x.Organization).Include(x => x.ProductAccesses)
            .SingleAsync(x => x.Id == userId, cancellationToken);
        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (!passwordResult.Succeeded || user.Status != UserStatus.Active ||
            user.Organization is { Status: not OrganizationStatus.Active })
        {
            await audit.WriteAsync("auth.login_failed", user.OrganizationId, user.Id, user.Id,
                new { reason = passwordResult.IsLockedOut ? "locked_out" : "invalid_or_inactive" },
                ipAddress, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw InvalidCredentials();
        }

        var response = await sessionService.CreateAsync(user, request.Client, ipAddress, cancellationToken);
        await audit.WriteAsync("auth.login_succeeded", user.OrganizationId, user.Id, user.Id,
            new { client = request.Client?.Type ?? "unknown" }, ipAddress, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static ApiProblemException InvalidCredentials()
        => new(StatusCodes.Status401Unauthorized, "Authentication failed.", "invalid_credentials");
}
