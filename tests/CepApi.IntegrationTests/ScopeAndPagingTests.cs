using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;

namespace CepApi.IntegrationTests;

public sealed class ScopeAndPagingTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("users", 100)]
    [InlineData("time-control/people", 200)]
    [InlineData("time-control/external-identities", 200)]
    [InlineData("time-control/analyses", 100)]
    [InlineData("time-control/notification-dispatches", 100)]
    public async Task Paging_normalizes_bounds_and_extreme_pages_return_empty_results(string path, int maximumSize)
    {
        var admin = await fixture.CreateUserAsync(UserRole.OrganizationAdmin);
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, admin.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var route = $"/api/v1/organization/{path}";
        var normal = await client.GetAsync($"{route}?page=-1&pageSize=2147483647", Ct);
        normal.EnsureSuccessStatusCode();
        using var normalJson = JsonDocument.Parse(await normal.Content.ReadAsStringAsync(Ct));
        Assert.Equal(1, normalJson.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(maximumSize, normalJson.RootElement.GetProperty("pageSize").GetInt32());
        var extreme = await client.GetAsync($"{route}?page=2147483647&pageSize=100", Ct);
        extreme.EnsureSuccessStatusCode();
        using var extremeJson = JsonDocument.Parse(await extreme.Content.ReadAsStringAsync(Ct));
        Assert.Equal(2147483647, extremeJson.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(normalJson.RootElement.GetProperty("total").GetInt64(), extremeJson.RootElement.GetProperty("total").GetInt64());
        Assert.Equal(0, extremeJson.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(UserRole.SystemAdmin, false, HttpStatusCode.BadRequest, "organization_context_required")]
    [InlineData(UserRole.SystemAdmin, true, HttpStatusCode.BadRequest, "organization_context_required")]
    [InlineData(UserRole.OrganizationAdmin, false, HttpStatusCode.Forbidden, "organization_context_forbidden")]
    [InlineData(UserRole.OrganizationAdmin, true, HttpStatusCode.Forbidden, "organization_context_forbidden")]
    public async Task Organization_selection_rejects_malformed_or_repeated_query_values(
        UserRole role, bool repeated, HttpStatusCode status, string code)
    {
        var user = await fixture.CreateUserAsync(role);
        using var client = fixture.Client();
        var tokens = await fixture.LoginAsync(client, user.Email!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var id = user.OrganizationId ?? Guid.NewGuid();
        var query = repeated ? $"organizationId={id}&organizationId={id}" : "organizationId=invalid";
        var response = await client.PostAsJsonAsync($"/api/v1/plugin/grants?{query}",
            new CreatePluginGrantRequest(Product.Revit, "test", "test-installation"), Ct);
        await AdministrationRegressionTests.AssertProblemAsync(response, status, code);
    }
}
