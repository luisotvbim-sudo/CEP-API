using System.Security.Cryptography;
using System.Text;
using CepApi.Application;
using Microsoft.Extensions.Options;

namespace CepApi.Infrastructure.Services;

public sealed class SecurityCodeService(IOptions<SecurityCodeOptions> options) : ISecurityCodeService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string HmacPrefix = "h1:";
    private readonly byte[] _hmacKey = ReadKey(options.Value.HmacKey);

    public string GenerateInvitationCode() => Generate(12);

    public string GeneratePasswordResetCode()
        => RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    public string Hash(string value)
        => HmacPrefix + Convert.ToBase64String(HMACSHA256.HashData(_hmacKey, Normalize(value)));

    public bool Verify(string value, string expectedHash)
    {
        byte[] expected;
        byte[] actual;
        try
        {
            if (expectedHash.StartsWith(HmacPrefix, StringComparison.Ordinal))
            {
                expected = Convert.FromBase64String(expectedHash[HmacPrefix.Length..]);
                actual = HMACSHA256.HashData(_hmacKey, Normalize(value));
            }
            else
            {
                // Transitional support for codes created before keyed hashing was introduced.
                expected = Convert.FromHexString(expectedHash);
                actual = SHA256.HashData(Normalize(value));
            }
        }
        catch (FormatException)
        {
            return false;
        }

        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Normalize(string value) => Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant());

    private static byte[] ReadKey(string? configuredKey)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey?.Trim() ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("SecurityCodes:HmacKey must be valid Base64.", exception);
        }

        if (key.Length < 32)
            throw new InvalidOperationException("SecurityCodes:HmacKey must contain at least 32 random bytes.");
        return key;
    }

    private static string Generate(int length)
    {
        Span<char> result = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(result);
    }
}
