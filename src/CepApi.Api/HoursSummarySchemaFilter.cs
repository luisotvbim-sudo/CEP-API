using CepApi.Application;
using CepApi.Domain;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CepApi.Api;

public sealed class HoursSummarySchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema input, SchemaFilterContext context)
    {
        var type = context.Type;
        if (type != typeof(HoursSummaryResponse) && type != typeof(HoursPersonResponse) &&
            type != typeof(HoursTeamMemberResponse) && type != typeof(HoursTeamResponse) &&
            type != typeof(HoursTotals)) return;
        if (input is not OpenApiSchema schema || schema.Properties is null) return;
        schema.Required = schema.Properties.Keys.ToHashSet();
        foreach (var property in schema.Properties.Values.OfType<OpenApiSchema>())
        {
            if (property.Type is { } flags && (flags.HasFlag(JsonSchemaType.Array) || flags.HasFlag(JsonSchemaType.String)))
                property.Type = flags & ~JsonSchemaType.Null;
        }
    }
}
