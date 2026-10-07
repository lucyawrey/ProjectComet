using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.RegularExpressions;
using Comet.Content;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Serialization;
using Tomlyn.Syntax;

namespace Comet.ContentBuild;

/// <summary>
/// Reads a game's content folder: one TOML file per entry, in folders by type. Each file is checked against its
/// C# type (syntax, unknown keys, missing or mistyped fields, the id's form and uniqueness, then the type's own
/// validation rules) and, if everything passes, gets its number from the ID registry. Games register their types
/// and Tomlyn's source-generated metadata for them, then write the compiled file themselves.
/// </summary>
public sealed class ContentBuilder
{
    private readonly string _root;
    private readonly List<IContentType> _types = new();

    public ContentBuilder(string contentRoot) => _root = Path.GetFullPath(contentRoot);

    /// <summary>Registers a content type read from <c>{folder}/**/*.toml</c>, with ids of the form <c>{kind}.name</c>.</summary>
    public ContentBuilder Add<T>(string kind, string folder, TomlTypeInfo<T> toml, Action<T, ContentValidation>? validate = null)
        where T : class, IContentEntry
    {
        _types.Add(new ContentType<T>(kind, folder, toml, validate));
        return this;
    }

    /// <summary>Reads and checks every registered type, assigning registry numbers and saving new ones if it all passes.</summary>
    public ContentBuildResult Build(bool saveRegistry = true)
    {
        var errors = new List<ContentError>();
        var registryPath = Path.Combine(_root, "ids.toml");
        var registry = IdRegistry.Load(registryPath, DisplayPath(registryPath), errors);
        var types = _types.Select(t => t.Read(this, errors)).ToList();
        if (errors.Count == 0)
        {
            foreach (var type in types)
            {
                type.AssignNumbers(registry);
            }

            if (saveRegistry && registry.Changed)
            {
                registry.Save();
            }
        }

        return new ContentBuildResult(errors, types);
    }

    /// <summary>A JSON Schema per content type, keyed by kind, for editor autocompletion and checking.</summary>
    public IReadOnlyDictionary<string, JsonNode> Schemas() => _types.ToDictionary(t => t.Kind, t => t.Schema());

    internal string DisplayPath(string path)
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    internal string ContentPath(string folder) => Path.Combine(_root, folder);

    internal static ContentError ErrorAt(string file, SourceSpan span, string message) =>
        new(file, span.Start.Line + 1, span.Start.Column + 1, message);

    internal static string KeyText(KeySyntax key) =>
        string.Join('.', new[] { key.Key! }.Concat(key.DotKeys.Select(d => d.Key!)).Select(KeyText));

    internal static string KeyText(BareKeyOrStringValueSyntax key) => key switch
    {
        BareKeySyntax bare => bare.Key!.Text!,
        StringValueSyntax quoted => quoted.Value!,
        _ => key.ToString(),
    };

    private interface IContentType
    {
        string Kind { get; }

        IReadContent Read(ContentBuilder builder, List<ContentError> errors);

        JsonNode Schema();
    }

    internal interface IReadContent
    {
        Type EntryType { get; }

        void AssignNumbers(IdRegistry registry);
    }

    private sealed class ContentType<T> : IContentType
        where T : class, IContentEntry
    {
        private readonly string _folder;
        private readonly TomlTypeInfo<T> _toml;
        private readonly Action<T, ContentValidation>? _validate;
        private readonly Regex _idPattern;

        public ContentType(string kind, string folder, TomlTypeInfo<T> toml, Action<T, ContentValidation>? validate)
        {
            Kind = kind;
            _folder = folder;
            _toml = toml;
            _validate = validate;
            _idPattern = new Regex($"^{Regex.Escape(kind)}\\.[a-z][a-z0-9_]*$");
        }

        public string Kind { get; }

        private string IdPattern => _idPattern.ToString();

        public IReadContent Read(ContentBuilder builder, List<ContentError> errors)
        {
            var read = new ReadContent<T>(Kind);
            var folder = builder.ContentPath(_folder);
            if (!Directory.Exists(folder))
            {
                return read;
            }

            var firstFileById = new Dictionary<string, string>();
            var files = Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories).Order(StringComparer.Ordinal);
            foreach (var path in files)
            {
                var file = builder.DisplayPath(path);
                var text = File.ReadAllText(path);
                var document = SyntaxParser.Parse(text, file, validate: true);
                if (document.HasErrors)
                {
                    errors.AddRange(document.Diagnostics.Select(d => ErrorAt(file, d.Span, d.Message)));
                    continue;
                }

                var fileErrors = UnknownKeys.Find(document, ContentJson.Options.GetTypeInfo(typeof(T)))
                    .Select(u => ErrorAt(file, u.Node.Span, u.Message))
                    .ToList();
                T? entry = null;
                try
                {
                    entry = TomlSerializer.Deserialize(text, _toml);
                }
                catch (TomlException e)
                {
                    // A missing key has no place of its own (Tomlyn points at the end of the file), so use the top.
                    var reason = Reason(e.Message);
                    fileErrors.Add(reason.StartsWith("Missing required", StringComparison.Ordinal)
                        ? new ContentError(file, 1, 1, reason)
                        : new ContentError(file, e.Line ?? 1, e.Column ?? 1, reason));
                }

                if (entry is not null && fileErrors.Count == 0)
                {
                    var validation = new ContentValidation(file, document);
                    if (!_idPattern.IsMatch(entry.Id))
                    {
                        validation.Error(nameof(IContentEntry.Id), $"'{entry.Id}' isn't a valid {Kind} id: use '{Kind}.' then lowercase letters, digits and underscores.");
                    }
                    else if (firstFileById.TryGetValue(entry.Id, out var first))
                    {
                        validation.Error(nameof(IContentEntry.Id), $"'{entry.Id}' is already used by {first}.");
                    }
                    else
                    {
                        firstFileById.Add(entry.Id, file);
                    }

                    _validate?.Invoke(entry, validation);
                    fileErrors.AddRange(validation.Errors);
                    if (fileErrors.Count == 0)
                    {
                        read.Entries.Add(entry);
                    }
                }

                errors.AddRange(fileErrors);
            }

            return read;
        }

        public JsonNode Schema()
        {
            var schema = ContentJson.Options.GetJsonSchemaAsNode(typeof(T), new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
            schema["title"] = Kind;
            if (schema["properties"]?["id"] is JsonObject id)
            {
                id["pattern"] = IdPattern;
            }

            return schema;
        }

        // Tomlyn wraps converter errors ("Exception while trying to convert … Reason: (3,8) : error : Invalid enum
        // name …"); the last part says what's wrong.
        private static string Reason(string message)
        {
            const string marker = " : error : ";
            var at = message.LastIndexOf(marker, StringComparison.Ordinal);
            return (at < 0 ? message : message[(at + marker.Length)..]).Trim();
        }
    }

    internal sealed class ReadContent<T> : IReadContent
        where T : class, IContentEntry
    {
        private readonly string _kind;

        public ReadContent(string kind) => _kind = kind;

        public Type EntryType => typeof(T);

        public List<T> Entries { get; } = new();

        public void AssignNumbers(IdRegistry registry)
        {
            var numbers = registry.Assign(_kind, Entries.Select(e => e.Id));
            foreach (var entry in Entries)
            {
                entry.Number = numbers[entry.Id];
            }

            Entries.Sort((a, b) => a.Number.CompareTo(b.Number));
        }
    }
}
