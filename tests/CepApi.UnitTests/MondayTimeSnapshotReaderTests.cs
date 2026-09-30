using System.Text.Json;
using CepApi.Infrastructure.Services;

namespace CepApi.UnitTests;

public sealed class MondayTimeSnapshotReaderTests
{
    private static readonly DateOnly Day = new(2026, 9, 20);
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, "2026-09-20T12:00:00Z", 1)]
    [InlineData(true, null, 0)]
    [InlineData(false, "2026-09-20T12:00:00Z", 0)]
    public void Multiple_open_sessions_only_include_the_confirmed_running_entry(bool running, string? columnStart, int expected)
    {
        var reader = new MondayTimeSnapshotReader(Day, Day, ["42"], true, Now);
        using var item = Item(running, columnStart, """
            [{"id":"first","started_at":"2026-09-20T12:00:00Z"},
             {"id":"second","started_at":"2026-09-20T13:00:00Z"},
             {"id":"deleted","status":"DELETED","started_at":"2026-09-20T12:00:00Z"}]
            """);
        reader.Read(item.RootElement, "owner");
        var snapshot = reader.Snapshot();
        Assert.True(snapshot.Complete);
        Assert.Equal(expected, snapshot.Records.Count);
        if (expected == 1)
        {
            var record = Assert.Single(snapshot.Records);
            Assert.Equal("item:time:first", record.ExternalKey);
            Assert.Equal(10800, record.DurationSeconds);
            Assert.Equal("running", record.State);
        }
    }

    [Fact]
    public void A_single_open_session_is_accepted_without_a_column_start_and_deleted_entries_do_not_count()
    {
        var reader = new MondayTimeSnapshotReader(Day, Day, ["42"], true, Now);
        using var item = Item(true, null, """
            [{"id":"only","started_at":"2026-09-20T12:00:00Z","manually_entered_start_time":true,"started_user_id":"other"},
             {"id":"deleted","status":"DELETED","started_at":"2026-09-20T13:00:00Z"}]
            """);
        reader.Read(item.RootElement, "owner");
        var record = Assert.Single(reader.Snapshot().Records);
        Assert.Equal("42", record.ExternalIdentityId);
        using var details = JsonDocument.Parse(record.DetailsJson!);
        Assert.True(details.RootElement.GetProperty("manual").GetBoolean());
        Assert.Equal("other", details.RootElement.GetProperty("startedByUserId").GetString());
    }

    [Fact]
    public void Repeated_pages_replace_the_same_session_without_retaining_disposed_json()
    {
        var reader = new MondayTimeSnapshotReader(Day, Day, ["42"], true, Now);
        using (var first = Item(true, null, """[{"id":"same","started_at":"2026-09-20T12:00:00Z"}]"""))
            reader.Read(first.RootElement, "owner");
        using (var next = Item(false, null, """[{"id":"same","started_at":"2026-09-20T12:00:00Z","ended_at":"2026-09-20T13:00:00Z"}]"""))
            reader.Read(next.RootElement, "owner");
        var record = Assert.Single(reader.Snapshot().Records);
        Assert.Equal(3600, record.DurationSeconds);
        Assert.Equal("closed", record.State);
    }

    private static JsonDocument Item(bool running, string? columnStart, string entries)
        => JsonDocument.Parse($$"""
            {"id":"item","column_values":[
              {"id":"owner","persons_and_teams":[{"id":"42","kind":"person"}]},
              {"id":"time","running":{{JsonSerializer.Serialize(running)}},"started_at":{{JsonSerializer.Serialize(columnStart)}},"history":{{entries}}}
            ]}
            """);
}
