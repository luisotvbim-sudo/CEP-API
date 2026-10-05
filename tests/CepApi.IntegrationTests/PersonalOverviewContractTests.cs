using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CepApi.IntegrationTests;

public sealed class PersonalOverviewContractTests
{
    [Fact]
    public async Task Contract_requires_authentication_and_exposes_only_official_period_input()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("EmailOutbox:Enabled", "false"); builder.UseSetting("TimeNotifications:WorkerEnabled", "false");
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/time-control/overview", ct)).StatusCode);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", ct));
        var root = document.RootElement;
        var operation = root.GetProperty("paths").GetProperty("/api/v1/me/time-control/overview").GetProperty("get");
        Assert.Equal("period", Assert.Single(operation.GetProperty("parameters").EnumerateArray()).GetProperty("name").GetString());
        foreach (var code in new[] { "200", "400", "401", "403", "429", "503" }) Assert.True(operation.GetProperty("responses").TryGetProperty(code, out _));
        var schema = root.GetProperty("components").GetProperty("schemas").GetProperty("PersonalOverviewResponse");
        Assert.Equal(7, schema.GetProperty("required").GetArrayLength());
        Assert.True(schema.GetProperty("properties").GetProperty("analysis").GetProperty("nullable").GetBoolean());
    }
}
