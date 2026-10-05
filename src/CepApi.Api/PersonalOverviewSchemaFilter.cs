using CepApi.Application;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CepApi.Api;

public sealed class PersonalOverviewSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema input, SchemaFilterContext context)
    {
        if (context.Type != typeof(PersonalOverviewResponse) && context.Type != typeof(PersonalOverviewPeriod) &&
            context.Type != typeof(PersonalOverviewDay) && context.Type != typeof(PersonalSourceDay)) return;
        if (input is not OpenApiSchema schema || schema.Properties is null) return;
        schema.Required = schema.Properties.Keys.ToHashSet();
        if (context.Type == typeof(PersonalOverviewResponse))
            schema.Properties["analysis"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object | JsonSchemaType.Null,
                AllOf = [schema.Properties["analysis"]]
            };
    }
}
