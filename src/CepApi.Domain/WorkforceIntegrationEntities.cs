namespace CepApi.Domain;

public sealed class ExternalWorkforceIdentity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public ExternalWorkforceSource Source { get; set; }
    public required string ExternalId { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? SourceUpdatedAt { get; set; }
}

public sealed class WorkforcePerson
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public Guid? UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public Guid MondayIdentityId { get; set; }
    public ExternalWorkforceIdentity MondayIdentity { get; set; } = null!;
    public Guid VrMaisIdentityId { get; set; }
    public ExternalWorkforceIdentity VrMaisIdentity { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}

public sealed class WorkforceSyncBatch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public Guid RequestedByUserId { get; set; }
    public WorkforceSyncStatus Status { get; set; } = WorkforceSyncStatus.Running;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public ICollection<WorkforceSyncSourceRun> Sources { get; set; } = [];
}

public sealed class WorkforceSyncSourceRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BatchId { get; set; }
    public WorkforceSyncBatch Batch { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public ExternalWorkforceSource Source { get; set; }
    public WorkforceSyncStatus Status { get; set; } = WorkforceSyncStatus.Running;
    public int ReceivedCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int DeactivatedCount { get; set; }
    public int TimeRecordReceivedCount { get; set; }
    public int TimeRecordCreatedCount { get; set; }
    public int TimeRecordUpdatedCount { get; set; }
    public int TimeRecordRemovedCount { get; set; }
    public bool CompleteSnapshot { get; set; }
    public DateOnly? CoverageFrom { get; set; }
    public DateOnly? CoverageTo { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class WorkforceTimeRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public ExternalWorkforceSource Source { get; set; }
    public Guid ExternalIdentityId { get; set; }
    public ExternalWorkforceIdentity ExternalIdentity { get; set; } = null!;
    public required string ExternalKey { get; set; }
    public DateOnly WorkDate { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public required string State { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? DetailsJson { get; set; }
    public bool IsRemoved { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
