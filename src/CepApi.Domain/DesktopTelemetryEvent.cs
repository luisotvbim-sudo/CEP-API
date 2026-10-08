namespace CepApi.Domain;

// These values are observations reported by an untrusted client, not authoritative energy decisions.
public sealed class DesktopTelemetryEvent
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid EventId { get; set; }
    public Guid InstallationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string Code { get; set; }
    public required string Phase { get; set; }
    public required string Outcome { get; set; }
    public string? Action { get; set; }
    public required string ErrorCode { get; set; }
    public required string AppVersion { get; set; }
    public Guid? OperationId { get; set; }
}
