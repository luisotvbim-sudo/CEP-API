using CepApi.Infrastructure.Services;
using MimeKit;

namespace CepApi.UnitTests;

public sealed class SecurityEmailTemplateTests
{
    private static readonly DateTimeOffset Expiration = new(2026, 10, 5, 18, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Emails_keep_code_expiration_and_action_in_both_formats(bool recovery)
    {
        var options = new EmailOptions
        {
            InvitationActivationUrl = "https://portal.example/?convite=1&source=email",
            PasswordRecoveryUrl = "https://portal.example/"
        };
        var body = recovery
            ? SecurityEmailTemplate.PasswordReset(options, "123456", Expiration)
            : SecurityEmailTemplate.Invitation(options, "Minha organização", "123456", Expiration);
        using var message = new MimeMessage { Body = body };
        using var stream = new MemoryStream();
        message.WriteTo(stream, TestContext.Current.CancellationToken);
        stream.Position = 0;
        using var parsed = MimeMessage.Load(stream, TestContext.Current.CancellationToken);

        Assert.IsType<MultipartAlternative>(parsed.Body);
        foreach (var content in new[] { parsed.TextBody, System.Net.WebUtility.HtmlDecode(parsed.HtmlBody) })
        {
            Assert.Contains("123456", content);
            Assert.Contains("05/10/2026 15:30", content);
            Assert.Contains("horário de São Paulo", content);
        }
        var url = recovery ? options.PasswordRecoveryUrl : options.InvitationActivationUrl;
        Assert.Contains(url, parsed.TextBody);
        Assert.Contains($"href=\"{System.Net.WebUtility.HtmlEncode(url)}\"", parsed.HtmlBody);
        Assert.Contains(recovery ? "Abrir portal" : "Ativar minha conta", parsed.HtmlBody);
        Assert.DoesNotContain("123456", url);
    }

    [Fact]
    public void Organization_and_code_are_encoded_as_text_in_html()
    {
        using var message = new MimeMessage
        {
            Body = SecurityEmailTemplate.Invitation(new EmailOptions(), "<img src=x onerror='alert(1)'> & CEP",
                "<123456>", Expiration)
        };

        Assert.DoesNotContain("<img", message.HtmlBody);
        Assert.Contains("&lt;img", message.HtmlBody);
        Assert.Contains("&amp; CEP", message.HtmlBody);
        Assert.Contains("&lt;123456&gt;", message.HtmlBody);
        Assert.Contains("<123456>", message.TextBody);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://portal.example/")]
    [InlineData("/relative")]
    public void Email_actions_reject_non_https_urls(string url)
    {
        var options = new EmailOptions { InvitationActivationUrl = url, PasswordRecoveryUrl = url };

        Assert.Throws<InvalidOperationException>(() => SecurityEmailTemplate.Invitation(options, "CEP", "123456", Expiration));
        Assert.Throws<InvalidOperationException>(() => SecurityEmailTemplate.PasswordReset(options, "123456", Expiration));
    }
}
