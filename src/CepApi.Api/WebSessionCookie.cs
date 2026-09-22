using System.Security.Cryptography;
using System.Text.Json;
using CepApi.Application;
using Microsoft.AspNetCore.DataProtection;

namespace CepApi.Api;

// Only the opaque, authenticated payload is persisted in the browser. JWTs remain in memory.
public sealed class WebSessionCookie(IDataProtectionProvider protection, IHostEnvironment environment, IClock clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("CEP.WebSession.v1");
    public sealed record Payload(string RefreshToken, DateTimeOffset ExpiresAt);

    private static bool IsLocal(HttpRequest request) =>
        Uri.TryCreate($"http://{request.Host}", UriKind.Absolute, out var uri) && uri.IsLoopback;

    private bool Secure(HttpRequest request) => request.IsHttps || environment.IsProduction() || !IsLocal(request);
    private string Name(HttpRequest request) => Secure(request) ? "__Host-cep-session" : "cep-session-local";

    public bool IsAllowed(HttpRequest request)
    {
        // No cross-origin cookie authentication. The same-origin proxy must preserve Host.
        if (!request.IsHttps && (environment.IsProduction() || !IsLocal(request))) return false;
        if (request.Headers["X-CEP-Web-Session"] != "1") return false;
        if (request.Headers.TryGetValue("Sec-Fetch-Site", out var site) && site != "same-origin") return false;
        return Uri.TryCreate(request.Headers.Origin.ToString(), UriKind.Absolute, out var origin) &&
            string.Equals(origin.GetLeftPart(UriPartial.Authority), $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase) &&
            origin.AbsolutePath == "/" && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment);
    }

    public Payload? Read(HttpRequest request, bool allowExpired = false)
    {
        if (!request.Cookies.TryGetValue(Name(request), out var value)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value));
            return payload is not null && !string.IsNullOrWhiteSpace(payload.RefreshToken) &&
                (allowExpired || payload.ExpiresAt > clock.UtcNow) ? payload : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private CookieOptions Options(HttpRequest request) => new()
    {
        HttpOnly = true, Secure = Secure(request), SameSite = SameSiteMode.Strict,
        Path = "/", IsEssential = true
    };

    public void Write(HttpContext context, TokenResponse tokens)
    {
        var options = Options(context.Request);
        options.Expires = tokens.RefreshTokenExpiresAt;
        context.Response.Cookies.Append(Name(context.Request),
            protector.Protect(JsonSerializer.Serialize(new Payload(tokens.RefreshToken, tokens.RefreshTokenExpiresAt))), options);
    }

    public void Clear(HttpContext context) => context.Response.Cookies.Delete(Name(context.Request), Options(context.Request));
}
