using System.Text;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Comet.ContentBuild;

/// <summary>
/// The committed ID registry (<c>content/ids.toml</c>): one table per content type, mapping each key (without its
/// type prefix) to a permanent number. New keys get the next number after the type's highest; numbers are never
/// changed or reused, and entries whose file is gone stay in place (drift checks and retiring come later).
/// </summary>
public sealed class IdRegistry
{
    private const string Header =
        """
        # Content ID registry: each key's permanent number, per content type.
        # The content build adds new keys; numbers never change and are never reused. Commit this file.

        """;

    private readonly Dictionary<string, SortedDictionary<int, string>> _kinds = new();

    private IdRegistry(string path) => Path = path;

    public string Path { get; }

    /// <summary>Whether <see cref="Assign"/> added any numbers since loading.</summary>
    public bool Changed { get; private set; }

    /// <summary>Loads the registry, or starts an empty one if the file doesn't exist yet.</summary>
    public static IdRegistry Load(string path, string displayPath, List<ContentError> errors)
    {
        var registry = new IdRegistry(path);
        if (!File.Exists(path))
        {
            return registry;
        }

        var document = SyntaxParser.Parse(File.ReadAllText(path), displayPath, validate: true);
        foreach (var diagnostic in document.Diagnostics)
        {
            errors.Add(ContentBuilder.ErrorAt(displayPath, diagnostic.Span, diagnostic.Message));
        }

        if (document.HasErrors)
        {
            return registry;
        }

        foreach (var keyValue in document.KeyValues)
        {
            errors.Add(ContentBuilder.ErrorAt(displayPath, keyValue.Span, "Registry entries belong in a [type] table."));
        }

        foreach (var table in document.Tables)
        {
            var kind = ContentBuilder.KeyText(table.Name!);
            if (table is TableArraySyntax || table.Name!.DotKeys.ChildrenCount > 0)
            {
                errors.Add(ContentBuilder.ErrorAt(displayPath, table.Span, $"Expected a plain [type] table, not '{kind}'."));
                continue;
            }

            var numbers = registry.KindTable(kind);
            var keys = new HashSet<string>();
            foreach (var item in table.Items)
            {
                if (item is not KeyValueSyntax { Value: IntegerValueSyntax value } keyValue || keyValue.Key!.DotKeys.ChildrenCount > 0)
                {
                    errors.Add(ContentBuilder.ErrorAt(displayPath, item.Span, "Expected 'key = number'."));
                    continue;
                }

                var key = ContentBuilder.KeyText(keyValue.Key);
                if (value.Value < 1 || value.Value > int.MaxValue)
                {
                    errors.Add(ContentBuilder.ErrorAt(displayPath, value.Span, $"Number {value.Value} for '{kind}.{key}' is out of range."));
                }
                else if (numbers.TryGetValue((int)value.Value, out var other))
                {
                    errors.Add(ContentBuilder.ErrorAt(displayPath, value.Span, $"Number {value.Value} is used by both '{kind}.{other}' and '{kind}.{key}'."));
                }
                else if (!keys.Add(key))
                {
                    errors.Add(ContentBuilder.ErrorAt(displayPath, keyValue.Span, $"'{kind}.{key}' is listed twice."));
                }
                else
                {
                    numbers.Add((int)value.Value, key);
                }
            }
        }

        return registry;
    }

    /// <summary>Returns each id's number, adding new ids (in key order) after the type's highest number.</summary>
    public IReadOnlyDictionary<string, int> Assign(string kind, IEnumerable<string> ids)
    {
        var numbers = KindTable(kind);
        var byKey = numbers.ToDictionary(n => n.Value, n => n.Key);
        var result = new Dictionary<string, int>();
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            var key = id[(kind.Length + 1)..];
            if (!byKey.TryGetValue(key, out var number))
            {
                number = numbers.Count == 0 ? 1 : numbers.Keys.Max() + 1;
                numbers.Add(number, key);
                byKey.Add(key, number);
                Changed = true;
            }

            result.Add(id, number);
        }

        return result;
    }

    /// <summary>Writes the registry, each type's entries in number order (so concurrent additions conflict in git).</summary>
    public void Save()
    {
        var text = new StringBuilder(Header);
        foreach (var (kind, numbers) in _kinds.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            text.Append('\n').Append('[').Append(kind).Append("]\n");
            foreach (var (number, key) in numbers)
            {
                text.Append(key).Append(" = ").Append(number).Append('\n');
            }
        }

        File.WriteAllText(Path, text.ToString());
        Changed = false;
    }

    private SortedDictionary<int, string> KindTable(string kind)
    {
        if (!_kinds.TryGetValue(kind, out var numbers))
        {
            numbers = new SortedDictionary<int, string>();
            _kinds.Add(kind, numbers);
        }

        return numbers;
    }
}
