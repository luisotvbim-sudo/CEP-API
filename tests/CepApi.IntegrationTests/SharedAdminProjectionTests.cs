using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class SharedAdminProjectionTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    [Fact]
    public async Task Shared_admin_projections_preserve_account_pagination_audit_details_and_organization_boundaries()
    {
        var ct = TestContext.Current.CancellationToken;
        var system = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        var coordinator = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var member = await fixture.CreateUserAsync(UserRole.User, coordinator.OrganizationId);
        var foreign = await fixture.CreateUserAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var ownAudit = new AuditEvent
        {
            OrganizationId = coordinator.OrganizationId, Action = "projection.fixture",
            DetailsJson = "{\"nested\":{\"known\":null},\"items\":[1,true]}", CreatedAt = cutoff.AddMinutes(-1)
        };
        var foreignAudit = new AuditEvent
        {
            OrganizationId = foreign.OrganizationId, Action = "projection.foreign", CreatedAt = cutoff.AddMinutes(-2)
        };
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AddRange(ownAudit, foreignAudit);
            await db.SaveChangesAsync(ct);
        }
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, system.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var globalUsers = (await client.GetFromJsonAsync<PagedResponse<UserResponse>>(
            $"/api/v1/admin/organizations/{coordinator.OrganizationId}/users?pageSize=1", SecurityFixture.Json, ct))!;
        var scopedUsers = (await client.GetFromJsonAsync<PagedResponse<UserResponse>>(
            $"/api/v1/organization/users?organizationId={coordinator.OrganizationId}&pageSize=1", SecurityFixture.Json, ct))!;
        Assert.Equal(2, globalUsers.Total);
        Assert.Equal(globalUsers.Total, scopedUsers.Total);
        Assert.Equal(globalUsers.Items.Single().Id, scopedUsers.Items.Single().Id);
        Assert.Contains(globalUsers.Items.Single().Id, new[] { coordinator.Id, member.Id });
        Assert.DoesNotContain(globalUsers.Items, user => user.Id == foreign.Id);
        var before = Uri.EscapeDataString(cutoff.ToString("O"));
        using var globalAudit = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/v1/admin/audit?organizationId={coordinator.OrganizationId}&before={before}&pageSize=1", ct));
        Assert.Equal(ownAudit.Id, globalAudit.RootElement.EnumerateArray().Single().GetProperty("id").GetGuid());
        tokens = await fixture.LoginAsync(client, coordinator.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var scopedAudit = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/v1/organization/audit?before={before}&pageSize=1", ct));
        Assert.Equal(globalAudit.RootElement.GetRawText(), scopedAudit.RootElement.GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/audit", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/v1/organization/users?organizationId={foreign.OrganizationId}", ct)).StatusCode);
    }
}
