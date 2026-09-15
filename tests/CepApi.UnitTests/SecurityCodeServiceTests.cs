using CepApi.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace CepApi.UnitTests;

public sealed class SecurityCodeServiceTests
{
    private readonly SecurityCodeService _service = new(Options.Create(new SecurityCodeOptions
    {
        HmacKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray())
    }));

    [Fact]
    public void Codes_are_hashed_and_compared_without_storing_plaintext()
    {
        const string code = "ABCD2345WXYZ";
        var hash = _service.Hash(code);

        Assert.NotEqual(code, hash);
        Assert.StartsWith("h1:", hash);
        Assert.True(_service.Verify(code, hash));
        Assert.False(_service.Verify("ABCD2345WXY2", hash));
    }

    [Fact]
    public void Legacy_sha256_hashes_remain_valid_during_rotation()
    {
        const string code = "12345678";
        var legacy = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(code)));

        Assert.True(_service.Verify(code, legacy));
        Assert.False(_service.Verify("12345679", legacy));
    }

    [Fact]
    public void Invitation_codes_avoid_ambiguous_characters()
    {
        var code = _service.GenerateInvitationCode();

        Assert.Equal(12, code.Length);
        Assert.DoesNotContain('0', code);
        Assert.DoesNotContain('1', code);
        Assert.DoesNotContain('I', code);
        Assert.DoesNotContain('O', code);
    }

    [Fact]
    public void Password_reset_codes_have_eight_digits()
    {
        var code = _service.GeneratePasswordResetCode();

        Assert.Equal(8, code.Length);
        Assert.True(code.All(char.IsAsciiDigit));
    }
}
