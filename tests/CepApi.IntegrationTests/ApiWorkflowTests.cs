using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace CepApi.IntegrationTests;

public sealed class ApiWorkflowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    [Fact]
    public async Task Complete_invitation_login_and_product_grant_workflow_is_isolated_and_signed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        PostgreSqlContainer postgres;
        try
        {
            postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("cep_api_tests").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(cancellationToken);
        }
        catch when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("Docker is not available; PostgreSQL integration tests run in CI.");
            return;
        }
        await using var postgresLifetime = postgres;

        var email = new CapturingEmailSender();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", postgres.GetConnectionString());
            builder.UseSetting("EmailOutbox:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(email);
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        await SeedDatabaseAsync(factory.Services, cancellationToken);

        var systemTokens = await LoginAsync(client, "system@example.com", "correct horse battery staple", cancellationToken);
        UseToken(client, systemTokens.AccessToken);
        var createOrganization = await client.PostAsJsonAsync("/api/v1/admin/organizations", new CreateOrganizationRequest(
            "Acme Engenharia", "acme-engenharia", "admin@acme.test", [Product.Revit, Product.Zwcad]), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createOrganization.StatusCode);
        await DispatchEmailAsync(factory.Services, cancellationToken);
        var adminCode = email.InvitationCodes["admin@acme.test"];

        client.DefaultRequestHeaders.Authorization = null;
        var acceptAdmin = await client.PostAsJsonAsync("/api/v1/auth/invitations/accept", new AcceptInvitationRequest(
            "admin@acme.test", adminCode, "Acme Admin", "correct horse battery staple", new ClientInfo("test")), cancellationToken);
        acceptAdmin.EnsureSuccessStatusCode();
        var adminTokens = (await acceptAdmin.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken))!;

        UseToken(client, adminTokens.AccessToken);
        var inviteUser = await client.PostAsJsonAsync("/api/v1/organization/invitations", new InviteUserRequest(
            "user@acme.test", UserRole.User, [Product.Revit]), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, inviteUser.StatusCode);
        await DispatchEmailAsync(factory.Services, cancellationToken);

        client.DefaultRequestHeaders.Authorization = null;
        var acceptUser = await client.PostAsJsonAsync("/api/v1/auth/invitations/accept", new AcceptInvitationRequest(
            "user@acme.test", email.InvitationCodes["user@acme.test"], "Plugin User",
            "correct horse battery staple", new ClientInfo("revit", "2026.1", "install-1")), cancellationToken);
        acceptUser.EnsureSuccessStatusCode();
        var userTokens = (await acceptUser.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken))!;
        UseToken(client, userTokens.AccessToken);

        var revitGrantResponse = await client.PostAsJsonAsync("/api/v1/plugin/grants",
            new CreatePluginGrantRequest(Product.Revit, "2026.1", "install-1"), cancellationToken);
        revitGrantResponse.EnsureSuccessStatusCode();
        var revitGrant = (await revitGrantResponse.Content.ReadFromJsonAsync<PluginGrantResponse>(cancellationToken))!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(revitGrant.GrantToken);
        Assert.Equal("revit", jwt.Claims.Single(x => x.Type == "product").Value);
        Assert.Equal(TimeSpan.FromHours(72), jwt.ValidTo - jwt.ValidFrom);
        Assert.Equal(revitGrant.ExpiresAt.ToUnixTimeSeconds(), new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero).ToUnixTimeSeconds());

        var denied = await client.PostAsJsonAsync("/api/v1/plugin/grants",
            new CreatePluginGrantRequest(Product.Zwcad, "2026.1", "install-1"), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var jwks = await client.GetAsync("/.well-known/jwks.json", cancellationToken);
        jwks.EnsureSuccessStatusCode();
        Assert.Contains("development-key", await jwks.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);

        var openApi = await client.GetAsync("/swagger/v1/swagger.json", cancellationToken);
        openApi.EnsureSuccessStatusCode();
        Assert.Contains("CEP Plugins API", await openApi.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    private static async Task SeedDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;
        var systemAdmin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "system@example.com",
            Email = "system@example.com",
            EmailConfirmed = true,
            DisplayName = "System Admin",
            Role = UserRole.SystemAdmin,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        var result = await manager.CreateAsync(systemAdmin, "correct horse battery staple");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string email, string password, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password, new ClientInfo("test")), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken))!;
    }

    private static async Task DispatchEmailAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>().DispatchOneAsync(cancellationToken));
    }

    private static void UseToken(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private sealed class CapturingEmailSender : IEmailSender
    {
        public Dictionary<string, string> InvitationCodes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task SendInvitationAsync(string email, string organizationName, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            InvitationCodes[email] = code;
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
