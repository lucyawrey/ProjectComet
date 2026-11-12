using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.RegularExpressions;
using Comet.Content;
using Comet.ZoneFiles;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Syntax;
using Group = Comet.Content.Group;

namespace Comet.ContentBuild;

public sealed class ContentBuilder
{
    private readonly string _root;
    private readonly List<IContentType> _types = new();
    private readonly List<(string Kind, Func<IReadOnlyList<IReadContent>, IEnumerable<string>> Keys)> _keyed = new();
    private string? _stringFile;
    private IReadOnlyCollection<string>? _placeholders;
    private string? _zonesFolder;
    private string? _groupsFolder;

    public ContentBuilder(string contentRoot) => _root = Path.GetFullPath(contentRoot);

    /// <summary>Reads <c>{folder}/**/*.toml</c>, with ids of the form <c>{kind}.name</c>.</summary>
    public ContentBuilder Add<T>(string kind, string folder, TomlTypeInfo<T> toml, Action<T, ContentValidation>? validate = null)
        where T : class, IContentEntry
    {
        _types.Add(new ContentType<T>(kind, folder, toml, validate));
        return this;
    }

    public ContentBuilder AddKeys<T>(string kind, Func<T, IEnumerable<string>> keysOf)
        where T : class, IContentEntry
    {
        _keyed.Add((kind, types => types.OfType<ReadContent<T>>().SelectMany(t => t.Entries).SelectMany(keysOf)));
        return this;
    }

    /// <summary>Reads <c>{folder}/en.toml</c>, which must exist; given <paramref name="placeholders"/>, any other placeholder is an error.</summary>
    public ContentBuilder AddStrings(string folder = "strings", IReadOnlyCollection<string>? placeholders = null)
    {
        _stringFile = Path.Combine(folder, "en.toml");
        _placeholders = placeholders;
        return this;
    }

    public ContentBuilder AddProps(string modelRoot, string folder = "props")
    {
        var root = Path.GetFullPath(modelRoot);
        return Add(
            "prop",
            folder,
            CometToml.Default.Prop,
            (prop, validation) =>
            {
                if (prop.Color > 0xFFFFFF)
                {
                    validation.Error(nameof(Prop.Color), $"0x{prop.Color:X} isn't a color: use 0xRRGGBB.");
                }

                if (Path.IsPathRooted(prop.Model) || prop.Model.Split('/', '\\').Contains(".."))
                {
                    validation.Error(nameof(Prop.Model), $"'{prop.Model}' must be a path inside the model folder, without '..'.");
                    return;
                }

                try
                {
                    ModelBaker.Bake(prop, Path.Combine(root, prop.Model));
                }
                catch (ContentException e)
                {
                    validation.Error(nameof(Prop.Model), e.Message);
                }
            }
        );
    }

    public ContentBuilder AddGroups(string folder = "groups")
    {
        _groupsFolder = folder;
        return this;
    }

    public ContentBuilder AddZones(string folder = "zones")
    {
        _zonesFolder = folder;
        return this;
    }

    /// <summary>Saves new registry numbers only if every type passes; with <paramref name="release"/>, warnings are errors.</summary>
    public ContentBuildResult Build(bool saveRegistry = true, bool release = false)
    {
        var errors = new List<ContentError>();
        var warnings = new List<ContentError>();
        var registryPath = Path.Combine(_root, "ids.toml");
        var registry = IdRegistry.Load(registryPath, DisplayPath(registryPath), errors);
        var types = _types.Select(t => t.Read(this, errors)).ToList();
        StringTable? strings = null;
        if (_stringFile is not null)
        {
            var path = ContentPath(_stringFile);
            if (File.Exists(path))
            {
                strings = StringFile.Read(path, DisplayPath(path), _placeholders, errors);
            }
            else
            {
                errors.Add(new ContentError(DisplayPath(path), 1, 1, "No string file: the game's interface text goes here."));
            }
        }

        var props = types.OfType<ReadContent<Prop>>().SingleOrDefault()?.Entries.ToDictionary(a => a.Id) ?? new Dictionary<string, Prop>();
        var groups = _groupsFolder is null ? new List<Group>() : ReadGroups(ContentPath(_groupsFolder), props, errors);
        var groupsByKey = groups.ToDictionary(g => g.Id);
        var zones = _zonesFolder is null ? new List<Zone>() : ReadZones(ContentPath(_zonesFolder), props, groupsByKey, errors, warnings);
        if (release)
        {
            errors.AddRange(warnings.Select(w => w with { Warning = false }));
            warnings.Clear();
        }

        var keyNumbers = new Dictionary<string, IReadOnlyDictionary<string, int>>();
        if (errors.Count == 0)
        {
            foreach (var type in types)
            {
                type.AssignNumbers(registry);
            }

            foreach (var (kind, keys) in _keyed)
            {
                keyNumbers[kind] = registry.Assign(kind, keys(types).ToList());
            }

            AssignZoneNumbers(zones, registry);
            Number(registry, "group", groups);
            groups.Sort((a, b) => a.Number.CompareTo(b.Number));

            if (saveRegistry && registry.Changed)
            {
                registry.Save();
            }
        }

        return new ContentBuildResult(errors, warnings, types, strings, zones, groups, keyNumbers);
    }

    private static List<Group> ReadGroups(string folder, Dictionary<string, Prop> props, List<ContentError> errors)
    {
        var groupErrors = new List<ZoneFileError>();
        var groups = GroupFile.ReadFolder(folder, groupErrors);
        groupErrors.AddRange(ZoneValidation.CheckGroups(groups, key => props.TryGetValue(key, out var prop) ? prop : null));
        errors.AddRange(groupErrors.Select(e => new ContentError(DisplayPath(Path.Combine(folder, e.File)), 1, 1, e.Message)));
        return groups.Select(g => g.Group).GroupBy(g => g.Id).Select(same => same.First()).ToList();
    }

    private static List<Zone> ReadZones(
        string folder,
        Dictionary<string, Prop> props,
        Dictionary<string, Group> groups,
        List<ContentError> errors,
        List<ContentError> warnings
    )
    {
        var zones = new List<Zone>();
        if (!Directory.Exists(folder))
        {
            return zones;
        }

        var zoneFolders = new Dictionary<string, string>();
        foreach (var zoneFolder in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
        {
            var zoneErrors = new List<ZoneFileError>();
            var zone = ZoneFolder.Read(zoneFolder, zoneErrors);
            if (zoneErrors.Count == 0)
            {
                zoneErrors.AddRange(ZoneValidation.Check(zone));
                zoneErrors.AddRange(
                    ZoneValidation.CheckPlacements(
                        zone,
                        key => props.TryGetValue(key, out var prop) ? prop : null,
                        key => groups.TryGetValue(key, out var group) ? group : null
                    )
                );
            }

            var zoneFile = DisplayPath(Path.Combine(zoneFolder, ZoneFolder.ZoneFile));
            if (zoneErrors.Count == 0 && zoneFolders.TryGetValue(zone.Id, out var first))
            {
                errors.Add(new ContentError(zoneFile, 1, 1, $"'{zone.Id}' is already used by {first}."));
            }

            errors.AddRange(zoneErrors.Select(e => new ContentError(DisplayPath(Path.Combine(zoneFolder, e.File)), 1, 1, e.Message)));
            if (zoneErrors.Count == 0)
            {
                warnings.AddRange(
                    ZoneValidation
                        .Blockout(zone, key => groups.TryGetValue(key, out var group) ? group : null)
                        .Select(w => new ContentError(DisplayPath(Path.Combine(zoneFolder, w.File)), 1, 1, w.Message, Warning: true))
                );

                Groups.Expand(zone, key => groups.TryGetValue(key, out var group) ? group : null);
                zoneFolders.TryAdd(zone.Id, zoneFile);
                zones.Add(zone);
            }
        }

        var teleporters = zones.SelectMany(z => z.Chunks).SelectMany(c => c.Teleporters).Select(t => t.Id).ToHashSet();
        foreach (var zone in zones)
        {
            foreach (var teleporter in zone.Chunks.SelectMany(c => c.Teleporters).Where(t => !teleporters.Contains(t.Target)))
            {
                var message =
                    teleporter.Target == ""
                        ? $"{teleporter.Id}: no target yet, so it goes nowhere."
                        : $"{teleporter.Id}: there's no teleporter '{teleporter.Target}' to target yet, so it goes nowhere.";
                warnings.Add(new ContentError(zoneFolders[zone.Id], 1, 1, message, Warning: true));
            }
        }

        return zones;
    }

    private static void AssignZoneNumbers(List<Zone> zones, IdRegistry registry)
    {
        var chunks = zones.SelectMany(z => z.Chunks).ToList();
        Number(registry, "zone", zones);
        Number(registry, "spawn", chunks.SelectMany(c => c.Spawns));
        Number(registry, "teleporter", chunks.SelectMany(c => c.Teleporters));
        Number(registry, "trigger", chunks.SelectMany(c => c.Triggers));
        zones.Sort((a, b) => a.Number.CompareTo(b.Number));
    }

    private static void Number<T>(IdRegistry registry, string kind, IEnumerable<T> entries)
        where T : class, IContentEntry
    {
        var list = entries.ToList();
        var numbers = registry.Assign(kind, list.Select(e => e.Id));
        foreach (var entry in list)
        {
            entry.Number = numbers[entry.Id];
        }
    }

    public IReadOnlyDictionary<string, JsonNode> Schemas() => _types.ToDictionary(t => t.Kind, t => t.Schema());

    internal static string DisplayPath(string path)
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    internal string ContentPath(string folder) => Path.Combine(_root, folder);

    /// <summary>Line and column are 1-based.</summary>
    internal static ContentError ErrorAt(string file, SourceSpan span, string message) =>
        new(file, span.Start.Line + 1, span.Start.Column + 1, message);

    internal const string NamePattern = "[a-z][a-z0-9_]*";

    internal static string KeyText(KeySyntax key) => string.Join('.', KeyParts(key).Select(KeyText));

    internal static IEnumerable<BareKeyOrStringValueSyntax> KeyParts(KeySyntax key) => new[] { key.Key! }.Concat(key.DotKeys.Select(d => d.Key!));

    internal static string KeyText(BareKeyOrStringValueSyntax key) =>
        key switch
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
            _idPattern = new Regex($"^{Regex.Escape(kind)}\\.{NamePattern}$");
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
                var file = DisplayPath(path);
                var text = File.ReadAllText(path);
                var document = SyntaxParser.Parse(text, file, validate: true);
                if (document.HasErrors)
                {
                    errors.AddRange(document.Diagnostics.Select(d => ErrorAt(file, d.Span, d.Message)));
                    continue;
                }

                var fileErrors = UnknownKeys
                    .Find(document, ContentJson.Options.GetTypeInfo(typeof(T)))
                    .Select(u => ErrorAt(file, u.Node.Span, u.Message))
                    .ToList();
                // TOML allows nan and inf, but no content number means either, and NaN passes every range check because its comparisons are false.
                fileErrors.AddRange(
                    document
                        .Descendants()
                        .OfType<FloatValueSyntax>()
                        .Where(f => !double.IsFinite(f.Value))
                        .Select(f => ErrorAt(file, f.Span, $"{f.Token?.Text ?? f.Value.ToString()} isn't allowed: content numbers must be finite."))
                );
                T? entry = null;
                try
                {
                    entry = TomlSerializer.Deserialize(text, _toml);
                }
                catch (TomlException e)
                {
                    // A missing key has no place of its own (Tomlyn points at the end of the file), so use the top.
                    var reason = Reason(e.Message);
                    fileErrors.Add(
                        reason.StartsWith("Missing required", StringComparison.Ordinal)
                            ? new ContentError(file, 1, 1, reason)
                            : new ContentError(file, e.Line ?? 1, e.Column ?? 1, reason)
                    );
                }

                if (entry is not null && fileErrors.Count == 0)
                {
                    var validation = new ContentValidation(file, document);
                    if (!_idPattern.IsMatch(entry.Id))
                    {
                        validation.Error(
                            nameof(IContentEntry.Id),
                            $"'{entry.Id}' isn't a valid {Kind} id: use '{Kind}.' then lowercase letters, digits and underscores."
                        );
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

        // Tomlyn wraps converter errors; only the part after the last " : error : " says what's wrong.
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

        public List<T> Entries { get; } = new();

        public void AssignNumbers(IdRegistry registry)
        {
            Number(registry, _kind, Entries);
            Entries.Sort((a, b) => a.Number.CompareTo(b.Number));
        }
    }
}
