using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CepApi.Application;
using CepApi.Api.Authorization;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/notification-dispatches")]
[Authorize(Roles = "SystemAdmin,OrganizationAdmin")]
[OrganizationScope]
public sealed class TimeNotificationDispatchesController(AppDbContext db, IClock clock, IAuditService audit,
    OrganizationScopeService scope) : ApiControllerBase
{
    [HttpGet("preview")]
    public async Task<ActionResult<TimeDispatchPreviewResponse>> Preview([FromQuery] AnalysisPeriod period, [FromQuery] Guid? userId, [FromQuery] Guid? organizationId, CancellationToken ct)
    {
        if (!ValidPeriod(period)) return ApiProblem(400, "Invalid period.", "invalid_analysis_period");
        var org = await scope.ResolveAsync(User, organizationId, ct);
        var window = TimeAnalysisEngine.ResolvePeriod(period, clock.UtcNow);
        return Ok(new TimeDispatchPreviewResponse(window.From, window.To, window.Cutoff, await Recipients(org, userId).CountAsync(ct)));
    }

    [HttpPost]
    [EnableRateLimiting("account")]
    public async Task<ActionResult<TimeDispatchResponse>> Send(SendTimeNotificationRequest request, [FromQuery] Guid? organizationId, CancellationToken ct)
    {
        if (!ValidPeriod(request.Period) || request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Message))
            return ApiProblem(400, "Invalid notification request.", "invalid_notification_request");
        var org = await scope.ResolveAsync(User, organizationId, ct);
        var key = $"manual:{CurrentUserId}:{request.RequestId}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize organization sends across instances and enforce the durable flood limit.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({org.ToString()}, 371))", ct);
        var existing = await db.Set<TimeNotificationDispatch>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.DeduplicationKey == key, ct);
        if (existing is not null)
            return existing.RequestHash == hash ? Ok(ToResponse(existing)) : ApiProblem(409, "Request identifier already used.", "notification_request_conflict");
        if (!await db.Organizations.AnyAsync(x => x.Id == org && x.Status == OrganizationStatus.Active, ct))
            return ApiProblem(403, "Organization is inactive.", "organization_inactive");
        var recipientCount = await Recipients(org, request.UserId).CountAsync(ct);
        if (recipientCount == 0) return ApiProblem(400, "No active associated recipients in scope.", "notification_scope_empty");
        var now = clock.UtcNow;
        if (await db.Set<TimeNotificationDispatch>().AnyAsync(x => x.OrganizationId == org && x.ActorUserId != null && x.CreatedAt > now.AddMinutes(-1), ct))
            return ApiProblem(429, "Wait one minute before sending another manual notification.", "notification_cooldown");
        var settings = await db.Set<TimeControlSettings>().AsNoTracking().SingleAsync(ct);
        var dispatch = new TimeNotificationDispatch { OrganizationId = org, ActorUserId = CurrentUserId, UserId = request.UserId,
            DeduplicationKey = key, RequestHash = hash, Message = request.Message.Trim(), Period = request.Period,
            CreatedAt = now, ToleranceMinutes = settings.ToleranceMinutes, SettingsVersion = settings.Version };
        db.Add(dispatch);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("time_control.notification_requested", org, CurrentUserId, details: new { dispatch.Id, request.UserId, request.Period, recipientCount }, cancellationToken: ct);
        await transaction.CommitAsync(ct);
        return AcceptedAtAction(nameof(List), new { organizationId }, ToResponse(dispatch));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<TimeDispatchResponse>>> List([FromQuery] Guid? organizationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
    {
        var org = await scope.ResolveAsync(User, organizationId, ct);
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = db.Set<TimeNotificationDispatch>().AsNoTracking().Where(x => x.OrganizationId == org && !x.ReportOnly);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new PagedResponse<TimeDispatchResponse>(items.Select(ToResponse).ToArray(), page, pageSize, count));
    }

    private IQueryable<WorkforcePerson> Recipients(Guid org, Guid? userId)
        => db.WorkforcePeople.Where(p => p.OrganizationId == org && p.UserId != null && (!userId.HasValue || p.UserId == userId) &&
            db.Users.Any(u => u.Id == p.UserId && u.OrganizationId == org && u.Status == UserStatus.Active));
    private static bool ValidPeriod(AnalysisPeriod period) => period is AnalysisPeriod.Daily or AnalysisPeriod.Weekly or AnalysisPeriod.Sprint;
    private static TimeDispatchResponse ToResponse(TimeNotificationDispatch d) => new(d.Id, d.Status, d.CreatedAt, d.CompletedAt, d.RecipientCount, d.ErrorCode, d.Period, d.Message, d.UserId);
}

[Route("api/v1/me/notifications")]
[Authorize]
public sealed class TimeNotificationsController(AppDbContext db, IClock clock) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<TimeNotificationResponse>>> List([FromQuery] bool unreadOnly = false, [FromQuery] bool pendingOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = Own().Where(x => (!unreadOnly || x.ReadAt == null) && (!pendingOnly || x.DeliveredAt == null && x.ReadAt == null));
        var total = await query.LongCountAsync(ct);
        var rows = await query.Include(x => x.Report).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new PagedResponse<TimeNotificationResponse>(rows.Select(x => new TimeNotificationResponse(x.Id, x.Message, x.CreatedAt, x.ReadAt, x.DeliveredAt, TimeAnalysisJson.Read(x.Report.AnalysisJson))).ToArray(), page, pageSize, total));
    }

    [HttpPost("received")]
    public async Task<IActionResult> Received(ReceiveTimeNotificationsRequest request, CancellationToken ct)
    {
        await Own().Where(x => request.Ids.Contains(x.Id) && x.DeliveredAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeliveredAt, clock.UtcNow), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        if (!await Own().AnyAsync(x => x.Id == id, ct)) return ApiProblem(404, "Notification not found.", "notification_not_found");
        await Own().Where(x => x.Id == id && x.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, clock.UtcNow), ct);
        return NoContent();
    }

    private IQueryable<TimeNotification> Own() => db.Set<TimeNotification>().Where(x => x.UserId == CurrentUserId &&
        db.Users.Any(u => u.Id == CurrentUserId && u.OrganizationId == x.OrganizationId && u.Status == UserStatus.Active) &&
        db.Organizations.Any(o => o.Id == x.OrganizationId && o.Status == OrganizationStatus.Active));
}

[Route("api/v1/organization/time-control/analyses")]
[Authorize]
[OrganizationScope]
public sealed class TimeAnalysesController(AppDbContext db, OrganizationScopeService scope, TimeControlAccessService access) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<TimeReportResponse>>> List([FromQuery] Guid? organizationId, [FromQuery] Guid? workforcePersonId,
        [FromQuery] AnalysisPeriod? period, [FromQuery] DateOnly? day, [FromQuery] string? issue,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
    {
        if (issue is not null && issue is not ("odd_punches" or "running_timer" or "above_tolerance" or "incomplete"))
            return ApiProblem(400, "Invalid issue filter.", "invalid_analysis_issue");
        var org = await scope.ResolveAsync(User, organizationId, ct);
        var allowed = await access.ResolveAsync(org, CurrentUserId, CurrentRole, ct);
        var query = db.Set<TimeAnalysisReport>().AsNoTracking().Where(x => x.OrganizationId == org);
        if (!allowed.HasFullAccess) query = query.Where(x => allowed.VisibleUserIds.Contains(x.UserId));
        if (workforcePersonId.HasValue) query = query.Where(x => x.WorkforcePersonId == workforcePersonId);
        if (period.HasValue) query = query.Where(x => x.Period == period);
        if (day.HasValue || issue is not null)
        {
            var match = new Dictionary<string, object>();
            if (day.HasValue) match["day"] = day.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (issue is not null) match["issues"] = new[] { issue };
            var json = JsonSerializer.Serialize(new { days = new[] { match } });
            query = query.Where(x => EF.Functions.JsonContains(x.AnalysisJson, json));
        }
        (page, pageSize) = NormalizePage(page, pageSize);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new PagedResponse<TimeReportResponse>(rows.Select(x => new TimeReportResponse(x.Id, x.WorkforcePersonId, x.UserId, x.DisplayName, x.CreatedAt, TimeAnalysisJson.Read(x.AnalysisJson))).ToArray(), page, pageSize, total));
    }
}

internal static class TimeAnalysisJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static TimeAnalysisResponse Read(string json) => JsonSerializer.Deserialize<TimeAnalysisResponse>(json, Options)!;
}
