using CepApi.Domain;

namespace CepApi.Application;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IEmailSender
{
    Task SendInvitationAsync(string email, string organizationName, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task SendPasswordResetAsync(string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

public interface ISecurityCodeService
{
    string GenerateInvitationCode();
    string GeneratePasswordResetCode();
    string Hash(string value);
    bool Verify(string value, string expectedHash);
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);
public sealed record PluginGrantResult(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(TokenUser user, DateTimeOffset now);
    PluginGrantResult CreatePluginGrant(TokenUser user, Product product, DateTimeOffset now);
    string CreateRefreshToken();
    string HashRefreshToken(string token);
}

public sealed record TokenUser(Guid Id, string DisplayName, string Email, Guid? OrganizationId, UserRole Role);

public interface IAuditService
{
    Task WriteAsync(string action, Guid? organizationId = null, Guid? actorUserId = null,
        Guid? targetUserId = null, object? details = null, string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
