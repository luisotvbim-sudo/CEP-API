using System.Net;
using MimeKit;

namespace CepApi.Infrastructure.Services;

internal static class SecurityEmailTemplate
{
    public static MimeEntity Invitation(EmailOptions options, string organizationName, string code, DateTimeOffset expiresAt)
        => Build("Ative sua conta CEP", $"Você foi convidado para {organizationName}.",
            "Clique no botão para informar seu e-mail, nome e o código abaixo. Crie uma senha de pelo menos 6 caracteres.",
            "Código de ativação", code, expiresAt, "Ativar minha conta", options.InvitationActivationUrl,
            "O código não é sua senha. Depois de ativar, entre com seu e-mail e a senha criada. Se o convite expirar, peça um novo ao administrador.",
            "Se não esperava este convite, ignore esta mensagem.");

    public static MimeEntity PasswordReset(EmailOptions options, string code, DateTimeOffset expiresAt)
        => Build("Recupere seu acesso", "Recebemos uma solicitação para redefinir sua senha.",
            "Volte à tela de recuperação em que solicitou este e-mail e informe o código abaixo para criar sua nova senha.",
            "Código de recuperação", code, expiresAt, "Abrir portal", options.PasswordRecoveryUrl,
            "Se fechou a tela de recuperação, abra o portal e selecione Esqueci minha senha para solicitar um novo código. Somente o código mais recente será válido.",
            "Se não solicitou esta alteração, ignore esta mensagem. Sua senha continua a mesma. Não compartilhe este código.");

    private static MimeEntity Build(string title, string introduction, string instructions, string codeLabel,
        string code, DateTimeOffset expiresAt, string action, string actionUrl, string help, string footer)
    {
        if (!Uri.TryCreate(actionUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Security email links must use an absolute HTTPS URL.");

        var validity = $"Válido até {TimeZoneInfo.ConvertTimeBySystemTimeZoneId(expiresAt, "America/Sao_Paulo"):dd/MM/yyyy HH:mm} (horário de São Paulo).";
        return new BodyBuilder
        {
            TextBody = $"{title}\n\n{introduction}\n\n{instructions}\n\n{codeLabel}: {code}\n{validity}\n\n{action}: {actionUrl}\n\n{help}\n\n{footer}",
            HtmlBody = $"""
                <!doctype html>
                <html lang="pt-BR">
                <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{Encode(title)}</title></head>
                <body style="margin:0;padding:0;background-color:#f5f5f3;color:#30312e;font-family:Arial,Helvetica,sans-serif;">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#f5f5f3;">
                    <tr><td align="center" style="padding:32px 16px;">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background-color:#fdfdfb;border:1px solid #e2e3de;border-radius:16px;">
                        <tr><td style="padding:24px;background-color:#292c28;border-radius:16px 16px 0 0;color:#f9f9f5;font-size:20px;font-weight:bold;">
                          CEP <span style="color:#f28b4e;font-size:13px;font-weight:normal;">| Conceito</span>
                        </td></tr>
                        <tr><td style="padding:28px 24px;font-size:16px;line-height:1.6;">
                          <h1 style="margin:0 0 16px;font-size:26px;line-height:1.25;color:#30312e;">{Encode(title)}</h1>
                          <p style="margin:0 0 12px;">{Encode(introduction)}</p>
                          <p style="margin:0 0 24px;">{Encode(instructions)}</p>
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background-color:#f5f5f3;border:1px solid #e2e3de;border-radius:8px;">
                            <tr><td align="center" style="padding:20px 12px;">
                              <p style="margin:0 0 8px;font-size:13px;color:#74756f;">{Encode(codeLabel)}</p>
                              <p style="margin:0;font-family:Consolas,monospace;font-size:24px;font-weight:bold;letter-spacing:2px;overflow-wrap:anywhere;">{Encode(code)}</p>
                            </td></tr>
                          </table>
                          <p style="margin:10px 0 24px;font-size:12px;color:#74756f;">{Encode(validity)}</p>
                          <table role="presentation" cellspacing="0" cellpadding="0">
                            <tr><td bgcolor="#c54c10" style="border-radius:8px;text-align:center;mso-padding-alt:14px 24px;">
                              <a href="{Encode(actionUrl)}" style="display:inline-block;padding:14px 24px;border:1px solid #c54c10;border-radius:8px;background-color:#c54c10;color:#ffffff;text-decoration:none;font-size:16px;font-weight:bold;">{Encode(action)}</a>
                            </td></tr>
                          </table>
                          <p style="margin:24px 0 0;font-size:13px;color:#74756f;">{Encode(help)}</p>
                        </td></tr>
                        <tr><td style="padding:20px 24px;border-top:1px solid #e2e3de;font-size:12px;line-height:1.6;color:#74756f;">{Encode(footer)}</td></tr>
                      </table>
                    </td></tr>
                  </table>
                </body>
                </html>
                """
        }.ToMessageBody();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
