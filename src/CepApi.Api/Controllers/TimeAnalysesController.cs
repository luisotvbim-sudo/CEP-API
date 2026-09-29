using System.Text.Json;
using CepApi.Application;
using CepApi.Api.Authorization;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/analyses")]
[Authorize]
[OrganizationScope]
public sealed class TimeAnalysesController(AppDbContext db, TimeControlAccessService access) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<TimeReportResponse>>> List([FromQuery] Guid? organizationId, [FromQuery] Guid? workforcePersonId,
        [FromQuery] AnalysisPeriod? period, [FromQuery] DateOnly? day, [FromQuery] string? issue,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken cancellationToken = default)
    {
        if (issue is not null && issue is not ("odd_punches" or "running_timer" or "above_tolerance" or "incomplete"))
            return ApiProblem(400, "Invalid issue filter.", "invalid_analysis_issue");
        var scopedOrganizationId = ScopedOrganizationId;
        var allowed = await access.ResolveAsync(scopedOrganizationId, CurrentUserId, CurrentRole, cancellationToken);
        var query = db.Set<TimeAnalysisReport>().AsNoTracking().Where(x => x.OrganizationId == scopedOrganizationId);
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
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Page(page, pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<TimeReportResponse>(rows.Select(x => new TimeReportResponse(x.Id, x.WorkforcePersonId, x.UserId, x.DisplayName, x.CreatedAt, TimeAnalysisJson.Read(x.AnalysisJson))).ToArray(), page, pageSize, total));
    }
}
