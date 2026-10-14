using System.Text.RegularExpressions;
using Comet.Content;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Comet.ContentBuild;

/// <summary>
/// Reads a string file (<c>strings/en.toml</c>): interface text grouped in TOML tables, each key's text a string,
/// keyed by its dotted path (<c>[join]</c> then <c>name_taken = "…"</c> is <c>join.name_taken</c>). Key parts are
/// lowercase letters, digits and underscores; placeholders are <c>{name}</c>, with no other braces.
/// </summary>
internal static class StringFile
{
    private static readonly Regex KeyPart = new("^[a-z][a-z0-9_]*$");
    private static readonly Regex Braces = new(@"\{[^{}]*\}|[{}]");
    private static readonly Regex Placeholder = new(@"^\{[a-z][a-z0-9_]*\}$");

    /// <summary>Reads the string file at <paramref name="path"/>, adding problems to <paramref name="errors"/> under the name <paramref name="file"/>.</summary>
    public static StringTable Read(string path, string file, List<ContentError> errors)
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
            Add(keyValue, "", table, file, errors);
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
                    Add(keyValue, prefix + ".", table, file, errors);
                }
            }
        }

        return table;
    }

    private static void Add(KeyValueSyntax keyValue, string prefix, StringTable table, string file, List<ContentError> errors)
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
                errors.Add(ContentBuilder.ErrorAt(file, value.Span, $"'{key}' has '{brace.Value}': placeholders are a lowercase name in braces, like {{name}}."));
                return;
            }
        }

        table.Entries[key] = value.Value!;
    }

    // A key's dotted path, or null after recording a part that isn't allowed.
    private static string? Key(KeySyntax key, string file, List<ContentError> errors)
    {
        var parts = new[] { key.Key! }.Concat(key.DotKeys.Select(d => d.Key!)).ToList();
        var bad = parts.FirstOrDefault(p => !KeyPart.IsMatch(ContentBuilder.KeyText(p)));
        if (bad is not null)
        {
            errors.Add(ContentBuilder.ErrorAt(file, bad.Span, $"'{ContentBuilder.KeyText(bad)}' isn't a valid key: use lowercase letters, digits and underscores."));
            return null;
        }

        return string.Join('.', parts.Select(ContentBuilder.KeyText));
    }
}
