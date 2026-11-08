using System.Data;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Services;

public sealed class HoursSummaryService(AppDbContext db)
{
    public async Task<HoursSummaryResponse> ReadAsync(Guid organizationId, DateOnly from, DateOnly to,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var today = TimeAnalysisEngine.LocalDate(now);
        var tolerance = await db.Set<TimeControlSettings>().Select(x => x.ToleranceMinutes).SingleAsync(cancellationToken);
        var scope = db.WorkforcePeople.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        var people = await scope.OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.UserId, x.DisplayName, x.MondayIdentityId, x.VrMaisIdentityId })
            .ToListAsync(cancellationToken);
        var records = await db.WorkforceTimeRecords.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.WorkDate >= from && x.WorkDate <= to && !x.IsRemoved &&
                scope.Any(p => p.MondayIdentityId == x.ExternalIdentityId || p.VrMaisIdentityId == x.ExternalIdentityId))
            .ToListAsync(cancellationToken);
        var activeTeams = db.WorkforceTeams.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.IsActive);
        var teams = await activeTeams.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken);
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
            return new HoursPersonResponse(person.Id, person.DisplayName, HoursTotals.Sum(days), days);
        }).ToArray();
        var byPerson = summaries.ToDictionary(x => x.Id);
        var peopleByUser = people.Where(x => x.UserId.HasValue).ToLookup(x => x.UserId!.Value);
        var byTeam = assignments.ToLookup(x => x.TeamId);
        var teamSummaries = teams.Select(team =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = byTeam[team.Id].GroupBy(x => x.UserId)
                .Where(group => group.Any(x => x.EffectiveTo == null || x.EffectiveTo >= today))
                .SelectMany(group => peopleByUser[group.Key].Select(person => new
                {
                    Person = byPerson[person.Id],
                    Days = byPerson[person.Id].Days.Where(day => group.Any(x =>
                        x.EffectiveFrom <= day.Day && (x.EffectiveTo == null || x.EffectiveTo >= day.Day))).ToArray()
                })).OrderBy(x => x.Person.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Person.Id).ToArray();
            return new HoursTeamResponse(team.Id, team.Name, HoursTotals.Sum(members.SelectMany(x => x.Days)),
                members.Select(x => new HoursTeamMemberResponse(x.Person.Id, HoursTotals.Sum(x.Days),
                    x.Days.Select(d => d.Day).ToArray())).ToArray());
        }).ToArray();
        return new(from, to, now, tolerance, HoursTotals.Sum(summaries.SelectMany(x => x.Days)), summaries, teamSummaries);
    }
}
