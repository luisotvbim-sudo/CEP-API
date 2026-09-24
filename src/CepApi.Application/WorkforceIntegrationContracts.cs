using System.ComponentModel.DataAnnotations;
using CepApi.Domain;

namespace CepApi.Application;

public sealed record ExternalWorkforceIdentityResponse(
    Guid Id,
    ExternalWorkforceSource Source,
    string ExternalId,
    string DisplayName,
    string? Email,
    bool IsActive,
    DateTimeOffset LastSeenAt,
    Guid? WorkforcePersonId);

public sealed record InviteWorkforcePersonRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MaxLength(200)] string DisplayName,
    Guid MondayIdentityId,
    Guid VrMaisIdentityId);

public sealed record WorkforcePersonResponse(
    Guid Id,
    Guid? UserId,
    string DisplayName,
    string Email,
    ExternalWorkforceIdentityResponse Monday,
    ExternalWorkforceIdentityResponse VrMais,
    Guid? InvitationId,
    DateTimeOffset? InvitationExpiresAt,
    DateTimeOffset? InvitationAcceptedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record InviteWorkforcePersonResponse(
    WorkforcePersonResponse Person,
    InvitationResponse Invitation);

public sealed record WorkforceSyncSourceResponse(
    ExternalWorkforceSource Source,
    WorkforceSyncStatus Status,
    int ReceivedCount,
    int CreatedCount,
    int UpdatedCount,
    int DeactivatedCount,
    int TimeRecordReceivedCount,
    int TimeRecordCreatedCount,
    int TimeRecordUpdatedCount,
    int TimeRecordRemovedCount,
    bool CompleteSnapshot,
    DateOnly? CoverageFrom,
    DateOnly? CoverageTo,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record WorkforceSyncResponse(
    Guid Id,
    WorkforceSyncStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyCollection<WorkforceSyncSourceResponse> Sources);

public sealed record ExternalWorkforceIdentitySnapshot(
    string ExternalId,
    string DisplayName,
    string? Email,
    bool IsActive,
    DateTimeOffset? SourceUpdatedAt = null);

public sealed record ExternalWorkforceDirectorySnapshot(
    IReadOnlyCollection<ExternalWorkforceIdentitySnapshot> Identities,
    bool Complete);

public sealed record ExternalWorkforceTimeRecordSnapshot(
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

public sealed record ExternalWorkforceTimeSnapshot(
    DateOnly From,
    DateOnly To,
    IReadOnlyCollection<ExternalWorkforceTimeRecordSnapshot> Records,
    bool Complete);

public sealed record WorkforceTimeRecordResponse(
    Guid Id,
    ExternalWorkforceSource Source,
    string ExternalKey,
    DateOnly WorkDate,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int? DurationSeconds,
    string State,
    string? Title,
    string? Url,
    string? DetailsJson,
    DateTimeOffset LastSyncedAt);

public sealed record WorkforcePersonHistoryResponse(
    Guid WorkforcePersonId,
    Guid? UserId,
    string DisplayName,
    string Email,
    IReadOnlyCollection<WorkforceTimeRecordResponse> Records);

public sealed record WorkforceAdminHistoryResponse(
    DateOnly From,
    DateOnly To,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<WorkforcePersonHistoryResponse> People);

public interface IExternalWorkforceDirectorySource
{
    ExternalWorkforceSource Source { get; }
    Task<ExternalWorkforceDirectorySnapshot> FetchAsync(CancellationToken cancellationToken);
}

public interface IExternalWorkforceTimeSource
{
    ExternalWorkforceSource Source { get; }
    Task<ExternalWorkforceTimeSnapshot> FetchAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string> activeExternalIdentityIds,
        CancellationToken cancellationToken);
}

public interface IWorkforceDirectorySyncService
{
    Task<WorkforceSyncBatch> SynchronizeAsync(
        Guid organizationId,
        Guid requestedByUserId,
        bool fullRefresh,
        IReadOnlyCollection<Guid>? visibleUserIds,
        CancellationToken cancellationToken);
}

public sealed class ExternalDirectoryException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
