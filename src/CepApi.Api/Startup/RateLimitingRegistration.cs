using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CepApi.Api.Startup;

internal static class RateLimitingRegistration
{
    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", context => SlidingWindow(
                $"{context.Connection.RemoteIpAddress}:{context.Request.Path.Value?.TrimEnd('/').ToLowerInvariant()}",
                configuration.GetValue("RateLimiting:AuthPerMinute", 30),
                TimeSpan.FromMinutes(1),
                segments: 6));
            options.AddPolicy("login", context => SlidingWindow(
                ClientAddress(context),
                configuration.GetValue("RateLimiting:LoginPerMinute", 10),
                TimeSpan.FromMinutes(1),
                segments: 6));
            options.AddPolicy("recovery-request", context => SlidingWindow(
                ClientAddress(context),
                configuration.GetValue("RateLimiting:RecoveryRequestsPer15Minutes", 5),
                TimeSpan.FromMinutes(15),
                segments: 15));
            options.AddPolicy("recovery-verify", context => SlidingWindow(
                ClientAddress(context),
                configuration.GetValue("RateLimiting:RecoveryAttemptsPer15Minutes", 10),
                TimeSpan.FromMinutes(15),
                segments: 15));
            options.AddPolicy("account", context => SlidingWindow(
                context.User.FindFirstValue("sub") ?? ClientAddress(context),
                configuration.GetValue("RateLimiting:AccountPerMinute", 10),
                TimeSpan.FromMinutes(1),
                segments: 6));
        });
    }

    private static RateLimitPartition<string> SlidingWindow(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        int segments)
        => RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            SegmentsPerWindow = segments,
            QueueLimit = 0,
            AutoReplenishment = true
        });

    private static string ClientAddress(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
