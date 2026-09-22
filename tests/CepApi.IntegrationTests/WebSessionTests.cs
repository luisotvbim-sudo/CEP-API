using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CepApi.Application;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CepApi.IntegrationTests;

public sealed class WebSessionTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private HttpClient Client(string origin = "https://localhost")
    {
        var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", origin);
        client.DefaultRequestHeaders.Add("X-CEP-Web-Session", "1");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        return client;
    }

    private static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    private static void UseCookie(HttpClient client, string cookie)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    private async Task<HttpResponseMessage> Login(HttpClient client)
    {
        var user = await fixture.CreateUserAsync();
        return await client.PostAsJsonAsync("/api/v1/auth/web/login",
            new LoginRequest(user.Email!, SecurityFixture.Password, new ClientInfo("test")), Ct);
    }

    [Fact]
    public async Task Login_persists_protected_cookie_for_seven_days_and_rotation_keeps_absolute_deadline()
    {
        using var client = Client();
        using var login = await Login(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var json = await login.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("refreshToken", json);
        var first = JsonSerializer.Deserialize<WebSessionResponse>(json, Json)!;
        Assert.InRange(first.SessionExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromDays(6.99), TimeSpan.FromDays(7));
        Assert.Equal("no-store", login.Headers.CacheControl?.ToString());
        var setCookie = login.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith("__Host-cep-session=", setCookie);
        Assert.Contains("httponly", setCookie);
        Assert.Contains("secure", setCookie);
        Assert.Contains("samesite=strict", setCookie);
        Assert.Contains("expires=", setCookie);
        Assert.DoesNotContain(first.AccessToken, setCookie);

        // New client simulates a reload/new browser process with only its persistent cookie.
        using var restored = Client();
        UseCookie(restored, Cookie(login));
        using var refresh = await restored.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var second = (await refresh.Content.ReadFromJsonAsync<WebSessionResponse>(Json, Ct))!;
        Assert.Equal(first.User.Id, second.User.Id);
        Assert.True((first.SessionExpiresAt - second.SessionExpiresAt).Duration() < TimeSpan.FromMilliseconds(1));
        Assert.NotEqual(Cookie(login), Cookie(refresh));
        restored.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await restored.GetAsync("/api/v1/me", Ct)).StatusCode);

        UseCookie(restored, Cookie(refresh));
        var logout = await restored.PostAsJsonAsync("/api/v1/auth/web/logout", new { }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", logout.Headers.GetValues("Set-Cookie").Single());
        Assert.Equal(HttpStatusCode.Unauthorized, (await restored.GetAsync("/api/v1/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restored.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/web/login")]
    [InlineData("/api/v1/auth/web/refresh")]
    [InlineData("/api/v1/auth/web/logout")]
    public async Task Cookie_operations_reject_cross_origin_and_missing_custom_header(string path)
    {
        using var client = Client("https://attacker.invalid");
        var body = new LoginRequest("test@example.test", "test-password", null);
        var response = await client.PostAsJsonAsync(path, body, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("web_origin_invalid", problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out _));
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        client.DefaultRequestHeaders.Remove("X-CEP-Web-Session");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, body, Ct)).StatusCode);
    }

    [Fact]
    public async Task Missing_tampered_expired_and_reused_cookies_cannot_restore_session()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct)).StatusCode);
        UseCookie(client, "__Host-cep-session=invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct)).StatusCode);
        using var login = await Login(client);
        UseCookie(client, Cookie(login));
        using var rotated = await client.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct)).StatusCode);
        UseCookie(client, Cookie(rotated));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct)).StatusCode);

        using var newLogin = await Login(client);
        var fresh = (await newLogin.Content.ReadFromJsonAsync<WebSessionResponse>(Json, Ct))!;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.RefreshSessions.Where(x => x.UserId == fresh.User.Id).ExecuteUpdateAsync(
            updates => updates.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)), Ct);
        UseCookie(client, Cookie(newLogin));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Seven_day_deadline_rejects_cookie_and_bearer_even_if_browser_retains_them()
    {
        var clock = new MutableClock { UtcNow = DateTimeOffset.UtcNow };
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        client.DefaultRequestHeaders.Add("X-CEP-Web-Session", "1");
        using var login = await Login(client);
        var session = (await login.Content.ReadFromJsonAsync<WebSessionResponse>(Json, Ct))!;
        Assert.Equal(clock.UtcNow.AddDays(7), session.SessionExpiresAt);
        UseCookie(client, Cookie(login));
        clock.UtcNow = session.SessionExpiresAt;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/web/refresh", new { }, Ct)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me", Ct)).StatusCode);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
