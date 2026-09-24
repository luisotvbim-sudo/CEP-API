using CepApi.Infrastructure.Services;

namespace CepApi.Api.Startup;

internal static class ProductionConfigurationValidator
{
    public static void Validate(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsProduction()) return;

        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
        var email = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>();
        var securityCodes = builder.Configuration.GetSection(SecurityCodeOptions.SectionName).Get<SecurityCodeOptions>();
        var workforce = builder.Configuration.GetSection(WorkforceIntegrationOptions.SectionName)
            .Get<WorkforceIntegrationOptions>() ?? new WorkforceIntegrationOptions();

        ValidateJwt(jwt);
        ValidateEmail(email);
        ValidateSecurityCodes(securityCodes);

        if (string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeysPath"]))
            throw new InvalidOperationException(
                "DataProtection:KeysPath is required in Production for the durable email outbox.");

        ValidateWorkforceIntegration("Monday", workforce.Monday.Enabled, workforce.Monday.Token,
            workforce.Monday.ApiUrl, workforce.Monday.BoardId);
        ValidateWorkforceIntegration("VR Mais", workforce.VrMais.Enabled, workforce.VrMais.Token,
            workforce.VrMais.ApiUrl);
    }

    private static void ValidateJwt(JwtOptions? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt?.PrivateKeyPem))
            throw new InvalidOperationException("Jwt:PrivateKeyPem is required in Production.");
        if (jwt.KeyId == "development-key" || jwt.AccessTokenMinutes is < 1 or > 15 ||
            jwt.PluginGrantHours is < 1 or > 72)
            throw new InvalidOperationException("Configure a production Jwt:KeyId and bounded token lifetimes.");
    }

    private static void ValidateEmail(EmailOptions? email)
    {
        if (string.IsNullOrWhiteSpace(email?.Host) || string.IsNullOrWhiteSpace(email.FromAddress))
            throw new InvalidOperationException("A valid SMTP Email configuration is required in Production.");
        if (email.AllowInsecureTransport)
            throw new InvalidOperationException("SMTP TLS is required in Production.");
    }

    private static void ValidateSecurityCodes(SecurityCodeOptions? securityCodes)
    {
        try
        {
            if (Convert.FromBase64String(securityCodes?.HmacKey?.Trim() ?? string.Empty).Length < 32)
                throw new InvalidOperationException("SecurityCodes:HmacKey must contain at least 32 random bytes.");
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("SecurityCodes:HmacKey must be valid Base64.", exception);
        }
    }

    private static void ValidateWorkforceIntegration(
        string name,
        bool enabled,
        string? token,
        string apiUrl,
        string? boardId = null)
    {
        if (!enabled) return;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"{name} token is required when the integration is enabled.");
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{name} API URL must use HTTPS.");
        if (boardId is not null && string.IsNullOrWhiteSpace(boardId))
            throw new InvalidOperationException($"{name} board id is required when the integration is enabled.");
    }
}
