using System.Text.Json;
using System.Text.Json.Serialization;

namespace CepApi.Application;

public sealed class DesktopTelemetryBatchRequest
{
    public List<DesktopTelemetryItem>? Events { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record class DesktopTelemetryItem
{
    public Guid EventId { get; set; }
    public Guid InstallationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Code { get; set; }
    public string? Phase { get; set; }
    public string? Outcome { get; set; }
    public string? Action { get; set; }
    public string? ErrorCode { get; set; }
    public string? AppVersion { get; set; }
    public Guid? OperationId { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record DesktopTelemetryBatchResponse(IReadOnlyList<Guid> AcceptedEventIds, IReadOnlyList<Guid> RejectedEventIds);
