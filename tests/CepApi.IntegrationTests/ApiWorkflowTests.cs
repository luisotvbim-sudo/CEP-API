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
        var mondaySource = new FakeWorkforceSource(ExternalWorkforceSource.Monday,
            new ExternalWorkforceIdentitySnapshot("monday-42", "Workforce User", "workforce@acme.test", true));
        var vrSource = new FakeWorkforceSource(ExternalWorkforceSource.VrMais,
            new ExternalWorkforceIdentitySnapshot("vr-84", "Workforce User", "workforce@acme.test", true));
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
        await VerifyWebSessionAsync(factory, cancellationToken);
        UseToken(client, systemTokens.AccessToken);
        var createOrganization = await client.PostAsJsonAsync("/api/v1/admin/organizations", new CreateOrganizationRequest(
            "Acme Engenharia", "acme-engenharia", "admin@acme.test", [Product.Revit, Product.Zwcad]), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createOrganization.StatusCode);
        var createdOrganization = (await createOrganization.Content.ReadFromJsonAsync<OrganizationResponse>(
            Json, cancellationToken))!;
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
            Assert.Equal(source.CoverageTo!.Value.AddDays(-89), source.CoverageFrom);
        });

        var fullSyncResponse = await client.PostAsync(
            "/api/v1/organization/time-control/synchronizations?full=true", null, cancellationToken);
        fullSyncResponse.EnsureSuccessStatusCode();
        var fullSync = (await fullSyncResponse.Content.ReadFromJsonAsync<WorkforceSyncResponse>(Json, cancellationToken))!;
        Assert.All(fullSync.Sources, source =>
            Assert.Equal(source.CoverageTo!.Value.AddDays(-89), source.CoverageFrom));

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

        var incrementalSyncResponse = await client.PostAsync(
            "/api/v1/organization/time-control/synchronizations", null, cancellationToken);
        incrementalSyncResponse.EnsureSuccessStatusCode();
        var incrementalSync = (await incrementalSyncResponse.Content.ReadFromJsonAsync<WorkforceSyncResponse>(Json, cancellationToken))!;
        Assert.All(incrementalSync.Sources, source =>
        {
            Assert.Equal(source.CoverageTo!.Value.AddDays(-6), source.CoverageFrom);
            Assert.Equal(0, source.ReceivedCount);
            Assert.Equal(0, source.TimeRecordCreatedCount);
        });
        Assert.Equal(2, mondaySource.DirectoryFetchCount);
        Assert.Equal(2, vrSource.DirectoryFetchCount);

        var historyDay = synchronization.Sources.First().CoverageTo!.Value;
        var historyResponse = await client.GetAsync(
            $"/api/v1/organization/time-control/history?from={historyDay:yyyy-MM-dd}&to={historyDay:yyyy-MM-dd}&workforcePersonId={workforcePerson.Id}",
            cancellationToken);
        historyResponse.EnsureSuccessStatusCode();
        var history = (await historyResponse.Content.ReadFromJsonAsync<WorkforceAdminHistoryResponse>(Json, cancellationToken))!;
        Assert.Equal(2, Assert.Single(history.People).Records.Count);

        var ninetyDayHistory = await client.GetAsync(
            $"/api/v1/organization/time-control/history?from={historyDay.AddDays(-89):yyyy-MM-dd}&to={historyDay:yyyy-MM-dd}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, ninetyDayHistory.StatusCode);
        var oversizedHistory = await client.GetAsync(
            $"/api/v1/organization/time-control/history?from={historyDay.AddDays(-90):yyyy-MM-dd}&to={historyDay:yyyy-MM-dd}",
            cancellationToken);
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

        UseToken(client, userTokens.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(
            "/api/v1/organization/time-control/synchronizations", null, cancellationToken)).StatusCode);

        UseToken(client, adminTokens.AccessToken);
        var createTeam = await client.PostAsJsonAsync("/api/v1/organization/time-control/teams",
            new CreateWorkforceTeamRequest("Projetos"), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createTeam.StatusCode);
        var team = (await createTeam.Content.ReadFromJsonAsync<WorkforceTeamResponse>(Json, cancellationToken))!;
        Assert.Equal("Projetos", team.Name);

        var outsideScope = await SeedWorkforcePersonAsync(
            factory.Services, createdOrganization.Id, cancellationToken);
        var coordinatorSync = await client.PostAsync(
            "/api/v1/organization/time-control/synchronizations", null, cancellationToken);
        coordinatorSync.EnsureSuccessStatusCode();
        Assert.Equal(2, mondaySource.TimeRequests.Last().ExternalIds.Length);
        Assert.Equal(2, vrSource.TimeRequests.Last().ExternalIds.Length);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var outsideMondayId = await db.WorkforcePeople.AsNoTracking()
                .Where(x => x.Id == outsideScope.PersonId)
                .Select(x => x.MondayIdentityId)
                .SingleAsync(cancellationToken);
            var mondayRecord = await db.WorkforceTimeRecords.SingleAsync(x =>
                x.OrganizationId == createdOrganization.Id && x.Source == ExternalWorkforceSource.Monday &&
                x.ExternalKey.StartsWith("session:monday-42:"),
                cancellationToken);
            mondayRecord.ExternalIdentityId = outsideMondayId;
            mondayRecord.WorkDate = historyDay.AddDays(-30);
            await db.SaveChangesAsync(cancellationToken);
        }
        var createOtherTeam = await client.PostAsJsonAsync("/api/v1/organization/time-control/teams",
            new CreateWorkforceTeamRequest("Outro time"), cancellationToken);
        createOtherTeam.EnsureSuccessStatusCode();
        var otherTeam = (await createOtherTeam.Content.ReadFromJsonAsync<WorkforceTeamResponse>(
            Json, cancellationToken))!;

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

        var createLeaderAssignment = await client.PostAsJsonAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments",
            new CreateTeamAssignmentRequest(userTokens.User.Id, TeamAssignmentRole.Manager, effectiveFrom, null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createLeaderAssignment.StatusCode);

        var createWorkforceMemberAssignment = await client.PostAsJsonAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments",
            new CreateTeamAssignmentRequest(workforceTokens.User.Id, TeamAssignmentRole.Member, effectiveFrom, null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createWorkforceMemberAssignment.StatusCode);

        var createOutsideScopeAssignment = await client.PostAsJsonAsync(
            $"/api/v1/organization/time-control/teams/{otherTeam.Id}/assignments",
            new CreateTeamAssignmentRequest(outsideScope.UserId, TeamAssignmentRole.Member, effectiveFrom, null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createOutsideScopeAssignment.StatusCode);

        var assignmentsResponse = await client.GetAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments?asOf=2026-03-01", cancellationToken);
        assignmentsResponse.EnsureSuccessStatusCode();
        var assignments = (await assignmentsResponse.Content.ReadFromJsonAsync<TeamAssignmentResponse[]>(Json, cancellationToken))!;
        Assert.Equal(3, assignments.Length);
        var assignment = Assert.Single(assignments, x =>
            x.UserId == userTokens.User.Id && x.Role == TeamAssignmentRole.Member);
        var leaderAssignment = Assert.Single(assignments, x =>
            x.UserId == userTokens.User.Id && x.Role == TeamAssignmentRole.Manager);

        UseToken(client, userTokens.AccessToken);
        var leaderSync = await client.PostAsync(
            "/api/v1/organization/time-control/synchronizations", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, leaderSync.StatusCode);
        var leaderSyncBatch = (await leaderSync.Content.ReadFromJsonAsync<WorkforceSyncResponse>(Json, cancellationToken))!;
        Assert.All(leaderSyncBatch.Sources, source =>
            Assert.Equal(source.CoverageTo!.Value.AddDays(-6), source.CoverageFrom));
        Assert.Equal(["monday-42"], mondaySource.TimeRequests.Last().ExternalIds);
        Assert.Equal(["vr-84"], vrSource.TimeRequests.Last().ExternalIds);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reassignedMonday = await db.WorkforceTimeRecords.AsNoTracking()
                .Where(x => x.OrganizationId == createdOrganization.Id && x.Source == ExternalWorkforceSource.Monday &&
                    x.ExternalKey.StartsWith("session:monday-42:"))
                .Select(x => new { x.ExternalIdentityId, x.WorkDate })
                .SingleAsync(cancellationToken);
            Assert.Equal(mondayIdentity.Id, reassignedMonday.ExternalIdentityId);
            Assert.Equal(historyDay, reassignedMonday.WorkDate);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            "/api/v1/organization/time-control/synchronizations/latest", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/v1/organization/time-control/synchronizations/{synchronization.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(
            "/api/v1/organization/time-control/synchronizations?full=true", null, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            "/api/v1/organization/time-control/external-identities", cancellationToken)).StatusCode);
        var leaderTeamsResponse = await client.GetAsync(
            "/api/v1/organization/time-control/teams", cancellationToken);
        leaderTeamsResponse.EnsureSuccessStatusCode();
        Assert.Equal(team.Id, Assert.Single((await leaderTeamsResponse.Content.ReadFromJsonAsync<WorkforceTeamResponse[]>(
            Json, cancellationToken))!).Id);

        var leaderPeopleResponse = await client.GetAsync(
            "/api/v1/organization/time-control/people", cancellationToken);
        leaderPeopleResponse.EnsureSuccessStatusCode();
        var leaderPeople = (await leaderPeopleResponse.Content.ReadFromJsonAsync<PagedResponse<WorkforcePersonResponse>>(
            Json, cancellationToken))!;
        Assert.Equal(workforcePerson.Id, Assert.Single(leaderPeople.Items).Id);

        var leaderHistoryResponse = await client.GetAsync(
            $"/api/v1/organization/time-control/history?from={historyDay:yyyy-MM-dd}&to={historyDay:yyyy-MM-dd}",
            cancellationToken);
        leaderHistoryResponse.EnsureSuccessStatusCode();
        var leaderHistory = (await leaderHistoryResponse.Content.ReadFromJsonAsync<WorkforceAdminHistoryResponse>(
            Json, cancellationToken))!;
        Assert.Equal(workforcePerson.Id, Assert.Single(leaderHistory.People).WorkforcePersonId);
        Assert.DoesNotContain(leaderHistory.People, x => x.WorkforcePersonId == outsideScope.PersonId);

        var hiddenTeam = await client.GetAsync(
            $"/api/v1/organization/time-control/teams/{otherTeam.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, hiddenTeam.StatusCode);
        var forbiddenLeaderMutation = await client.PostAsJsonAsync(
            "/api/v1/organization/time-control/teams", new CreateWorkforceTeamRequest("Não permitido"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenLeaderMutation.StatusCode);

        UseToken(client, workforceTokens.AccessToken);
        var memberSync = await client.PostAsync(
            "/api/v1/organization/time-control/synchronizations", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, memberSync.StatusCode);
        var memberSyncBatch = (await memberSync.Content.ReadFromJsonAsync<WorkforceSyncResponse>(Json, cancellationToken))!;
        Assert.All(memberSyncBatch.Sources, source =>
            Assert.Equal(source.CoverageTo!.Value.AddDays(-6), source.CoverageFrom));
        Assert.Equal(["monday-42"], mondaySource.TimeRequests.Last().ExternalIds);
        Assert.Equal(["vr-84"], vrSource.TimeRequests.Last().ExternalIds);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/v1/organization/time-control/synchronizations/{memberSyncBatch.Id}", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/v1/organization/time-control/synchronizations/{leaderSyncBatch.Id}", cancellationToken)).StatusCode);
        Assert.Equal(2, mondaySource.DirectoryFetchCount);
        Assert.Equal(2, vrSource.DirectoryFetchCount);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(
            $"/api/v1/organization/time-control/synchronizations?organizationId={Guid.NewGuid()}", null,
            cancellationToken)).StatusCode);
        var memberHistoryResponse = await client.GetAsync(
            $"/api/v1/organization/time-control/history?from={historyDay:yyyy-MM-dd}&to={historyDay:yyyy-MM-dd}",
            cancellationToken);
        memberHistoryResponse.EnsureSuccessStatusCode();
        var memberHistory = (await memberHistoryResponse.Content.ReadFromJsonAsync<WorkforceAdminHistoryResponse>(
            Json, cancellationToken))!;
        Assert.Equal(workforcePerson.Id, Assert.Single(memberHistory.People).WorkforcePersonId);
        var memberTeamsResponse = await client.GetAsync(
            "/api/v1/organization/time-control/teams", cancellationToken);
        memberTeamsResponse.EnsureSuccessStatusCode();
        Assert.Empty((await memberTeamsResponse.Content.ReadFromJsonAsync<WorkforceTeamResponse[]>(
            Json, cancellationToken))!);
        var forbiddenMemberMutation = await client.PostAsJsonAsync(
            "/api/v1/organization/time-control/teams", new CreateWorkforceTeamRequest("Também não permitido"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenMemberMutation.StatusCode);
        var forbiddenOrganizationOverride = await client.GetAsync(
            $"/api/v1/organization/time-control/history?from={historyDay:yyyy-MM-dd}&to={historyDay:yyyy-MM-dd}&organizationId={Guid.NewGuid()}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenOrganizationOverride.StatusCode);

        UseToken(client, adminTokens.AccessToken);
        var endAssignment = await client.PatchAsJsonAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments/{assignment.Id}/end",
            new EndTeamAssignmentRequest(new DateOnly(2026, 6, 30)), cancellationToken);
        endAssignment.EnsureSuccessStatusCode();

        var endLeaderAssignment = await client.PatchAsJsonAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments/{leaderAssignment.Id}/end",
            new EndTeamAssignmentRequest(new DateOnly(2026, 6, 30)), cancellationToken);
        endLeaderAssignment.EnsureSuccessStatusCode();

        var endedAssignmentsResponse = await client.GetAsync(
            $"/api/v1/organization/time-control/teams/{team.Id}/assignments?asOf=2026-07-01", cancellationToken);
        endedAssignmentsResponse.EnsureSuccessStatusCode();
        var endedAssignments = (await endedAssignmentsResponse.Content.ReadFromJsonAsync<TeamAssignmentResponse[]>(Json, cancellationToken))!;
        Assert.Equal(workforceTokens.User.Id, Assert.Single(endedAssignments).UserId);

        UseToken(client, userTokens.AccessToken);

        var teamsAfterLeaderAssignmentEnded = await client.GetAsync(
            "/api/v1/organization/time-control/teams", cancellationToken);
        teamsAfterLeaderAssignmentEnded.EnsureSuccessStatusCode();
        Assert.Empty((await teamsAfterLeaderAssignmentEnded.Content.ReadFromJsonAsync<WorkforceTeamResponse[]>(
            Json, cancellationToken))!);

        UseToken(client, systemTokens.AccessToken);
        var missingSystemScope = await client.GetAsync(
            "/api/v1/organization/time-control/teams", cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingSystemScope.StatusCode);
        var unknownSystemScope = await client.GetAsync(
            $"/api/v1/organization/time-control/teams?organizationId={Guid.NewGuid()}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknownSystemScope.StatusCode);
        var systemScopedTeams = await client.GetAsync(
            $"/api/v1/organization/time-control/teams?organizationId={createdOrganization.Id}", cancellationToken);
        systemScopedTeams.EnsureSuccessStatusCode();
        var allTeams = (await systemScopedTeams.Content.ReadFromJsonAsync<WorkforceTeamResponse[]>(
            Json, cancellationToken))!;
        Assert.Equal(2, allTeams.Length);
        Assert.Contains(allTeams, x => x.Id == team.Id);
        Assert.Contains(allTeams, x => x.Id == otherTeam.Id);

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

    private static async Task<(Guid UserId, Guid PersonId)> SeedWorkforcePersonAsync(
        IServiceProvider services,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "outside-scope@acme.test",
            Email = "outside-scope@acme.test",
            EmailConfirmed = true,
            DisplayName = "Outside Scope",
            OrganizationId = organizationId,
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        var result = await manager.CreateAsync(user, "correct horse battery staple");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));

        var monday = new ExternalWorkforceIdentity
        {
            OrganizationId = organizationId,
            Source = ExternalWorkforceSource.Monday,
            ExternalId = "outside-monday",
            DisplayName = user.DisplayName,
            Email = user.Email,
            FirstSeenAt = now,
            LastSeenAt = now
        };
        var vrMais = new ExternalWorkforceIdentity
        {
            OrganizationId = organizationId,
            Source = ExternalWorkforceSource.VrMais,
            ExternalId = "outside-vr",
            DisplayName = user.DisplayName,
            Email = user.Email,
            FirstSeenAt = now,
            LastSeenAt = now
        };
        var person = new WorkforcePerson
        {
            OrganizationId = organizationId,
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email!,
            MondayIdentityId = monday.Id,
            VrMaisIdentityId = vrMais.Id,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = user.Id
        };
        db.ExternalWorkforceIdentities.AddRange(monday, vrMais);
        db.WorkforcePeople.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        return (user.Id, person.Id);
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string email, string password, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password, new ClientInfo("test")), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken))!;
    }

    private static async Task VerifyWebSessionAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        using var rejectedClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        var rejected = await rejectedClient.PostAsJsonAsync("/api/v1/auth/web/login",
            new LoginRequest("system@example.com", "correct horse battery staple", new ClientInfo("web-test")),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var webClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        webClient.DefaultRequestHeaders.Add("X-CEP-Web-Session", "1");
        webClient.DefaultRequestHeaders.Add("Origin", "https://localhost");

        var login = await webClient.PostAsJsonAsync("/api/v1/auth/web/login",
            new LoginRequest("system@example.com", "correct horse battery staple", new ClientInfo("web-test")),
            cancellationToken);
        login.EnsureSuccessStatusCode();
        var loginBody = (await login.Content.ReadFromJsonAsync<WebSessionResponse>(Json, cancellationToken))!;
        Assert.Equal("system@example.com", loginBody.User.Email);
        Assert.DoesNotContain("refreshToken", await login.Content.ReadAsStringAsync(cancellationToken),
            StringComparison.OrdinalIgnoreCase);
        var loginCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-cep-session=", loginCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", loginCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", loginCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", loginCookie, StringComparison.OrdinalIgnoreCase);

        var refresh = await webClient.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, cancellationToken);
        refresh.EnsureSuccessStatusCode();
        var refreshBody = (await refresh.Content.ReadFromJsonAsync<WebSessionResponse>(Json, cancellationToken))!;
        Assert.NotEqual(loginBody.AccessToken, refreshBody.AccessToken);

        var logout = await webClient.PostAsJsonAsync("/api/v1/auth/web/logout", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var deletedCookie = Assert.Single(logout.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-cep-session=", deletedCookie, StringComparison.Ordinal);
        Assert.Contains("expires=", deletedCookie, StringComparison.OrdinalIgnoreCase);

        var expired = await webClient.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
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
        public int DirectoryFetchCount { get; private set; }
        public List<(DateOnly From, DateOnly To, string[] ExternalIds)> TimeRequests { get; } = [];

        public Task<ExternalWorkforceDirectorySnapshot> FetchAsync(CancellationToken cancellationToken)
        {
            DirectoryFetchCount++;
            return Task.FromResult(new ExternalWorkforceDirectorySnapshot(identities, true));
        }

        public Task<ExternalWorkforceTimeSnapshot> FetchAsync(
            DateOnly from,
            DateOnly to,
            IReadOnlyCollection<string> activeExternalIdentityIds,
            CancellationToken cancellationToken)
        {
            TimeRequests.Add((from, to, activeExternalIdentityIds.ToArray()));
            var records = activeExternalIdentityIds.Select(identity => Source == ExternalWorkforceSource.Monday
                ? new ExternalWorkforceTimeRecordSnapshot(identity, $"session:{identity}:{to:yyyy-MM-dd}", to,
                    new DateTimeOffset(to.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
                    new DateTimeOffset(to.ToDateTime(new TimeOnly(13, 0)), TimeSpan.Zero),
                    3600, "closed", "Activity", "https://example.monday.com/boards/1", "{\"manual\":false}")
                : new ExternalWorkforceTimeRecordSnapshot(identity, $"work-day:{identity}:{to:yyyy-MM-dd}", to,
                    null, null, 28800, "reported", null, null, "{\"timeCards\":[\"08:00\",\"17:00\"]}"))
                .ToArray();
            return Task.FromResult(new ExternalWorkforceTimeSnapshot(from, to, records, true));
        }
    }
}
