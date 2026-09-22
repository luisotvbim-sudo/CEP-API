using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class SystemAdminScopeTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task System_admin_can_manage_selected_organization_with_real_actor_audit_and_isolation()
    {
        var system = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        var first = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var second = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, system.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/organization/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/organization/users?organizationId={Guid.NewGuid()}", Ct)).StatusCode);
        var response = await client.GetAsync($"/api/v1/organization/users?organizationId={first.OrganizationId}", Ct);
        response.EnsureSuccessStatusCode();
        var users = (await response.Content.ReadFromJsonAsync<PagedResponse<UserResponse>>(SecurityFixture.Json, Ct))!;
        Assert.Contains(users.Items, x => x.Id == first.Id);
        Assert.DoesNotContain(users.Items, x => x.Id == second.Id);
        var created = await client.PostAsJsonAsync($"/api/v1/organization/time-control/teams?organizationId={first.OrganizationId}", new { name = "Global administration" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var team = (await created.Content.ReadFromJsonAsync<WorkforceTeamResponse>(SecurityFixture.Json, Ct))!;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/organization/time-control/teams/{team.Id}?organizationId={second.OrganizationId}", Ct)).StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(x => x.ActorUserId == system.Id && x.OrganizationId == first.OrganizationId, Ct));
        foreach (var path in new[] { "invitations", "audit", "time-control/people", "time-control/external-identities?source=monday" })
        {
            var separator = path.Contains('?') ? '&' : '?';
            (await client.GetAsync($"/api/v1/organization/{path}{separator}organizationId={first.OrganizationId}", Ct)).EnsureSuccessStatusCode();
        }
        var grant = await client.PostAsJsonAsync($"/api/v1/plugin/grants?organizationId={first.OrganizationId}",
            new CreatePluginGrantRequest(Product.Revit, "test", "test-installation"), Ct);
        grant.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Organization_admin_cannot_override_scope_and_regular_user_cannot_gain_admin_access()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var other = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, admin.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        (await client.GetAsync("/api/v1/organization/users", Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/organization/users?organizationId={other.OrganizationId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/organizations", Ct)).StatusCode);
        tokens = await fixture.LoginAsync(client, other.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/organization/users?organizationId={other.OrganizationId}", Ct)).StatusCode);
    }
}
