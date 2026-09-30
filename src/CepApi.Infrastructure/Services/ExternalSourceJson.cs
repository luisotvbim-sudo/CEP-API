using System.Text.Json;

namespace CepApi.Infrastructure.Services;

internal static class ExternalSourceJson
{
    public static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }
}
