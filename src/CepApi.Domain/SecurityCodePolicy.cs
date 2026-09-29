namespace CepApi.Domain;

public static class SecurityCodePolicy
{
    public const int MaximumAttempts = 5;
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(48);
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(15);
}
