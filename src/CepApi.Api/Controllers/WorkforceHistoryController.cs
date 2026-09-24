using CepApi.Application;
using CepApi.Api.Authorization;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/history")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)},{nameof(UserRole.User)}")]
public sealed class WorkforceHistoryController(
    AppDbContext db,
    IClock clock,
    IAuditService audit,
    OrganizationScopeService organizationScope,
    TimeControlAccessService accessService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WorkforceAdminHistoryResponse>> Get(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? workforcePersonId,
        [FromQuery] ExternalWorkforceSource? source,
        [FromQuery] string? search,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (from == default || to == default || to < from)
            return ApiProblem(StatusCodes.Status400BadRequest, "The history period is invalid.", "invalid_history_period");
        if (to.DayNumber - from.DayNumber + 1 > WorkforceHistoryPolicy.RetentionDays)
            return ApiProblem(StatusCodes.Status400BadRequest,
                $"The history period cannot exceed {WorkforceHistoryPolicy.RetentionDays} days.", "history_period_too_large");

        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var access = await accessService.ResolveAsync(
            scopedOrganizationId, CurrentUserId, CurrentRole, cancellationToken);
        var query = db.WorkforcePeople.AsNoTracking().Where(x => x.OrganizationId == scopedOrganizationId);
        if (!access.HasFullAccess)
            query = query.Where(x => x.UserId != null && access.VisibleUserIds.Contains(x.UserId.Value));
        if (workforcePersonId is not null) query = query.Where(x => x.Id == workforcePersonId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(x => x.DisplayName.ToLower().Contains(value) || x.Email.ToLower().Contains(value));
        }
        var people = await query.OrderBy(x => x.DisplayName).Take(200).ToListAsync(cancellationToken);
        var identityIds = people.SelectMany(x => new[] { x.MondayIdentityId, x.VrMaisIdentityId }).ToArray();
        var recordsQuery = db.WorkforceTimeRecords.AsNoTracking().Where(x => x.OrganizationId == scopedOrganizationId &&
            identityIds.Contains(x.ExternalIdentityId) && x.WorkDate >= from && x.WorkDate <= to && !x.IsRemoved);
        if (source is not null) recordsQuery = recordsQuery.Where(x => x.Source == source);
        var records = await recordsQuery.OrderBy(x => x.WorkDate).ThenBy(x => x.Source).ThenBy(x => x.StartedAt)
            .ToListAsync(cancellationToken);
        var recordsByIdentity = records.GroupBy(x => x.ExternalIdentityId).ToDictionary(x => x.Key, x => x.ToArray());

        var response = new WorkforceAdminHistoryResponse(from, to, clock.UtcNow, people.Select(person =>
        {
            var personRecords = recordsByIdentity.GetValueOrDefault(person.MondayIdentityId, [])
                .Concat(recordsByIdentity.GetValueOrDefault(person.VrMaisIdentityId, []))
                .OrderBy(x => x.WorkDate).ThenBy(x => x.Source).ThenBy(x => x.StartedAt)
                .Select(ToResponse).ToArray();
            return new WorkforcePersonHistoryResponse(person.Id, person.UserId, person.DisplayName, person.Email, personRecords);
        }).ToArray());

        await audit.WriteAsync("time_control.history_viewed", scopedOrganizationId, CurrentUserId,
            details: new { from, to, workforcePersonId, source = source?.ToString(), searchApplied = !string.IsNullOrWhiteSpace(search), people = people.Count },
            ipAddress: IpAddress, cancellationToken: cancellationToken);
        return Ok(response);
    }

    private static WorkforceTimeRecordResponse ToResponse(WorkforceTimeRecord record)
        => new(record.Id, record.Source, record.ExternalKey, record.WorkDate, record.StartedAt, record.EndedAt,
            record.DurationSeconds, record.State, record.Title, record.Url, record.DetailsJson, record.LastSyncedAt);
}
