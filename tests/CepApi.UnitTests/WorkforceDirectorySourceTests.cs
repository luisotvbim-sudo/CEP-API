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
    public async Task Monday_directory_keeps_only_active_board_subscribers()
    {
        using var client = new HttpClient(new JsonHandler(_ => """
            {"data":{"boards":[{"id":"9920862624","subscribers":[{"id":"1"}]}],
            "users":[{"id":"1","name":"Ana","email":"ana@example.com"},{"id":"2","name":"Bia","email":"bia@example.com"}]}}
            """));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await source.FetchAsync(TestContext.Current.CancellationToken);

        var identity = Assert.Single(result.Identities);
        Assert.Equal("1", identity.ExternalId);
        Assert.Equal("Ana", identity.DisplayName);
        Assert.True(result.Complete);
    }

    [Fact]
    public async Task Monday_time_source_normalizes_a_closed_session()
    {
        using var client = new HttpClient(new JsonHandler(_ => """
            {"data":{"boards":[{"id":"9920862624","items_page":{"cursor":null,"items":[{
              "id":"item-1","name":"Projeto","url":"https://example.monday.com/boards/1","board":{"id":"9920862624"},
              "column_values":[{"id":"time","running":false,"started_at":null,"history":[{
                "id":"session-1","status":"STOPPED","started_at":"2026-09-20T12:00:00Z","ended_at":"2026-09-20T13:00:00Z",
                "started_user_id":"1","manually_entered_start_date":false,"manually_entered_start_time":false,
                "manually_entered_end_date":false,"manually_entered_end_time":false}]}],"subitems":[]}]}}]}}
            """));
        var source = new MondayDirectorySource(client, Options.Create(OptionsValue()));

        var result = await ((IExternalWorkforceTimeSource)source).FetchAsync(
            new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20), ["1"], TestContext.Current.CancellationToken);

        var record = Assert.Single(result.Records);
        Assert.Equal("item-1:time:session-1", record.ExternalKey);
        Assert.Equal(3600, record.DurationSeconds);
        Assert.Equal("closed", record.State);
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
