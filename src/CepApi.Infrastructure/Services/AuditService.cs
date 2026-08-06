using System.Text.Json;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;

namespace CepApi.Infrastructure.Services;

public sealed class AuditService(AppDbContext dbContext, IClock clock) : IAuditService
{
    public async Task WriteAsync(string action, Guid? organizationId = null, Guid? actorUserId = null,
        Guid? targetUserId = null, object? details = null, string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Action = action,
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress = ipAddress,
            CreatedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
