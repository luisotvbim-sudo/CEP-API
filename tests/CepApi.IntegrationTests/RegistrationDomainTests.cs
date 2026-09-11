using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class RegistrationDomainTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Domain_matching_is_exact_and_empty_allowlist_denies_registration()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var policy = scope.ServiceProvider.GetRequiredService<IRegistrationEmailPolicy>();
        Assert.True(await policy.IsAllowedAsync("Person@CONCEITOPROJETOS.COM", Ct));
        foreach (var email in new string?[] { null, "", "invalid", "a@gmail.com", "a@sub.conceitoprojetos.com",
                     "a@conceitoprojetos.com.evil.test", "a@notconceitoprojetos.com", "a@conceitoprojetos.com.",
                     "Name <a@conceitoprojetos.com>", "a@evil.test@conceitoprojetos.com", "a@conceitoprojetos.com,b@evil.test" })
            Assert.False(await policy.IsAllowedAsync(email, Ct), email);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        await db.AllowedEmailDomains.ExecuteDeleteAsync(Ct);
        Assert.False(await policy.IsAllowedAsync("person@conceitoprojetos.com", Ct));
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task Disallowed_initial_administrator_does_not_create_organization_invitation_or_email()
    {
        var admin = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, admin.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var slug = $"blocked-{Guid.NewGuid():N}";
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitationsBefore = await db.Invitations.CountAsync(Ct);
        var emailsBefore = await db.EmailOutbox.CountAsync(Ct);
        using var response = await client.PostAsJsonAsync("/api/v1/admin/organizations",
            new CreateOrganizationRequest("Blocked", slug, "person@gmail.com", [Product.Revit]), Ct);
        await AssertDomainRejectedAsync(response);
        Assert.False(await db.Organizations.AnyAsync(x => x.Slug == slug, Ct));
        Assert.Equal(invitationsBefore, await db.Invitations.CountAsync(Ct));
        Assert.Equal(emailsBefore, await db.EmailOutbox.CountAsync(Ct));
    }

    [Fact]
    public async Task Database_changes_apply_to_new_invitations_resends_and_pending_acceptance_without_restart()
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        var systemAdmin = await fixture.CreateUserAsync(UserRole.SystemAdmin);
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, admin.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var domain = $"company-{Guid.NewGuid():N}.test";
        var email = $"person@{domain}";
        using (var blocked = await client.PostAsJsonAsync("/api/v1/organization/invitations",
                   new InviteUserRequest(email, UserRole.User, [Product.Revit]), Ct))
            await AssertDomainRejectedAsync(blocked);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AllowedEmailDomains.Add(new AllowedEmailDomain { Domain = domain });
        await db.SaveChangesAsync(Ct);
        using var created = await client.PostAsJsonAsync("/api/v1/organization/invitations",
            new InviteUserRequest(email, UserRole.User, [Product.Revit]), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invitation = (await created.Content.ReadFromJsonAsync<InvitationResponse>(SecurityFixture.Json, Ct))!;
        await fixture.DispatchAsync();
        var code = fixture.Email.InvitationCodes[email];

        await db.AllowedEmailDomains.Where(x => x.Domain == domain)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsEnabled, false), Ct);
        using (var resent = await client.PostAsync($"/api/v1/organization/invitations/{invitation.Id}/resend", null, Ct))
            await AssertDomainRejectedAsync(resent);
        var systemTokens = await fixture.LoginAsync(client, systemAdmin.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", systemTokens.AccessToken);
        using (var resent = await client.PostAsync($"/api/v1/admin/organizations/{admin.OrganizationId}/invitations/{invitation.Id}/resend", null, Ct))
            await AssertDomainRejectedAsync(resent);

        client.DefaultRequestHeaders.Authorization = null;
        var accept = new AcceptInvitationRequest(email, code, "Allowed user", SecurityFixture.Password, new ClientInfo("test"));
        using (var rejected = await client.PostAsJsonAsync("/api/v1/auth/invitations/accept", accept, Ct))
            await AssertDomainRejectedAsync(rejected);
        Assert.False(await db.Users.AnyAsync(x => x.Email == email, Ct));
        Assert.Null((await db.Invitations.AsNoTracking().SingleAsync(x => x.Id == invitation.Id, Ct)).AcceptedAt);

        await db.AllowedEmailDomains.Where(x => x.Domain == domain)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsEnabled, true), Ct);
        using var accepted = await client.PostAsJsonAsync("/api/v1/auth/invitations/accept", accept, Ct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True(await db.Users.AnyAsync(x => x.Email == email, Ct));
    }

    private static async Task AssertDomainRejectedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("email_domain_not_allowed", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
