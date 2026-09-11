using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CepApi.Infrastructure.Persistence;

public static class SecurityTransactions
{
    // Serialize authentication changes for one account across all API instances.
    // Read mutable user/session/code state only AFTER obtaining this lock.
    public static async Task<IDbContextTransaction> BeginForUserAsync(this AppDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.LockUserAsync(userId, cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public static Task<int> LockUserAsync(this AppDbContext db, Guid userId, CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM users WHERE \"Id\" = {userId} FOR UPDATE", cancellationToken);

    public static Task<int> RevokeFamilyAsync(this AppDbContext db, Guid userId, Guid familyId, DateTimeOffset now, string reason, CancellationToken cancellationToken)
        => db.RefreshSessions.Where(x => x.UserId == userId && x.FamilyId == familyId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevocationReason, reason), cancellationToken);

    public static Task<int> InvalidateResetCodesAsync(this AppDbContext db, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        => db.PasswordResets.Where(x => x.UserId == userId && x.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), cancellationToken);
}
