using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using static CepApi.Infrastructure.Services.ExternalSourceJson;

namespace CepApi.Infrastructure.Services;

// One reader per fetch: attribution, time window and deduplication use the same context
// across all pages and parent/subitem boards. JSON elements never outlive their page.
internal sealed class MondayTimeSnapshotReader(
    DateOnly from, DateOnly to, HashSet<string> allowedUsers, bool includeOverlap, DateTimeOffset now)
{
    private readonly Dictionary<string, ExternalWorkforceTimeRecordSnapshot> records = new(StringComparer.Ordinal);
    private bool hasRelevantAmbiguity;

    public ExternalWorkforceTimeSnapshot Snapshot()
        => new(from, to, records.Values.ToArray(), !includeOverlap || !hasRelevantAmbiguity);

    public ResponsibleAssignment Read(JsonElement item, string? responsibleColumnId, ResponsibleAssignment? inherited = null)
    {
        if (!item.TryGetProperty("column_values", out var columns) || columns.ValueKind != JsonValueKind.Array)
            return inherited ?? new(null, false);
        var assignment = ResolveAssignment(columns, responsibleColumnId, inherited);
        foreach (var column in columns.EnumerateArray())
            ReadSessions(item, column, assignment);
        return assignment;
    }

    private ResponsibleAssignment ResolveAssignment(JsonElement columns, string? columnId, ResponsibleAssignment? inherited)
    {
        var column = columnId is null ? default : columns.EnumerateArray()
            .FirstOrDefault(value => ReadString(value, "id") == columnId);
        if (column.ValueKind == JsonValueKind.Undefined || !column.TryGetProperty("persons_and_teams", out var people))
            return inherited ?? new(null, false);
        if (people.ValueKind != JsonValueKind.Array)
            throw new ExternalDirectoryException("monday_invalid_response", "Monday returned invalid responsible people.");
        var ids = people.EnumerateArray().Where(person => ReadString(person, "kind") == "person")
            .Select(person => ReadString(person, "id")).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length switch
        {
            > 1 => new(null, true, ids.Any(allowedUsers.Contains)),
            1 => new(ids[0], false),
            _ => inherited ?? new(null, false)
        };
    }

    private void ReadSessions(JsonElement item, JsonElement column, ResponsibleAssignment assignment)
    {
        if (!column.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array) return;
        var entries = history.EnumerateArray().Where(entry => !IsDeleted(entry)).ToArray();
        var openCandidates = entries.Count(entry => ReadDate(entry, "started_at") is not null && ReadDate(entry, "ended_at") is null);
        var columnRunning = column.TryGetProperty("running", out var running) && running.ValueKind == JsonValueKind.True;
        var columnStartedAt = ReadDate(column, "started_at");
        foreach (var entry in entries)
        {
            var startedAt = ReadDate(entry, "started_at");
            var endedAt = ReadDate(entry, "ended_at");
            if (startedAt is null || endedAt < startedAt) continue;
            var isRunning = endedAt is null && columnRunning && (columnStartedAt == startedAt || openCandidates == 1);
            if (endedAt is null && !isRunning) continue;
            var workDate = TimeAnalysisEngine.LocalDate(startedAt.Value);
            if (workDate > to || workDate < from && (!includeOverlap || endedAt <= TimeAnalysisEngine.StartOfDay(from))) continue;
            if (assignment.Ambiguous)
            {
                hasRelevantAmbiguity |= assignment.RelevantAmbiguity;
                continue;
            }
            if (assignment.IdentityId is not { } responsibleId || !allowedUsers.Contains(responsibleId)) continue;
            var historyId = ReadString(entry, "id");
            var columnId = ReadString(column, "id");
            if (historyId is null || columnId is null) continue;
            var itemId = ReadString(item, "id") ?? string.Empty;
            var externalKey = $"{itemId}:{columnId}:{historyId}";
            var duration = Math.Clamp(((endedAt ?? now) - startedAt.Value).TotalSeconds, 0, int.MaxValue);
            var manual = new[] { "manually_entered_start_date", "manually_entered_start_time",
                "manually_entered_end_date", "manually_entered_end_time" }
                .Any(name => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True);
            var details = JsonSerializer.Serialize(new { itemId, manual, running = isRunning, startedByUserId = ReadString(entry, "started_user_id") });
            records[externalKey] = new(responsibleId, externalKey, workDate, startedAt, endedAt, (int)duration,
                isRunning ? "running" : "closed", ReadString(item, "name"), ReadString(item, "url"), details);
        }
    }

    private static bool IsDeleted(JsonElement entry)
        => ReadString(entry, "status")?.Contains("deleted", StringComparison.OrdinalIgnoreCase) == true;

    private static DateTimeOffset? ReadDate(JsonElement element, string name)
        => DateTimeOffset.TryParse(ReadString(element, name), out var value) ? value : null;

    internal sealed record ResponsibleAssignment(string? IdentityId, bool Ambiguous, bool RelevantAmbiguity = false);
}
