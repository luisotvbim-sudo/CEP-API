using CepApi.Application;
using CepApi.Api.Controllers;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api;

// Callers must apply authorization and organization filters before these shared projections.
internal static class ApiQueryExtensions
{
    public static async Task<PagedResponse<UserResponse>> ReadUserPageAsync(
        this IQueryable<ApplicationUser> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await query.LongCountAsync(cancellationToken);
        var users = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
            .Page(page, pageSize).ToListAsync(cancellationToken);
        return new(users.Select(x => x.ToUserResponse()).ToArray(), page, pageSize, total);
    }

    public static async Task<AuditEventResponse[]> ReadAuditEventsAsync(
        this IQueryable<AuditEvent> query, DateTimeOffset? before, int pageSize, CancellationToken cancellationToken)
    {
        if (before is not null) query = query.Where(x => x.CreatedAt < before);
        var events = await query.OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(pageSize, 1, 200)).ToListAsync(cancellationToken);
        return events.Select(x => x.ToAuditEventResponse()).ToArray();
    }
}
