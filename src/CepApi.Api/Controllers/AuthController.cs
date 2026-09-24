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
    WebSessionCookieService webCookie) : ApiControllerBase
{
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
        webCookie.ValidateRequest(Request);
        var tokens = await credentialAuthentication.AuthenticateAsync(
            request, IpAddress, cancellationToken);
        webCookie.Write(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(ToWebResponse(tokens));
    }

    [HttpPost("web/refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<WebSessionResponse>> WebRefresh(CancellationToken cancellationToken)
    {
        webCookie.ValidateRequest(Request);
        var refreshToken = webCookie.Read(Request);
        try
        {
            var tokens = await refreshSessions.RotateAsync(refreshToken, IpAddress, cancellationToken);
            webCookie.Write(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
            return Ok(ToWebResponse(tokens));
        }
        catch (ApiProblemException exception) when (exception.StatusCode == StatusCodes.Status401Unauthorized)
        {
            webCookie.Delete(Response);
            throw;
        }
    }

    [HttpPost("web/logout")]
    public async Task<IActionResult> WebLogout(CancellationToken cancellationToken)
    {
        webCookie.ValidateRequest(Request);
        try
        {
            var refreshToken = webCookie.Read(Request);
            await refreshSessions.LogoutAsync(refreshToken, IpAddress, cancellationToken);
        }
        catch (ApiProblemException exception) when (exception.Code == "session_expired")
        {
            // Logout remains idempotent when the browser no longer has a valid session cookie.
        }
        finally
        {
            webCookie.Delete(Response);
        }
        return NoContent();
    }

    private static WebSessionResponse ToWebResponse(TokenResponse tokens)
        => new(tokens.AccessToken, tokens.AccessTokenExpiresAt,
            tokens.RefreshTokenExpiresAt, tokens.User);
}
