using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/telemetry")]
[Authorize(Roles = nameof(UserRole.OrganizationAdmin))]
public sealed class OrganizationTelemetryController(AppDbContext db, IClock clock) : ApiControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<PluginTelemetrySummaryResponse>> Summary(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Product? product,
        [FromQuery] Guid? userId,
        [FromQuery] string? command,
        CancellationToken cancellationToken)
    {
        var rangeTo = to ?? clock.UtcNow;
        var rangeFrom = from ?? rangeTo.AddDays(-30);
        if (rangeFrom >= rangeTo || rangeTo - rangeFrom > TimeSpan.FromDays(366))
            return ApiProblem(StatusCodes.Status400BadRequest, "Telemetry date range is invalid.", "invalid_telemetry_range");
        if (product is not null && !Enum.IsDefined(product.Value))
            return ApiProblem(StatusCodes.Status400BadRequest, "Product is invalid.", "invalid_product");
        if (command?.Length > 100)
            return ApiProblem(StatusCodes.Status400BadRequest, "Command filter is too long.", "invalid_command_filter");

        var organizationId = CurrentOrganizationId ?? throw new InvalidOperationException("Organization claim is required.");
        var query = db.PluginUsageEvents.AsNoTracking().Where(x =>
            x.OrganizationId == organizationId && x.OccurredAt >= rangeFrom && x.OccurredAt < rangeTo);
        if (product is not null) query = query.Where(x => x.Product == product.Value);
        if (userId is not null) query = query.Where(x => x.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(command)) query = query.Where(x => x.Command == command.Trim());

        var totalEvents = await query.LongCountAsync(cancellationToken);
        var uniqueUsers = await query.Select(x => x.UserId).Distinct().CountAsync(cancellationToken);
        var succeeded = await query.LongCountAsync(x => x.Outcome == PluginUsageOutcome.Succeeded, cancellationToken);
        var failed = await query.LongCountAsync(x => x.Outcome == PluginUsageOutcome.Failed, cancellationToken);
        var cancelled = await query.LongCountAsync(x => x.Outcome == PluginUsageOutcome.Cancelled, cancellationToken);
        var averageDuration = await query.Where(x => x.DurationMs != null)
            .AverageAsync(x => (double?)x.DurationMs, cancellationToken);
        var commandAggregates = await query.GroupBy(x => x.Command)
            .Select(group => new
            {
                Command = group.Key,
                Uses = group.Count(),
                UniqueUsers = group.Select(x => x.UserId).Distinct().Count(),
                Succeeded = group.Count(x => x.Outcome == PluginUsageOutcome.Succeeded),
                Failed = group.Count(x => x.Outcome == PluginUsageOutcome.Failed),
                Cancelled = group.Count(x => x.Outcome == PluginUsageOutcome.Cancelled),
                AverageDurationMs = group.Average(x => (double?)x.DurationMs)
            })
            .OrderByDescending(x => x.Uses)
            .ThenBy(x => x.Command)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        var commands = commandAggregates.Select(x => new PluginCommandTelemetrySummary(
            x.Command, x.Uses, x.UniqueUsers, x.Succeeded, x.Failed, x.Cancelled, x.AverageDurationMs)).ToArray();

        return Ok(new PluginTelemetrySummaryResponse(rangeFrom, rangeTo, totalEvents, uniqueUsers,
            succeeded, failed, cancelled, averageDuration, commands));
    }
}
