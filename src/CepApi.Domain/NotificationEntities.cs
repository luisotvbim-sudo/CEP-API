namespace CepApi.Domain;

public static class AutomaticNotificationDefaults
{
    public const string TimeZoneId = "America/Sao_Paulo";
    public static readonly TimeOnly LocalTime = new(11, 50);
}

public sealed class AutomaticNotificationSchedule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public TimeOnly LocalTime { get; set; }
    public required string TimeZoneId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class AutomaticNotificationExecution
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Guid ScheduleId { get; set; }
    public AutomaticNotificationSchedule Schedule { get; set; } = null!;
    public DateOnly LocalDate { get; set; }
    public DateTimeOffset ScheduledFor { get; set; }
    public AutomaticNotificationExecutionStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LeaseExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int CreatedNotificationCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class TimeControlNotification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Guid RecipientUserId { get; set; }
    public Guid? ExecutionId { get; set; }
    public AutomaticNotificationExecution? Execution { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? DataJson { get; set; }
    public required string DedupeKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
