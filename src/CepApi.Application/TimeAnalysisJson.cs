using System.Text.Json;
using System.Text.Json.Serialization;

namespace CepApi.Application;

public static class TimeAnalysisJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Write(TimeAnalysisResponse analysis) => JsonSerializer.Serialize(analysis, Options);
    public static TimeAnalysisResponse Read(string json) => JsonSerializer.Deserialize<TimeAnalysisResponse>(json, Options)
        ?? throw new JsonException("The stored time analysis is empty.");
}
