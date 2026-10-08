using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using CepApi.Application;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/desktop-telemetry")]
[Authorize]
[EnableRateLimiting("desktop-telemetry")]
[RequestSizeLimit(32768)]
public sealed partial class DesktopTelemetryController(AppDbContext db, IClock clock, IConfiguration configuration) : ApiControllerBase
{
    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal)
    {
        "desktop_started", "desktop_start_failed", "power_check_allowed", "power_check_denied",
        "power_check_failed", "power_schedule_confirmed", "power_schedule_failed",
        "power_cancel_confirmed", "power_cancel_failed", "power_recovery_required",
        "power_reconciled", "update_check_failed", "update_install_started", "update_install_failed"
    };
    private static readonly HashSet<string> Phases = new(StringComparer.Ordinal)
        { "startup", "authorization", "schedule", "cancel", "reconcile", "update" };
    private static readonly HashSet<string> Outcomes = new(StringComparer.Ordinal)
        { "success", "denied", "failure", "uncertain" };
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal)
        { "shutdown", "restart", "hibernate" };
    private static readonly HashSet<string> ErrorCodes = new(StringComparer.Ordinal)
        { "none", "transport_unavailable", "http_error", "invalid_response", "service_unavailable",
          "permission_denied", "timeout", "internal_error" };

    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}\.\d{1,4}(?:[-+][0-9A-Za-z.-]{1,16})?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [HttpPost("events")]
    [ProducesResponseType<DesktopTelemetryBatchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<DesktopTelemetryBatchResponse>> Ingest(
        [FromBody, Required] DesktopTelemetryBatchRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (CurrentOrganizationId is not { } organizationId)
            return ApiProblem(403, "An operational organization is required.", "forbidden");
        if (request.Extra is { Count: > 0 } || request.Events is not { Count: >= 1 and <= 50 })
            return ApiProblem(400, "A batch must contain 1 to 50 events and no extra fields.", "invalid_telemetry_batch");

        var now = clock.UtcNow;
        var retentionDays = configuration.GetValue("DesktopTelemetry:RetentionDays", 30);
        var ids = new HashSet<Guid>();
        foreach (var item in request.Events)
        {
            if (item is null || item.EventId == Guid.Empty || !ids.Add(item.EventId))
                return ApiProblem(400, "An event ID is missing or repeated.", "invalid_telemetry_batch");
        }
        var accepted = new List<DesktopTelemetryItem>();
        var rejected = new List<Guid>();
        foreach (var item in request.Events)
        {
            if (item.Extra is { Count: > 0 } || item.InstallationId == Guid.Empty || item.OperationId == Guid.Empty ||
                item.OccurredAt.Offset != TimeSpan.Zero ||
                item.OccurredAt < now.AddDays(-retentionDays) || item.OccurredAt > now.AddMinutes(5) ||
                item.Code is null || !Codes.Contains(item.Code) ||
                item.Phase is null || !Phases.Contains(item.Phase) ||
                item.Outcome is null || !Outcomes.Contains(item.Outcome) ||
                item.ErrorCode is null || !ErrorCodes.Contains(item.ErrorCode) ||
                item.AppVersion is null || item.AppVersion.Length > 32 || !VersionPattern().IsMatch(item.AppVersion) ||
                (item.Action is not null && !Actions.Contains(item.Action)) ||
                !IsCoherent(item) ||
                (item.Code.StartsWith("power_", StringComparison.Ordinal)
                    ? item.Code == "power_cancel_failed" ? (item.Action is null) != (item.OperationId is null)
                        : item.Action is null || item.OperationId is null
                    : item.Action is not null || item.OperationId is not null))
                rejected.Add(item.EventId);
            else accepted.Add(item);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in accepted)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO time_control.desktop_telemetry_events
                    ("OrganizationId", "UserId", "EventId", "InstallationId", "OccurredAt", "ReceivedAt", "Code", "Phase", "Outcome", "Action", "ErrorCode", "AppVersion", "OperationId")
                VALUES ({organizationId}, {CurrentUserId}, {item.EventId}, {item.InstallationId}, {item.OccurredAt}, {now}, {item.Code}, {item.Phase}, {item.Outcome}, {item.Action}, {item.ErrorCode}, {item.AppVersion}, {item.OperationId})
                ON CONFLICT ("OrganizationId", "UserId", "EventId") DO NOTHING
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Ok(new DesktopTelemetryBatchResponse(accepted.Select(x => x.EventId).ToArray(), rejected));
    }

    private static bool IsCoherent(DesktopTelemetryItem item)
    {
        var valid = item.Code switch
        {
            "desktop_started" => item.Phase == "startup" && item.Outcome == "success",
            "desktop_start_failed" => item.Phase == "startup" && item.Outcome == "failure",
            "power_check_allowed" => item.Phase == "authorization" && item.Outcome == "success",
            "power_check_denied" => item.Phase == "authorization" && item.Outcome == "denied",
            "power_check_failed" => item.Phase == "authorization" && item.Outcome == "failure",
            "power_schedule_confirmed" => item.Phase == "schedule" && item.Outcome == "success",
            "power_schedule_failed" => item.Phase == "schedule" && item.Outcome is ("failure" or "uncertain"),
            "power_cancel_confirmed" => item.Phase == "cancel" && item.Outcome == "success",
            "power_cancel_failed" => item.Phase == "cancel" && item.Outcome == "uncertain",
            "power_recovery_required" => item.Phase == "reconcile" && item.Outcome == "uncertain",
            "power_reconciled" => item.Phase == "reconcile" && item.Outcome is ("success" or "uncertain"),
            "update_check_failed" or "update_install_failed" => item.Phase == "update" && item.Outcome == "failure",
            "update_install_started" => item.Phase == "update" && item.Outcome == "success",
            _ => false
        };
        if (!valid) return false;
        // Reconciliation can be required by a persisted pending request (none) or a classified IPC failure.
        if (item.Code == "power_recovery_required") return true;
        var expectsError = item.Code is "desktop_start_failed" or "power_check_failed" or "power_schedule_failed"
            or "power_cancel_failed" or "update_check_failed" or "update_install_failed";
        return (item.ErrorCode != "none") == expectsError;
    }
}
