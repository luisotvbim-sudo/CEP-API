using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CepApi.Domain;

namespace CepApi.Application;

public sealed record TimeSettingsResponse(int ToleranceMinutes, string TimeZoneId, bool AutomaticEnabled, Guid Version, DateTimeOffset UpdatedAt, Guid? UpdatedByUserId);
public sealed record UpdateTimeSettingsRequest(Guid Version, [Range(0, 1440)] int ToleranceMinutes, bool AutomaticEnabled);
public sealed record TimeScheduleRequest(TimeOnly LocalTime, [Required, MaxLength(2000)] string Message, NotificationScheduleKind Kind, bool IsEnabled, Guid? Version = null);
public sealed record TimeScheduleResponse(Guid Id, TimeOnly LocalTime, string Message, NotificationScheduleKind Kind, bool IsEnabled, Guid Version);
public sealed record SendTimeNotificationRequest(Guid RequestId, Guid? UserId, [Required, MaxLength(2000)] string Message, AnalysisPeriod Period);
public sealed record ReceiveTimeNotificationsRequest([Required, MinLength(1), MaxLength(100)] Guid[] Ids);
public sealed record TimeDispatchResponse(Guid Id, NotificationDispatchStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, int RecipientCount, string? ErrorCode, AnalysisPeriod Period, string Message, Guid? UserId);
public sealed record TimeNotificationResponse(Guid Id, string Message, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, DateTimeOffset? DeliveredAt, TimeAnalysisResponse Analysis);
public sealed record TimeReportResponse(Guid Id, Guid WorkforcePersonId, Guid UserId, string DisplayName, DateTimeOffset CreatedAt, TimeAnalysisResponse Analysis);
public sealed record TimeDispatchPreviewResponse(DateOnly From, DateOnly To, DateTimeOffset Cutoff, int RecipientCount);
public sealed record TimeAnalysisSourceResponse(ExternalWorkforceSource Source, string Status, string? ErrorCode, DateTimeOffset ObservedAt);
public sealed record TimeAnalysisResponse(DateOnly From, DateOnly To, DateTimeOffset Cutoff, int ToleranceMinutes,
    IReadOnlyList<TimeAnalysisDay> Days, long? VrSeconds, long? MondaySeconds, long? DeltaSeconds, long? AbsoluteDivergenceSeconds, bool HasIssues,
    Guid SettingsVersion, IReadOnlyList<TimeAnalysisSourceResponse> Sources);
