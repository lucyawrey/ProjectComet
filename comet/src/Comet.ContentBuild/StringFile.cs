using System.Text.RegularExpressions;
using Comet.Content;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Comet.ContentBuild;

/// <summary>Key parts are lowercase letters, digits, underscores; only <c>{name}</c> placeholders of names the game lists, no other braces.</summary>
internal static class StringFile
{
    private static readonly Regex KeyPart = new($"^{ContentBuilder.NamePattern}$");
    private static readonly Regex Braces = new(@"\{[^{}]*\}|[{}]");
    private static readonly Regex Placeholder = new($"^\\{{{ContentBuilder.NamePattern}\\}}$");

    public static StringTable Read(string path, string file, IReadOnlyCollection<string>? placeholders, List<ContentError> errors)
    {
        var table = new StringTable();
        var document = SyntaxParser.Parse(File.ReadAllText(path), file, validate: true);
        if (document.HasErrors)
        {
            errors.AddRange(document.Diagnostics.Select(d => ContentBuilder.ErrorAt(file, d.Span, d.Message)));
            return table;
        }

        foreach (var keyValue in document.KeyValues)
        {
            Add(keyValue, "", table, file, placeholders, errors);
        }

        foreach (var group in document.Tables)
        {
            if (group is TableArraySyntax)
            {
                errors.Add(ContentBuilder.ErrorAt(file, group.Span, "Arrays of tables aren't allowed in a string file."));
                continue;
            }

            var prefix = Key(group.Name!, file, errors);
            foreach (var item in group.Items)
            {
                if (item is KeyValueSyntax keyValue && prefix is not null)
                {
                    Add(keyValue, prefix + ".", table, file, placeholders, errors);
                }
            }
        }

        return table;
    }

    private static void Add(
        KeyValueSyntax keyValue,
        string prefix,
        StringTable table,
        string file,
        IReadOnlyCollection<string>? placeholders,
        List<ContentError> errors
    )
    {
        var name = Key(keyValue.Key!, file, errors);
        if (name is null)
        {
            return;
        }

        var key = prefix + name;
        if (keyValue.Value is not StringValueSyntax value)
        {
            errors.Add(ContentBuilder.ErrorAt(file, keyValue.Value!.Span, $"'{key}' must be text in quotes."));
            return;
        }

        foreach (Match brace in Braces.Matches(value.Value!))
        {
            if (!Placeholder.IsMatch(brace.Value))
            {
                errors.Add(
                    ContentBuilder.ErrorAt(
                        file,
                        value.Span,
                        $"'{key}' has '{brace.Value}': placeholders are a lowercase name in braces, like {{name}}."
                    )
                );
                return;
            }

            if (placeholders is not null && !placeholders.Contains(brace.Value[1..^1]))
            {
                errors.Add(
                    ContentBuilder.ErrorAt(
                        file,
                        value.Span,
                        $"'{key}' has '{brace.Value}', which the game doesn't fill in: use one of {string.Join(", ", placeholders.Select(p => $"{{{p}}}"))}."
                    )
                );
                return;
            }
        }

        table.Entries[key] = value.Value!;
    }

    private static string? Key(KeySyntax key, string file, List<ContentError> errors)
    {
        var parts = ContentBuilder.KeyParts(key).ToList();
        var bad = parts.FirstOrDefault(p => !KeyPart.IsMatch(ContentBuilder.KeyText(p)));
        if (bad is not null)
        {
            errors.Add(
                ContentBuilder.ErrorAt(
                    file,
                    bad.Span,
                    $"'{ContentBuilder.KeyText(bad)}' isn't a valid key: use lowercase letters, digits and underscores."
                )
            );
            return null;
        }

        return string.Join('.', parts.Select(ContentBuilder.KeyText));
    }
}
