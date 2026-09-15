namespace CepApi.Infrastructure.Services;

public sealed class SecurityCodeOptions
{
    public const string SectionName = "SecurityCodes";
    public string HmacKey { get; set; } = string.Empty;
}
