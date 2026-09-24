using CepApi.Application;
using CepApi.Api.Authorization;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/organization/time-control/teams/{teamId:guid}/assignments")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)},{nameof(UserRole.User)}")]
[OrganizationScope]
public sealed class TimeControlTeamAssignmentsController(
    AppDbContext db,
    IClock clock,
    IAuditService audit,
    OrganizationScopeService organizationScope,
    TimeControlAccessService accessService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TeamAssignmentResponse>>> List(
        Guid teamId,
        [FromQuery] bool includeHistory = false,
        [FromQuery] DateOnly? asOf = null,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        var access = await accessService.ResolveAsync(
            scopedOrganizationId, CurrentUserId, CurrentRole, cancellationToken);
        if (!access.CanReadTeam(teamId) ||
            !await TeamExistsAsync(teamId, scopedOrganizationId, cancellationToken))
            return ApiProblem(StatusCodes.Status404NotFound, "Team not found.", "team_not_found");

        var effectiveDate = asOf ?? TimeControlCalendar.Today(clock.UtcNow);
        var query = db.TeamAssignments.AsNoTracking().Where(x => x.TeamId == teamId);
        if (!includeHistory)
        {
            query = query.Where(x => x.EffectiveFrom <= effectiveDate &&
                (x.EffectiveTo == null || x.EffectiveTo >= effectiveDate));
        }

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

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
    public async Task<ActionResult<TeamAssignmentResponse>> Assign(
        Guid teamId,
        CreateTeamAssignmentRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        if (request.UserId == Guid.Empty || request.EffectiveTo < request.EffectiveFrom)
            return ApiProblem(StatusCodes.Status400BadRequest, "The assignment period is invalid.", "invalid_assignment_period");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockTeamAsync(teamId, scopedOrganizationId, cancellationToken);
        var team = await db.WorkforceTeams.SingleOrDefaultAsync(
            x => x.Id == teamId && x.OrganizationId == scopedOrganizationId, cancellationToken);
        if (team is null)
            return ApiProblem(StatusCodes.Status404NotFound, "Team not found.", "team_not_found");
        if (!team.IsActive)
            return ApiProblem(StatusCodes.Status409Conflict, "Assignments cannot be added to an inactive team.", "team_inactive");

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.UserId && x.OrganizationId == scopedOrganizationId, cancellationToken);
        if (user is null)
            return ApiProblem(StatusCodes.Status404NotFound, "User not found in this organization.", "user_not_found");
        if (user.Status != UserStatus.Active)
            return ApiProblem(StatusCodes.Status409Conflict, "Inactive users cannot receive a new team assignment.", "user_inactive");

        var exact = await db.TeamAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.TeamId == teamId &&
            x.UserId == request.UserId && x.Role == request.Role && x.EffectiveFrom == request.EffectiveFrom &&
            x.EffectiveTo == request.EffectiveTo, cancellationToken);
        if (exact is not null)
            return Ok(ToResponse(exact, user.DisplayName, user.Email!));

        if (await HasOverlappingAssignmentAsync(teamId, request, cancellationToken))
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
        await audit.WriteAsync("time_control.team_assignment_created", scopedOrganizationId, CurrentUserId, request.UserId,
            new { assignment.Id, assignment.TeamId, role = assignment.Role.ToString(), assignment.EffectiveFrom, assignment.EffectiveTo },
            IpAddress, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(List),
            new { teamId, organizationId = scopedOrganizationId },
            ToResponse(assignment, user.DisplayName, user.Email!));
    }

    [HttpPatch("{assignmentId:guid}/end")]
    [Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)}")]
    public async Task<ActionResult<TeamAssignmentResponse>> End(
        Guid teamId,
        Guid assignmentId,
        EndTeamAssignmentRequest request,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var scopedOrganizationId = await organizationScope.ResolveAsync(User, organizationId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAssignmentAsync(teamId, assignmentId, scopedOrganizationId, cancellationToken);
        var assignment = await db.TeamAssignments.Include(x => x.Team).SingleOrDefaultAsync(
            x => x.Id == assignmentId && x.TeamId == teamId && x.Team.OrganizationId == scopedOrganizationId,
            cancellationToken);
        if (assignment is null)
            return ApiProblem(StatusCodes.Status404NotFound, "Team assignment not found.", "team_assignment_not_found");
        if (assignment.EffectiveTo is not null)
            return ApiProblem(StatusCodes.Status409Conflict, "This assignment already has an end date.", "team_assignment_already_ended");
        if (request.EffectiveTo < assignment.EffectiveFrom)
            return ApiProblem(StatusCodes.Status400BadRequest, "The assignment end date is invalid.", "invalid_assignment_period");

        assignment.EffectiveTo = request.EffectiveTo;
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == assignment.UserId, cancellationToken);
        await audit.WriteAsync("time_control.team_assignment_ended", scopedOrganizationId, CurrentUserId, assignment.UserId,
            new { assignment.Id, assignment.TeamId, assignment.EffectiveTo }, IpAddress, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(assignment, user.DisplayName, user.Email!));
    }

    private Task<bool> TeamExistsAsync(Guid teamId, Guid organizationId, CancellationToken cancellationToken)
        => db.WorkforceTeams.AnyAsync(
            x => x.Id == teamId && x.OrganizationId == organizationId, cancellationToken);

    private Task LockTeamAsync(Guid teamId, Guid organizationId, CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM workforce_teams WHERE \"Id\" = {teamId} AND \"OrganizationId\" = {organizationId} FOR UPDATE",
            cancellationToken);

    private Task LockAssignmentAsync(
        Guid teamId,
        Guid assignmentId,
        Guid organizationId,
        CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT assignment."Id"
            FROM team_assignments AS assignment
            INNER JOIN workforce_teams AS team ON team."Id" = assignment."TeamId"
            WHERE assignment."Id" = {assignmentId} AND assignment."TeamId" = {teamId}
              AND team."OrganizationId" = {organizationId}
            FOR UPDATE OF assignment
            """, cancellationToken);

    private Task<bool> HasOverlappingAssignmentAsync(
        Guid teamId,
        CreateTeamAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var requestedEnd = request.EffectiveTo ?? DateOnly.MaxValue;
        return db.TeamAssignments.AnyAsync(x => x.TeamId == teamId && x.UserId == request.UserId &&
            x.Role == request.Role && x.EffectiveFrom <= requestedEnd &&
            (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom), cancellationToken);
    }

    private static TeamAssignmentResponse ToResponse(
        TeamAssignment assignment,
        string displayName,
        string email)
        => new(assignment.Id, assignment.TeamId, assignment.UserId, displayName, email, assignment.Role,
            assignment.EffectiveFrom, assignment.EffectiveTo, assignment.CreatedAt);
}
