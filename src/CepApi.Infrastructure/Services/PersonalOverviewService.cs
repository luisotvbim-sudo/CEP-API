using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Services;

public sealed class PersonalOverviewService(AppDbContext db, IClock clock, FreshTimeAnalysisService analysis)
{
    public async Task<PersonalOverviewResponse> ReadAsync(Guid organizationId, Guid userId,
        AnalysisPeriod period, CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow;
        var person = await db.ActiveNotificationRecipients(organizationId, userId).AsNoTracking()
            .Include(person => person.MondayIdentity).Include(person => person.VrMaisIdentity)
            .SingleOrDefaultAsync(cancellationToken);
        if (person is null) return Unavailable(PersonalOverviewStatus.NotAssociated);
        if (!person.MondayIdentity.IsActive || !person.VrMaisIdentity.IsActive)
            return Unavailable(PersonalOverviewStatus.InactiveIdentity);

        var settings = await db.Set<TimeControlSettings>().AsNoTracking().SingleAsync(cancellationToken);
        var window = TimeAnalysisEngine.ResolvePeriod(period, cutoff);
        var (snapshots, states) = await analysis.FetchAsync([person], window, cancellationToken);
        var result = analysis.Analyze(person, window, settings.ToleranceMinutes, settings.Version, snapshots, states);

        // Keep the existing all-source aggregate semantics. Separate daily source values only
        // use a complete targeted read for this person, never an organization's import timestamp.
        var available = new Dictionary<ExternalWorkforceSource, IReadOnlyList<TimeAnalysisDay>>();
        foreach (var (source, snapshot) in snapshots)
        {
            if (!snapshot.Complete || snapshot.From > window.From || snapshot.To < window.To) continue;
            var projected = analysis.ProjectAvailableSource(person, window, settings.ToleranceMinutes, settings.Version,
                source, snapshot, states);
            available[source] = projected.Days;
        }
        var sourceDays = result.Days.Select((day, index) => new PersonalSourceDay(day.Day,
            available.TryGetValue(ExternalWorkforceSource.VrMais, out var vr) ? vr[index].VrSeconds : null,
            available.TryGetValue(ExternalWorkforceSource.Monday, out var monday) ? monday[index].MondaySeconds : null)).ToArray();
        return PersonalOverviewProjection.FromAnalysis(period, result, sourceDays);

        PersonalOverviewResponse Unavailable(PersonalOverviewStatus status)
            => new(period, cutoff, PersonalOverviewProjection.Periods(cutoff), status, null, [], []);
    }
}
