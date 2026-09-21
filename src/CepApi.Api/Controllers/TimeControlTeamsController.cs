using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/teams")]
[Authorize(Roles = nameof(UserRole.OrganizationAdmin))]
public sealed class TimeControlTeamsController(
    AppDbContext db,
    IClock clock,
    IAuditService audit) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<WorkforceTeamResponse>>> List(
        [FromQuery] bool includeInactive = false,
        [FromQuery] DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationId();
        var effectiveDate = asOf ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var query = db.WorkforceTeams.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (!includeInactive) query = query.Where(x => x.IsActive);

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
        CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationId();
        var effectiveDate = asOf ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var team = await db.WorkforceTeams.AsNoTracking()
            .Where(x => x.Id == teamId && x.OrganizationId == organizationId)
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
    public async Task<ActionResult<WorkforceTeamResponse>> Create(
        CreateWorkforceTeamRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        var name = request.Name.Trim();
        if (name.Length == 0)
            return ApiProblem(StatusCodes.Status400BadRequest, "Team name cannot be empty.", "invalid_team_name");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM organizations WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);
        var normalizedName = NormalizeName(name);
        if (await db.WorkforceTeams.AnyAsync(x => x.OrganizationId == organizationId &&
            x.NormalizedName == normalizedName, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "A team with this name already exists.", "team_name_unavailable");

        var now = clock.UtcNow;
        var team = new WorkforceTeam
        {
            OrganizationId = organizationId,
            Name = name,
            NormalizedName = normalizedName,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.WorkforceTeams.Add(team);
        await audit.WriteAsync("time_control.team_created", organizationId, CurrentUserId,
            details: new { team.Id, team.Name }, ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { teamId = team.Id }, ToResponse(team));
    }

    [HttpPatch("{teamId:guid}")]
    public async Task<ActionResult<WorkforceTeamResponse>> Update(
        Guid teamId,
        UpdateWorkforceTeamRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        var name = request.Name.Trim();
        if (name.Length == 0)
            return ApiProblem(StatusCodes.Status400BadRequest, "Team name cannot be empty.", "invalid_team_name");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM organizations WHERE \"Id\" = {organizationId} FOR UPDATE", cancellationToken);
        var team = await db.WorkforceTeams.SingleOrDefaultAsync(
            x => x.Id == teamId && x.OrganizationId == organizationId, cancellationToken);
        if (team is null)
            return ApiProblem(StatusCodes.Status404NotFound, "Team not found.", "team_not_found");

        var normalizedName = NormalizeName(name);
        if (await db.WorkforceTeams.AnyAsync(x => x.OrganizationId == organizationId && x.Id != teamId &&
            x.NormalizedName == normalizedName, cancellationToken))
            return ApiProblem(StatusCodes.Status409Conflict, "A team with this name already exists.", "team_name_unavailable");

        var previous = new { team.Name, team.IsActive };
        team.Name = name;
        team.NormalizedName = normalizedName;
        team.IsActive = request.IsActive;
        team.UpdatedAt = clock.UtcNow;
        await audit.WriteAsync("time_control.team_updated", organizationId, CurrentUserId,
            details: new { team.Id, before = previous, after = new { team.Name, team.IsActive } },
            ipAddress: IpAddress, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(team));
    }

    [HttpGet("{teamId:guid}/assignments")]
    public async Task<ActionResult<IReadOnlyCollection<TeamAssignmentResponse>>> ListAssignments(
        Guid teamId,
        [FromQuery] bool includeHistory = false,
        [FromQuery] DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationId();
        if (!await db.WorkforceTeams.AnyAsync(x => x.Id == teamId && x.OrganizationId == organizationId, cancellationToken))
            return ApiProblem(StatusCodes.Status404NotFound, "Team not found.", "team_not_found");

        var effectiveDate = asOf ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var query = db.TeamAssignments.AsNoTracking().Where(x => x.TeamId == teamId);
        if (!includeHistory)
            query = query.Where(x => x.EffectiveFrom <= effectiveDate &&
                (x.EffectiveTo == null || x.EffectiveTo >= effectiveDate));

        var assignments = await (from assignment in query
                                 join user in db.Users.AsNoTracking() on assignment.UserId equals user.Id
                                 orderby assignment.Role, user.DisplayName, assignment.EffectiveFrom
                                 select new TeamAssignmentResponse(
                                     assignment.Id,
                                     assignment.TeamId,
                                     assignment.UserId,
                                     user.DisplayName,
                                     user.Email!,
                                     assignment.Role,
                                     assignment.EffectiveFrom,
                                     assignment.EffectiveTo,
                                     assignment.CreatedAt)).ToListAsync(cancellationToken);

        return Ok(assignments);
    }

    [HttpPost("{teamId:guid}/assignments")]
    public async Task<ActionResult<TeamAssignmentResponse>> Assign(
        Guid teamId,
        CreateTeamAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        if (request.UserId == Guid.Empty || request.EffectiveTo < request.EffectiveFrom)
            return ApiProblem(StatusCodes.Status400BadRequest, "The assignment period is invalid.", "invalid_assignment_period");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM workforce_teams WHERE \"Id\" = {teamId} AND \"OrganizationId\" = {organizationId} FOR UPDATE",
            cancellationToken);
        var team = await db.WorkforceTeams.SingleOrDefaultAsync(
            x => x.Id == teamId && x.OrganizationId == organizationId, cancellationToken);
        if (team is null)
            return ApiProblem(StatusCodes.Status404NotFound, "Team not found.", "team_not_found");
        if (!team.IsActive)
            return ApiProblem(StatusCodes.Status409Conflict, "Assignments cannot be added to an inactive team.", "team_inactive");

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.UserId && x.OrganizationId == organizationId, cancellationToken);
        if (user is null)
            return ApiProblem(StatusCodes.Status404NotFound, "User not found in this organization.", "user_not_found");
        if (user.Status != UserStatus.Active)
            return ApiProblem(StatusCodes.Status409Conflict, "Inactive users cannot receive a new team assignment.", "user_inactive");

        var exact = await db.TeamAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId &&
            x.UserId == request.UserId && x.Role == request.Role && x.EffectiveFrom == request.EffectiveFrom &&
            x.EffectiveTo == request.EffectiveTo, cancellationToken);
        if (exact is not null)
            return Ok(ToResponse(exact, user.DisplayName, user.Email!));

        var requestedEnd = request.EffectiveTo ?? DateOnly.MaxValue;
        var overlaps = await db.TeamAssignments.AnyAsync(x => x.TeamId == teamId && x.UserId == request.UserId &&
            x.Role == request.Role && x.EffectiveFrom <= requestedEnd &&
            (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom), cancellationToken);
        if (overlaps)
            return ApiProblem(StatusCodes.Status409Conflict, "This user already has an overlapping assignment.", "team_assignment_overlap");

        var assignment = new TeamAssignment
        {
            TeamId = teamId,
            UserId = request.UserId,
            Role = request.Role,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            CreatedAt = clock.UtcNow,
            CreatedByUserId = CurrentUserId
        };
        db.TeamAssignments.Add(assignment);
        await audit.WriteAsync("time_control.team_assignment_created", organizationId, CurrentUserId, request.UserId,
            new { assignment.Id, assignment.TeamId, role = assignment.Role.ToString(), assignment.EffectiveFrom, assignment.EffectiveTo },
            IpAddress, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(ListAssignments), new { teamId },
            ToResponse(assignment, user.DisplayName, user.Email!));
    }

    [HttpPatch("{teamId:guid}/assignments/{assignmentId:guid}/end")]
    public async Task<ActionResult<TeamAssignmentResponse>> EndAssignment(
        Guid teamId,
        Guid assignmentId,
        EndTeamAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganizationId();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT assignment."Id"
            FROM team_assignments AS assignment
            INNER JOIN workforce_teams AS team ON team."Id" = assignment."TeamId"
            WHERE assignment."Id" = {assignmentId} AND assignment."TeamId" = {teamId}
              AND team."OrganizationId" = {organizationId}
            FOR UPDATE OF assignment
            """, cancellationToken);
        var assignment = await db.TeamAssignments.Include(x => x.Team).SingleOrDefaultAsync(
            x => x.Id == assignmentId && x.TeamId == teamId && x.Team.OrganizationId == organizationId,
            cancellationToken);
        if (assignment is null)
            return ApiProblem(StatusCodes.Status404NotFound, "Team assignment not found.", "team_assignment_not_found");
        if (assignment.EffectiveTo is not null)
            return ApiProblem(StatusCodes.Status409Conflict, "This assignment already has an end date.", "team_assignment_already_ended");
        if (request.EffectiveTo < assignment.EffectiveFrom)
            return ApiProblem(StatusCodes.Status400BadRequest, "The assignment end date is invalid.", "invalid_assignment_period");

        assignment.EffectiveTo = request.EffectiveTo;
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == assignment.UserId, cancellationToken);
        await audit.WriteAsync("time_control.team_assignment_ended", organizationId, CurrentUserId, assignment.UserId,
            new { assignment.Id, assignment.TeamId, assignment.EffectiveTo }, IpAddress, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(assignment, user.DisplayName, user.Email!));
    }

    private Guid RequireOrganizationId()
        => CurrentOrganizationId ?? throw new InvalidOperationException("An organization claim is required.");

    private static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    private static WorkforceTeamResponse ToResponse(WorkforceTeam team)
        => new(team.Id, team.Name, team.IsActive, 0, 0, team.CreatedAt, team.UpdatedAt);

    private static TeamAssignmentResponse ToResponse(TeamAssignment assignment, string displayName, string email)
        => new(assignment.Id, assignment.TeamId, assignment.UserId, displayName, email, assignment.Role,
            assignment.EffectiveFrom, assignment.EffectiveTo, assignment.CreatedAt);
}
