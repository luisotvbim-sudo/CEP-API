using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Startup;

internal static class BootstrapAdministrator
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(x => x.Role == UserRole.SystemAdmin))
            throw new InvalidOperationException("A SystemAdmin already exists; bootstrap refuses to overwrite it.");

        var email = Required(configuration, "BootstrapAdmin:Email");
        var registrationPolicy = scope.ServiceProvider.GetRequiredService<IRegistrationEmailPolicy>();
        if (!await registrationPolicy.IsAllowedAsync(email))
            throw new InvalidOperationException("Bootstrap administrator email domain is not allowed for registration.");

        var password = Required(configuration, "BootstrapAdmin:Password");
        var displayName = configuration["BootstrapAdmin:DisplayName"] ?? "System Administrator";
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            Role = UserRole.SystemAdmin,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }

    private static string Required(IConfiguration configuration, string key)
        => configuration[key] ?? throw new InvalidOperationException($"{key} is required.");
}
