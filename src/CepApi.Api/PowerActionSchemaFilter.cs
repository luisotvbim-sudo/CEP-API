using System.Text.Json.Nodes;
using CepApi.Application;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CepApi.Api;

// Keep the generated client contract as explicit as the wire format.
public sealed class PowerActionSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema input, SchemaFilterContext context)
    {
        if (context.Type != typeof(PowerActionCheckRequest) && context.Type != typeof(PowerActionCheckResponse)) return;
        if (input is not OpenApiSchema schema) return;
        var action = (OpenApiSchema)schema.Properties!["action"];
        action.Enum = [JsonValue.Create("shutdown"), JsonValue.Create("restart"), JsonValue.Create("hibernate")];
        action.Type = JsonSchemaType.String;
        schema.Required ??= new HashSet<string>();
        schema.Required.Add("action");
        if (context.Type != typeof(PowerActionCheckResponse)) return;
        foreach (var name in new[] { "decision", "code", "message", "analysis" }) schema.Required.Add(name);
        foreach (var name in new[] { "decision", "code", "message" })
            ((OpenApiSchema)schema.Properties[name]).Type = JsonSchemaType.String;
        ((OpenApiSchema)schema.Properties["decision"]).Enum =
            [JsonValue.Create("allowed"), JsonValue.Create("blocked"), JsonValue.Create("indeterminate")];
        ((OpenApiSchema)schema.Properties["code"]).Enum =
            [JsonValue.Create("within_tolerance"), JsonValue.Create("above_tolerance"), JsonValue.Create("analysis_incomplete"),
             JsonValue.Create("workforce_person_not_associated"), JsonValue.Create("external_identity_inactive")];
        schema.Properties["analysis"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object | JsonSchemaType.Null,
            AllOf = [schema.Properties["analysis"]]
        };
    }
}
