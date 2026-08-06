namespace CepApi.Infrastructure.Services;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "cep-api";
    public string ApiAudience { get; set; } = "cep-api";
    public string PluginAudience { get; set; } = "cep-plugin";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int PluginGrantHours { get; set; } = 72;
    public string KeyId { get; set; } = "development-key";
    public string? PrivateKeyPem { get; set; }
    public List<PreviousPublicKeyOptions> PreviousPublicKeys { get; set; } = [];
}

public sealed class PreviousPublicKeyOptions
{
    public required string KeyId { get; set; }
    public required string PublicKeyPem { get; set; }
}
