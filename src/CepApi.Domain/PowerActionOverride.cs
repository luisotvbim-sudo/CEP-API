namespace CepApi.Domain;

public sealed class PowerActionOverride
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PinVersion { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public required string RecipientSecurityVersion { get; set; }
}

public sealed class PowerPinConfiguration
{
    public int Id { get; set; } = 1;
    public required string PinHash { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAt { get; set; }
}
