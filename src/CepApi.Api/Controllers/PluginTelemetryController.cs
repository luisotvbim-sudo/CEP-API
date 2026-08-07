using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/plugin/telemetry/events")]
[Authorize(Roles = $"{nameof(UserRole.OrganizationAdmin)},{nameof(UserRole.User)}")]
[EnableRateLimiting("telemetry")]
public sealed class PluginTelemetryController(AppDbContext db, IClock clock) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PluginTelemetryIngestionResponse>> Create(
        CreatePluginTelemetryBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Product) || request.Events.Count is < 1 or > PluginTelemetryRules.MaxBatchSize)
            return ApiProblem(StatusCodes.Status400BadRequest, "Telemetry batch is invalid.", "invalid_telemetry_batch");
        if (string.IsNullOrWhiteSpace(request.PluginVersion) || string.IsNullOrWhiteSpace(request.HostVersion) ||
            string.IsNullOrWhiteSpace(request.InstallationId))
            return ApiProblem(StatusCodes.Status400BadRequest, "Plugin client information is required.", "invalid_plugin_client");

        var receivedAt = clock.UtcNow;
        foreach (var telemetryEvent in request.Events)
        {
            if (telemetryEvent.EventId == Guid.Empty || string.IsNullOrWhiteSpace(telemetryEvent.Command) ||
                !Enum.IsDefined(telemetryEvent.Outcome) || !PluginTelemetryRules.IsDurationAllowed(telemetryEvent.DurationMs))
                return ApiProblem(StatusCodes.Status400BadRequest, "A telemetry event is invalid.", "invalid_telemetry_event");
            if (!PluginTelemetryRules.IsTimestampAllowed(telemetryEvent.OccurredAt, receivedAt))
                return ApiProblem(StatusCodes.Status400BadRequest, "Telemetry timestamp is outside the accepted window.", "telemetry_timestamp_out_of_range");
        }

        var user = await db.Users.AsNoTracking().Include(x => x.Organization).Include(x => x.ProductAccesses)
            .SingleAsync(x => x.Id == CurrentUserId, cancellationToken);
        if (user.Status != UserStatus.Active || user.Organization?.Status != OrganizationStatus.Active)
            return ApiProblem(StatusCodes.Status403Forbidden, "Account or organization is inactive.", "account_inactive");
        if (user.OrganizationId is not { } organizationId)
            return ApiProblem(StatusCodes.Status403Forbidden, "An organization is required.", "organization_required");
        if (!user.ProductAccesses.Any(x => x.Product == request.Product))
            return ApiProblem(StatusCodes.Status403Forbidden, "Product access was not granted.", "product_access_denied");

        var accepted = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var telemetryEvent in request.Events)
        {
            var id = Guid.CreateVersion7();
            var product = request.Product.ToString();
            var command = telemetryEvent.Command.Trim();
            var outcome = telemetryEvent.Outcome.ToString();
            var errorCode = string.IsNullOrWhiteSpace(telemetryEvent.ErrorCode) ? null : telemetryEvent.ErrorCode.Trim();
            var pluginVersion = request.PluginVersion.Trim();
            var hostVersion = request.HostVersion.Trim();
            var installationId = request.InstallationId.Trim();

            accepted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO plugin_usage_events
                    ("Id", "ClientEventId", "OrganizationId", "UserId", "Product", "Command", "OccurredAt", "ReceivedAt",
                     "DurationMs", "Outcome", "ErrorCode", "PluginVersion", "HostVersion", "InstallationId")
                VALUES
                    ({id}, {telemetryEvent.EventId}, {organizationId}, {user.Id}, {product}, {command},
                     {telemetryEvent.OccurredAt}, {receivedAt}, {telemetryEvent.DurationMs}, {outcome}, {errorCode},
                     {pluginVersion}, {hostVersion}, {installationId})
                ON CONFLICT ("OrganizationId", "ClientEventId") DO NOTHING
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        return Ok(new PluginTelemetryIngestionResponse(request.Events.Count, accepted, request.Events.Count - accepted));
    }
}
