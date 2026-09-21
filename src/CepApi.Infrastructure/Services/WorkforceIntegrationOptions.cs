namespace CepApi.Infrastructure.Services;

public sealed class WorkforceIntegrationOptions
{
    public const string SectionName = "WorkforceIntegrations";
    public MondayDirectoryOptions Monday { get; set; } = new();
    public VrMaisDirectoryOptions VrMais { get; set; } = new();
}

public sealed class MondayDirectoryOptions
{
    public bool Enabled { get; set; }
    public string ApiUrl { get; set; } = "https://api.monday.com/v2";
    public string ApiVersion { get; set; } = "2026-07";
    public string BoardId { get; set; } = "9920862624";
    public string? Token { get; set; }
}

public sealed class VrMaisDirectoryOptions
{
    public bool Enabled { get; set; }
    public string ApiUrl { get; set; } = "https://api.pontomais.com.br/external_api/v1";
    public string? Token { get; set; }
}
