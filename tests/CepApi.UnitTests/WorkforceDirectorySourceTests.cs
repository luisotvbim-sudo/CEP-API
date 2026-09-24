using System.Net;
using System.Text;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace CepApi.UnitTests;

public sealed class WorkforceDirectorySourceTests
{
    [Fact]
    public async Task Monday_directory_keeps_active_professionals_even_when_they_are_not_board_subscribers()
    {
        using var client = new HttpClient(new JsonHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("users(status:", StringComparison.Ordinal))
                return """
                    {"data":{"users":[{"id":"1","name":"Ana","email":"ana@example.com"},
                    {"id":"2","name":"Bia","email":"bia@example.com"},
                    {"id":"3","name":"Caio","email":"caio@example.com"}]}}
                    """;
            if (body.Contains("hierarchy_type", StringComparison.Ordinal))
                return """
                    {"data":{"boards":[{"id":"9920862624","hierarchy_type":"multi_level",
                    "columns":[{"id":"rt","title":"R.T.","type":"people","settings":null},
                    {"id":"professional","title":"PROFISSIONAL","type":"people","settings":null}]}]}}
                    """;
            Assert.Contains("\"professionalColumn\":\"professional\"", body, StringComparison.Ordinal);
            return """
                {"data":{"boards":[{"id":"9920862624","items_page":{"cursor":null,"items":[
                  {"column_values":[{"id":"professional","persons_and_teams":[
                    {"id":"1","kind":"person"},{"id":"2","kind":"person"}]}]}
                ]}}]}}
                """;
        }));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await source.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2"], result.Identities.Select(identity => identity.ExternalId));
        Assert.True(result.Complete);
    }

    [Fact]
    public async Task Monday_time_source_filters_by_responsible_and_attributes_sessions_to_that_person()
    {
        using var client = new HttpClient(new JsonHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("hierarchy_type", StringComparison.Ordinal))
                return """
                    {"data":{"boards":[{"id":"9920862624","hierarchy_type":"multi_level",
                    "columns":[{"id":"responsavel","title":"Responsável","type":"people","settings":null}]}]}}
                    """;
            Assert.Contains("person-1", body, StringComparison.Ordinal);
            Assert.Contains("responsavel", body, StringComparison.Ordinal);
            return """
            {"data":{"boards":[{"id":"9920862624","items_page":{"cursor":null,"items":[{
              "id":"item-1","name":"Projeto","url":"https://example.monday.com/boards/1","board":{"id":"9920862624"},
              "column_values":[{"id":"responsavel","persons_and_teams":[{"id":"1","kind":"person"}]},
                {"id":"time","running":false,"started_at":null,"history":[{
                "id":"session-1","status":"STOPPED","started_at":"2026-09-20T12:00:00Z","ended_at":"2026-09-20T13:00:00Z",
                "started_user_id":"2","manually_entered_start_date":false,"manually_entered_start_time":false,
                "manually_entered_end_date":false,"manually_entered_end_time":false},
                {"id":"session-old","status":"STOPPED","started_at":"2026-09-01T12:00:00Z","ended_at":"2026-09-01T13:00:00Z",
                "started_user_id":"2"}]}]}]}}]}}
            """;
        }));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await ((IExternalWorkforceTimeSource)source).FetchAsync(
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), ["1"], TestContext.Current.CancellationToken);

        var record = Assert.Single(result.Records);
        Assert.Equal("1", record.ExternalIdentityId);
        Assert.Equal("item-1:time:session-1", record.ExternalKey);
        Assert.Equal(3600, record.DurationSeconds);
        Assert.Equal("closed", record.State);
        Assert.Contains("\"startedByUserId\":\"2\"", record.DetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Monday_time_source_uses_profissional_when_board_has_multiple_people_columns()
    {
        using var client = new HttpClient(new JsonHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("hierarchy_type", StringComparison.Ordinal))
                return """
                    {"data":{"boards":[{"id":"9920862624","hierarchy_type":"multi_level",
                    "columns":[{"id":"rt","title":"R.T.","type":"people","settings":null},
                    {"id":"professional","title":"PROFISSIONAL","type":"people","settings":null}]}]}}
                    """;
            Assert.Contains("\"responsibleColumn\":\"professional\"", body, StringComparison.Ordinal);
            return """
                {"data":{"boards":[{"id":"9920862624","items_page":{"cursor":null,"items":[{
                  "id":"item-1","name":"Projeto","column_values":[
                    {"id":"rt","persons_and_teams":[{"id":"2","kind":"person"}]},
                    {"id":"professional","persons_and_teams":[{"id":"1","kind":"person"}]},
                    {"id":"time","running":false,"history":[{"id":"session-1","status":"STOPPED",
                      "started_at":"2026-09-20T12:00:00Z","ended_at":"2026-09-20T13:00:00Z",
                      "started_user_id":"2"}]}]}]}}]}}
                """;
        }));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await ((IExternalWorkforceTimeSource)source).FetchAsync(
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), ["1"], TestContext.Current.CancellationToken);

        Assert.Equal("1", Assert.Single(result.Records).ExternalIdentityId);
    }

    [Fact]
    public async Task Monday_time_source_inherits_parent_profissional_when_subitem_has_no_people_column()
    {
        using var client = new HttpClient(new JsonHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("hierarchy_type", StringComparison.Ordinal))
                return body.Contains("sub-board", StringComparison.Ordinal)
                    ? """
                      {"data":{"boards":[{"id":"sub-board","hierarchy_type":"classic",
                      "columns":[{"id":"sub-time","title":"H.GASTA","type":"time_tracking","settings":null}]}]}}
                      """
                    : """
                      {"data":{"boards":[{"id":"9920862624","hierarchy_type":"classic",
                      "columns":[{"id":"rt","title":"R.T.","type":"people","settings":null},
                      {"id":"professional","title":"PROFISSIONAL","type":"people","settings":null},
                      {"id":"subitems","title":"Subelementos","type":"subtasks",
                        "settings":{"boardIds":["sub-board"]}}]}]}}
                      """;
            Assert.Contains("\"responsibleColumn\":\"professional\"", body, StringComparison.Ordinal);
            return """
                {"data":{"boards":[{"id":"9920862624","items_page":{"cursor":null,"items":[{
                  "id":"item-1","column_values":[
                    {"id":"rt","persons_and_teams":[{"id":"2","kind":"person"}]},
                    {"id":"professional","persons_and_teams":[{"id":"1","kind":"person"}]}],
                  "subitems":[{"id":"subitem-1","board":{"id":"sub-board"},
                    "column_values":[{"id":"sub-time","running":false,"history":[
                      {"id":"session-1","status":"STOPPED",
                       "started_at":"2026-09-20T12:00:00Z","ended_at":"2026-09-20T13:00:00Z",
                       "started_user_id":"2"}]}]}]}]}}]}}
                """;
        }));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await ((IExternalWorkforceTimeSource)source).FetchAsync(
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), ["1"], TestContext.Current.CancellationToken);

        var record = Assert.Single(result.Records);
        Assert.Equal("1", record.ExternalIdentityId);
        Assert.Equal("subitem-1:sub-time:session-1", record.ExternalKey);
    }

    [Fact]
    public async Task Monday_time_source_prefers_subitem_profissional_over_parent()
    {
        using var client = new HttpClient(new JsonHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("hierarchy_type", StringComparison.Ordinal))
                return body.Contains("sub-board", StringComparison.Ordinal)
                    ? """
                      {"data":{"boards":[{"id":"sub-board","hierarchy_type":"classic",
                      "columns":[{"id":"sub-professional","title":"PROFISSIONAL","type":"people","settings":null}]}]}}
                      """
                    : """
                      {"data":{"boards":[{"id":"9920862624","hierarchy_type":"classic",
                      "columns":[{"id":"professional","title":"PROFISSIONAL","type":"people","settings":null},
                      {"id":"subitems","title":"Subelementos","type":"subtasks",
                        "settings":{"boardIds":["sub-board"]}}]}]}}
                      """;
            if (body.Contains("\"ids\":[\"sub-board\"]", StringComparison.Ordinal))
                return """{"data":{"boards":[{"id":"sub-board","items_page":{"cursor":null,"items":[]}}]}}""";
            return """
                {"data":{"boards":[{"id":"9920862624","items_page":{"cursor":null,"items":[{
                  "id":"item-1","column_values":[
                    {"id":"professional","persons_and_teams":[{"id":"1","kind":"person"}]}],
                  "subitems":[{"id":"subitem-1","board":{"id":"sub-board"},
                    "column_values":[{"id":"sub-professional","persons_and_teams":[{"id":"2","kind":"person"}]},
                      {"id":"sub-time","running":false,"history":[
                        {"id":"session-1","status":"STOPPED",
                         "started_at":"2026-09-20T12:00:00Z","ended_at":"2026-09-20T13:00:00Z",
                         "started_user_id":"1"}]}]}]}]}}]}}
                """;
        }));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await ((IExternalWorkforceTimeSource)source).FetchAsync(
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), ["1", "2"],
            TestContext.Current.CancellationToken);

        Assert.Equal("2", Assert.Single(result.Records).ExternalIdentityId);
    }

    [Theory]
    [InlineData("2026-03-31T12:00:00Z", "2026-03-31T13:00:00Z")]
    [InlineData("2026-09-20T12:00:00Z", "2026-09-20T13:00:00Z")]
    public async Task Monday_time_source_ignores_sessions_with_multiple_professionals(
        string startedAt,
        string endedAt)
    {
        using var client = new HttpClient(new JsonHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("hierarchy_type", StringComparison.Ordinal))
                return """
                    {"data":{"boards":[{"id":"9920862624","hierarchy_type":"multi_level",
                    "columns":[{"id":"professional","title":"PROFISSIONAL","type":"people","settings":null}]}]}}
                    """;
            return """
                {"data":{"boards":[{"id":"9920862624","items_page":{"cursor":null,"items":[{
                  "id":"item-1","column_values":[
                    {"id":"professional","persons_and_teams":[
                      {"id":"1","kind":"person"},{"id":"2","kind":"person"}]},
                    {"id":"time","running":false,"history":[{"id":"session-1","status":"STOPPED",
                      "started_at":"$START","ended_at":"$END"}]}]},
                  {"id":"item-2","column_values":[
                    {"id":"professional","persons_and_teams":[{"id":"1","kind":"person"}]},
                    {"id":"time","running":false,"history":[{"id":"session-2","status":"STOPPED",
                      "started_at":"2026-09-20T12:00:00Z","ended_at":"2026-09-20T13:00:00Z"}]}]}]}}]}}
                """.Replace("$START", startedAt, StringComparison.Ordinal)
                   .Replace("$END", endedAt, StringComparison.Ordinal);
        }));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await ((IExternalWorkforceTimeSource)source).FetchAsync(
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), ["1", "2"],
            TestContext.Current.CancellationToken);

        Assert.Equal("item-2:time:session-2", Assert.Single(result.Records).ExternalKey);
    }

    [Fact]
    public async Task Monday_time_source_filters_classic_subitems_by_their_own_responsible_column()
    {
        using var client = new HttpClient(new JsonHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var subitemBoard = body.Contains("sub-board", StringComparison.Ordinal);
            if (body.Contains("hierarchy_type", StringComparison.Ordinal))
                return subitemBoard
                    ? """
                      {"data":{"boards":[{"id":"sub-board","hierarchy_type":"classic",
                      "columns":[{"id":"sub_owner","title":"Responsável","type":"people","settings":null}]}]}}
                      """
                    : """
                      {"data":{"boards":[{"id":"9920862624","hierarchy_type":"classic",
                      "columns":[{"id":"owner","title":"Responsável","type":"people","settings":null},
                      {"id":"subitems","title":"Subitens","type":"subtasks","settings":{"boardIds":["sub-board"]}}]}]}}
                      """;
            Assert.Contains("person-1", body, StringComparison.Ordinal);
            return subitemBoard
                ? """
                  {"data":{"boards":[{"id":"sub-board","items_page":{"cursor":null,"items":[{
                    "id":"subitem-1","name":"Atividade","url":"https://example.monday.com/subitems/1",
                    "board":{"id":"sub-board"},"column_values":[
                      {"id":"sub_owner","persons_and_teams":[{"id":"1","kind":"person"}]},
                      {"id":"time","running":false,"history":[{"id":"session-1","status":"STOPPED",
                        "started_at":"2026-09-20T12:00:00Z","ended_at":"2026-09-20T13:00:00Z",
                        "started_user_id":"2"}]}]}]}}]}}
                  """
                : """{"data":{"boards":[{"id":"9920862624","items_page":{"cursor":null,"items":[]}}]}}""";
        }));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await ((IExternalWorkforceTimeSource)source).FetchAsync(
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), ["1"], TestContext.Current.CancellationToken);

        var record = Assert.Single(result.Records);
        Assert.Equal("1", record.ExternalIdentityId);
        Assert.Equal("subitem-1:time:session-1", record.ExternalKey);
    }

    [Fact]
    public async Task VrMais_source_reads_employee_and_work_day()
    {
        using var client = new HttpClient(new JsonHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/employees")
            ? """{"employees":[{"id":84,"first_name":"Ana","last_name":"Silva","email":"ana@example.com","active":true}]}"""
            : """{"data":[{"data":[{"date":"2026-09-20","total_time":"08:00","time_cards":[{"csv_value":"08:00"},{"csv_value":"17:00"}]}]}]}"""));
        var source = new VrMaisDirectorySource(client, Options.Create(OptionsValue()));

        var directory = await source.FetchAsync(TestContext.Current.CancellationToken);
        var identity = Assert.Single(directory.Identities);
        var time = await ((IExternalWorkforceTimeSource)source).FetchAsync(
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), [identity.ExternalId],
            TestContext.Current.CancellationToken);

        var record = Assert.Single(time.Records);
        Assert.Equal(28800, record.DurationSeconds);
        Assert.Equal("reported", record.State);
        Assert.Contains("08:00", record.DetailsJson, StringComparison.Ordinal);
    }

    private static WorkforceIntegrationOptions OptionsValue() => new()
    {
        Monday = new MondayDirectoryOptions
        {
            Enabled = true,
            Token = "monday-token",
            BoardId = "9920862624"
        },
        VrMais = new VrMaisDirectoryOptions
        {
            Enabled = true,
            Token = "vr-token"
        }
    };

    private sealed class JsonHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(request), Encoding.UTF8, "application/json")
            });
    }
}
