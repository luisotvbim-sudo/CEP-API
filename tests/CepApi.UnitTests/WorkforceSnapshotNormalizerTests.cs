using CepApi.Application;
using CepApi.Infrastructure.Services;

namespace CepApi.UnitTests;

public sealed class WorkforceSnapshotNormalizerTests
{
    [Fact]
    public void Directory_snapshot_is_normalized_before_persistence()
    {
        var snapshot = new ExternalWorkforceDirectorySnapshot(
            [new ExternalWorkforceIdentitySnapshot(" 42 ", " Ada Lovelace ", " ADA@EXAMPLE.COM ", true)],
            Complete: true);

        var identity = Assert.Single(WorkforceSnapshotNormalizer.NormalizeDirectory(snapshot));

        Assert.Equal("42", identity.ExternalId);
        Assert.Equal("Ada Lovelace", identity.DisplayName);
        Assert.Equal("ada@example.com", identity.Email);
    }

    [Fact]
    public void Directory_snapshot_rejects_duplicate_external_identifiers()
    {
        var snapshot = new ExternalWorkforceDirectorySnapshot(
            [
                new ExternalWorkforceIdentitySnapshot("42", "Ada", null, true),
                new ExternalWorkforceIdentitySnapshot("42", "Grace", null, true)
            ],
            Complete: true);

        var exception = Assert.Throws<ExternalDirectoryException>(
            () => WorkforceSnapshotNormalizer.NormalizeDirectory(snapshot));

        Assert.Equal("directory_duplicate_id", exception.Code);
    }

    [Fact]
    public void Time_snapshot_rejects_records_outside_requested_period()
    {
        var snapshot = CreateTimeSnapshot(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 8, 31),
            detailsJson: "{}");

        var exception = Assert.Throws<ExternalDirectoryException>(
            () => WorkforceSnapshotNormalizer.NormalizeTime(snapshot));

        Assert.Equal("time_snapshot_out_of_range", exception.Code);
    }

    [Fact]
    public void Time_snapshot_rejects_invalid_details_json()
    {
        var snapshot = CreateTimeSnapshot(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 10),
            detailsJson: "{invalid}");

        var exception = Assert.Throws<ExternalDirectoryException>(
            () => WorkforceSnapshotNormalizer.NormalizeTime(snapshot));

        Assert.Equal("time_snapshot_invalid_details", exception.Code);
    }

    private static ExternalWorkforceTimeSnapshot CreateTimeSnapshot(
        DateOnly from,
        DateOnly to,
        DateOnly workDate,
        string detailsJson)
        => new(
            from,
            to,
            [new ExternalWorkforceTimeRecordSnapshot(
                "employee-42",
                "record-1",
                workDate,
                null,
                null,
                3600,
                " approved ",
                "Work item",
                "https://example.com/item/1",
                detailsJson)],
            Complete: true);
}
