using System.Security.Claims;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CepApi.Api;

// Organization selection never changes the authenticated actor or their claims.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class OrganizationScopeAttribute : Attribute, IAsyncActionFilter
{
    public const string ItemKey = "CepApi.OrganizationScope";
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var selected = http.Request.Query["organizationId"];
        Guid organizationId;
        if (http.User.IsInRole(nameof(UserRole.SystemAdmin)))
        {
            if (selected.Count != 1 || !Guid.TryParse(selected[0], out organizationId))
            {
                Reject(context, 400, "organization_context_required", "Select an organization using organizationId.");
                return;
            }
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            if (!await db.Organizations.AnyAsync(x => x.Id == organizationId, http.RequestAborted))
            {
                Reject(context, 404, "organization_not_found", "Organization not found.");
                return;
            }
        }
        else
        {
            if (!Guid.TryParse(http.User.FindFirstValue("org_id"), out organizationId))
            {
                Reject(context, 403, "organization_context_required", "Organization membership is required.");
                return;
            }
            if (selected.Count > 0 && (selected.Count != 1 || !Guid.TryParse(selected[0], out var requested) || requested != organizationId))
            {
                Reject(context, 403, "organization_context_forbidden", "Cannot select another organization.");
                return;
            }
        }
        http.Items[ItemKey] = organizationId;
        await next();
    }

    private static void Reject(ActionExecutingContext context, int status, string code, string title)
        => context.Result = new ObjectResult(new ProblemDetails
        {
            Status = status, Title = title,
            Extensions = { ["code"] = code, ["correlationId"] = context.HttpContext.TraceIdentifier }
        }) { StatusCode = status };
}

public sealed class OrganizationScopeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.MethodInfo.DeclaringType!.IsDefined(typeof(OrganizationScopeAttribute), true)) return;
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "organizationId", In = ParameterLocation.Query, Required = false,
            Description = "Required for SystemAdmin: organization to administer. Other roles remain restricted to their own organization.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
        });
    }
}
