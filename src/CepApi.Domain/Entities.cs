namespace CepApi.Domain;

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProductAccess
{
    public Guid UserId { get; set; }
    public Product Product { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}

public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public required string Email { get; set; }
    public UserRole Role { get; set; }
    public bool CanUseRevit { get; set; }
    public bool CanUseZwcad { get; set; }
    public required string CodeHash { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class PasswordReset
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class RefreshSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid FamilyId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public required string ClientType { get; set; }
    public string? ClientVersion { get; set; }
    public string? InstallationId { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public required string Action { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PluginUsageEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClientEventId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Product Product { get; set; }
    public required string Command { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public int? DurationMs { get; set; }
    public PluginUsageOutcome Outcome { get; set; }
    public string? ErrorCode { get; set; }
    public required string PluginVersion { get; set; }
    public required string HostVersion { get; set; }
    public required string InstallationId { get; set; }
}

public sealed class EmailOutboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string ProtectedPayload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Attempts { get; set; }
}
