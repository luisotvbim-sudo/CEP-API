using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CepApi.UnitTests;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void Plugin_grant_is_signed_and_expires_after_exactly_72_hours()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            ApiAudience = "test-api",
            PluginAudience = "test-plugin",
            PluginGrantHours = 72,
            KeyId = "test-key"
        });
        using var keys = new JwtKeyRing(options, NullLogger<JwtKeyRing>.Instance);
        var service = new JwtTokenService(options, keys);
        var now = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        var user = new TokenUser(Guid.NewGuid(), "Test User", "test@example.com", Guid.NewGuid(), UserRole.User);

        var grant = service.CreatePluginGrant(user, Product.Revit, now);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(grant.Token);

        Assert.Equal(now.AddHours(72), grant.ExpiresAt);
        Assert.Equal("revit", token.Claims.Single(x => x.Type == "product").Value);
        Assert.Equal(user.OrganizationId.ToString(), token.Claims.Single(x => x.Type == "org_id").Value);
        Assert.Equal("test-plugin", token.Audiences.Single());
        Assert.Equal("test-key", token.Header.Kid);
        var handler = new JwtSecurityTokenHandler();
        var validation = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys.ValidationKeys,
            ValidateIssuer = true,
            ValidIssuer = "test-issuer",
            ValidateAudience = true,
            ValidAudience = "test-plugin",
            ValidateLifetime = false,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidTypes = ["plugin-grant+jwt"]
        };
        handler.ValidateToken(grant.Token, validation, out _);
        var parts = grant.Token.Split('.');
        var signature = Base64UrlEncoder.DecodeBytes(parts[2]);
        signature[0] ^= 1;
        var tampered = $"{parts[0]}.{parts[1]}.{Base64UrlEncoder.Encode(signature)}";
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(tampered, validation, out _));
        validation.ValidAudience = "test-api";
        Assert.Throws<SecurityTokenInvalidAudienceException>(() => handler.ValidateToken(grant.Token, validation, out _));
    }

    [Fact]
    public void Refresh_tokens_are_random_and_only_their_hash_is_stable()
    {
        var options = Options.Create(new JwtOptions());
        using var keys = new JwtKeyRing(options, NullLogger<JwtKeyRing>.Instance);
        var service = new JwtTokenService(options, keys);

        var first = service.CreateRefreshToken();
        var second = service.CreateRefreshToken();

        Assert.NotEqual(first, second);
        Assert.Equal(service.HashRefreshToken(first), service.HashRefreshToken(first));
        Assert.NotEqual(first, service.HashRefreshToken(first));
    }

    [Fact]
    public void Key_rotation_publishes_only_public_material_and_validates_grants_signed_by_the_previous_key()
    {
        using var previousRsa = RSA.Create(2048);
        using var currentRsa = RSA.Create(2048);
        var previousOptions = Options.Create(new JwtOptions { KeyId = "previous", PrivateKeyPem = previousRsa.ExportPkcs8PrivateKeyPem() });
        using var previousKeys = new JwtKeyRing(previousOptions, NullLogger<JwtKeyRing>.Instance);
        var user = new TokenUser(Guid.NewGuid(), "Test", "test@example.test", Guid.NewGuid(), UserRole.User);
        var grant = new JwtTokenService(previousOptions, previousKeys).CreatePluginGrant(user, Product.Revit, DateTimeOffset.UtcNow);
        var options = Options.Create(new JwtOptions
        {
            KeyId = "current",
            PrivateKeyPem = currentRsa.ExportPkcs8PrivateKeyPem(),
            PreviousPublicKeys = [new() { KeyId = "previous", PublicKeyPem = previousRsa.ExportSubjectPublicKeyInfoPem() }]
        });
        using var keys = new JwtKeyRing(options, NullLogger<JwtKeyRing>.Instance);
        var jwksJson = JsonSerializer.Serialize(keys.GetJwks());
        using var jwks = JsonDocument.Parse(jwksJson);
        Assert.Equal(2, jwks.RootElement.GetProperty("keys").GetArrayLength());
        foreach (var key in jwks.RootElement.GetProperty("keys").EnumerateArray())
        {
            Assert.Equal("RSA", key.GetProperty("kty").GetString());
            foreach (var privateField in new[] { "d", "p", "q", "dp", "dq", "qi" })
                Assert.False(key.TryGetProperty(privateField, out _));
        }
        new JwtSecurityTokenHandler().ValidateToken(grant.Token, new TokenValidationParameters
        {
            IssuerSigningKeys = new JsonWebKeySet(jwksJson).GetSigningKeys(),
            ValidateIssuerSigningKey = true,
            ValidIssuer = options.Value.Issuer,
            ValidAudience = options.Value.PluginAudience,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidTypes = ["plugin-grant+jwt"],
            ClockSkew = TimeSpan.Zero
        }, out _);
    }

    [Fact]
    public void Access_tokens_include_session_and_security_version_without_exposing_the_security_stamp()
    {
        var options = Options.Create(new JwtOptions());
        using var keys = new JwtKeyRing(options, NullLogger<JwtKeyRing>.Instance);
        var service = new JwtTokenService(options, keys);
        var familyId = Guid.NewGuid();
        var stamp = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        var user = new TokenUser(Guid.NewGuid(), "Admin", "admin@example.test", null, UserRole.SystemAdmin);
        var access = service.CreateAccessToken(user, familyId, stamp, now);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(access.Token);
        Assert.Equal("at+jwt", token.Header.Typ);
        Assert.Equal(now.AddMinutes(options.Value.AccessTokenMinutes), access.ExpiresAt);
        Assert.Equal(familyId.ToString(), token.Claims.Single(x => x.Type == "sid").Value);
        Assert.Equal(JwtTokenService.SecurityVersion(stamp), token.Claims.Single(x => x.Type == "security_version").Value);
        Assert.DoesNotContain(token.Claims, x => x.Value == stamp || x.Type == "org_id");
        Assert.NotEqual(JwtTokenService.SecurityVersion(stamp), JwtTokenService.SecurityVersion(Guid.NewGuid().ToString()));
    }
}
