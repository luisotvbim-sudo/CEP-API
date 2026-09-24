using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Authorization;

public sealed record TimeControlAccessScope(
    bool HasFullAccess,
    IReadOnlyCollection<Guid> ManagedTeamIds,
    IReadOnlyCollection<Guid> VisibleUserIds)
{
    public bool CanReadTeam(Guid teamId) => HasFullAccess || ManagedTeamIds.Contains(teamId);
}

public sealed class TimeControlAccessService(AppDbContext db, IClock clock)
{
    public async Task<TimeControlAccessScope> ResolveAsync(
        Guid organizationId,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken)
    {
        if (role is UserRole.OrganizationAdmin or UserRole.SystemAdmin)
            return new TimeControlAccessScope(true, [], []);

        var today = TimeControlCalendar.Today(clock.UtcNow);
        var assignments = await db.TeamAssignments.AsNoTracking()
            .Where(x => x.UserId == userId && x.Team.OrganizationId == organizationId &&
                x.Team.IsActive && x.EffectiveFrom <= today &&
                (x.EffectiveTo == null || x.EffectiveTo >= today))
            .Select(x => new { x.TeamId, x.Role })
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
            return new TimeControlAccessScope(false, [], []);

        var managedTeamIds = assignments
            .Where(x => x.Role == TeamAssignmentRole.Manager)
            .Select(x => x.TeamId)
            .Distinct()
            .ToArray();

        var visibleUserIds = managedTeamIds.Length == 0
            ? [userId]
            : await db.TeamAssignments.AsNoTracking()
                .Where(x => managedTeamIds.Contains(x.TeamId) && x.EffectiveFrom <= today &&
                    (x.EffectiveTo == null || x.EffectiveTo >= today))
                .Select(x => x.UserId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

        if (!visibleUserIds.Contains(userId))
            visibleUserIds = [.. visibleUserIds, userId];

        return new TimeControlAccessScope(false, managedTeamIds, visibleUserIds);
    }
}
