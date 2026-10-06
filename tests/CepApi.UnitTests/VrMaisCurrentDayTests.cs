using System.Net;
using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace CepApi.UnitTests;

public sealed class VrMaisCurrentDayTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.Parse("2026-10-06T11:00:00-03:00");

    [Theory]
    [InlineData("08:00,09:00", 3600)]
    [InlineData("08:00,09:00,10:00", 7200)]
    public async Task Current_punches_are_read_from_time_cards_and_calculated_at_cutoff(string times, long expectedSeconds)
    {
        var seen = new List<string>();
        using var client = new HttpClient(new ReportHandler(async request =>
        {
            seen.Add(request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var report = body.RootElement.GetProperty("report");
            Assert.Equal(42, report.GetProperty("employee_id").GetInt64());
            if (request.RequestUri.AbsolutePath.EndsWith("/time_cards"))
            {
                Assert.Equal("2026-10-06", report.GetProperty("start_date").GetString());
                Assert.Equal("2026-10-06", report.GetProperty("end_date").GetString());
                return TimeCards(times.Split(','));
            }
            return """{"data":[{"data":[]}]}""";
        }));
        var snapshot = await Source(client).FetchAsync(Today, Today, ["42"], TestContext.Current.CancellationToken);

        Assert.Equal(2, seen.Count);
        Assert.Contains(seen, path => path.EndsWith("/work_days"));
        Assert.Contains(seen, path => path.EndsWith("/time_cards"));
        var record = Assert.Single(snapshot.Records);
        Assert.Equal("unrecognized", record.State);
        Assert.Null(record.DurationSeconds);
        Assert.Equal(expectedSeconds, Analyze(record).VrSeconds);
    }

    [Fact]
    public async Task Closed_day_preserves_official_total_and_current_day_uses_raw_punches()
    {
        using var client = new HttpClient(new ReportHandler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/time_cards")
                ? TimeCards(["08:00", "09:00"])
                : """{"data":[{"data":[{"date":"2026-10-05","total_time":"07:30","time_cards":[{"csv_value":"08:00"},{"csv_value":"17:00"}]}]}]}""")));
        var snapshot = await Source(client).FetchAsync(Today.AddDays(-1), Today, ["42"], TestContext.Current.CancellationToken);

        var past = Assert.Single(snapshot.Records, x => x.WorkDate == Today.AddDays(-1));
        Assert.Equal(27000, past.DurationSeconds);
        Assert.Equal("reported", past.State);
        var current = Assert.Single(snapshot.Records, x => x.WorkDate == Today);
        Assert.Null(current.DurationSeconds);
        Assert.Equal(3600, Analyze(current).VrSeconds);
    }

    [Fact]
    public async Task Empty_time_card_report_keeps_current_day_unknown()
    {
        using var client = new HttpClient(new ReportHandler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/time_cards")
                ? """{"data":[]}"""
                : """{"data":[{"data":[]}]}""")));
        var snapshot = await Source(client).FetchAsync(Today, Today, ["42"], TestContext.Current.CancellationToken);
        var record = Assert.Single(snapshot.Records);
        Assert.Equal("missing", record.State);
        Assert.Null(Analyze(record).VrSeconds);
    }

    [Fact]
    public async Task Existing_current_work_day_is_preserved_without_fallback_request()
    {
        var calls = 0;
        using var client = new HttpClient(new ReportHandler(_ =>
        {
            calls++;
            return Task.FromResult("""{"data":[{"data":[{"date":"2026-10-06","total_time":"08:00","time_cards":[{"csv_value":"08:00"},{"csv_value":"09:00"}]}]}]}""");
        }));
        var snapshot = await Source(client).FetchAsync(Today, Today, ["42"], TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        var record = Assert.Single(snapshot.Records);
        Assert.Equal("reported", record.State);
        Assert.Equal(28800, record.DurationSeconds);
        Assert.Equal(3600, Analyze(record).VrSeconds);
    }

    [Fact]
    public async Task Failed_current_fallback_preserves_past_records_and_marks_snapshot_incomplete()
    {
        using var client = new HttpClient(new ReportHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/time_cards")
                ? Task.FromException<string>(new HttpRequestException("fixture failure"))
                : Task.FromResult("""{"data":[{"data":[{"date":"2026-10-05","total_time":"07:30","time_cards":[{"csv_value":"08:00"},{"csv_value":"17:00"}]}]}]}""")));
        var snapshot = await Source(client).FetchAsync(Today.AddDays(-1), Today, ["42"], TestContext.Current.CancellationToken);

        Assert.False(snapshot.Complete);
        Assert.Equal(27000, Assert.Single(snapshot.Records, x => x.WorkDate == Today.AddDays(-1)).DurationSeconds);
        Assert.DoesNotContain(snapshot.Records, x => x.WorkDate == Today);
    }

    [Theory]
    [InlineData("Saída,Entrada")]
    [InlineData("Entrada,Entrada")]
    [InlineData("Indefinida,Saída")]
    [InlineData("2ª Entrada,1ª Saída")]
    public async Task Invalid_or_unknown_direction_is_not_inferred(string directions)
    {
        using var client = new HttpClient(new ReportHandler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/time_cards")
                ? TimeCards(["08:00", "09:00"], directions.Split(','))
                : """{"data":[{"data":[]}]}""")));
        var snapshot = await Source(client).FetchAsync(Today, Today, ["42"], TestContext.Current.CancellationToken);
        Assert.False(snapshot.Complete);
        Assert.Empty(snapshot.Records);
    }

    private static IExternalWorkforceTimeSource Source(HttpClient client) => new VrMaisDirectorySource(client,
        Options.Create(new WorkforceIntegrationOptions { VrMais = new() { Enabled = true, Token = "fixture-token" } }),
        new FixedClock());

    private static TimeAnalysisDay Analyze(ExternalWorkforceTimeRecordSnapshot record)
    {
        var row = new WorkforceTimeRecord
        {
            Source = ExternalWorkforceSource.VrMais,
            ExternalKey = record.ExternalKey,
            WorkDate = record.WorkDate,
            State = record.State,
            DurationSeconds = record.DurationSeconds,
            DetailsJson = record.DetailsJson
        };
        return Assert.Single(TimeAnalysisEngine.Analyze(Today, Today, Cutoff, 30, [row], true).Days);
    }

    private static string TimeCards(string[] times, string[]? directions = null)
    {
        directions ??= times.Select((_, i) => $"{i / 2 + 1}ª {(i % 2 == 0 ? "Entrada" : "Saída")}").ToArray();
        var rows = times.Select((time, i) => new { date = "Ter, 06/10/2026", time, time_card_index = directions[i] });
        return JsonSerializer.Serialize(new { data = new object[] { new object[] { new { data = rows } } } });
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Cutoff;
    }

    private sealed class ReportHandler(Func<HttpRequestMessage, Task<string>> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => new(HttpStatusCode.OK) { Content = new StringContent(await response(request)) };
    }
}
