using CepApi.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CepApi.Api.Controllers;

[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(
    CredentialAuthenticationService credentialAuthentication,
    RefreshTokenSessionService refreshSessions,
    WebSessionCookie webCookie) : ApiControllerBase
{
    private bool ValidateWebRequest()
    {
        Response.Headers.CacheControl = "no-store";
        return webCookie.IsAllowed(Request);
    }

    private ObjectResult InvalidWebRequest()
        => ApiProblem(StatusCodes.Status403Forbidden,
            "Same-origin browser request required.", "web_origin_invalid");

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<TokenResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
        => Ok(await credentialAuthentication.AuthenticateAsync(
            request, IpAddress, cancellationToken));

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> Refresh(
        RefreshRequest request,
        CancellationToken cancellationToken)
        => Ok(await refreshSessions.RotateAsync(
            request.RefreshToken, IpAddress, cancellationToken));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await refreshSessions.LogoutAsync(request.RefreshToken, IpAddress, cancellationToken);
        return NoContent();
    }

    [HttpPost("web/login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<WebSessionResponse>> WebLogin(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!ValidateWebRequest()) return InvalidWebRequest();
        var tokens = await credentialAuthentication.AuthenticateAsync(
            request, IpAddress, cancellationToken, web: true);
        webCookie.Write(HttpContext, tokens);
        return Ok(ToWebResponse(tokens));
    }

    [HttpPost("web/refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<WebSessionResponse>> WebRefresh(CancellationToken cancellationToken)
    {
        if (!ValidateWebRequest()) return InvalidWebRequest();
        var cookie = webCookie.Read(Request);
        if (cookie is null)
        {
            webCookie.Clear(HttpContext);
            return ApiProblem(StatusCodes.Status401Unauthorized,
                "Browser session expired.", "session_expired");
        }
        try
        {
            var tokens = await refreshSessions.RotateAsync(cookie.RefreshToken, IpAddress, cancellationToken);
            webCookie.Write(HttpContext, tokens);
            return Ok(ToWebResponse(tokens));
        }
        catch (ApiProblemException exception) when (exception.StatusCode == StatusCodes.Status401Unauthorized)
        {
            webCookie.Clear(HttpContext);
            throw;
        }
    }

    [HttpPost("web/logout")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> WebLogout(CancellationToken cancellationToken)
    {
        if (!ValidateWebRequest()) return InvalidWebRequest();
        if (webCookie.Read(Request, allowExpired: true) is { } cookie)
            await refreshSessions.LogoutAsync(cookie.RefreshToken, IpAddress, cancellationToken);
        webCookie.Clear(HttpContext);
        return NoContent();
    }

    private static WebSessionResponse ToWebResponse(TokenResponse tokens)
        => new(tokens.AccessToken, tokens.AccessTokenExpiresAt,
            tokens.RefreshTokenExpiresAt, tokens.User);
}
