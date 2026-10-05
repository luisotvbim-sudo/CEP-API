using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CepApi.Api.Services;
using CepApi.Api.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace CepApi.Api.Startup;

internal static class ApiServiceRegistration
{
    public static void AddApiServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddScoped<AuthenticationSessionService>();
        builder.Services.AddScoped<CredentialAuthenticationService>();
        builder.Services.AddScoped<PowerActionUnlockService>();
        builder.Services.AddScoped<RefreshTokenSessionService>();
        builder.Services.AddScoped<InvitationService>();
        builder.Services.AddScoped<WebSessionCookie>();
        builder.Services.AddScoped<OrganizationScopeService>();
        builder.Services.AddScoped<TimeControlAccessService>();
        builder.Services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problem = new ValidationProblemDetails(context.ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Request validation failed."
                };
                return new BadRequestObjectResult(ApiProblems.Enrich(problem, context.HttpContext, "validation_failed"));
            };
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "CEP API", Version = "v1" });
            options.OperationFilter<OrganizationScopeOperationFilter>();
            options.SchemaFilter<PowerActionSchemaFilter>();
            options.SchemaFilter<PersonalOverviewSchemaFilter>();
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header
            });
        });

        JwtAuthenticationRegistration.Add(builder.Services);
        RateLimitingRegistration.Add(builder.Services, builder.Configuration);
        AddForwardedHeaders(builder.Services, builder.Configuration);
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
    }

    private static void AddForwardedHeaders(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var proxy in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
    }
}
