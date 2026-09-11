using System.Net.Mail;
using CepApi.Application;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Infrastructure.Services;

public sealed class RegistrationEmailPolicy(AppDbContext db) : IRegistrationEmailPolicy
{
    public Task<bool> IsAllowedAsync(string? email, CancellationToken cancellationToken = default)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value) || value.Count(c => c == '@') != 1 ||
            !MailAddress.TryCreate(value, out var address) ||
            !string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(false);

        var domain = address.Host.ToLowerInvariant();
        // Exact match: subdomains, suffix lookalikes and wildcards are not implicitly allowed.
        // Query every time so changes made by the operator take effect without a restart.
        return db.AllowedEmailDomains.AsNoTracking()
            .AnyAsync(x => x.Domain == domain && x.IsEnabled, cancellationToken);
    }
}
