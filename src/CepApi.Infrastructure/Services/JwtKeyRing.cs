using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CepApi.Infrastructure.Services;

public sealed class JwtKeyRing : IDisposable
{
    private readonly List<RSA> _ownedKeys = [];
    private readonly IReadOnlyCollection<RsaSecurityKey> _validationKeys;

    public JwtKeyRing(IOptions<JwtOptions> options, ILogger<JwtKeyRing> logger)
    {
        var settings = options.Value;
        var activeRsa = RSA.Create(3072);
        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPem))
        {
            activeRsa.ImportFromPem(NormalizePem(settings.PrivateKeyPem));
            if (activeRsa.KeySize < 2048)
                throw new InvalidOperationException("JWT signing requires RSA with at least 2048 bits.");
            _ = activeRsa.ExportParameters(true); // Reject public-only PEMs at startup.
        }
        else
        {
            logger.LogWarning("Jwt:PrivateKeyPem is not configured. Tokens use an ephemeral key and will be invalid after restart.");
        }

        _ownedKeys.Add(activeRsa);
        ActiveKey = new RsaSecurityKey(activeRsa) { KeyId = settings.KeyId };

        var validationKeys = new List<RsaSecurityKey> { ActiveKey };
        foreach (var previous in settings.PreviousPublicKeys)
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(NormalizePem(previous.PublicKeyPem));
            _ownedKeys.Add(rsa);
            validationKeys.Add(new RsaSecurityKey(rsa) { KeyId = previous.KeyId });
        }

        _validationKeys = validationKeys;
    }

    public RsaSecurityKey ActiveKey { get; }
    public IEnumerable<SecurityKey> ValidationKeys => _validationKeys;

    public object GetJwks() => new
    {
        keys = _validationKeys.Select(key =>
        {
            var parameters = key.Rsa?.ExportParameters(false) ?? key.Parameters;
            return new
            {
                kty = "RSA",
                use = "sig",
                alg = SecurityAlgorithms.RsaSha256,
                kid = key.KeyId,
                n = Base64UrlEncoder.Encode(parameters.Modulus),
                e = Base64UrlEncoder.Encode(parameters.Exponent)
            };
        })
    };

    public void Dispose()
    {
        foreach (var key in _ownedKeys)
        {
            key.Dispose();
        }
    }

    private static string NormalizePem(string pem) => pem.Replace("\\n", "\n", StringComparison.Ordinal);
}
