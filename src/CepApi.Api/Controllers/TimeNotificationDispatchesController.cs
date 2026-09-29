using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/notification-dispatches")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
[OrganizationScope]
public sealed class TimeNotificationDispatchesController(AppDbContext db, IClock clock, IAuditService audit) : ApiControllerBase
{
    [HttpGet("preview")]
    public async Task<ActionResult<TimeDispatchPreviewResponse>> Preview([FromQuery] AnalysisPeriod period, [FromQuery] Guid? userId, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        if (!ValidPeriod(period)) return ApiProblem(400, "Invalid period.", "invalid_analysis_period");
        var scopedOrganizationId = ScopedOrganizationId;
        var window = TimeAnalysisEngine.ResolvePeriod(period, clock.UtcNow);
        return Ok(new TimeDispatchPreviewResponse(window.From, window.To, window.Cutoff, await db.ActiveNotificationRecipients(scopedOrganizationId, userId).CountAsync(cancellationToken)));
    }

    [HttpPost]
    [EnableRateLimiting("account")]
    public async Task<ActionResult<TimeDispatchResponse>> Send(SendTimeNotificationRequest request, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        if (!ValidPeriod(request.Period) || request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Message))
            return ApiProblem(400, "Invalid notification request.", "invalid_notification_request");
        var scopedOrganizationId = ScopedOrganizationId;
        var key = $"manual:{CurrentUserId}:{request.RequestId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize organization sends across instances and enforce the durable flood limit.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({scopedOrganizationId.ToString()}, 371))", cancellationToken);
        var existing = await db.Set<TimeNotificationDispatch>().SingleOrDefaultAsync(x => x.OrganizationId == scopedOrganizationId && x.DeduplicationKey == key, cancellationToken);
        if (existing is not null)
            return existing.RequestHash == hash ? Ok(ToResponse(existing)) : ApiProblem(409, "Request identifier already used.", "notification_request_conflict");
        if (!await db.Organizations.AnyAsync(x => x.Id == scopedOrganizationId && x.Status == OrganizationStatus.Active, cancellationToken))
            return ApiProblem(403, "Organization is inactive.", "organization_inactive");
        var recipientCount = await db.ActiveNotificationRecipients(scopedOrganizationId, request.UserId).CountAsync(cancellationToken);
        if (recipientCount == 0) return ApiProblem(400, "No active associated recipients in scope.", "notification_scope_empty");
        var now = clock.UtcNow;
        if (await db.Set<TimeNotificationDispatch>().AnyAsync(x => x.OrganizationId == scopedOrganizationId && x.ActorUserId != null && x.CreatedAt > now.AddMinutes(-1), cancellationToken))
            return ApiProblem(429, "Wait one minute before sending another manual notification.", "notification_cooldown");
        var settings = await db.Set<TimeControlSettings>().AsNoTracking().SingleAsync(cancellationToken);
        var dispatch = new TimeNotificationDispatch
        {
            OrganizationId = scopedOrganizationId,
            ActorUserId = CurrentUserId,
            UserId = request.UserId,
            DeduplicationKey = key,
            RequestHash = hash,
            Message = request.Message.Trim(),
            Period = request.Period,
            CreatedAt = now,
            ToleranceMinutes = settings.ToleranceMinutes,
            SettingsVersion = settings.Version
        };
        db.Add(dispatch);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("time_control.notification_requested", scopedOrganizationId, CurrentUserId, details: new { dispatch.Id, request.UserId, request.Period, recipientCount }, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AcceptedAtAction(nameof(List), new { organizationId = scopedOrganizationId }, ToResponse(dispatch));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<TimeDispatchResponse>>> List([FromQuery] Guid? organizationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = ScopedOrganizationId;
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.Set<TimeNotificationDispatch>().AsNoTracking().Where(x => x.OrganizationId == scopedOrganizationId && !x.ReportOnly);
        var count = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Page(page, pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<TimeDispatchResponse>(items.Select(ToResponse).ToArray(), page, pageSize, count));
    }

    private static bool ValidPeriod(AnalysisPeriod period) => period is AnalysisPeriod.Daily or AnalysisPeriod.Weekly or AnalysisPeriod.Sprint;
    private static TimeDispatchResponse ToResponse(TimeNotificationDispatch d) => new(d.Id, d.Status, d.CreatedAt, d.CompletedAt, d.RecipientCount, d.ErrorCode, d.Period, d.Message, d.UserId);
}
