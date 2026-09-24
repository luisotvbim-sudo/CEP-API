using System.Text.Json;
using CepApi.Application;

namespace CepApi.Infrastructure.Services;

internal static class WorkforceSnapshotNormalizer
{
    private const int MaximumDirectorySize = 10_000;
    private const int MaximumTimeRecords = 500_000;
    private const int MaximumDetailsLength = 20_000;

    public static IReadOnlyCollection<NormalizedWorkforceIdentity> NormalizeDirectory(
        ExternalWorkforceDirectorySnapshot snapshot)
    {
        if (snapshot.Identities.Count > MaximumDirectorySize)
            throw new ExternalDirectoryException(
                "directory_too_large", "The external directory exceeded the supported limit.");

        var identities = snapshot.Identities.Select(item => new NormalizedWorkforceIdentity(
            Truncate(item.ExternalId.Trim(), 200),
            Truncate(item.DisplayName.Trim(), 200),
            NormalizeEmail(item.Email),
            item.IsActive,
            item.SourceUpdatedAt)).ToArray();

        if (identities.Any(x => x.ExternalId.Length == 0 || x.DisplayName.Length == 0))
            throw new ExternalDirectoryException(
                "directory_invalid_identity", "The external directory returned an invalid identity.");
        if (HasDuplicates(identities.Select(x => x.ExternalId)))
            throw new ExternalDirectoryException(
                "directory_duplicate_id", "The external directory returned duplicate identifiers.");

        return identities;
    }

    public static IReadOnlyCollection<NormalizedWorkforceTimeRecord> NormalizeTime(
        ExternalWorkforceTimeSnapshot snapshot)
    {
        if (snapshot.To < snapshot.From || snapshot.Records.Count > MaximumTimeRecords)
            throw new ExternalDirectoryException(
                "time_snapshot_invalid", "The external source returned an invalid time snapshot.");

        var records = snapshot.Records.Select(NormalizeTimeRecord).ToArray();
        if (HasDuplicates(records.Select(x => x.ExternalKey)))
            throw new ExternalDirectoryException(
                "time_snapshot_duplicate", "The external source returned duplicate time records.");
        if (records.Any(x => x.WorkDate < snapshot.From || x.WorkDate > snapshot.To))
            throw new ExternalDirectoryException(
                "time_snapshot_out_of_range", "The external source returned time records outside the requested period.");

        return records;
    }

    public static string[] NormalizeExternalIds(IEnumerable<string> externalIds)
        => externalIds.Select(x => Truncate(x.Trim(), 200)).Distinct(StringComparer.Ordinal).ToArray();

    private static NormalizedWorkforceTimeRecord NormalizeTimeRecord(
        ExternalWorkforceTimeRecordSnapshot item)
    {
        var externalIdentityId = Truncate(item.ExternalIdentityId.Trim(), 200);
        var externalKey = Truncate(item.ExternalKey.Trim(), 500);
        var state = Truncate(item.State.Trim().ToLowerInvariant(), 32);
        if (externalIdentityId.Length == 0 || externalKey.Length == 0 || state.Length == 0 ||
            item.DurationSeconds < 0)
            throw new ExternalDirectoryException(
                "time_snapshot_invalid_record", "The external source returned an invalid time record.");

        ValidateDetails(item.DetailsJson);
        return new NormalizedWorkforceTimeRecord(
            externalIdentityId,
            externalKey,
            item.WorkDate,
            item.StartedAt,
            item.EndedAt,
            item.DurationSeconds,
            state,
            TruncateNullable(item.Title, 500),
            NormalizeUrl(item.Url),
            item.DetailsJson);
    }

    private static void ValidateDetails(string? details)
    {
        if (details is { Length: > MaximumDetailsLength })
            throw new ExternalDirectoryException(
                "time_snapshot_details_too_large", "The external source returned oversized time record details.");
        if (details is null) return;

        try
        {
            using var _ = JsonDocument.Parse(details);
        }
        catch (JsonException exception)
        {
            throw new ExternalDirectoryException(
                "time_snapshot_invalid_details", "The external source returned invalid time record details.", exception);
        }
    }

    private static bool HasDuplicates(IEnumerable<string> values)
        => values.GroupBy(x => x, StringComparer.Ordinal).Any(group => group.Skip(1).Any());

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim().ToLowerInvariant(), 320);

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return uri.ToString();
    }

    private static string Truncate(string value, int maxLength)
        => value[..Math.Min(value.Length, maxLength)];

    private static string? TruncateNullable(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);
}

internal sealed record NormalizedWorkforceIdentity(
    string ExternalId,
    string DisplayName,
    string? Email,
    bool IsActive,
    DateTimeOffset? SourceUpdatedAt);

internal sealed record NormalizedWorkforceTimeRecord(
    string ExternalIdentityId,
    string ExternalKey,
    DateOnly WorkDate,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int? DurationSeconds,
    string State,
    string? Title,
    string? Url,
    string? DetailsJson);
