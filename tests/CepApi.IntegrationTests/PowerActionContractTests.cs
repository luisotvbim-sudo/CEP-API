using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace CepApi.IntegrationTests;

// These contract checks deliberately need no database or Docker.
public sealed class PowerActionContractTests
{
    [Fact]
    public async Task Both_routes_require_authentication_and_openapi_exposes_the_real_contract()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("EmailOutbox:Enabled", "false");
            builder.UseSetting("TimeNotifications:WorkerEnabled", "false");
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/time-control/power-action-status", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/me/time-control/power-action-check", new { action = "shutdown" }, TestContext.Current.CancellationToken)).StatusCode);
        _ = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var json = await client.GetStringAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var post = root.GetProperty("paths").GetProperty("/api/v1/me/time-control/power-action-check").GetProperty("post");
        var get = root.GetProperty("paths").GetProperty("/api/v1/me/time-control/power-action-status").GetProperty("get");
        Assert.True(post.GetProperty("requestBody").GetProperty("required").GetBoolean());
        foreach (var operation in new[] { post, get })
            foreach (var status in new[] { "200", "400", "401", "403", "429", "500" })
                Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        var schemas = root.GetProperty("components").GetProperty("schemas");
        foreach (var field in new[] { "action", "decision", "code", "message", "analysis" })
            Assert.True(schemas.GetProperty("PowerActionCheckResponse").GetProperty("properties").TryGetProperty(field, out _));
        var responseSchema = schemas.GetProperty("PowerActionCheckResponse");
        Assert.Equal(5, responseSchema.GetProperty("required").GetArrayLength());
        Assert.Equal(3, responseSchema.GetProperty("properties").GetProperty("decision").GetProperty("enum").GetArrayLength());
        Assert.True(responseSchema.GetProperty("properties").GetProperty("analysis").GetProperty("nullable").GetBoolean());
    }
}
