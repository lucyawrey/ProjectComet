using System.Text.Json.Serialization;
using Comet.Content;
using Tomlyn.Serialization;

namespace Comet.ContentBuild;

/// <summary>Tomlyn's source-generated metadata for Comet's own content types.</summary>
[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(Prop))]
internal partial class CometToml : TomlSerializerContext
{
}
