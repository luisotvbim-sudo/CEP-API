using System.Security.Cryptography;
using System.Text;
using CepApi.Application;

namespace CepApi.Infrastructure.Services;

public sealed class SecurityCodeService : ISecurityCodeService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string GenerateInvitationCode() => Generate(12);

    public string GeneratePasswordResetCode()
        => RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    public string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToUpperInvariant())));

    public bool Verify(string value, string expectedHash)
    {
        var actual = Convert.FromHexString(Hash(value));
        var expected = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
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
