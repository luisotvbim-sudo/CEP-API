using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>().DispatchOneAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Email outbox unavailable ({ErrorType}); retrying.", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
