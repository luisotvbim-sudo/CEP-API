using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace CepApi.Infrastructure.Services;

internal sealed record EmailEnvelope(string Email, string? OrganizationName, string Code, DateTimeOffset ExpiresAt);

public sealed class EmailOutbox(AppDbContext db, IDataProtectionProvider protection, IClock clock) : IEmailQueue
{
    internal const string Purpose = "CepApi.EmailOutbox.v1";

    public void Invitation(string email, string organizationName, string code, DateTimeOffset expiresAt)
        => Enqueue(new EmailEnvelope(email, organizationName, code, expiresAt));

    public void PasswordReset(string email, string code, DateTimeOffset expiresAt)
        => Enqueue(new EmailEnvelope(email, null, code, expiresAt));

    private void Enqueue(EmailEnvelope envelope)
    {
        var now = clock.UtcNow;
        db.EmailOutbox.Add(new EmailOutboxMessage
        {
            ProtectedPayload = protection.CreateProtector(Purpose).Protect(JsonSerializer.Serialize(envelope)),
            CreatedAt = now,
            NextAttemptAt = now,
            ExpiresAt = envelope.ExpiresAt
        });
    }
}
