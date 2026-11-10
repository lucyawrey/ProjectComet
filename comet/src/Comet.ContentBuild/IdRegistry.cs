using System.Text;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Comet.ContentBuild;

/// <summary>The committed ID registry (<c>content/ids.toml</c>): numbers are permanent, never changed or reused, even if the file is gone.</summary>
public sealed class IdRegistry
{
    private const string Header = """
        # Content ID registry: each id's permanent number, by name, per content type.
        # The content build adds new ids; numbers never change and are never reused. Commit this file.

        """;

    private readonly Dictionary<string, SortedDictionary<int, string>> _kinds = new();

    private IdRegistry(string path) => Path = path;

    public string Path { get; }

    public bool Changed { get; private set; }

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
            var names = new HashSet<string>();
            foreach (var item in table.Items)
            {
                if (item is not KeyValueSyntax { Value: IntegerValueSyntax value } keyValue || keyValue.Key!.DotKeys.ChildrenCount > 0)
                {
                    errors.Add(ContentBuilder.ErrorAt(displayPath, item.Span, "Expected 'name = number'."));
                    continue;
                }

                var name = ContentBuilder.KeyText(keyValue.Key);
                if (value.Value < 1 || value.Value > int.MaxValue)
                {
                    errors.Add(ContentBuilder.ErrorAt(displayPath, value.Span, $"Number {value.Value} for '{kind}.{name}' is out of range."));
                }
                else if (numbers.TryGetValue((int)value.Value, out var other))
                {
                    errors.Add(
                        ContentBuilder.ErrorAt(displayPath, value.Span, $"Number {value.Value} is used by both '{kind}.{other}' and '{kind}.{name}'.")
                    );
                }
                else if (!names.Add(name))
                {
                    errors.Add(ContentBuilder.ErrorAt(displayPath, keyValue.Span, $"'{kind}.{name}' is listed twice."));
                }
                else
                {
                    numbers.Add((int)value.Value, name);
                }
            }
        }

        return registry;
    }

    /// <summary>New ids are numbered in id order, after the type's highest number.</summary>
    public IReadOnlyDictionary<string, int> Assign(string kind, IEnumerable<string> ids)
    {
        var numbers = KindTable(kind);
        var byName = numbers.ToDictionary(n => n.Value, n => n.Key);
        var result = new Dictionary<string, int>();
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            var name = id[(kind.Length + 1)..];
            if (!byName.TryGetValue(name, out var number))
            {
                number = numbers.Count == 0 ? 1 : numbers.Keys.Max() + 1;
                numbers.Add(number, name);
                byName.Add(name, number);
                Changed = true;
            }

            result.Add(id, number);
        }

        return result;
    }

    public void Save()
    {
        var text = new StringBuilder(Header);
        foreach (var (kind, numbers) in _kinds.Where(k => k.Value.Count > 0).OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            text.Append('\n').Append('[').Append(kind).Append("]\n");
            foreach (var (number, name) in numbers)
            {
                // Names with a zone (meadow.gate, for spawn.meadow.gate) are quoted, or TOML would read a table.
                text.Append(name.Contains('.') ? $"\"{name}\"" : name).Append(" = ").Append(number).Append('\n');
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
