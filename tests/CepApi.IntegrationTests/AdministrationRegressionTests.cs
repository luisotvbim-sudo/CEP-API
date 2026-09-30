using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class AdministrationRegressionTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resend_rotates_the_code_resets_attempts_and_keeps_the_invitation_and_audit_actor(bool systemResend)
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var system = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        using var client = fixture.Client();
        await AuthorizeAsync(client, admin.Email!);
        var email = NewEmail();
        var invitation = await InviteAsync(client, email);
        await fixture.DispatchAsync();
        var oldCode = fixture.Email.InvitationCodes[email];
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Invitations.Where(x => x.Id == invitation.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FailedAttempts, 5)
                .SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
        }
        if (systemResend) await AuthorizeAsync(client, system.Email!);
        var path = systemResend
            ? $"/api/v1/admin/organizations/{admin.OrganizationId}/invitations/{invitation.Id}/resend"
            : $"/api/v1/organization/invitations/{invitation.Id}/resend";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(path, null, Ct)).StatusCode);
        await fixture.DispatchAsync();
        var newCode = fixture.Email.InvitationCodes[email];
        Assert.NotEqual(oldCode, newCode);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Invitations.SingleAsync(x => x.Id == invitation.Id, Ct);
            Assert.Equal(0, stored.FailedAttempts);
            Assert.InRange(stored.ExpiresAt, DateTimeOffset.UtcNow.AddHours(47), DateTimeOffset.UtcNow.AddHours(49));
            Assert.NotEqual(newCode, stored.CodeHash);
            var actor = systemResend ? system.Id : admin.Id;
            var action = systemResend ? "invitation.resent_by_system_admin" : "invitation.resent";
            Assert.True(await db.AuditEvents.AnyAsync(x => x.ActorUserId == actor &&
                x.OrganizationId == admin.OrganizationId && x.Action == action, Ct));
        }
        client.DefaultRequestHeaders.Authorization = null;
        await AssertProblemAsync(await ActivateAsync(client, email, oldCode), HttpStatusCode.BadRequest, "invalid_invitation");
        Assert.Equal(HttpStatusCode.NoContent, (await ActivateAsync(client, email, newCode)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_invitations_across_organizations_reserve_one_email_and_enqueue_once()
    {
        var first = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var second = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        using var a = fixture.Client();
        using var b = fixture.Client();
        await AuthorizeAsync(a, first.Email!);
        await AuthorizeAsync(b, second.Email!);
        var email = NewEmail();
        var normalized = email.ToUpperInvariant();
        var responses = await fixture.RunBlockedAsync(
            db => db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({normalized}, 372))", Ct),
            () => a.PostAsJsonAsync("/api/v1/organization/invitations", new InviteUserRequest(email, UserRole.User, []), Ct),
            () => b.PostAsJsonAsync("/api/v1/organization/invitations", new InviteUserRequest(email, UserRole.User, []), Ct));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        await AssertProblemAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict, "email_unavailable");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Invitations.CountAsync(x => x.Email == normalized, Ct));
        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "invitation.created" &&
            (x.ActorUserId == first.Id || x.ActorUserId == second.Id), Ct));
        await fixture.DispatchAsync();
        Assert.Equal(1, fixture.Email.InvitationDeliveries[email]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Finalized_invitations_cannot_be_resent_or_accepted_again(bool accept)
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        using var client = fixture.Client();
        await AuthorizeAsync(client, admin.Email!);
        var email = NewEmail();
        var invitation = await InviteAsync(client, email);
        await fixture.DispatchAsync();
        var code = fixture.Email.InvitationCodes[email];
        if (accept)
            Assert.Equal(HttpStatusCode.NoContent, (await ActivateAsync(client, email, code)).StatusCode);
        else
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/organization/invitations/{invitation.Id}", Ct)).StatusCode);
        await AssertProblemAsync(await client.PostAsync($"/api/v1/organization/invitations/{invitation.Id}/resend", null, Ct),
            HttpStatusCode.Conflict, "invitation_not_pending");
        await AssertProblemAsync(await ActivateAsync(client, email, code), HttpStatusCode.BadRequest, "invalid_invitation");
        var revoke = await client.DeleteAsync($"/api/v1/organization/invitations/{invitation.Id}", Ct);
        if (accept) await AssertProblemAsync(revoke, HttpStatusCode.Conflict, "invitation_already_accepted");
        else Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        await fixture.DispatchAsync();
        Assert.Equal(1, fixture.Email.InvitationDeliveries[email]);
    }

    [Fact]
    public async Task Concurrent_demotions_leave_one_active_administrator()
    {
        var first = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var second = await fixture.CreateUserAsync(UserRole.OrganizationAdmin, first.OrganizationId);
        var system = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        using var client = fixture.Client();
        await AuthorizeAsync(client, system.Email!);
        var request = new UpdateUserRequest(null, UserRole.User, UserStatus.Active, null);
        var responses = await fixture.RunBlockedAsync(
            db => db.LockOrganizationAsync(first.OrganizationId!.Value, Ct),
            () => client.PatchAsJsonAsync($"/api/v1/organization/users/{first.Id}?organizationId={first.OrganizationId}", request, Ct),
            () => client.PatchAsJsonAsync($"/api/v1/organization/users/{second.Id}?organizationId={first.OrganizationId}", request, Ct));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict, "last_organization_admin");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(x =>
            x.OrganizationId == first.OrganizationId && x.Role == UserRole.OrganizationAdmin && x.Status == UserStatus.Active, Ct));
    }

    [Fact]
    public async Task Suspending_an_organization_revokes_only_its_sessions_and_reactivation_does_not_restore_them()
    {
        var admin = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        var user = await fixture.CreateUserAsync();
        var peer = await fixture.CreateUserAsync(UserRole.User, user.OrganizationId);
        var foreign = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, user.Email!);
        var peerTokens = await fixture.LoginAsync(client, peer.Email!);
        var foreignTokens = await fixture.LoginAsync(client, foreign.Email!);
        await AuthorizeAsync(client, admin.Email!);
        var route = $"/api/v1/admin/organizations/{user.OrganizationId}/status";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync(route,
            new ChangeOrganizationStatusRequest(OrganizationStatus.Suspended), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync(route,
            new ChangeOrganizationStatusRequest(OrganizationStatus.Active), Ct)).StatusCode);
        foreach (var token in new[] { tokens, peerTokens })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(token.RefreshToken), Ct)).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
        await fixture.LoginAsync(client, user.Email!);
        await AuthorizeAsync(client, admin.Email!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync(route,
            new ChangeOrganizationStatusRequest(OrganizationStatus.Archived), Ct)).StatusCode);
        await AssertProblemAsync(await client.PatchAsJsonAsync(route, new ChangeOrganizationStatusRequest(OrganizationStatus.Active), Ct),
            HttpStatusCode.Conflict, "organization_archived");
    }

    [Fact]
    public async Task Product_changes_revoke_all_user_sessions_but_identical_products_and_profile_edits_do_not()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var user = await fixture.CreateUserAsync(UserRole.User, admin.OrganizationId);
        using var client = fixture.Client();
        using var member = fixture.Client();
        var first = await fixture.LoginAsync(member, user.Email!);
        var second = await fixture.LoginAsync(member, user.Email!);
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        await AuthorizeAsync(client, admin.Email!);
        var route = $"/api/v1/organization/users/{user.Id}";
        (await client.PatchAsJsonAsync(route, new UpdateUserRequest("Renamed", UserRole.User, UserStatus.Active,
            [Product.Revit, Product.Revit]), Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/me", Ct)).StatusCode);
        var update = await client.PatchAsJsonAsync(route,
            new UpdateUserRequest(null, UserRole.User, UserStatus.Active, [Product.Zwcad]), Ct);
        update.EnsureSuccessStatusCode();
        Assert.Equal([Product.Zwcad], (await update.Content.ReadFromJsonAsync<UserResponse>(SecurityFixture.Json, Ct))!.Products);
        foreach (var token in new[] { first, second })
        {
            member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/v1/me", Ct)).StatusCode);
        }
        var fresh = await fixture.LoginAsync(member, user.Email!);
        Assert.Equal([Product.Zwcad], fresh.User.Products);
    }

    [Fact]
    public async Task Identity_and_model_validation_errors_include_stable_code_and_correlation_id()
    {
        var user = await fixture.CreateUserAsync();
        using var client = fixture.Client();
        await AuthorizeAsync(client, user.Email!);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "refactor-validation");
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest("wrong password", SecurityFixture.NewPassword), Ct),
            HttpStatusCode.BadRequest, "password_change_failed");
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/v1/me/password",
            new ChangePasswordRequest(SecurityFixture.Password, "short"), Ct),
            HttpStatusCode.BadRequest, "validation_failed");
    }

    private async Task AuthorizeAsync(HttpClient client, string email)
    {
        var token = await fixture.LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    private static async Task<InvitationResponse> InviteAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/v1/organization/invitations",
            new InviteUserRequest(email, UserRole.User, null), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invitation = (await response.Content.ReadFromJsonAsync<InvitationResponse>(SecurityFixture.Json, Ct))!;
        Assert.False(invitation.CanUseRevit);
        Assert.False(invitation.CanUseZwcad);
        return invitation;
    }

    private static Task<HttpResponseMessage> ActivateAsync(HttpClient client, string email, string code)
        => client.PostAsJsonAsync("/api/v1/auth/invitations/activate",
            new AcceptInvitationRequest(email, code, "Invited person", SecurityFixture.Password, null), Ct);

    private static string NewEmail() => $"invitation-{Guid.NewGuid():N}@example.test";

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("correlationId").GetString()));
    }
}
