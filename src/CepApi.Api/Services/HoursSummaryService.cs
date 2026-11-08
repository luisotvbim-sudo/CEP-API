using System.Data;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Services;

public sealed class HoursSummaryService(AppDbContext db)
{
    private sealed record PersonDays(Guid Id, Guid? UserId, string Name, IReadOnlyList<TimeAnalysisDay> Days)
    {
        public HoursPersonResponse Summarize() => new(Id, Name, HoursTotals.Sum(Days), Days);
    }

    private sealed record TeamSummary(HoursTeamResponse Team, IReadOnlyList<HoursPersonResponse> People);
    private sealed record SummaryData(int Tolerance, PersonDays[] People, TeamSummary[] Teams);

    public async Task<HoursSummaryResponse> ReadAsync(Guid organizationId, DateOnly from, DateOnly to,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var data = (await LoadAsync(organizationId, from, to, now, null, null, cancellationToken))!;
        var people = data.People.Select(person => person.Summarize()).ToArray();
        return new(from, to, now, data.Tolerance, HoursTotals.Sum(people.SelectMany(x => x.Days)),
            people, data.Teams.Select(x => x.Team).ToArray());
    }

    public async Task<HoursTeamSummaryResponse?> ReadTeamAsync(Guid organizationId, Guid teamId, Guid? managerId,
        DateOnly from, DateOnly to, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var data = await LoadAsync(organizationId, from, to, now, teamId, managerId, cancellationToken);
        if (data is null) return null;
        var team = data.Teams.Single();
        return new(from, to, now, data.Tolerance, team.Team, team.People);
    }

    private async Task<SummaryData?> LoadAsync(Guid organizationId, DateOnly from, DateOnly to,
        DateTimeOffset now, Guid? teamId, Guid? managerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var today = TimeAnalysisEngine.LocalDate(now);
        var activeTeams = db.WorkforceTeams.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.IsActive);
        if (teamId is { } selected)
            activeTeams = activeTeams.Where(x => x.Id == selected);
        if (managerId is { } manager)
            activeTeams = activeTeams.Where(team => team.Assignments.Any(x => x.UserId == manager &&
                x.Role == TeamAssignmentRole.Manager && x.EffectiveFrom <= today &&
                (x.EffectiveTo == null || x.EffectiveTo >= today)));
        var teams = await activeTeams.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken);
        if (teamId.HasValue && teams.Count == 0) return null;
        var tolerance = await db.Set<TimeControlSettings>().Select(x => x.ToleranceMinutes).SingleAsync(cancellationToken);
        var scope = db.WorkforcePeople.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (teamId is { } scopeTeamId)
            scope = scope.Where(p => p.UserId != null && db.TeamAssignments.Any(x =>
                x.TeamId == scopeTeamId && x.UserId == p.UserId && x.EffectiveFrom <= today &&
                (x.EffectiveTo == null || x.EffectiveTo >= today)));
        var people = await scope.OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.UserId, x.DisplayName, x.MondayIdentityId, x.VrMaisIdentityId })
            .ToListAsync(cancellationToken);
        var records = await db.WorkforceTimeRecords.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.WorkDate >= from && x.WorkDate <= to && !x.IsRemoved &&
                scope.Any(p => p.MondayIdentityId == x.ExternalIdentityId || p.VrMaisIdentityId == x.ExternalIdentityId))
            .ToListAsync(cancellationToken);
        var assignments = await db.TeamAssignments.AsNoTracking()
            .Where(x => activeTeams.Any(t => t.Id == x.TeamId) &&
                x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= from))
            .Select(x => new { x.TeamId, x.UserId, x.EffectiveFrom, x.EffectiveTo })
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var byIdentity = records.ToLookup(x => x.ExternalIdentityId);
        var summaries = people.Select(person =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var days = TimeAnalysisEngine.SummarizeImportedDays(from, to, now, tolerance,
                byIdentity[person.MondayIdentityId].Concat(byIdentity[person.VrMaisIdentityId]));
            return new PersonDays(person.Id, person.UserId, person.DisplayName, days);
        }).ToArray();
        var peopleByUser = summaries.ToLookup(x => x.UserId);
        var byTeam = assignments.ToLookup(x => x.TeamId);
        var teamSummaries = teams.Select(team =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = byTeam[team.Id].GroupBy(x => x.UserId)
                .Where(group => group.Any(x => x.EffectiveTo == null || x.EffectiveTo >= today))
                .SelectMany(group => peopleByUser[group.Key].Select(person => person with
                {
                    Days = person.Days.Where(day => group.Any(x =>
                        x.EffectiveFrom <= day.Day && (x.EffectiveTo == null || x.EffectiveTo >= day.Day))).ToArray()
                }))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id)
                .Select(person => person.Summarize()).ToArray();
            var response = new HoursTeamResponse(team.Id, team.Name, HoursTotals.Sum(members.SelectMany(x => x.Days)),
                members.Select(x => new HoursTeamMemberResponse(x.Id, x.Totals,
                    x.Days.Select(d => d.Day).ToArray())).ToArray());
            return new TeamSummary(response, teamId.HasValue ? members : []);
        }).ToArray();
        return new(tolerance, summaries, teamSummaries);
    }
}
