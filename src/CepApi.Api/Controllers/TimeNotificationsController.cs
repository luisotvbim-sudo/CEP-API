using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/me/notifications")]
[Authorize]
public sealed class TimeNotificationsController(AppDbContext db, IClock clock) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<TimeNotificationResponse>>> List([FromQuery] bool unreadOnly = false, [FromQuery] bool pendingOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = Own().Where(x => (!unreadOnly || x.ReadAt == null) && (!pendingOnly || x.DeliveredAt == null && x.ReadAt == null));
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.Include(x => x.Report).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Page(page, pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<TimeNotificationResponse>(rows.Select(x => new TimeNotificationResponse(x.Id, x.Message, x.CreatedAt, x.ReadAt, x.DeliveredAt, TimeAnalysisJson.Read(x.Report.AnalysisJson))).ToArray(), page, pageSize, total));
    }

    [HttpPost("received")]
    public async Task<IActionResult> Received(ReceiveTimeNotificationsRequest request, CancellationToken cancellationToken)
    {
        await Own().Where(x => request.Ids.Contains(x.Id) && x.DeliveredAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeliveredAt, clock.UtcNow), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken cancellationToken)
    {
        if (!await Own().AnyAsync(x => x.Id == id, cancellationToken)) return ApiProblem(404, "Notification not found.", "notification_not_found");
        await Own().Where(x => x.Id == id && x.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, clock.UtcNow), cancellationToken);
        return NoContent();
    }

    private IQueryable<TimeNotification> Own() => db.Set<TimeNotification>().Where(x => x.UserId == CurrentUserId &&
        db.Users.Any(u => u.Id == CurrentUserId && u.OrganizationId == x.OrganizationId && u.Status == UserStatus.Active) &&
        db.Organizations.Any(o => o.Id == x.OrganizationId && o.Status == OrganizationStatus.Active));
}
