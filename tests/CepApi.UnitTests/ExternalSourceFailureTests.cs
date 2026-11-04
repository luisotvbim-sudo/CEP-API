using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CepApi.Application;
using CepApi.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CepApi.UnitTests;

public sealed class ExternalSourceFailureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, HttpStatusCode.Unauthorized, "monday_http_error")]
    [InlineData(false, HttpStatusCode.Unauthorized, "vr_mais_http_error")]
    [InlineData(true, HttpStatusCode.TooManyRequests, "monday_http_error")]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "vr_mais_http_error")]
    public async Task Http_errors_have_stable_codes_and_do_not_include_upstream_bodies(bool monday, HttpStatusCode status, string code)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("upstream confidential response")
        })));
        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() => Directory(monday, client).FetchAsync(Ct));
        Assert.Equal(code, exception.Code);
        Assert.DoesNotContain("confidential", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Caller_cancellation_is_propagated_instead_of_becoming_a_source_failure(bool monday)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var client = new HttpClient(new Handler((_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Directory(monday, client).FetchAsync(cancellation.Token));
    }

    [Theory]
    [InlineData(true, "monday_unreachable")]
    [InlineData(false, "vr_mais_unreachable")]
    public async Task Timeouts_remain_source_failures_when_the_caller_has_not_cancelled(bool monday, string code)
    {
        using var client = new HttpClient(new Handler((_, _) => throw new TaskCanceledException("timeout")));
        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() => Directory(monday, client).FetchAsync(Ct));
        Assert.Equal(code, exception.Code);
    }

    [Theory]
    [InlineData("""{"errors":[{"message":"private@example.test","extensions":{"code":"REQUEST_MAX_COMPLEXITY_EXCEEDED","retry_in_seconds":12}}],"data":{"users":[]}}""", "graphql_errors", "REQUEST_MAX_COMPLEXITY_EXCEEDED", 1, 12)]
    [InlineData("""{"errors":[{"message":"private@example.test","extensions":{"code":"private-token<script>","retry_in_seconds":99999}}],"data":{"users":[]}}""", "graphql_errors", "other", 1, 3600)]
    [InlineData("""{"private":"private@example.test"}""", "missing_data", "none", 0, null)]
    [InlineData("""{"errors":{"message":"private@example.test"},"data":{"users":[]}}""", "invalid_errors", "none", 0, null)]
    [InlineData("""{"errors":null,"data":{"users":[]}}""", "invalid_errors", "none", 0, null)]
    public async Task Monday_graphql_failures_log_only_allowlisted_diagnostics(
        string response, string failure, string safeCode, int errorCount, int? retryAfterSeconds)
    {
        var logger = new RecordingLogger();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(response))));
        var source = new MondayDirectorySource(client, Options.Create(Settings()), logger);

        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() => source.FetchAsync(Ct));

        Assert.Equal("monday_query_failed", exception.Code);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("users", entry.Fields["Operation"]);
        Assert.Equal(1, entry.Fields["Page"]);
        Assert.Equal(200, entry.Fields["HttpStatus"]);
        Assert.Equal(failure, entry.Fields["Failure"]);
        Assert.Equal(safeCode, entry.Fields["ErrorCodes"]);
        Assert.Equal(errorCount, entry.Fields["ErrorCount"]);
        Assert.Equal(retryAfterSeconds, entry.Fields["RetryAfterSeconds"]);
        Assert.DoesNotContain("private", entry.Rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", entry.Rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-token", entry.Rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Monday_time_page_failure_logs_operation_and_page_without_cursor()
    {
        var logger = new RecordingLogger();
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = body.RootElement.GetProperty("query").GetString()!;
            if (query.Contains("hierarchy_type", StringComparison.Ordinal))
                return Json("""{"data":{"boards":[{"id":"1","hierarchy_type":"multi_level","columns":[{"id":"people","title":"Profissional","type":"people"}]}]}}""");
            if (query.Contains("next_items_page", StringComparison.Ordinal))
            {
                var reply = Json("""{"errors":[{"message":"private@example.test","extensions":{"code":"InvalidArgumentException","retry_in_seconds":5}}]}""");
                reply.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
                return reply;
            }
            return Json("""{"data":{"boards":[{"id":"1","items_page":{"cursor":"private-cursor","items":[]}}]}}""");
        }));
        var source = new MondayDirectorySource(client, Options.Create(Settings()), logger);

        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() =>
            ((IExternalWorkforceTimeSource)source).FetchAsync(new(2026, 9, 1), new(2026, 9, 2), ["42"], Ct));

        Assert.Equal("monday_query_failed", exception.Code);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("time_items_main", entry.Fields["Operation"]);
        Assert.Equal(2, entry.Fields["Page"]);
        Assert.Equal("InvalidArgumentException", entry.Fields["ErrorCodes"]);
        Assert.Equal(17, entry.Fields["RetryAfterSeconds"]);
        Assert.DoesNotContain("private-cursor", entry.Rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.test", entry.Rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Monday_subitem_failure_uses_static_operation_without_board_ids()
    {
        const string mainId = "private-main-board";
        const string subitemId = "private-subitem-board";
        var settings = Settings();
        settings.Monday.BoardId = mainId;
        var logger = new RecordingLogger();
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = body.RootElement.GetProperty("query").GetString()!;
            var requestedBoard = body.RootElement.GetProperty("variables").GetProperty("ids")[0].GetString();
            if (query.Contains("hierarchy_type", StringComparison.Ordinal))
                return requestedBoard == mainId
                    ? Json("""{"data":{"boards":[{"id":"private-main-board","hierarchy_type":"classic","columns":[{"id":"people","title":"Profissional","type":"people"},{"id":"subtasks","title":"Subitems","type":"subtasks","settings":{"boardIds":["private-subitem-board"]}}]}]}}""")
                    : Json("""{"data":{"boards":[{"id":"private-subitem-board","hierarchy_type":"classic","columns":[{"id":"sub-people","title":"Profissional","type":"people"}]}]}}""");
            return requestedBoard == mainId
                ? Json("""{"data":{"boards":[{"id":"private-main-board","items_page":{"cursor":null,"items":[]}}]}}""")
                : Json("""{"errors":[{"extensions":{"code":"InvalidColumnIdException"},"message":"private@example.test"}]}""");
        }));
        var source = new MondayDirectorySource(client, Options.Create(settings), logger);

        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() =>
            ((IExternalWorkforceTimeSource)source).FetchAsync(new(2026, 9, 1), new(2026, 9, 2), ["42"], Ct));

        Assert.Equal("monday_query_failed", exception.Code);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("time_items_subitems", entry.Fields["Operation"]);
        Assert.Equal(1, entry.Fields["Page"]);
        Assert.Equal("InvalidColumnIdException", entry.Fields["ErrorCodes"]);
        Assert.DoesNotContain(mainId, entry.Rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(subitemId, entry.Rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.test", entry.Rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Monday_directory_subitem_failure_uses_static_operation()
    {
        var settings = Settings();
        settings.Monday.BoardId = "private-main-board";
        var logger = new RecordingLogger();
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = body.RootElement.GetProperty("query").GetString()!;
            if (query.Contains("users(status", StringComparison.Ordinal))
                return Json("""{"data":{"users":[{"id":"42","name":"Tester"}]}}""");
            var requestedBoard = body.RootElement.GetProperty("variables").GetProperty("ids")[0].GetString();
            if (query.Contains("hierarchy_type", StringComparison.Ordinal))
                return requestedBoard == settings.Monday.BoardId
                    ? Json("""{"data":{"boards":[{"id":"private-main-board","hierarchy_type":"classic","columns":[{"id":"people","title":"Profissional","type":"people"},{"id":"subtasks","title":"Subitems","type":"subtasks","settings":{"boardIds":["private-subitem-board"]}}]}]}}""")
                    : Json("""{"data":{"boards":[{"id":"private-subitem-board","hierarchy_type":"classic","columns":[{"id":"sub-people","title":"Profissional","type":"people"}]}]}}""");
            return requestedBoard == settings.Monday.BoardId
                ? Json("""{"data":{"boards":[{"id":"private-main-board","items_page":{"cursor":null,"items":[]}}]}}""")
                : Json("""{"errors":[{"extensions":{"code":"InvalidColumnIdException"}}]}""");
        }));
        var source = new MondayDirectorySource(client, Options.Create(settings), logger);

        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() => source.FetchAsync(Ct));

        Assert.Equal("monday_query_failed", exception.Code);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("directory_items_subitems", entry.Fields["Operation"]);
        Assert.Equal(1, entry.Fields["Page"]);
        Assert.DoesNotContain("private-main-board", entry.Rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("private-subitem-board", entry.Rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Insecure_endpoints_are_rejected_before_sending_credentials(bool monday)
    {
        var settings = Settings();
        settings.Monday.ApiUrl = "http://example.test";
        settings.VrMais.ApiUrl = "http://example.test";
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("HTTP must not be called")));
        IExternalWorkforceDirectorySource source = monday
            ? new MondayDirectorySource(client, Options.Create(settings))
            : new VrMaisDirectorySource(client, Options.Create(settings), new SystemClock());
        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() => source.FetchAsync(Ct));
        Assert.Equal(monday ? "monday_invalid_configuration" : "vr_mais_invalid_configuration", exception.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Monday_repeated_cursors_stop_both_directory_and_time_pagination(bool time)
    {
        var pageRequests = 0;
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = body.RootElement.GetProperty("query").GetString()!;
            if (query.Contains("users(status", StringComparison.Ordinal))
                return Json("""{"data":{"users":[{"id":"42","name":"Test"}]}}""");
            if (query.Contains("hierarchy_type", StringComparison.Ordinal))
                return Json("""{"data":{"boards":[{"id":"1","hierarchy_type":"multi_level","columns":[{"id":"people","title":"Profissional","type":"people"}]}]}}""");
            pageRequests++;
            return query.Contains("next_items_page", StringComparison.Ordinal)
                ? Json("""{"data":{"next_items_page":{"cursor":"repeated","items":[]}}}""")
                : Json("""{"data":{"boards":[{"id":"1","items_page":{"cursor":"repeated","items":[]}}]}}""");
        }));
        var source = new MondayDirectorySource(client, Options.Create(Settings()));
        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(async () =>
        {
            if (time)
                await ((IExternalWorkforceTimeSource)source).FetchAsync(new(2026, 9, 1), new(2026, 9, 2), ["42"], Ct);
            else
                await source.FetchAsync(Ct);
        });
        Assert.Equal("monday_pagination_repeated", exception.Code);
        Assert.Equal(2, pageRequests);
    }

    [Fact]
    public async Task VrMais_splits_periods_without_gaps_and_bounds_parallel_requests()
    {
        var requests = new ConcurrentBag<(long Employee, DateOnly From, DateOnly To)>();
        var active = 0;
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            var concurrent = Interlocked.Increment(ref active);
            try
            {
                Assert.InRange(concurrent, 1, 2);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var report = body.RootElement.GetProperty("report");
                requests.Add((report.GetProperty("employee_id").GetInt64(),
                    DateOnly.Parse(report.GetProperty("start_date").GetString()!),
                    DateOnly.Parse(report.GetProperty("end_date").GetString()!)));
                await Task.Yield();
                return Json("""{"data":[{"data":[]}]}""");
            }
            finally { Interlocked.Decrement(ref active); }
        }));
        IExternalWorkforceTimeSource source = new VrMaisDirectorySource(client, Options.Create(Settings()), new SystemClock());
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 3, 5);
        var snapshot = await source.FetchAsync(from, to, ["42", "84", "42"], Ct);
        Assert.Equal(6, requests.Count);
        foreach (var employee in new long[] { 42, 84 })
        {
            var periods = requests.Where(x => x.Employee == employee).OrderBy(x => x.From).ToArray();
            Assert.Equal(from, periods[0].From);
            Assert.Equal(to, periods[^1].To);
            Assert.All(periods, x => Assert.InRange(x.To.DayNumber - x.From.DayNumber + 1, 1, 31));
            Assert.Equal(periods[0].To.AddDays(1), periods[1].From);
            Assert.Equal(periods[1].To.AddDays(1), periods[2].From);
        }
        Assert.Equal(128, snapshot.Records.Count);
        Assert.All(snapshot.Records, record => { Assert.Equal("missing", record.State); Assert.Null(record.DurationSeconds); });
    }

    [Theory]
    [InlineData("""{"employees":[],"meta":{"next_page":2}}""", "vr_mais_directory_incomplete")]
    [InlineData("""{"employees":[],"meta":{"total_count":1}}""", "vr_mais_directory_incomplete")]
    [InlineData("""{"error":"private upstream details"}""", "vr_mais_query_failed")]
    public async Task VrMais_incomplete_directories_are_not_reported_as_complete(string response, string code)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(response))));
        var source = new VrMaisDirectorySource(client, Options.Create(Settings()), new SystemClock());
        var exception = await Assert.ThrowsAsync<ExternalDirectoryException>(() => source.FetchAsync(Ct));
        Assert.Equal(code, exception.Code);
    }

    private static IExternalWorkforceDirectorySource Directory(bool monday, HttpClient client)
        => monday ? new MondayDirectorySource(client, Options.Create(Settings())) : new VrMaisDirectorySource(client, Options.Create(Settings()), new SystemClock());

    private static WorkforceIntegrationOptions Settings() => new()
    {
        Monday = new() { Enabled = true, Token = "test-token", BoardId = "1" },
        VrMais = new() { Enabled = true, Token = "test-token" }
    };

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class RecordingLogger : ILogger<MondayDirectorySource>
    {
        public List<(LogLevel Level, string Rendered, IReadOnlyDictionary<string, object?> Fields)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(x => x.Key, x => x.Value)
                : new Dictionary<string, object?>();
            Entries.Add((logLevel, formatter(state, exception), fields));
        }
    }
}
