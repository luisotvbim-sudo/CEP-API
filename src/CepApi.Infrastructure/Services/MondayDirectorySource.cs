using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using Microsoft.Extensions.Options;
using static CepApi.Infrastructure.Services.ExternalSourceJson;

namespace CepApi.Infrastructure.Services;

public sealed class MondayDirectorySource(
    HttpClient httpClient,
    IOptions<WorkforceIntegrationOptions> options) : IExternalWorkforceDirectorySource, IExternalWorkforceOverlapTimeSource
{
    private const string BoardSchemaQuery = """
        query ($ids: [ID!]!) {
          boards(ids: $ids) {
            id hierarchy_type
            columns { id title type settings }
          }
        }
        """;
    private const string DirectoryQuery = """
        query ($page: Int!) {
          users(status: [ACTIVE], limit: 100, page: $page) { id name email }
        }
        """;
    private const string FirstDirectoryItemsQuery = """
        query ($ids: [ID!]!, $professionalColumn: ID!) {
          boards(ids: $ids) {
            id
            items_page(limit: 500, hierarchy_scope_config: "allItems",
              query_params: {rules: [{column_id: $professionalColumn, compare_value: [], operator: is_not_empty}]}) {
              cursor
              items {
                column_values(types: [people]) {
                  id
                  ... on PeopleValue { persons_and_teams { id kind } }
                }
              }
            }
          }
        }
        """;
    private const string NextDirectoryItemsQuery = """
        query ($cursor: String!) {
          next_items_page(cursor: $cursor, limit: 500) {
            cursor
            items {
              column_values(types: [people]) {
                id
                ... on PeopleValue { persons_and_teams { id kind } }
              }
            }
          }
        }
        """;
    private const string TimeColumns = """
        column_values(types: [people, time_tracking]) {
          id
          ... on PeopleValue { persons_and_teams { id kind } }
          ... on TimeTrackingValue {
            running started_at
            history { id status started_at ended_at started_user_id manually_entered_start_date manually_entered_start_time manually_entered_end_date manually_entered_end_time }
          }
        }
        """;
    private const string TimeItemFields = $$"""
        id name url board { id }
        {{TimeColumns}}
        subitems {
          id name url board { id }
          {{TimeColumns}}
        }
        """;
    private const string FirstTimePageQuery = $$"""
        query ($ids: [ID!]!, $responsibleColumn: ID!, $people: CompareValue!) {
          boards(ids: $ids) {
            id
            items_page(limit: 50, hierarchy_scope_config: "allItems",
              query_params: {rules: [{column_id: $responsibleColumn, compare_value: $people, operator: any_of}]}) {
              cursor
              items { {{TimeItemFields}} }
            }
          }
        }
        """;
    private const string NextTimePageQuery = $$"""
        query ($cursor: String!) {
          next_items_page(cursor: $cursor, limit: 50) {
            cursor
            items { {{TimeItemFields}} }
          }
        }
        """;

    public ExternalWorkforceSource Source => ExternalWorkforceSource.Monday;

    public async Task<ExternalWorkforceDirectorySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var settings = ValidateSettings();
        var users = new Dictionary<string, ExternalWorkforceIdentitySnapshot>(StringComparer.Ordinal);
        var usersComplete = false;
        for (var page = 1; page <= 20; page++)
        {
            using var document = await QueryAsync(settings, DirectoryQuery, new { page }, cancellationToken);
            var data = document.RootElement.GetProperty("data");
            if (!data.TryGetProperty("users", out var pageUsers) || pageUsers.ValueKind != JsonValueKind.Array)
                throw new ExternalDirectoryException("monday_invalid_response", "Monday did not return the active users.");

            foreach (var user in pageUsers.EnumerateArray())
            {
                var id = ReadString(user, "id");
                if (id is null) continue;
                var name = ReadString(user, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                users[id] = new ExternalWorkforceIdentitySnapshot(id, name, ReadString(user, "email"), true);
            }

            if (pageUsers.GetArrayLength() < 100)
            {
                usersComplete = true;
                break;
            }
        }
        if (!usersComplete)
            throw new ExternalDirectoryException("monday_directory_limit", "Monday user pagination exceeded the supported limit.");

        var assignedIds = new HashSet<string>(StringComparer.Ordinal);
        var boards = await LoadBoardSchemasAsync(settings, cancellationToken);
        foreach (var board in boards.Where(board => board.ResponsibleColumnId is not null))
            await FetchAssignedUserIdsAsync(settings, board, assignedIds, cancellationToken);
        return new ExternalWorkforceDirectorySnapshot(
            users.Values.Where(user => assignedIds.Contains(user.ExternalId))
                .OrderBy(user => user.DisplayName).ToArray(), true);
    }

    Task<ExternalWorkforceTimeSnapshot> IExternalWorkforceTimeSource.FetchAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string> activeExternalIdentityIds,
        CancellationToken cancellationToken)
        => FetchTimeAsync(from, to, activeExternalIdentityIds, false, cancellationToken);

    public Task<ExternalWorkforceTimeSnapshot> FetchIncludingOverlapAsync(DateOnly from, DateOnly to,
        IReadOnlyCollection<string> activeExternalIdentityIds, CancellationToken cancellationToken)
        => FetchTimeAsync(from, to, activeExternalIdentityIds, true, cancellationToken);

    private async Task<ExternalWorkforceTimeSnapshot> FetchTimeAsync(DateOnly from, DateOnly to,
        IReadOnlyCollection<string> activeExternalIdentityIds, bool includeOverlap, CancellationToken cancellationToken)
    {
        if (activeExternalIdentityIds.Count == 0)
            return new ExternalWorkforceTimeSnapshot(from, to, [], true);
        var settings = ValidateSettings();
        var allowedUsers = activeExternalIdentityIds.ToHashSet(StringComparer.Ordinal);
        var reader = new MondayTimeSnapshotReader(from, to, allowedUsers, includeOverlap, DateTimeOffset.UtcNow);

        var boards = await LoadBoardSchemasAsync(settings, cancellationToken);
        var subitemBoards = boards.Skip(1).ToDictionary(board => board.Id, StringComparer.Ordinal);
        foreach (var board in boards.Where(board => board.ResponsibleColumnId is not null))
            await FetchBoardTimeAsync(settings, board, subitemBoards, allowedUsers, reader, cancellationToken);

        return reader.Snapshot();
    }

    private async Task FetchBoardTimeAsync(
        MondayDirectoryOptions settings,
        MondayBoardSchema board,
        IReadOnlyDictionary<string, MondayBoardSchema> subitemBoards,
        HashSet<string> allowedUsers,
        MondayTimeSnapshotReader reader,
        CancellationToken cancellationToken)
    {
        var people = allowedUsers.Select(id => $"person-{id}").ToArray();
        await foreach (var items in ReadItemPagesAsync(settings, FirstTimePageQuery,
            new { ids = new[] { board.Id }, responsibleColumn = board.ResponsibleColumnId, people },
            NextTimePageQuery, cancellationToken))
        {
            foreach (var item in items.EnumerateArray())
            {
                var assignment = reader.Read(item, board.ResponsibleColumnId);
                if (board.Id != settings.BoardId.Trim() ||
                    !item.TryGetProperty("subitems", out var subitems) ||
                    subitems.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var subitem in subitems.EnumerateArray())
                {
                    var subitemBoardId = subitem.TryGetProperty("board", out var subitemBoard)
                        ? ReadString(subitemBoard, "id") : null;
                    var subitemColumnId = subitemBoardId is not null &&
                        subitemBoards.TryGetValue(subitemBoardId, out var schema)
                        ? schema.ResponsibleColumnId : null;
                    reader.Read(subitem, subitemColumnId, assignment);
                }
            }
        }
    }

    private async Task FetchAssignedUserIdsAsync(
        MondayDirectoryOptions settings,
        MondayBoardSchema board,
        ISet<string> assignedIds,
        CancellationToken cancellationToken)
    {
        await foreach (var items in ReadItemPagesAsync(settings, FirstDirectoryItemsQuery,
            new { ids = new[] { board.Id }, professionalColumn = board.ResponsibleColumnId },
            NextDirectoryItemsQuery, cancellationToken))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("column_values", out var columns) || columns.ValueKind != JsonValueKind.Array)
                    throw new ExternalDirectoryException("monday_invalid_response",
                        "Monday did not return the professional column.");
                var professional = columns.EnumerateArray()
                    .FirstOrDefault(column => ReadString(column, "id") == board.ResponsibleColumnId);
                if (professional.ValueKind == JsonValueKind.Undefined ||
                    !professional.TryGetProperty("persons_and_teams", out var people) ||
                    people.ValueKind != JsonValueKind.Array)
                    throw new ExternalDirectoryException("monday_invalid_response",
                        "Monday did not return the assigned professionals.");
                var personIds = people.EnumerateArray()
                    .Where(person => ReadString(person, "kind") == "person")
                    .Select(person => ReadString(person, "id"))
                    .Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
                foreach (var id in personIds)
                    assignedIds.Add(id!);
            }
        }
    }

    private async IAsyncEnumerable<JsonElement> ReadItemPagesAsync(
        MondayDirectoryOptions settings,
        string firstQuery,
        object firstVariables,
        string nextQuery,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; page < 500; page++)
        {
            using var document = page == 0
                ? await QueryAsync(settings, firstQuery, firstVariables, cancellationToken)
                : await QueryAsync(settings, nextQuery, new { cursor = cursor! }, cancellationToken);
            var data = document.RootElement.GetProperty("data");
            var pageData = page == 0 ? SingleBoard(document).GetProperty("items_page") : data.GetProperty("next_items_page");
            if (!pageData.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new ExternalDirectoryException("monday_invalid_response", "Monday did not return the board items.");
            yield return items;

            cursor = ReadString(pageData, "cursor");
            if (string.IsNullOrWhiteSpace(cursor)) yield break;
            if (!cursors.Add(cursor))
                throw new ExternalDirectoryException("monday_pagination_repeated", "Monday repeated a pagination cursor.");
        }
        throw new ExternalDirectoryException("monday_pagination_limit", "Monday item pagination exceeded the supported limit.");
    }

    private async Task<IReadOnlyCollection<MondayBoardSchema>> LoadBoardSchemasAsync(
        MondayDirectoryOptions settings,
        CancellationToken cancellationToken)
    {
        using var mainDocument = await QueryAsync(settings, BoardSchemaQuery,
            new { ids = new[] { settings.BoardId.Trim() } }, cancellationToken);
        var mainBoard = SingleBoard(mainDocument);
        var schemas = new List<MondayBoardSchema> { ParseBoardSchema(mainBoard, true) };
        if (ReadString(mainBoard, "hierarchy_type") != "classic")
            return schemas;

        var subitemBoardIds = mainBoard.GetProperty("columns").EnumerateArray()
            .Where(x => ReadString(x, "type") == "subtasks")
            .SelectMany(ReadSubitemBoardIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (subitemBoardIds.Length == 0)
            return schemas;

        using var subitemDocument = await QueryAsync(settings, BoardSchemaQuery,
            new { ids = subitemBoardIds }, cancellationToken);
        var subitemBoards = subitemDocument.RootElement.GetProperty("data").GetProperty("boards");
        if (subitemBoards.ValueKind != JsonValueKind.Array || subitemBoards.GetArrayLength() != subitemBoardIds.Length)
            throw new ExternalDirectoryException("monday_subitem_board_unavailable",
                "Monday did not return the configured subitem board.");
        foreach (var board in subitemBoards.EnumerateArray())
            schemas.Add(ParseBoardSchema(board, false));
        return schemas;
    }

    private static JsonElement SingleBoard(JsonDocument document)
    {
        var boards = document.RootElement.GetProperty("data").GetProperty("boards");
        if (boards.ValueKind != JsonValueKind.Array || boards.GetArrayLength() != 1)
            throw new ExternalDirectoryException("monday_board_unavailable", "The configured Monday board is not available.");
        return boards[0];
    }

    private static MondayBoardSchema ParseBoardSchema(JsonElement board, bool responsibleRequired)
    {
        var boardId = ReadString(board, "id") ?? throw new ExternalDirectoryException(
            "monday_invalid_response", "Monday did not return a board id.");
        if (!board.TryGetProperty("columns", out var columns) || columns.ValueKind != JsonValueKind.Array)
            throw new ExternalDirectoryException("monday_invalid_response", "Monday did not return board columns.");
        var peopleColumns = columns.EnumerateArray()
            .Where(x => ReadString(x, "type") == "people")
            .Select(x => new { Id = ReadString(x, "id"), Title = ReadString(x, "title") })
            .Where(x => x.Id is not null)
            .ToArray();
        var professionalColumns = peopleColumns.Where(x =>
            string.Equals(x.Title?.Trim(), "Profissional", StringComparison.OrdinalIgnoreCase)).ToArray();
        var responsibleColumns = peopleColumns.Where(x =>
            x.Title?.Contains("respons", StringComparison.OrdinalIgnoreCase) == true).ToArray();
        var selected = professionalColumns.Length == 1
            ? professionalColumns[0]
            : professionalColumns.Length == 0 && responsibleColumns.Length == 1
                ? responsibleColumns[0]
                : professionalColumns.Length == 0 && responsibleColumns.Length == 0 && peopleColumns.Length == 1
                    ? peopleColumns[0]
                    : null;
        if (selected is null && (responsibleRequired || peopleColumns.Length > 0))
            throw new ExternalDirectoryException("monday_responsible_column_unavailable",
                "Monday must have one identifiable People column for the activity responsible person.");
        return new MondayBoardSchema(boardId, selected?.Id);
    }

    private static IReadOnlyCollection<string> ReadSubitemBoardIds(JsonElement column)
    {
        if (!column.TryGetProperty("settings", out var settings) || settings.ValueKind == JsonValueKind.Null)
            return [];
        var json = settings.ValueKind == JsonValueKind.String ? settings.GetString() : settings.GetRawText();
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("boardIds", out var ids) || ids.ValueKind != JsonValueKind.Array)
                return [];
            return ids.EnumerateArray().Select(x => x.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }
        catch (JsonException exception)
        {
            throw new ExternalDirectoryException("monday_subitem_settings_invalid",
                "Monday returned invalid subitem board settings.", exception);
        }
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
        using var response = await ExternalSourceHttp.SendAsync(httpClient, request, "monday", "Monday", cancellationToken);
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

    private sealed record MondayBoardSchema(string Id, string? ResponsibleColumnId);
}
