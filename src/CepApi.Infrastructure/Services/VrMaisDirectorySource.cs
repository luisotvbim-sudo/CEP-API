using System.Text.Json;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Globalization;
using CepApi.Application;
using CepApi.Domain;
using Microsoft.Extensions.Options;
using static CepApi.Infrastructure.Services.ExternalSourceJson;

namespace CepApi.Infrastructure.Services;

public sealed class VrMaisDirectorySource(
    HttpClient httpClient,
    IOptions<WorkforceIntegrationOptions> options,
    IClock clock) : IExternalWorkforceDirectorySource, IExternalWorkforceTimeSource
{
    public ExternalWorkforceSource Source => ExternalWorkforceSource.VrMais;

    public async Task<ExternalWorkforceDirectorySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var (settings, baseUri) = ValidateSettings();

        var endpoint = new Uri(baseUri, "employees?attributes=id,first_name,last_name,name,email,active,status&incluirAnexos=false");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation("access-token", settings.Token!.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await ExternalSourceHttp.SendAsync(httpClient, request, "vr_mais", "VR Mais", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out _) || root.TryGetProperty("error", out _))
            throw new ExternalDirectoryException("vr_mais_query_failed", "VR Mais did not complete the directory query.");
        if (!root.TryGetProperty("employees", out var employees) || employees.ValueKind != JsonValueKind.Array)
            throw new ExternalDirectoryException("vr_mais_invalid_response", "VR Mais returned an invalid employee list.");
        if (employees.GetArrayLength() > 10_000 || IsPaginated(root, employees.GetArrayLength()))
            throw new ExternalDirectoryException("vr_mais_directory_incomplete", "VR Mais returned an incomplete employee list.");

        var identities = new Dictionary<string, ExternalWorkforceIdentitySnapshot>(StringComparer.Ordinal);
        foreach (var employee in employees.EnumerateArray())
        {
            var id = ReadString(employee, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var firstName = ReadString(employee, "first_name");
            var lastName = ReadString(employee, "last_name");
            var displayName = string.Join(' ', new[] { firstName, lastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (string.IsNullOrWhiteSpace(displayName)) displayName = ReadString(employee, "name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(displayName)) continue;
            identities[id] = new ExternalWorkforceIdentitySnapshot(
                id,
                displayName,
                ReadString(employee, "email"),
                ReadActive(employee));
        }

        return new ExternalWorkforceDirectorySnapshot(identities.Values.OrderBy(x => x.DisplayName).ToArray(), true);
    }

    async Task<ExternalWorkforceTimeSnapshot> IExternalWorkforceTimeSource.FetchAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string> activeExternalIdentityIds,
        CancellationToken cancellationToken)
    {
        if (to < from)
            throw new ExternalDirectoryException("vr_mais_invalid_period", "VR Mais synchronization period is invalid.");
        var (settings, baseUri) = ValidateSettings();

        var employees = activeExternalIdentityIds.Where(id => long.TryParse(id, out _)).Distinct(StringComparer.Ordinal).ToArray();
        var requests = employees.SelectMany(employeeId => SplitPeriod(from, to)
            .Select(period => (employeeId, period.From, period.To)));
        var today = TimeAnalysisEngine.LocalDate(clock.UtcNow);
        var records = new ConcurrentDictionary<string, ExternalWorkforceTimeRecordSnapshot>(StringComparer.Ordinal);
        var complete = 1;
        await Parallel.ForEachAsync(requests, new ParallelOptions
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        }, async (item, token) =>
        {
            var chunk = await FetchReportAsync(baseUri, settings.Token!, item.employeeId, item.From, item.To, today, token);
            if (!chunk.Complete) Interlocked.Exchange(ref complete, 0);
            foreach (var record in chunk.Records) records[record.ExternalKey] = record;
        });

        return new ExternalWorkforceTimeSnapshot(from, to, records.Values.OrderBy(x => x.WorkDate).ToArray(), complete == 1);
    }

    private async Task<(IReadOnlyCollection<ExternalWorkforceTimeRecordSnapshot> Records, bool Complete)> FetchReportAsync(
        Uri baseUri,
        string token,
        string employeeId,
        DateOnly from,
        DateOnly to,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "reports/work_days"));
        request.Headers.TryAddWithoutValidation("access-token", token.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Content = JsonContent.Create(new
        {
            report = new
            {
                start_date = from.ToString("yyyy-MM-dd"),
                end_date = to.ToString("yyyy-MM-dd"),
                employee_id = long.Parse(employeeId),
                group_by = "employee",
                columns = "date,total_time,time_cards",
                format = "json"
            }
        });
        using var response = await ExternalSourceHttp.SendAsync(httpClient, request, "vr_mais", "VR Mais", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out _) || root.TryGetProperty("error", out _))
            throw new ExternalDirectoryException("vr_mais_report_failed", "VR Mais did not complete the work-day report.");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || IsPaginated(root, data.GetArrayLength()))
            throw new ExternalDirectoryException("vr_mais_report_incomplete", "VR Mais returned an incomplete work-day report.");

        var rows = new Dictionary<DateOnly, VrDay>();
        var groupCount = 0;
        VisitGroups(data, rows, ref groupCount);
        if (groupCount > 1)
            throw new ExternalDirectoryException("vr_mais_ambiguous_report", "VR Mais returned more than one group for an employee.");
        // The work-day report can omit an in-progress day even after punches exist.
        // Fill only that gap; a reported work-day row retains its existing semantics.
        var complete = true;
        if (from <= today && today <= to && !rows.ContainsKey(today))
        {
            try
            {
                var punches = await FetchCurrentTimeCardsAsync(baseUri, token, employeeId, today, cancellationToken);
                if (punches.Length > 0) rows[today] = new VrDay(null, punches);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is ExternalDirectoryException or HttpRequestException or TaskCanceledException or JsonException)
            {
                complete = false;
            }
        }

        var result = new List<ExternalWorkforceTimeRecordSnapshot>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var externalKey = $"{employeeId}:{day:yyyy-MM-dd}";
            if (!rows.TryGetValue(day, out var row))
            {
                // An unsuccessful fallback is not a missing day and must not replace a
                // previously imported current-day record during partial synchronization.
                if (day == today && !complete) continue;
                result.Add(new ExternalWorkforceTimeRecordSnapshot(employeeId, externalKey, day, null, null, null,
                    "missing", null, null, JsonSerializer.Serialize(new { timeCards = Array.Empty<string>() })));
                continue;
            }
            result.Add(new ExternalWorkforceTimeRecordSnapshot(employeeId, externalKey, day, null, null,
                row.DurationSeconds, row.DurationSeconds is null ? "unrecognized" : "reported", null, null,
                JsonSerializer.Serialize(new { timeCards = row.TimeCards })));
        }
        return (result, complete);
    }

    private async Task<string[]> FetchCurrentTimeCardsAsync(
        Uri baseUri, string token, string employeeId, DateOnly day, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "reports/time_cards"));
        request.Headers.TryAddWithoutValidation("access-token", token.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Content = JsonContent.Create(new
        {
            report = new
            {
                start_date = day.ToString("yyyy-MM-dd"),
                end_date = day.ToString("yyyy-MM-dd"),
                employee_id = long.Parse(employeeId),
                group_by = "employee",
                columns = "employee_name,date,time,time_card_index",
                format = "json"
            }
        });
        using var response = await ExternalSourceHttp.SendAsync(httpClient, request, "vr_mais", "VR Mais", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out _) || root.TryGetProperty("error", out _))
            throw new ExternalDirectoryException("vr_mais_time_cards_failed", "VR Mais did not complete the time-card report.");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || IsPaginated(root, data.GetArrayLength()))
            throw new ExternalDirectoryException("vr_mais_time_cards_incomplete", "VR Mais returned an incomplete time-card report.");

        var punches = new List<(TimeOnly Time, string Direction)>();
        var groupCount = 0;
        VisitTimeCardGroups(data, day, punches, ref groupCount);
        if (groupCount > 1)
            throw new ExternalDirectoryException("vr_mais_ambiguous_time_cards", "VR Mais returned more than one time-card group for an employee.");
        var ordered = punches.OrderBy(x => x.Time).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var expected = $"{index / 2 + 1}ª {(index % 2 == 0 ? "Entrada" : "Saída")}";
            if (!ordered[index].Direction.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                index > 0 && ordered[index].Time <= ordered[index - 1].Time)
                throw new ExternalDirectoryException("vr_mais_invalid_time_cards", "VR Mais returned inconsistent time-card directions.");
        }
        return ordered.Select(x => x.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).ToArray();
    }

    private static void VisitTimeCardGroups(JsonElement groups, DateOnly day,
        ICollection<(TimeOnly Time, string Direction)> punches, ref int groupCount)
    {
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind == JsonValueKind.Array)
            {
                VisitTimeCardGroups(group, day, punches, ref groupCount);
                continue;
            }
            if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("data", out var rows) ||
                rows.ValueKind != JsonValueKind.Array) continue;
            groupCount++;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || ReadDay(ReadString(row, "date")) is not { } rowDay ||
                    !TimeOnly.TryParseExact(ReadString(row, "time"), ["HH:mm", "HH:mm:ss"],
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    throw new ExternalDirectoryException("vr_mais_invalid_time_cards", "VR Mais returned an invalid time-card row.");
                if (rowDay != day)
                    throw new ExternalDirectoryException("vr_mais_invalid_time_cards", "VR Mais returned a time-card row outside the requested day.");
                var direction = ReadString(row, "time_card_index");
                if (string.IsNullOrWhiteSpace(direction))
                    throw new ExternalDirectoryException("vr_mais_invalid_time_cards", "VR Mais returned an unknown time-card direction.");
                punches.Add((time, direction.Trim()));
            }
        }
    }

    private static void VisitGroups(JsonElement groups, IDictionary<DateOnly, VrDay> rows, ref int groupCount)
    {
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind == JsonValueKind.Array)
            {
                VisitGroups(group, rows, ref groupCount);
                continue;
            }
            if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array) continue;
            groupCount++;
            foreach (var row in data.EnumerateArray())
            {
                var day = ReadDay(ReadString(row, "date"));
                if (day is null) continue;
                if (rows.ContainsKey(day.Value))
                {
                    rows[day.Value] = new VrDay(null, []);
                    continue;
                }
                rows[day.Value] = new VrDay(ReadDuration(ReadString(row, "total_time")), ReadTimeCards(row));
            }
        }
    }

    private static DateOnly? ReadDay(string? value)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", out var iso)) return iso;
        if (value is { Length: >= 10 } && DateOnly.TryParseExact(value[^10..], "dd/MM/yyyy", out var brazilian))
            return brazilian;
        return null;
    }

    private static int? ReadDuration(string? value)
    {
        if (value is null) return null;
        var parts = value.Trim().Split(':');
        if (parts.Length is < 2 or > 3 || !int.TryParse(parts[0], out var hours) ||
            !int.TryParse(parts[1], out var minutes) || minutes is < 0 or > 59 ||
            parts.Length == 3 && (!int.TryParse(parts[2], out var seconds) || seconds is < 0 or > 59))
            return null;
        var parsedSeconds = parts.Length == 3 ? int.Parse(parts[2]) : 0;
        var total = (long)hours * 3600 + minutes * 60 + parsedSeconds;
        return total <= int.MaxValue ? (int)total : null;
    }

    private static string[] ReadTimeCards(JsonElement row)
    {
        if (!row.TryGetProperty("time_cards", out var values) || values.ValueKind != JsonValueKind.Array) return [];
        return values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : ReadString(value, "csv_value"))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
    }

    private static IEnumerable<(DateOnly From, DateOnly To)> SplitPeriod(DateOnly from, DateOnly to)
    {
        for (var start = from; start <= to; start = start.AddDays(31))
            yield return (start, start.AddDays(30) < to ? start.AddDays(30) : to);
    }

    private (VrMaisDirectoryOptions Settings, Uri BaseUri) ValidateSettings()
    {
        var settings = options.Value.VrMais;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Token))
            throw new ExternalDirectoryException("vr_mais_not_configured", "VR Mais integration is not configured.");
        if (!Uri.TryCreate(settings.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new ExternalDirectoryException("vr_mais_invalid_configuration", "VR Mais API URL must use HTTPS.");
        return (settings, baseUri);
    }

    private static bool IsPaginated(JsonElement root, int received)
    {
        if (!root.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object) return false;
        if (meta.TryGetProperty("next_page", out var nextPage) && HasValue(nextPage))
            return true;
        if (meta.TryGetProperty("next", out var next) && HasValue(next))
            return true;
        if (meta.TryGetProperty("total_pages", out var totalPages) && totalPages.TryGetInt32(out var pages) && pages > 1)
            return true;
        return meta.TryGetProperty("total_count", out var total) && total.TryGetInt32(out var count) && count > received;
    }

    private static bool HasValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Number => !value.TryGetInt64(out var number) || number != 0,
            _ => true
        };

    private static bool ReadActive(JsonElement employee)
    {
        foreach (var name in new[] { "active", "is_active" })
        {
            if (!employee.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
            if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        var status = ReadString(employee, "status");
        return status is null || !status.Equals("inactive", StringComparison.OrdinalIgnoreCase) &&
            !status.Equals("inativo", StringComparison.OrdinalIgnoreCase) &&
            !status.Equals("dismissed", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record VrDay(int? DurationSeconds, string[] TimeCards);
}
