using Microsoft.AspNetCore.DataProtection;

namespace CepApi.Api.Controllers;

public sealed class WebSessionCookieService(IDataProtectionProvider dataProtectionProvider)
{
    public const string CookieName = "__Host-cep-session";
    private const string MarkerHeader = "X-CEP-Web-Session";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("CEP-API.WebSession.v1");

    public void ValidateRequest(HttpRequest request)
    {
        if (!string.Equals(request.Headers[MarkerHeader], "1", StringComparison.Ordinal))
            throw InvalidOrigin();

        if (request.Headers.Origin.Count > 0 &&
            (!Uri.TryCreate(request.Headers.Origin.ToString(), UriKind.Absolute, out var origin) ||
             !string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)))
            throw InvalidOrigin();

        var fetchSite = request.Headers["Sec-Fetch-Site"];
        if (fetchSite.Count > 0 &&
            !string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase))
            throw InvalidOrigin();
    }

    public string Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var protectedToken))
            throw MissingSession();

        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch
        {
            throw MissingSession();
        }
    }

    public void Write(HttpResponse response, string refreshToken, DateTimeOffset expiresAt)
        => response.Cookies.Append(CookieName, _protector.Protect(refreshToken), Options(expiresAt));

    public void Delete(HttpResponse response)
        => response.Cookies.Delete(CookieName, Options(DateTimeOffset.UnixEpoch));

    private static CookieOptions Options(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
        Expires = expiresAt
    };

    private static ApiProblemException InvalidOrigin()
        => new(StatusCodes.Status403Forbidden, "The web session origin is invalid.", "web_origin_invalid");

    private static ApiProblemException MissingSession()
        => new(StatusCodes.Status401Unauthorized, "The web session has expired.", "session_expired");
}
