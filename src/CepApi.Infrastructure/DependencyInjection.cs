using CepApi.Application;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CepApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<SecurityCodeOptions>(configuration.GetSection(SecurityCodeOptions.SectionName));
        services.Configure<WorkforceIntegrationOptions>(configuration.GetSection(WorkforceIntegrationOptions.SectionName));
        var protection = services.AddDataProtection().SetApplicationName("CEP-API");
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.CommandTimeout(configuration.GetValue("Database:CommandTimeoutSeconds", 30));
            }));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISecurityCodeService, SecurityCodeService>();
        services.AddSingleton<JwtKeyRing>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IEmailQueue, EmailOutbox>();
        services.AddScoped<IRegistrationEmailPolicy, RegistrationEmailPolicy>();
        services.AddScoped<IWorkforceDirectorySyncService, WorkforceDirectorySyncService>();
        services.AddHttpClient<MondayDirectorySource>(client => client.Timeout = TimeSpan.FromSeconds(45))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<VrMaisDirectorySource>(client => client.Timeout = TimeSpan.FromSeconds(45))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IExternalWorkforceDirectorySource>(provider => provider.GetRequiredService<MondayDirectorySource>());
        services.AddScoped<IExternalWorkforceDirectorySource>(provider => provider.GetRequiredService<VrMaisDirectorySource>());
        services.AddScoped<IExternalWorkforceTimeSource>(provider => provider.GetRequiredService<MondayDirectorySource>());
        services.AddScoped<IExternalWorkforceTimeSource>(provider => provider.GetRequiredService<VrMaisDirectorySource>());
        services.AddScoped<EmailOutboxDispatcher>();
        if (configuration.GetValue("EmailOutbox:Enabled", true))
            services.AddHostedService<EmailOutboxWorker>();
        if (environment.IsDevelopment())
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
