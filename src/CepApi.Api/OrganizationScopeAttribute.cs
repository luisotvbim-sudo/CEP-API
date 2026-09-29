using CepApi.Api.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
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
        http.Items[ItemKey] = await http.RequestServices.GetRequiredService<OrganizationScopeService>().ResolveAsync(http);
        await next();
    }
}

public sealed class OrganizationScopeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.MethodInfo.DeclaringType!.IsDefined(typeof(OrganizationScopeAttribute), true)) return;
        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Query && parameter.Name == "organizationId")) return;
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "organizationId",
            In = ParameterLocation.Query,
            Required = false,
            Description = "Required for SystemAdmin: organization to administer. Other roles remain restricted to their own organization.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
        });
    }
}
