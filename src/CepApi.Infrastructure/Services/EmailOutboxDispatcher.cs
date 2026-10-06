using System.Text.Json;
using CepApi.Application;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CepApi.Infrastructure.Services;

public sealed class EmailOutboxDispatcher(AppDbContext db, IDataProtectionProvider protection,
    IEmailSender sender, IClock clock, ILogger<EmailOutboxDispatcher> logger)
{
    // SKIP LOCKED allows multiple API instances without delivering the same row concurrently.
    // Delivery is at-least-once: a process crash after SMTP acceptance can resend the same code.
    public async Task<bool> DispatchOneAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.UtcNow;
        var messages = await db.EmailOutbox.FromSqlInterpolated($"""
            SELECT * FROM email_outbox WHERE "NextAttemptAt" <= {now}
            ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var message = messages.SingleOrDefault();
        if (message is null) return false;

        if (message.ExpiresAt <= now)
        {
            db.EmailOutbox.Remove(message);
            logger.LogWarning("Expired email outbox message {MessageId} was discarded.", message.Id);
        }
        else
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<EmailEnvelope>(
                    protection.CreateProtector(EmailOutbox.Purpose).Unprotect(message.ProtectedPayload))!;
                if (envelope.OrganizationName is { } organization)
                    await sender.SendInvitationAsync(envelope.Email, organization, envelope.Code, envelope.ExpiresAt, cancellationToken);
                else
                    await sender.SendPasswordResetAsync(envelope.Email, envelope.Code, envelope.ExpiresAt, cancellationToken);
                db.EmailOutbox.Remove(message);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                message.Attempts++;
                message.NextAttemptAt = now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(message.Attempts, 6))));
                // Avoid SMTP exception details containing recipients or message content.
                logger.LogWarning("Email outbox {MessageId} attempt {Attempt} failed ({ErrorType}); retry scheduled.",
                    message.Id, message.Attempts, exception.GetType().Name);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

