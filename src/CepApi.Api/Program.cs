using CepApi.Api;
using CepApi.Api.Startup;
using CepApi.Infrastructure;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.AddApiServices();

ProductionConfigurationValidator.Validate(builder);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    var (title, code) = response.StatusCode switch
    {
        StatusCodes.Status401Unauthorized => ("Authentication is required.", "unauthorized"),
        StatusCodes.Status403Forbidden => ("Access is forbidden.", "forbidden"),
        StatusCodes.Status429TooManyRequests => ("Too many requests.", "rate_limit_exceeded"),
        _ => ("Request failed.", "request_failed")
    };
    await response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = response.StatusCode,
        Title = title,
        Extensions = { ["code"] = code, ["correlationId"] = statusContext.HttpContext.TraceIdentifier }
    });
});
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.MapGet("/.well-known/jwks.json", (JwtKeyRing keyRing) => Results.Ok(keyRing.GetJwks()))
    .AllowAnonymous().WithTags("Discovery");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}

if (args.Contains("bootstrap-admin", StringComparer.OrdinalIgnoreCase))
{
    await BootstrapAdministrator.RunAsync(app.Services, builder.Configuration);
    return;
}

await app.RunAsync();

public partial class Program;
