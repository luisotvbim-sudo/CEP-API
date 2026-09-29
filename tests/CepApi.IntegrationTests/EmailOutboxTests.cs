using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CepApi.IntegrationTests;

public sealed class EmailOutboxTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rolling_back_the_business_transaction_also_rolls_back_its_email()
    {
        var email = $"rollback-{Guid.NewGuid():N}@example.test";
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IEmailQueue>()
                .Invitation(email, "Test", "test-code", DateTimeOffset.UtcNow.AddHours(1));
            await db.SaveChangesAsync(Ct);
            await transaction.RollbackAsync(Ct);
        }
        await fixture.DispatchAsync();
        Assert.False(fixture.Email.InvitationCodes.ContainsKey(email));
        await using var check = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutbox.AnyAsync(Ct));
    }

    [Fact]
    public async Task Expired_messages_are_discarded_without_attempting_delivery()
    {
        var email = $"expired-{Guid.NewGuid():N}@example.test";
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IEmailQueue>()
                .Invitation(email, "Test", "expired-code", DateTimeOffset.UtcNow.AddMinutes(-1));
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(Ct);
        }
        await fixture.DispatchAsync();
        Assert.False(fixture.Email.InvitationCodes.ContainsKey(email));
        await using var check = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutbox.AnyAsync(Ct));
    }

    [Fact]
    public async Task Unreadable_payload_is_rescheduled_instead_of_losing_the_message()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var message = new EmailOutboxMessage
        {
            ProtectedPayload = "corrupted",
            CreatedAt = now,
            NextAttemptAt = now,
            ExpiresAt = now.AddHours(1)
        };
        db.EmailOutbox.Add(message);
        await db.SaveChangesAsync(Ct);
        var dispatcher = scope.ServiceProvider.GetRequiredService<EmailOutboxDispatcher>();
        Assert.True(await dispatcher.DispatchOneAsync(Ct));
        await db.Entry(message).ReloadAsync(Ct);
        Assert.Equal(1, message.Attempts);
        Assert.True(message.NextAttemptAt > now);
        Assert.False(await dispatcher.DispatchOneAsync(Ct));
        db.EmailOutbox.Remove(message);
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Concurrent_dispatchers_skip_the_locked_message_and_deliver_it_once()
    {
        await using var firstScope = fixture.Factory.Services.CreateAsyncScope();
        await using var secondScope = fixture.Factory.Services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        firstScope.ServiceProvider.GetRequiredService<IEmailQueue>()
            .Invitation("concurrent@example.test", "Test", "test-code", DateTimeOffset.UtcNow.AddHours(1));
        await firstDb.SaveChangesAsync(Ct);
        var sender = new BlockingSender();
        var first = CreateDispatcher(firstScope.ServiceProvider, sender);
        var second = CreateDispatcher(secondScope.ServiceProvider, sender);
        var delivery = first.DispatchOneAsync(Ct);
        try
        {
            await sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            Assert.False(await second.DispatchOneAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct));
        }
        finally { sender.Release.TrySetResult(); }
        Assert.True(await delivery);
        Assert.Equal(1, sender.Deliveries);
        Assert.False(await firstDb.EmailOutbox.AnyAsync(Ct));
    }

    private static EmailOutboxDispatcher CreateDispatcher(IServiceProvider services, IEmailSender sender)
        => new(services.GetRequiredService<AppDbContext>(), services.GetRequiredService<IDataProtectionProvider>(),
            sender, services.GetRequiredService<IClock>(), NullLogger<EmailOutboxDispatcher>.Instance);

    private sealed class BlockingSender : IEmailSender
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Deliveries { get; private set; }

        public async Task SendInvitationAsync(string email, string organizationName, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            Deliveries++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        public Task SendPasswordResetAsync(string email, string code, DateTimeOffset expiresAt, CancellationToken cancellationToken)
            => throw new InvalidOperationException("This test only enqueues invitations.");
    }
}
