using System.Text.Json.Serialization.Metadata;
using Tomlyn.Syntax;

namespace Comet.ContentBuild;

/// <summary>
/// Finds keys in a TOML document that the content type doesn't have. Tomlyn ignores unknown keys, so a typo
/// (<c>max_sped</c>) would otherwise silently leave a field at its default.
/// </summary>
internal static class UnknownKeys
{
    public static IEnumerable<(SyntaxNode Node, string Message)> Find(DocumentSyntax document, JsonTypeInfo root)
    {
        var problems = new List<(SyntaxNode, string)>();
        foreach (var keyValue in document.KeyValues)
        {
            CheckKeyValue(keyValue, root, problems);
        }

        foreach (var table in document.Tables)
        {
            var type = Resolve(table.Name!, root, problems);
            if (type is not null && table is TableArraySyntax)
            {
                type = ElementOf(type);
            }

            if (type is null)
            {
                continue;
            }

            foreach (var item in table.Items)
            {
                if (item is KeyValueSyntax keyValue)
                {
                    CheckKeyValue(keyValue, type, problems);
                }
            }
        }

        return problems;
    }

    private static void CheckKeyValue(KeyValueSyntax keyValue, JsonTypeInfo owner, List<(SyntaxNode, string)> problems)
    {
        var type = Resolve(keyValue.Key!, owner, problems);
        if (type is not null)
        {
            CheckValue(keyValue.Value, type, problems);
        }
    }

    private static void CheckValue(ValueSyntax? value, JsonTypeInfo type, List<(SyntaxNode, string)> problems)
    {
        switch (value)
        {
            case InlineTableSyntax table:
                foreach (var item in table.Items)
                {
                    if (item.KeyValue is not null)
                    {
                        CheckKeyValue(item.KeyValue, type, problems);
                    }
                }

                break;
            case ArraySyntax array when ElementOf(type) is { } element:
                foreach (var item in array.Items)
                {
                    CheckValue(item.Value, element, problems);
                }

                break;
        }
    }

    // Walks a (possibly dotted) key from a type, returning the value's type, or null after recording an unknown key.
    private static JsonTypeInfo? Resolve(KeySyntax key, JsonTypeInfo owner, List<(SyntaxNode, string)> problems)
    {
        var type = owner;
        var parts = new List<BareKeyOrStringValueSyntax> { key.Key! };
        parts.AddRange(key.DotKeys.Select(d => d.Key!));
        foreach (var part in parts)
        {
            var name = ContentBuilder.KeyText(part);
            switch (type.Kind)
            {
                case JsonTypeInfoKind.Object:
                    // [JsonIgnore] properties (such as the registry's number) are listed but never read.
                    var readable = type.Properties.Where(p => p.Set is not null || p.Get is not null).ToList();
                    var property = readable.FirstOrDefault(p => p.Name == name);
                    if (property is null)
                    {
                        var known = string.Join(", ", readable.Select(p => p.Name));
                        problems.Add((part, $"Unknown key '{name}' in {Describe(type)}. Known keys: {known}."));
                        return null;
                    }

                    type = type.Options.GetTypeInfo(property.PropertyType);
                    break;
                case JsonTypeInfoKind.Dictionary:
                    type = type.Options.GetTypeInfo(type.ElementType!);
                    break;
                default:
                    // Not a table: Tomlyn reports the type mismatch itself.
                    return null;
            }
        }

        return type;
    }

    private static JsonTypeInfo? ElementOf(JsonTypeInfo type) =>
        type.Kind == JsonTypeInfoKind.Enumerable ? type.Options.GetTypeInfo(type.ElementType!) : null;

    private static string Describe(JsonTypeInfo type) => ContentJson.KeyFor(type.Type.Name);
}
