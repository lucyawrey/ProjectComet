using System.Text.Json.Serialization;
using Comet.Content;
using Tomlyn.Serialization;

namespace Comet.ContentBuild;

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(Prop))]
internal sealed partial class CometToml : TomlSerializerContext { }
