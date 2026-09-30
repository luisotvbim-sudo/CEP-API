using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CepApi.Infrastructure.Services;

internal sealed class TimeNotificationWorker(IServiceScopeFactory scopes, ILogger<TimeNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TimeNotificationProcessor>().TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Do not log external responses, personnel data, messages or credentials.
                logger.LogWarning("Time notification processing failed; pending work will be retried.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
