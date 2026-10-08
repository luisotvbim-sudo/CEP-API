using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Services;

// A bounded, periodic server-side purge. The setting is operational and must be reviewed before production rollout.
public sealed class DesktopTelemetryRetentionService(IServiceScopeFactory scopes, IConfiguration config,
    ILogger<DesktopTelemetryRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                var days = config.GetValue("DesktopTelemetry:RetentionDays", 30);
                if (days is < 7 or > 365)
                    throw new InvalidOperationException("DesktopTelemetry:RetentionDays must be between 7 and 365.");
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.DesktopTelemetryEvents.Where(x => x.ReceivedAt < DateTimeOffset.UtcNow.AddDays(-days))
                    .ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Desktop telemetry retention purge failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
