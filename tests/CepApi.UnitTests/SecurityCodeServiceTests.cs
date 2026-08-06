using CepApi.Infrastructure.Services;

namespace CepApi.UnitTests;

public sealed class SecurityCodeServiceTests
{
    private readonly SecurityCodeService _service = new();

    [Fact]
    public void Codes_are_hashed_and_compared_without_storing_plaintext()
    {
        const string code = "ABCD2345WXYZ";
        var hash = _service.Hash(code);

        Assert.NotEqual(code, hash);
        Assert.True(_service.Verify(code, hash));
        Assert.False(_service.Verify("ABCD2345WXY2", hash));
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
