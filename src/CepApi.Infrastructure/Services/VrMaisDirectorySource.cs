using System.Text.Json;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using CepApi.Application;
using CepApi.Domain;
using Microsoft.Extensions.Options;

namespace CepApi.Infrastructure.Services;

public sealed class VrMaisDirectorySource(
    HttpClient httpClient,
    IOptions<WorkforceIntegrationOptions> options) : IExternalWorkforceDirectorySource, IExternalWorkforceTimeSource
{
    public ExternalWorkforceSource Source => ExternalWorkforceSource.VrMais;

    public async Task<ExternalWorkforceDirectorySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.VrMais;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Token))
            throw new ExternalDirectoryException("vr_mais_not_configured", "VR Mais integration is not configured.");
        if (!Uri.TryCreate(settings.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new ExternalDirectoryException("vr_mais_invalid_configuration", "VR Mais API URL must use HTTPS.");

        var endpoint = new Uri(baseUri, "employees?attributes=id,first_name,last_name,name,email,active,status&incluirAnexos=false");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation("access-token", settings.Token.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await SendAsync(request, cancellationToken);
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
        var settings = options.Value.VrMais;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Token))
            throw new ExternalDirectoryException("vr_mais_not_configured", "VR Mais integration is not configured.");
        if (!Uri.TryCreate(settings.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new ExternalDirectoryException("vr_mais_invalid_configuration", "VR Mais API URL must use HTTPS.");

        var employees = activeExternalIdentityIds.Where(id => long.TryParse(id, out _)).Distinct(StringComparer.Ordinal).ToArray();
        var requests = employees.SelectMany(employeeId => SplitPeriod(from, to)
            .Select(period => (employeeId, period.From, period.To))).ToArray();
        var records = new ConcurrentDictionary<string, ExternalWorkforceTimeRecordSnapshot>(StringComparer.Ordinal);
        using var concurrency = new SemaphoreSlim(2, 2);
        await Task.WhenAll(requests.Select(async item =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                var chunk = await FetchReportAsync(baseUri, settings.Token!, item.employeeId, item.From, item.To, cancellationToken);
                foreach (var record in chunk) records[record.ExternalKey] = record;
            }
            finally
            {
                concurrency.Release();
            }
        }));

        return new ExternalWorkforceTimeSnapshot(from, to, records.Values.OrderBy(x => x.WorkDate).ToArray(), true);
    }

    private async Task<IReadOnlyCollection<ExternalWorkforceTimeRecordSnapshot>> FetchReportAsync(
        Uri baseUri,
        string token,
        string employeeId,
        DateOnly from,
        DateOnly to,
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
        using var response = await SendAsync(request, cancellationToken);
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

        var result = new List<ExternalWorkforceTimeRecordSnapshot>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var externalKey = $"{employeeId}:{day:yyyy-MM-dd}";
            if (!rows.TryGetValue(day, out var row))
            {
                result.Add(new ExternalWorkforceTimeRecordSnapshot(employeeId, externalKey, day, null, null, null,
                    "missing", null, null, JsonSerializer.Serialize(new { timeCards = Array.Empty<string>() })));
                continue;
            }
            result.Add(new ExternalWorkforceTimeRecordSnapshot(employeeId, externalKey, day, null, null,
                row.DurationSeconds, row.DurationSeconds is null ? "unrecognized" : "reported", null, null,
                JsonSerializer.Serialize(new { timeCards = row.TimeCards })));
        }
        return result;
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

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new ExternalDirectoryException("vr_mais_http_error", "VR Mais returned an unsuccessful response.");
            }
            return response;
        }
        catch (ExternalDirectoryException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ExternalDirectoryException("vr_mais_unreachable", "VR Mais could not be reached.", exception);
        }
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

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private sealed record VrDay(int? DurationSeconds, string[] TimeCards);
}
