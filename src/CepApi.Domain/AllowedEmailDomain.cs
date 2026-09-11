namespace CepApi.Domain;

public sealed class AllowedEmailDomain
{
    public required string Domain { get; set; }
    public bool IsEnabled { get; set; } = true;
}
