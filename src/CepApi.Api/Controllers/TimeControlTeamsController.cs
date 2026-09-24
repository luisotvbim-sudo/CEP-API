using CepApi.Application;
using CepApi.Api.Authorization;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/teams")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)},{nameof(UserRole.User)}")]
public sealed class TimeControlTeamsController(
    AppDbContext db,
    IClock clock,
    IAuditService audit,
    OrganizationScopeService organizationScope,
    TimeControlAccessService accessService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<WorkforceTeamResponse>>> List(
        [FromQuery] bool includeInactive = false,
        [FromQuery] DateOnly? asOf = null,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var access = await accessService.ResolveAsync(
            scopedOrganizationId, CurrentUserId, CurrentRole, cancellationToken);
        var effectiveDate = asOf ?? TimeControlCalendar.Today(clock.UtcNow);
        var query = db.WorkforceTeams.AsNoTracking().Where(x => x.OrganizationId == scopedOrganizationId);
        if (!access.HasFullAccess)
            query = query.Where(x => access.ManagedTeamIds.Contains(x.Id));
        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        var items = await query.OrderBy(x => x.Name).Select(team => new WorkforceTeamResponse(
            team.Id,
            team.Name,
            team.IsActive,
            team.Assignments.Count(x => x.Role == TeamAssignmentRole.Member &&
                x.EffectiveFrom <= effectiveDate && (x.EffectiveTo == null || x.EffectiveTo >= effectiveDate)),
            team.Assignments.Count(x => x.Role == TeamAssignmentRole.Manager &&
                x.EffectiveFrom <= effectiveDate && (x.EffectiveTo == null || x.EffectiveTo >= effectiveDate)),
            team.CreatedAt,
            team.UpdatedAt)).ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{teamId:guid}")]
    public async Task<ActionResult<WorkforceTeamResponse>> Get(
        Guid teamId,
        [FromQuery] DateOnly? asOf = null,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var access = await accessService.ResolveAsync(
            scopedOrganizationId, CurrentUserId, CurrentRole, cancellationToken);
        if (!access.CanReadTeam(teamId))
            return ApiProblem(StatusCodes.Status404NotFound, "Team not found.", "team_not_found");
        var effectiveDate = asOf ?? TimeControlCalendar.Today(clock.UtcNow);
        var team = await db.WorkforceTeams.AsNoTracking()
            .Where(x => x.Id == teamId && x.OrganizationId == scopedOrganizationId)
            .Select(x => new WorkforceTeamResponse(
                x.Id,
                x.Name,
                x.IsActive,
                x.Assignments.Count(assignment => assignment.Role == TeamAssignmentRole.Member &&
                    assignment.EffectiveFrom <= effectiveDate &&
                    (assignment.EffectiveTo == null || assignment.EffectiveTo >= effectiveDate)),
                x.Assignments.Count(assignment => assignment.Role == TeamAssignmentRole.Manager &&
                    assignment.EffectiveFrom <= effectiveDate &&
                    (assignment.EffectiveTo == null || assignment.EffectiveTo >= effectiveDate)),
                x.CreatedAt,
                x.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return team is null
            ? ApiProblem(StatusCodes.Status404NotFound, "Team not found.", "team_not_found")
            : Ok(team);
    }

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
    public async Task<ActionResult<WorkforceTeamResponse>> Create(
        CreateWorkforceTeamRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var name = request.Name.Trim();
        if (name.Length == 0)
            return ApiProblem(StatusCodes.Status400BadRequest, "Team name cannot be empty.", "invalid_team_name");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockOrganizationAsync(scopedOrganizationId, cancellationToken);
        var normalizedName = NormalizeName(name);
        if (await TeamNameExistsAsync(scopedOrganizationId, normalizedName, excludedTeamId: null, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "A team with this name already exists.", "team_name_unavailable");

        var now = clock.UtcNow;
        var team = new WorkforceTeam
        {
            OrganizationId = scopedOrganizationId,
            Name = name,
            NormalizedName = normalizedName,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.WorkforceTeams.Add(team);
        await audit.WriteAsync("time_control.team_created", scopedOrganizationId, CurrentUserId,
            details: new { team.Id, team.Name }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(Get),
            new { teamId = team.Id, organizationId = scopedOrganizationId }, ToResponse(team));
    }

    [HttpPatch("{teamId:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
    public async Task<ActionResult<WorkforceTeamResponse>> Update(
        Guid teamId,
        UpdateWorkforceTeamRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var name = request.Name.Trim();
        if (name.Length == 0)
            return ApiProblem(StatusCodes.Status400BadRequest, "Team name cannot be empty.", "invalid_team_name");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockOrganizationAsync(scopedOrganizationId, cancellationToken);
        var team = await db.WorkforceTeams.SingleOrDefaultAsync(
            x => x.Id == teamId && x.OrganizationId == scopedOrganizationId, cancellationToken);
        if (team is null)
            return ApiProblem(StatusCodes.Status404NotFound, "Team not found.", "team_not_found");

        var normalizedName = NormalizeName(name);
        if (await TeamNameExistsAsync(scopedOrganizationId, normalizedName, teamId, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "A team with this name already exists.", "team_name_unavailable");

        var previous = new { team.Name, team.IsActive };
        team.Name = name;
        team.NormalizedName = normalizedName;
        team.IsActive = request.IsActive;
        team.UpdatedAt = clock.UtcNow;
        await audit.WriteAsync("time_control.team_updated", scopedOrganizationId, CurrentUserId,
            details: new { team.Id, before = previous, after = new { team.Name, team.IsActive } },
            ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(team));
    }

    private Task LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM organizations WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);

    private async Task<bool> TeamNameExistsAsync(
        Guid organizationId,
        string normalizedName,
        Guid? excludedTeamId,
        CancellationToken cancellationToken)
    {
        var query = db.WorkforceTeams.Where(x =>
            x.OrganizationId == organizationId && x.NormalizedName == normalizedName);
        if (excludedTeamId is { } teamId)
            query = query.Where(x => x.Id != teamId);
        return await query.AnyAsync(cancellationToken);
    }

    private static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    private static WorkforceTeamResponse ToResponse(WorkforceTeam team)
        => new(team.Id, team.Name, team.IsActive, 0, 0, team.CreatedAt, team.UpdatedAt);
}
