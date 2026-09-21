using System.Net.Http.Json;
using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using Microsoft.Extensions.Options;

namespace CepApi.Infrastructure.Services;

public sealed class MondayDirectorySource(
    HttpClient httpClient,
    IOptions<WorkforceIntegrationOptions> options) : IExternalWorkforceDirectorySource, IExternalWorkforceTimeSource
{
    private const string DirectoryQuery = """
        query ($ids: [ID!]!, $page: Int!) {
          boards(ids: $ids) { id subscribers { id } }
          users(status: [ACTIVE], limit: 100, page: $page) { id name email }
        }
        """;
    private const string FirstTimePageQuery = """
        query ($ids: [ID!]!) {
          boards(ids: $ids) {
            id
            items_page(limit: 50) {
              cursor
              items {
                id name url board { id }
                column_values(types: [time_tracking]) {
                  id
                  ... on TimeTrackingValue {
                    running started_at
                    history { id status started_at ended_at started_user_id manually_entered_start_date manually_entered_start_time manually_entered_end_date manually_entered_end_time }
                  }
                }
                subitems {
                  id name url board { id }
                  column_values(types: [time_tracking]) {
                    id
                    ... on TimeTrackingValue {
                      running started_at
                      history { id status started_at ended_at started_user_id manually_entered_start_date manually_entered_start_time manually_entered_end_date manually_entered_end_time }
                    }
                  }
                }
              }
            }
          }
        }
        """;
    private const string NextTimePageQuery = """
        query ($cursor: String!) {
          next_items_page(cursor: $cursor, limit: 50) {
            cursor
            items {
              id name url board { id }
              column_values(types: [time_tracking]) {
                id
                ... on TimeTrackingValue {
                  running started_at
                  history { id status started_at ended_at started_user_id manually_entered_start_date manually_entered_start_time manually_entered_end_date manually_entered_end_time }
                }
              }
              subitems {
                id name url board { id }
                column_values(types: [time_tracking]) {
                  id
                  ... on TimeTrackingValue {
                    running started_at
                    history { id status started_at ended_at started_user_id manually_entered_start_date manually_entered_start_time manually_entered_end_date manually_entered_end_time }
                  }
                }
              }
            }
          }
        }
        """;

    public ExternalWorkforceSource Source => ExternalWorkforceSource.Monday;

    public async Task<ExternalWorkforceDirectorySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Monday;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Token))
            throw new ExternalDirectoryException("monday_not_configured", "Monday integration is not configured.");
        if (!Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ExternalDirectoryException("monday_invalid_configuration", "Monday API URL must use HTTPS.");
        if (string.IsNullOrWhiteSpace(settings.BoardId))
            throw new ExternalDirectoryException("monday_invalid_configuration", "Monday board id is required.");

        HashSet<string>? boardSubscribers = null;
        var users = new Dictionary<string, ExternalWorkforceIdentitySnapshot>(StringComparer.Ordinal);
        for (var page = 1; page <= 20; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation("Authorization", settings.Token.Trim());
            request.Headers.TryAddWithoutValidation("API-Version", settings.ApiVersion);
            request.Content = JsonContent.Create(new
            {
                query = DirectoryQuery,
                variables = new { ids = new[] { settings.BoardId.Trim() }, page }
            });

            using var response = await SendAsync(request, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
                throw new ExternalDirectoryException("monday_query_failed", "Monday did not complete the directory query.");
            if (!root.TryGetProperty("data", out var data))
                throw new ExternalDirectoryException("monday_invalid_response", "Monday returned an invalid response.");

            if (boardSubscribers is null)
            {
                if (!data.TryGetProperty("boards", out var boards) || boards.GetArrayLength() != 1)
                    throw new ExternalDirectoryException("monday_board_unavailable", "The configured Monday board is not available.");
                var board = boards[0];
                if (!board.TryGetProperty("subscribers", out var subscribers) || subscribers.ValueKind != JsonValueKind.Array)
                    throw new ExternalDirectoryException("monday_invalid_response", "Monday did not return the board subscribers.");
                boardSubscribers = subscribers.EnumerateArray()
                    .Select(item => ReadString(item, "id"))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.Ordinal)!;
            }

            if (!data.TryGetProperty("users", out var pageUsers) || pageUsers.ValueKind != JsonValueKind.Array)
                throw new ExternalDirectoryException("monday_invalid_response", "Monday did not return the active users.");

            foreach (var user in pageUsers.EnumerateArray())
            {
                var id = ReadString(user, "id");
                if (id is null || !boardSubscribers.Contains(id)) continue;
                var name = ReadString(user, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                users[id] = new ExternalWorkforceIdentitySnapshot(id, name, ReadString(user, "email"), true);
            }

            if (pageUsers.GetArrayLength() < 100)
                return new ExternalWorkforceDirectorySnapshot(users.Values.OrderBy(x => x.DisplayName).ToArray(), true);
        }

        throw new ExternalDirectoryException("monday_directory_limit", "Monday user pagination exceeded the supported limit.");
    }

    async Task<ExternalWorkforceTimeSnapshot> IExternalWorkforceTimeSource.FetchAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string> activeExternalIdentityIds,
        CancellationToken cancellationToken)
    {
        var settings = ValidateSettings();
        var allowedUsers = activeExternalIdentityIds.ToHashSet(StringComparer.Ordinal);
        var records = new Dictionary<string, ExternalWorkforceTimeRecordSnapshot>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        var now = DateTimeOffset.UtcNow;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        for (var page = 0; page < 500; page++)
        {
            using var document = page == 0
                ? await QueryAsync(settings, FirstTimePageQuery, new { ids = new[] { settings.BoardId.Trim() } }, cancellationToken)
                : await QueryAsync(settings, NextTimePageQuery, new { cursor = cursor! }, cancellationToken);
            var data = document.RootElement.GetProperty("data");
            JsonElement pageData;
            if (page == 0)
            {
                var boards = data.GetProperty("boards");
                if (boards.GetArrayLength() != 1)
                    throw new ExternalDirectoryException("monday_board_unavailable", "The configured Monday board is not available.");
                pageData = boards[0].GetProperty("items_page");
            }
            else
            {
                pageData = data.GetProperty("next_items_page");
            }

            if (!pageData.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new ExternalDirectoryException("monday_invalid_response", "Monday did not return the board items.");
            foreach (var item in items.EnumerateArray())
                ExtractTimeRecords(item, allowedUsers, from, to, now, timeZone, records);

            cursor = ReadString(pageData, "cursor");
            if (string.IsNullOrWhiteSpace(cursor))
                return new ExternalWorkforceTimeSnapshot(from, to, records.Values.ToArray(), true);
            if (!cursors.Add(cursor))
                throw new ExternalDirectoryException("monday_pagination_repeated", "Monday repeated a pagination cursor.");
        }

        throw new ExternalDirectoryException("monday_pagination_limit", "Monday item pagination exceeded the supported limit.");
    }

    private MondayDirectoryOptions ValidateSettings()
    {
        var settings = options.Value.Monday;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Token))
            throw new ExternalDirectoryException("monday_not_configured", "Monday integration is not configured.");
        if (!Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ExternalDirectoryException("monday_invalid_configuration", "Monday API URL must use HTTPS.");
        if (string.IsNullOrWhiteSpace(settings.BoardId))
            throw new ExternalDirectoryException("monday_invalid_configuration", "Monday board id is required.");
        return settings;
    }

    private async Task<JsonDocument> QueryAsync(
        MondayDirectoryOptions settings,
        string query,
        object variables,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl);
        request.Headers.TryAddWithoutValidation("Authorization", settings.Token!.Trim());
        request.Headers.TryAddWithoutValidation("API-Version", settings.ApiVersion);
        request.Content = JsonContent.Create(new { query, variables });
        using var response = await SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0 ||
            !root.TryGetProperty("data", out _))
        {
            document.Dispose();
            throw new ExternalDirectoryException("monday_query_failed", "Monday did not complete the time data query.");
        }
        return document;
    }

    private static void ExtractTimeRecords(
        JsonElement item,
        HashSet<string> allowedUsers,
        DateOnly from,
        DateOnly to,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        IDictionary<string, ExternalWorkforceTimeRecordSnapshot> records)
    {
        var itemId = ReadString(item, "id") ?? string.Empty;
        var itemName = ReadString(item, "name");
        var itemUrl = ReadString(item, "url");
        if (item.TryGetProperty("column_values", out var columns) && columns.ValueKind == JsonValueKind.Array)
        {
            foreach (var column in columns.EnumerateArray())
            {
                if (!column.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array) continue;
                var entries = history.EnumerateArray().ToArray();
                var openCandidates = entries.Count(entry => ReadDate(entry, "started_at") is not null &&
                    ReadDate(entry, "ended_at") is null && !IsDeleted(entry));
                var columnRunning = column.TryGetProperty("running", out var runningValue) &&
                    runningValue.ValueKind == JsonValueKind.True;
                var columnStartedAt = ReadDate(column, "started_at");
                foreach (var entry in entries)
                {
                    if (IsDeleted(entry)) continue;
                    var startedAt = ReadDate(entry, "started_at");
                    var endedAt = ReadDate(entry, "ended_at");
                    if (startedAt is null || endedAt < startedAt) continue;
                    var running = endedAt is null && columnRunning &&
                        (columnStartedAt == startedAt || openCandidates == 1);
                    if (endedAt is null && !running) continue;
                    var userId = ReadString(entry, "started_user_id");
                    if (userId is null || !allowedUsers.Contains(userId)) continue;
                    var workDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startedAt.Value, timeZone).DateTime);
                    if (workDate < from || workDate > to) continue;
                    var historyId = ReadString(entry, "id");
                    var columnId = ReadString(column, "id");
                    if (historyId is null || columnId is null) continue;
                    var externalKey = $"{itemId}:{columnId}:{historyId}";
                    var duration = (long)Math.Max(0, ((endedAt ?? now) - startedAt.Value).TotalSeconds);
                    var manual = new[]
                    {
                        "manually_entered_start_date", "manually_entered_start_time",
                        "manually_entered_end_date", "manually_entered_end_time"
                    }.Any(name => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True);
                    var details = JsonSerializer.Serialize(new { itemId, manual, running });
                    records[externalKey] = new ExternalWorkforceTimeRecordSnapshot(
                        userId, externalKey, workDate, startedAt, endedAt,
                        (int)Math.Min(duration, int.MaxValue), running ? "running" : "closed",
                        itemName, itemUrl, details);
                }
            }
        }
        if (item.TryGetProperty("subitems", out var subitems) && subitems.ValueKind == JsonValueKind.Array)
            foreach (var subitem in subitems.EnumerateArray())
                ExtractTimeRecords(subitem, allowedUsers, from, to, now, timeZone, records);
    }

    private static bool IsDeleted(JsonElement entry)
        => ReadString(entry, "status")?.Contains("deleted", StringComparison.OrdinalIgnoreCase) == true;

    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName)
        => DateTimeOffset.TryParse(ReadString(element, propertyName), out var value) ? value : null;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new ExternalDirectoryException("monday_http_error", "Monday returned an unsuccessful response.");
            }
            return response;
        }
        catch (ExternalDirectoryException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ExternalDirectoryException("monday_unreachable", "Monday could not be reached.", exception);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }
}
