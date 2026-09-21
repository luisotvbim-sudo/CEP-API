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
    public async Task Complete_invitation_time_control_and_product_grant_workflow_is_isolated_and_signed()
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
                services.RemoveAll<IExternalWorkforceDirectorySource>();
                services.RemoveAll<IExternalWorkforceTimeSource>();
                var mondaySource = new FakeWorkforceSource(ExternalWorkforceSource.Monday,
                    new ExternalWorkforceIdentitySnapshot("monday-42", "Workforce User", "workforce@acme.test", true));
                var vrSource = new FakeWorkforceSource(ExternalWorkforceSource.VrMais,
                    new ExternalWorkforceIdentitySnapshot("vr-84", "Workforce User", "workforce@acme.test", true));
                services.AddSingleton<IExternalWorkforceDirectorySource>(mondaySource);
                services.AddSingleton<IExternalWorkforceDirectorySource>(vrSource);
                services.AddSingleton<IExternalWorkforceTimeSource>(mondaySource);
                services.AddSingleton<IExternalWorkforceTimeSource>(vrSource);
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
        var synchronize = await client.PostAsync("/api/v1/organization/time-control/synchronizations", null, cancellationToken);
        synchronize.EnsureSuccessStatusCode();
        var synchronization = (await synchronize.Content.ReadFromJsonAsync<WorkforceSyncResponse>(Json, cancellationToken))!;
        Assert.Equal(WorkforceSyncStatus.Succeeded, synchronization.Status);
        Assert.All(synchronization.Sources, source => Assert.Equal(WorkforceSyncStatus.Succeeded, source.Status));
        Assert.All(synchronization.Sources, source =>
        {
            Assert.Equal(1, source.TimeRecordReceivedCount);
            Assert.Equal(source.CoverageTo!.Value.AddDays(-59), source.CoverageFrom);
        });

        var incrementalSyncResponse = await client.PostAsync(
            "/api/v1/organization/time-control/synchronizations", null, cancellationToken);
        incrementalSyncResponse.EnsureSuccessStatusCode();
        var incrementalSync = (await incrementalSyncResponse.Content.ReadFromJsonAsync<WorkforceSyncResponse>(Json, cancellationToken))!;
        Assert.All(incrementalSync.Sources, source =>
        {
            Assert.Equal(source.CoverageTo!.Value.AddDays(-1), source.CoverageFrom);
            Assert.Equal(0, source.TimeRecordCreatedCount);
        });

        var identitiesResponse = await client.GetAsync(
            "/api/v1/organization/time-control/external-identities?mapped=false&pageSize=10", cancellationToken);
        identitiesResponse.EnsureSuccessStatusCode();
        var identities = (await identitiesResponse.Content.ReadFromJsonAsync<PagedResponse<ExternalWorkforceIdentityResponse>>(
            Json, cancellationToken))!;
        Assert.Equal(2, identities.Total);
        var mondayIdentity = Assert.Single(identities.Items, x => x.Source == ExternalWorkforceSource.Monday);
        var vrIdentity = Assert.Single(identities.Items, x => x.Source == ExternalWorkforceSource.VrMais);

        var inviteWorkforcePerson = await client.PostAsJsonAsync(
            "/api/v1/organization/time-control/people/invitations",
            new InviteWorkforcePersonRequest("workforce@acme.test", "Workforce User", mondayIdentity.Id, vrIdentity.Id),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, inviteWorkforcePerson.StatusCode);

        var duplicateMapping = await client.PostAsJsonAsync(
            "/api/v1/organization/time-control/people/invitations",
            new InviteWorkforcePersonRequest("other@acme.test", "Other User", mondayIdentity.Id, vrIdentity.Id),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicateMapping.StatusCode);
        await DispatchEmailAsync(factory.Services, cancellationToken);

        client.DefaultRequestHeaders.Authorization = null;
        var acceptWorkforceUser = await client.PostAsJsonAsync("/api/v1/auth/invitations/accept", new AcceptInvitationRequest(
            "workforce@acme.test", email.InvitationCodes["workforce@acme.test"], "Workforce User",
            "correct horse battery staple", new ClientInfo("desktop")), cancellationToken);
        acceptWorkforceUser.EnsureSuccessStatusCode();
        var workforceTokens = (await acceptWorkforceUser.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken))!;

        UseToken(client, adminTokens.AccessToken);
        var peopleResponse = await client.GetAsync("/api/v1/organization/time-control/people", cancellationToken);
        peopleResponse.EnsureSuccessStatusCode();
        var people = (await peopleResponse.Content.ReadFromJsonAsync<PagedResponse<WorkforcePersonResponse>>(
            Json, cancellationToken))!;
        var workforcePerson = Assert.Single(people.Items);
        Assert.Equal(workforceTokens.User.Id, workforcePerson.UserId);
        Assert.Equal("monday-42", workforcePerson.Monday.ExternalId);
        Assert.Equal("vr-84", workforcePerson.VrMais.ExternalId);

        var historyDay = synchronization.Sources.First().CoverageTo!.Value;
        var historyResponse = await client.GetAsync(
            $"/api/v1/organization/time-control/history?from={historyDay:yyyy-MM-dd}&to={historyDay:yyyy-MM-dd}&workforcePersonId={workforcePerson.Id}",
            cancellationToken);
        historyResponse.EnsureSuccessStatusCode();
        var history = (await historyResponse.Content.ReadFromJsonAsync<WorkforceAdminHistoryResponse>(Json, cancellationToken))!;
        Assert.Equal(2, Assert.Single(history.People).Records.Count);

        var oversizedHistory = await client.GetAsync(
            "/api/v1/organization/time-control/history?from=2026-01-01&to=2026-03-02", cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedHistory.StatusCode);

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

        UseToken(client, adminTokens.AccessToken);
        var createTeam = await client.PostAsJsonAsync("/api/v1/organization/time-control/teams",
            new CreateWorkforceTeamRequest("Projetos"), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createTeam.StatusCode);
        var team = (await createTeam.Content.ReadFromJsonAsync<WorkforceTeamResponse>(Json, cancellationToken))!;
        Assert.Equal("Projetos", team.Name);

        var effectiveFrom = new DateOnly(2026, 1, 1);
        var createAssignment = await client.PostAsJsonAsync($"/api/v1/organization/time-control/teams/{team.Id}/assignments",
            new CreateTeamAssignmentRequest(userTokens.User.Id, TeamAssignmentRole.Member, effectiveFrom, null), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createAssignment.StatusCode);

        var duplicateAssignment = await client.PostAsJsonAsync($"/api/v1/organization/time-control/teams/{team.Id}/assignments",
            new CreateTeamAssignmentRequest(userTokens.User.Id, TeamAssignmentRole.Member, effectiveFrom, null), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, duplicateAssignment.StatusCode);

        var overlappingAssignment = await client.PostAsJsonAsync($"/api/v1/organization/time-control/teams/{team.Id}/assignments",
            new CreateTeamAssignmentRequest(userTokens.User.Id, TeamAssignmentRole.Member, new DateOnly(2026, 2, 1), null), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, overlappingAssignment.StatusCode);

        var assignmentsResponse = await client.GetAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments?asOf=2026-03-01", cancellationToken);
        assignmentsResponse.EnsureSuccessStatusCode();
        var assignments = (await assignmentsResponse.Content.ReadFromJsonAsync<TeamAssignmentResponse[]>(Json, cancellationToken))!;
        var assignment = Assert.Single(assignments);
        Assert.Equal(userTokens.User.Id, assignment.UserId);

        UseToken(client, userTokens.AccessToken);
        var forbiddenTeams = await client.GetAsync("/api/v1/organization/time-control/teams", cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenTeams.StatusCode);
        var forbiddenHistory = await client.GetAsync(
            "/api/v1/organization/time-control/history?from=2026-01-01&to=2026-01-01", cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenHistory.StatusCode);

        UseToken(client, adminTokens.AccessToken);
        var endAssignment = await client.PatchAsJsonAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments/{assignment.Id}/end",
            new EndTeamAssignmentRequest(new DateOnly(2026, 6, 30)), cancellationToken);
        endAssignment.EnsureSuccessStatusCode();

        var endedAssignmentsResponse = await client.GetAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments?asOf=2026-07-01", cancellationToken);
        endedAssignmentsResponse.EnsureSuccessStatusCode();
        var endedAssignments = (await endedAssignmentsResponse.Content.ReadFromJsonAsync<TeamAssignmentResponse[]>(Json, cancellationToken))!;
        Assert.Empty(endedAssignments);

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
        Assert.Contains("CEP API", await openApi.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    private static async Task SeedDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        db.AllowedEmailDomains.AddRange(new AllowedEmailDomain { Domain = "example.com" }, new AllowedEmailDomain { Domain = "acme.test" });
        await db.SaveChangesAsync(cancellationToken);
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

    private sealed class FakeWorkforceSource(
        ExternalWorkforceSource source,
        params ExternalWorkforceIdentitySnapshot[] identities) : IExternalWorkforceDirectorySource, IExternalWorkforceTimeSource
    {
        public ExternalWorkforceSource Source { get; } = source;

        public Task<ExternalWorkforceDirectorySnapshot> FetchAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ExternalWorkforceDirectorySnapshot(identities, true));

        public Task<ExternalWorkforceTimeSnapshot> FetchAsync(
            DateOnly from,
            DateOnly to,
            IReadOnlyCollection<string> activeExternalIdentityIds,
            CancellationToken cancellationToken)
        {
            var identity = Assert.Single(activeExternalIdentityIds);
            var record = Source == ExternalWorkforceSource.Monday
                ? new ExternalWorkforceTimeRecordSnapshot(identity, $"session:{identity}:{to:yyyy-MM-dd}", to,
                    new DateTimeOffset(to.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
                    new DateTimeOffset(to.ToDateTime(new TimeOnly(13, 0)), TimeSpan.Zero),
                    3600, "closed", "Activity", "https://example.monday.com/boards/1", "{\"manual\":false}")
                : new ExternalWorkforceTimeRecordSnapshot(identity, $"work-day:{identity}:{to:yyyy-MM-dd}", to,
                    null, null, 28800, "reported", null, null, "{\"timeCards\":[\"08:00\",\"17:00\"]}");
            return Task.FromResult(new ExternalWorkforceTimeSnapshot(from, to, [record], true));
        }
    }
}
