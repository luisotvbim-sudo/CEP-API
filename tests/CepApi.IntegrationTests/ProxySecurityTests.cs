using System.Net;
using System.Net.Http.Json;
using CepApi.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CepApi.IntegrationTests;

public sealed class ProxySecurityTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    [Theory]
    [InlineData("192.0.2.2", true)]
    [InlineData("192.0.2.3", false)]
    public async Task Only_trusted_proxy_can_set_https_scheme(string remoteIp, bool trusted)
    {
        await using var factory = Configure(remoteIp);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest("invalid"), TestContext.Current.CancellationToken);
        Assert.Equal(trusted ? HttpStatusCode.Unauthorized : HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    [Theory]
    [InlineData("192.0.2.2", true)]
    [InlineData("192.0.2.3", false)]
    public async Task Only_trusted_proxy_can_partition_rate_limit_by_forwarded_client_ip(string remoteIp, bool trusted)
    {
        await using var factory = Configure(remoteIp);
        using var first = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var second = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        first.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        second.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.11");
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await first.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest("invalid"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/API/V1/AUTH/REFRESH", new RefreshRequest("invalid"), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(trusted ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests,
            (await second.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest("invalid"), TestContext.Current.CancellationToken)).StatusCode);
    }

    private WebApplicationFactory<Program> Configure(string ip)
        => fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ReverseProxy:KnownProxies:0", "192.0.2.2");
            builder.UseSetting("RateLimiting:AuthPerMinute", "2");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(IPAddress.Parse(ip)));
                services.AddHttpsRedirection(options => options.HttpsPort = 443);
            });
        });

    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextRequest) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextRequest(context);
            });
            next(app);
        };
    }
}
