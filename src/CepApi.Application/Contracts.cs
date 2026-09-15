using CepApi.Domain;
using System.ComponentModel.DataAnnotations;

namespace CepApi.Application;

public sealed record ClientInfo(string Type = "unknown", string? Version = null, string? InstallationId = null);
public sealed record LoginRequest([Required, EmailAddress, MaxLength(320)] string Email, [Required, MaxLength(200)] string Password, ClientInfo? Client);
public sealed record RefreshRequest([Required, MaxLength(1000)] string RefreshToken);
public sealed record LogoutRequest([Required, MaxLength(1000)] string RefreshToken);
public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, UserResponse User);
public sealed record AcceptInvitationRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string DisplayName,
    [Required, MinLength(12), MaxLength(200)] string Password,
    ClientInfo? Client);
public sealed record ForgotPasswordRequest([Required, EmailAddress, MaxLength(320)] string Email);
public sealed record ResetPasswordRequest([Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MaxLength(50)] string Code, [Required, MinLength(12), MaxLength(200)] string NewPassword);
public sealed record ChangePasswordRequest([Required, MaxLength(200)] string CurrentPassword,
    [Required, MinLength(12), MaxLength(200)] string NewPassword);
public sealed record UpdateProfileRequest([Required, MaxLength(200)] string DisplayName);
public sealed record CreatePluginGrantRequest(Product Product, [Required, MaxLength(50)] string PluginVersion,
    [Required, MaxLength(200)] string InstallationId);
public sealed record PluginGrantResponse(string GrantToken, DateTimeOffset ExpiresAt);
public sealed record PluginTelemetryEventRequest(
    Guid EventId,
    [Required, MaxLength(100)] string Command,
    DateTimeOffset OccurredAt,
    int? DurationMs,
    PluginUsageOutcome Outcome,
    [MaxLength(100)] string? ErrorCode);
public sealed record CreatePluginTelemetryBatchRequest(
    Product Product,
    [Required, MaxLength(50)] string PluginVersion,
    [Required, MaxLength(50)] string HostVersion,
    [Required, MaxLength(200)] string InstallationId,
    [Required, MinLength(1), MaxLength(PluginTelemetryRules.MaxBatchSize)] IReadOnlyCollection<PluginTelemetryEventRequest> Events);
public sealed record PluginTelemetryIngestionResponse(int Received, int Accepted, int Duplicates);
public sealed record PluginCommandTelemetrySummary(
    string Command,
    long Uses,
    int UniqueUsers,
    long Succeeded,
    long Failed,
    long Cancelled,
    double? AverageDurationMs);
public sealed record PluginTelemetrySummaryResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    long TotalEvents,
    int UniqueUsers,
    long Succeeded,
    long Failed,
    long Cancelled,
    double? AverageDurationMs,
    IReadOnlyCollection<PluginCommandTelemetrySummary> Commands);

public sealed record UserResponse(Guid Id, string DisplayName, string Email, Guid? OrganizationId, UserRole Role, UserStatus Status, IReadOnlyCollection<Product> Products);
public sealed record SessionResponse(Guid Id, string ClientType, string? ClientVersion, string? InstallationId, string? IpAddress, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? LastUsedAt);

public sealed record CreateOrganizationRequest([Required, MaxLength(200)] string Name,
    [Required, MaxLength(100)] string Slug,
    [Required, EmailAddress, MaxLength(320)] string InitialAdminEmail, IReadOnlyCollection<Product>? Products);
public sealed record ChangeOrganizationStatusRequest(OrganizationStatus Status);
public sealed record OrganizationResponse(Guid Id, string Name, string Slug, OrganizationStatus Status, DateTimeOffset CreatedAt);
public sealed record InviteUserRequest([Required, EmailAddress, MaxLength(320)] string Email, UserRole Role, IReadOnlyCollection<Product>? Products);
public sealed record UpdateUserRequest([MaxLength(200)] string? DisplayName, UserRole Role, UserStatus Status, IReadOnlyCollection<Product>? Products);
public sealed record InvitationResponse(Guid Id, string Email, UserRole Role, bool CanUseRevit, bool CanUseZwcad, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);
public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, long Total);
