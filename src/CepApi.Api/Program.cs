using System.Security.Claims;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CepApi.Api;
using CepApi.Infrastructure;
using CepApi.Infrastructure.Identity;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<WebSessionCookie>();
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request validation failed."
        };
        problem.Extensions["code"] = "validation_failed";
        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem);
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "CEP API", Version = "v1" });
    options.OperationFilter<OrganizationScopeOperationFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtKeyRing, IOptions<JwtOptions>>((bearer, keyRing, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keyRing.ValidationKeys,
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.ApiAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidTypes = ["at+jwt"],
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            NameClaimType = "name",
            RoleClaimType = "role"
        };
        bearer.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var userId))
                {
                    context.Fail("Invalid subject.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.Users.AsNoTracking().Include(x => x.Organization)
                    .SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);
                if (user is null || user.Status != CepApi.Domain.UserStatus.Active ||
                    user.Organization is { Status: not CepApi.Domain.OrganizationStatus.Active })
                {
                    context.Fail("Account is not active.");
                    return;
                }

                var tokenRole = context.Principal?.FindFirstValue("role");
                var now = context.HttpContext.RequestServices.GetRequiredService<CepApi.Application.IClock>().UtcNow;
                if (string.IsNullOrEmpty(user.SecurityStamp) ||
                    context.Principal?.FindFirstValue("security_version") != JwtTokenService.SecurityVersion(user.SecurityStamp) ||
                    !Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var familyId) ||
                    !await db.RefreshSessions.AsNoTracking().AnyAsync(x => x.UserId == user.Id &&
                        x.FamilyId == familyId && x.RevokedAt == null && x.ExpiresAt > now, context.HttpContext.RequestAborted))
                {
                    context.Fail("Session has been revoked.");
                    return;
                }
                var tokenOrganization = context.Principal?.FindFirstValue("org_id");
                if (!string.Equals(tokenRole, user.Role.ToString(), StringComparison.Ordinal) ||
                    !string.Equals(tokenOrganization, user.OrganizationId?.ToString(), StringComparison.Ordinal))
                    context.Fail("Token authorization claims are stale.");
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"{httpContext.Connection.RemoteIpAddress}:{httpContext.Request.Path.Value?.TrimEnd('/').ToLowerInvariant()}",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPerMinute", 30),
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("login", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPerMinute", 10),
            Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("recovery-request", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:RecoveryRequestsPer15Minutes", 5),
            Window = TimeSpan.FromMinutes(15), SegmentsPerWindow = 15, QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("recovery-verify", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:RecoveryAttemptsPer15Minutes", 10),
            Window = TimeSpan.FromMinutes(15), SegmentsPerWindow = 15, QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("account", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:AccountPerMinute", 10),
            Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0, AutoReplenishment = true
        }));
});

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

ValidateProductionConfiguration(builder);

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
    await BootstrapAdminAsync(app.Services, builder.Configuration);
    return;
}

await app.RunAsync();

static void ValidateProductionConfiguration(WebApplicationBuilder builder)
{
    if (!builder.Environment.IsProduction()) return;

    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
    var email = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>();
    var securityCodes = builder.Configuration.GetSection(SecurityCodeOptions.SectionName).Get<SecurityCodeOptions>();
    var workforce = builder.Configuration.GetSection(WorkforceIntegrationOptions.SectionName)
        .Get<WorkforceIntegrationOptions>() ?? new WorkforceIntegrationOptions();
    if (string.IsNullOrWhiteSpace(jwt?.PrivateKeyPem))
        throw new InvalidOperationException("Jwt:PrivateKeyPem is required in Production.");
    if (string.IsNullOrWhiteSpace(email?.Host) || string.IsNullOrWhiteSpace(email.FromAddress))
        throw new InvalidOperationException("A valid SMTP Email configuration is required in Production.");
    if (email.AllowInsecureTransport)
        throw new InvalidOperationException("SMTP TLS is required in Production.");
    if (string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeysPath"]))
        throw new InvalidOperationException("DataProtection:KeysPath is required in Production for the durable email outbox.");
    try
    {
        if (Convert.FromBase64String(securityCodes?.HmacKey?.Trim() ?? string.Empty).Length < 32)
            throw new InvalidOperationException("SecurityCodes:HmacKey must contain at least 32 random bytes.");
    }
    catch (FormatException exception)
    {
        throw new InvalidOperationException("SecurityCodes:HmacKey must be valid Base64.", exception);
    }
    if (jwt.KeyId == "development-key" || jwt.AccessTokenMinutes is < 1 or > 15 || jwt.PluginGrantHours is < 1 or > 72)
        throw new InvalidOperationException("Configure a production Jwt:KeyId and bounded token lifetimes.");
    ValidateWorkforceIntegration("Monday", workforce.Monday.Enabled, workforce.Monday.Token,
        workforce.Monday.ApiUrl, workforce.Monday.BoardId);
    ValidateWorkforceIntegration("VR Mais", workforce.VrMais.Enabled, workforce.VrMais.Token,
        workforce.VrMais.ApiUrl);
}

static void ValidateWorkforceIntegration(
    string name,
    bool enabled,
    string? token,
    string apiUrl,
    string? boardId = null)
{
    if (!enabled) return;
    if (string.IsNullOrWhiteSpace(token))
        throw new InvalidOperationException($"{name} token is required when the integration is enabled.");
    if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException($"{name} API URL must use HTTPS.");
    if (boardId is not null && string.IsNullOrWhiteSpace(boardId))
        throw new InvalidOperationException($"{name} board id is required when the integration is enabled.");
}

static async Task BootstrapAdminAsync(IServiceProvider services, IConfiguration configuration)
{
    await using var scope = services.CreateAsyncScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (await db.Users.AnyAsync(x => x.Role == CepApi.Domain.UserRole.SystemAdmin))
        throw new InvalidOperationException("A SystemAdmin already exists; bootstrap refuses to overwrite it.");

    var email = configuration["BootstrapAdmin:Email"] ?? throw new InvalidOperationException("BootstrapAdmin:Email is required.");
    if (!await scope.ServiceProvider.GetRequiredService<CepApi.Application.IRegistrationEmailPolicy>().IsAllowedAsync(email))
        throw new InvalidOperationException("Bootstrap administrator email domain is not allowed for registration.");
    var password = configuration["BootstrapAdmin:Password"] ?? throw new InvalidOperationException("BootstrapAdmin:Password is required.");
    var displayName = configuration["BootstrapAdmin:DisplayName"] ?? "System Administrator";
    var now = DateTimeOffset.UtcNow;
    var user = new ApplicationUser
    {
        Id = Guid.CreateVersion7(),
        UserName = email,
        Email = email,
        EmailConfirmed = true,
        DisplayName = displayName,
        Role = CepApi.Domain.UserRole.SystemAdmin,
        Status = CepApi.Domain.UserStatus.Active,
        CreatedAt = now,
        UpdatedAt = now
    };
    var result = await userManager.CreateAsync(user, password);
    if (!result.Succeeded)
        throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
}

public partial class Program;
