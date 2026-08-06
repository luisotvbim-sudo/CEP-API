namespace CepApi.Infrastructure.Services;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseSsl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@cep-api.local";
    public string FromName { get; set; } = "CEP Plugins";
}
