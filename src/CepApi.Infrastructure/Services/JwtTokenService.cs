using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CepApi.Application;
using CepApi.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CepApi.Infrastructure.Services;

public sealed class JwtTokenService(IOptions<JwtOptions> options, JwtKeyRing keyRing) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly SigningCredentials _credentials = new(keyRing.ActiveKey, SecurityAlgorithms.RsaSha256);

    public AccessTokenResult CreateAccessToken(TokenUser user, DateTimeOffset now)
    {
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = BaseClaims(user, now);
        claims.Add(new Claim("role", user.Role.ToString()));
        var token = WriteToken(claims, _options.ApiAudience, now, expiresAt, "at+jwt");
        return new AccessTokenResult(token, expiresAt);
    }

    public PluginGrantResult CreatePluginGrant(TokenUser user, Product product, DateTimeOffset now)
    {
        var expiresAt = now.AddHours(_options.PluginGrantHours);
        var claims = BaseClaims(user, now);
        claims.Add(new Claim("product", product.ToString().ToLowerInvariant()));
        var token = WriteToken(claims, _options.PluginAudience, now, expiresAt, "plugin-grant+jwt");
        return new PluginGrantResult(token, expiresAt);
    }

    public string CreateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static List<Claim> BaseClaims(TokenUser user, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new("name", user.DisplayName),
            new("email", user.Email)
        };
        if (user.OrganizationId is { } organizationId)
        {
            claims.Add(new Claim("org_id", organizationId.ToString()));
        }
        return claims;
    }

    private string WriteToken(IEnumerable<Claim> claims, string audience, DateTimeOffset now, DateTimeOffset expiresAt, string type)
    {
        var header = new JwtHeader(_credentials) { [JwtHeaderParameterNames.Typ] = type };
        var payload = new JwtPayload(_options.Issuer, audience, claims, now.UtcDateTime, expiresAt.UtcDateTime, now.UtcDateTime);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}
