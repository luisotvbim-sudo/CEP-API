namespace CepApi.Domain;

public enum NotificationScheduleKind { PreviousDay, Lunch, EndOfDay }
public enum NotificationDispatchStatus { Pending, Completed, Failed }

public sealed class TimeControlSettings
{
    public int Id { get; set; } = 1;
    public int ToleranceMinutes { get; set; } = 30;
    public bool AutomaticEnabled { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class TimeNotificationSchedule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public TimeOnly LocalTime { get; set; }
    public required string Message { get; set; }
    public NotificationScheduleKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsDeleted { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

// Durable work item. No source calls occur inside HTTP transactions.
public sealed class TimeNotificationDispatch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ScheduleId { get; set; }
    public required string DeduplicationKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Message { get; set; }
    public AnalysisPeriod Period { get; set; }
    public NotificationScheduleKind? Kind { get; set; }
    public bool ReportOnly { get; set; }
    public int ToleranceMinutes { get; set; }
    public Guid SettingsVersion { get; set; }
    public NotificationDispatchStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int RecipientCount { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class TimeAnalysisReport
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Guid DispatchId { get; set; }
    public Guid WorkforcePersonId { get; set; }
    public Guid UserId { get; set; }
    public required string DisplayName { get; set; }
    public AnalysisPeriod Period { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string AnalysisJson { get; set; }
}

public sealed class TimeNotification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid ReportId { get; set; }
    public TimeAnalysisReport Report { get; set; } = null!;
    public required string Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
