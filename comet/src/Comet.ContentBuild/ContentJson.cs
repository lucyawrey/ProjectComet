using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Comet.ContentBuild;

/// <summary>Matches how Tomlyn reads content, so the unknown-key check and the JSON Schema can't disagree with the reader.</summary>
public static class ContentJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }

    public static string KeyFor(string propertyName) => JsonNamingPolicy.SnakeCaseLower.ConvertName(propertyName);
}
