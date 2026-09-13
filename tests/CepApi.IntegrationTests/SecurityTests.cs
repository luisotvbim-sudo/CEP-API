using System.Collections.Concurrent;
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
using Npgsql;
using Testcontainers.PostgreSql;

namespace CepApi.IntegrationTests;

public sealed class SecurityTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Repeated_reset_requests_keep_the_current_code_and_recovery_revokes_old_access()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, user.Email!);
        var firstCode = await fixture.ResetCodeAsync(client, user.Email!);
        var repeatedCode = await fixture.ResetCodeAsync(client, user.Email!);
        Assert.Equal(firstCode, repeatedCode);
        Assert.Equal(1, fixture.Email.PasswordResetDeliveries[user.Email!]);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/password/reset",
            new ResetPasswordRequest(user.Email!, firstCode, SecurityFixture.NewPassword), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/password/reset",
            new ResetPasswordRequest(user.Email!, firstCode, SecurityFixture.Password), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/plugin/grants", new CreatePluginGrantRequest(Product.Revit, "test", "one"), Ct)).StatusCode);
        await fixture.LoginAsync(client, user.Email!, SecurityFixture.NewPassword);
    }

    [Fact]
    public async Task Password_change_revokes_other_sessions_and_pending_reset_codes()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var first = await fixture.LoginAsync(client, user.Email!);
        var second = await fixture.LoginAsync(client, user.Email!);
        var code = await fixture.ResetCodeAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest(SecurityFixture.Password, SecurityFixture.NewPassword), Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(second.RefreshToken), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/password/reset",
            new ResetPasswordRequest(user.Email!, code, SecurityFixture.Password), Ct)).StatusCode);
    }

    [Fact]
    public async Task Logout_with_rotated_token_revokes_only_its_session_family()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var first = await fixture.LoginAsync(client, user.Email!);
        var independent = await fixture.LoginAsync(client, user.Email!);
        var rotated = await fixture.ReadTokensAsync(await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first.RefreshToken), Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(first.RefreshToken), Ct)).StatusCode);
        foreach (var token in new[] { first.AccessToken, rotated.AccessToken })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", independent.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Revocation_using_session_id_captured_before_rotation_still_works()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var first = await fixture.LoginAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        var sessions = (await client.GetFromJsonAsync<SessionResponse[]>("/api/v1/me/sessions", Ct))!;
        var rotated = await fixture.ReadTokensAsync(await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first.RefreshToken), Ct));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/me/sessions/{sessions.Single().Id}", Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_has_one_winner_and_replay_revokes_the_family()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, user.Email!);
        var responses = await fixture.RunContendedAsync(user.Id, () => client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), Ct));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
        var winner = await fixture.ReadTokensAsync(responses.Single(x => x.IsSuccessStatusCode));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", winner.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshSessions.AnyAsync(x => x.UserId == user.Id && x.RevokedAt == null, Ct));
    }

    [Fact]
    public async Task Concurrent_reset_consumes_the_code_only_once()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var code = await fixture.ResetCodeAsync(client, user.Email!);
        var responses = await fixture.RunContendedAsync(user.Id, () => client.PostAsJsonAsync("/api/v1/auth/password/reset",
            new ResetPasswordRequest(user.Email!, code, SecurityFixture.NewPassword), Ct));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        await fixture.LoginAsync(client, user.Email!, SecurityFixture.NewPassword);
    }

    [Fact]
    public async Task Reset_code_is_blocked_after_five_failed_attempts()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var code = await fixture.ResetCodeAsync(client, user.Email!);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/password/reset",
                new ResetPasswordRequest(user.Email!, "invalid", SecurityFixture.NewPassword), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/password/reset",
            new ResetPasswordRequest(user.Email!, code, SecurityFixture.NewPassword), Ct)).StatusCode);
    }

    [Fact]
    public async Task Organization_admin_cannot_read_change_or_revoke_another_organizations_data()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var other = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var otherTokens = await fixture.LoginAsync(client, other.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherTokens.AccessToken);
        var otherSessions = (await client.GetFromJsonAsync<SessionResponse[]>("/api/v1/me/sessions", Ct))!;
        var adminTokens = await fixture.LoginAsync(client, admin.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminTokens.AccessToken);
        var listing = (await client.GetFromJsonAsync<PagedResponse<UserResponse>>("/api/v1/organization/users", SecurityFixture.Json, Ct))!;
        Assert.All(listing.Items, item => Assert.Equal(admin.OrganizationId, item.OrganizationId));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync($"/api/v1/organization/users/{other.Id}",
            new UpdateUserRequest("changed", UserRole.User, UserStatus.Suspended, []), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/me/sessions/{otherSessions.Single().Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/organizations", Ct)).StatusCode);
    }

    [Fact]
    public async Task Smtp_failure_keeps_encrypted_message_and_retry_delivers_invitation()
    {
        var admin = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, admin.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var email = $"invite-{Guid.NewGuid():N}@example.test";
        fixture.Email.Fail = true;
        try
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/admin/organizations",
                new CreateOrganizationRequest("Outbox Test", Guid.NewGuid().ToString("N"), email, [Product.Revit]), Ct)).StatusCode);
            await fixture.DispatchAsync();
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.EmailOutbox.SingleAsync(Ct);
            Assert.Equal(1, message.Attempts);
            Assert.DoesNotContain(email, message.ProtectedPayload, StringComparison.Ordinal);
            Assert.False(fixture.Email.InvitationCodes.ContainsKey(email));
            message.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync(Ct);
        }
        finally { fixture.Email.Fail = false; }
        await fixture.DispatchAsync();
        Assert.True(fixture.Email.InvitationCodes.ContainsKey(email));
        await using var check = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutbox.AnyAsync(Ct));
    }
}

public sealed class SecurityFixture : IAsyncLifetime
{
    public const string Password = "original correct horse password";
    public const string NewPassword = "replacement correct horse password";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public TestEmailSender Email { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
            builder.UseSetting("EmailOutbox:Enabled", "false");
            builder.UseSetting("RateLimiting:AuthPerMinute", "1000");
            builder.UseSetting("RateLimiting:LoginPerMinute", "1000");
            builder.UseSetting("RateLimiting:RecoveryRequestsPer15Minutes", "1000");
            builder.UseSetting("RateLimiting:RecoveryAttemptsPer15Minutes", "1000");
            builder.UseSetting("RateLimiting:AccountPerMinute", "1000");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Email);
            });
        });
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        db.AllowedEmailDomains.Add(new AllowedEmailDomain { Domain = "example.test" });
        await db.SaveChangesAsync();
    }

    public HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    public async Task<ApplicationUser> CreateUserAsync(UserRole role = UserRole.User)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organization = new Organization { Name = "Test", Slug = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(), UserName = email, Email = email, EmailConfirmed = true,
            DisplayName = "Security test", Role = role, Status = UserStatus.Active,
            OrganizationId = role == UserRole.SystemAdmin ? null : organization.Id,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(x => x.Description)));
        db.ProductAccesses.Add(new ProductAccess { UserId = user.Id, Product = Product.Revit, GrantedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<TokenResponse> LoginAsync(HttpClient client, string email, string password = Password)
        => await ReadTokensAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password, new ClientInfo("test"))));

    public async Task<TokenResponse> ReadTokensAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
    }

    public async Task<string> ResetCodeAsync(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new ForgotPasswordRequest(email))).StatusCode);
        await DispatchAsync();
        return Email.ResetCodes[email];
    }

    public async Task DispatchAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        while (await scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>().DispatchOneAsync(CancellationToken.None)) { }
    }

    public async Task<HttpResponseMessage[]> RunContendedAsync(Guid userId, Func<Task<HttpResponseMessage>> request)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var blocker = new NpgsqlCommand("SELECT \"Id\" FROM users WHERE \"Id\" = @id FOR UPDATE", connection, transaction);
        blocker.Parameters.AddWithValue("id", userId);
        await blocker.ExecuteNonQueryAsync();
        var first = request();
        var second = request();
        var blocked = false;
        try
        {
            for (var i = 0; i < 100; i++)
            {
                await using var check = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%FOR UPDATE%'", connection, transaction);
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) >= 2) { blocked = true; break; }
                // Clear PostgreSQL's cached statistics snapshot inside our transaction.
                await using var clear = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", connection, transaction);
                await clear.ExecuteNonQueryAsync();
                await Task.Delay(30);
            }
        }
        finally { await transaction.CommitAsync(); }
        var responses = await Task.WhenAll(first, second);
        Assert.True(blocked, "Both requests must contend for the account lock to verify the race deterministically.");
        return responses;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed class TestEmailSender : IEmailSender
{
    public bool Fail { get; set; }
    public ConcurrentDictionary<string, string> ResetCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, int> PasswordResetDeliveries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, string> InvitationCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Task SendInvitationAsync(string email, string organizationName, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (Fail) throw new IOException("Simulated SMTP failure");
        InvitationCodes[email] = code;
        return Task.CompletedTask;
    }
    public Task SendPasswordResetAsync(string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (Fail) throw new IOException("Simulated SMTP failure");
        ResetCodes[email] = code;
        PasswordResetDeliveries.AddOrUpdate(email, 1, (_, count) => count + 1);
        return Task.CompletedTask;
    }
}
