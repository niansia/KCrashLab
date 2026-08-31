using System.Text.Json;
using System.Text.Json.Serialization;

namespace KCrashLab.Contracts;

public static class ContractJson
{
    public static JsonSerializerOptions Create(bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = indented,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        return options;
    }

    public static readonly JsonSerializerOptions Compact = Create();
    public static readonly JsonSerializerOptions Indented = Create(indented: true);
}
