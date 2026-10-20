using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.RegularExpressions;
using Comet.Content;
using Group = Comet.Content.Group;
using Comet.ZoneFiles;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Serialization;
using Tomlyn.Syntax;

namespace Comet.ContentBuild;

/// <summary>
/// Reads a game's content folder: one TOML file per entry, in folders by type. Each file is checked against its
/// C# type (syntax, unknown keys, missing or mistyped fields, the id's form and uniqueness, then the type's own
/// validation rules) and, if everything passes, gets its number from the ID registry. Games register their types
/// and Tomlyn's source-generated metadata for them, then write the compiled file themselves. A game with interface
/// text also registers its string file (<see cref="AddStrings"/>), and a game with zones their folder (<see cref="AddZones"/>).
/// </summary>
public sealed class ContentBuilder
{
    private readonly string _root;
    private readonly List<IContentType> _types = new();
    private string? _stringFile;
    private string? _zonesFolder;
    private string? _groupsFolder;

    /// <summary>A builder for the content folder at <paramref name="contentRoot"/>.</summary>
    public ContentBuilder(string contentRoot) => _root = Path.GetFullPath(contentRoot);

    /// <summary>Registers a content type read from <c>{folder}/**/*.toml</c>, with ids of the form <c>{kind}.name</c>.</summary>
    public ContentBuilder Add<T>(string kind, string folder, TomlTypeInfo<T> toml, Action<T, ContentValidation>? validate = null)
        where T : class, IContentEntry
    {
        _types.Add(new ContentType<T>(kind, folder, toml, validate));
        return this;
    }

    /// <summary>Reads interface text from <c>{folder}/en.toml</c> (<see cref="StringFile"/>); the file must exist.</summary>
    public ContentBuilder AddStrings(string folder = "strings")
    {
        _stringFile = Path.Combine(folder, "en.toml");
        return this;
    }

    /// <summary>
    /// Reads props (<see cref="Prop"/>) from <c>{folder}/**/*.toml</c>, baking each one's collision and bounds from
    /// its model, which is found under <paramref name="modelRoot"/>. Zone placements are checked against them.
    /// </summary>
    public ContentBuilder AddProps(string modelRoot, string folder = "props")
    {
        var root = Path.GetFullPath(modelRoot);
        return Add("prop", folder, CometToml.Default.Prop, (prop, validation) =>
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
        });
    }

    /// <summary>
    /// Reads groups from <c>{folder}/**/*.toml</c> (<see cref="GroupFile"/>), checked by
    /// <see cref="ZoneValidation.CheckGroups"/> and given registry numbers. Zones place copies of them, which the
    /// build expands into their parts (<see cref="Groups.Expand(Zone, System.Func{string, Group?})"/>).
    /// </summary>
    public ContentBuilder AddGroups(string folder = "groups")
    {
        _groupsFolder = folder;
        return this;
    }

    /// <summary>
    /// Reads zones from <c>{folder}/&lt;name&gt;/</c>, each a zone folder (<see cref="ZoneFolder"/>), checked by
    /// <see cref="ZoneValidation"/>. Zone and spawn keys get registry numbers like other content.
    /// </summary>
    public ContentBuilder AddZones(string folder = "zones")
    {
        _zonesFolder = folder;
        return this;
    }

    /// <summary>
    /// Reads and checks every registered type, assigning registry numbers and saving new ones if it all passes. With
    /// <paramref name="release"/>, what's otherwise a warning (blockout placements) is an error.
    /// </summary>
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
                strings = StringFile.Read(path, DisplayPath(path), errors);
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
            // A release ships no stand-ins.
            errors.AddRange(warnings.Select(w => w with { Warning = false }));
            warnings.Clear();
        }

        if (errors.Count == 0)
        {
            foreach (var type in types)
            {
                type.AssignNumbers(registry);
            }

            AssignZoneNumbers(zones, registry);
            var groupNumbers = registry.Assign("group", groups.Select(g => g.Id));
            foreach (var group in groups)
            {
                group.Number = groupNumbers[group.Id];
            }

            groups.Sort((a, b) => a.Number.CompareTo(b.Number));

            if (saveRegistry && registry.Changed)
            {
                registry.Save();
            }
        }

        return new ContentBuildResult(errors, warnings, types, strings, zones, groups);
    }

    private List<Group> ReadGroups(string folder, Dictionary<string, Prop> props, List<ContentError> errors)
    {
        var groupErrors = new List<ZoneFileError>();
        var groups = GroupFile.ReadFolder(folder, groupErrors);
        groupErrors.AddRange(ZoneValidation.CheckGroups(groups, key => props.TryGetValue(key, out var prop) ? prop : null));
        errors.AddRange(groupErrors.Select(e => new ContentError(DisplayPath(Path.Combine(folder, e.File)), 1, 1, e.Message)));
        // Kept even with errors (the build fails on them anyway), so zones don't also report each copy as unknown; of
        // groups sharing an id, the first.
        return groups.Select(g => g.Group).GroupBy(g => g.Id).Select(same => same.First()).ToList();
    }

    private List<Zone> ReadZones(string folder, Dictionary<string, Prop> props, Dictionary<string, Group> groups, List<ContentError> errors, List<ContentError> warnings)
    {
        var zones = new List<Zone>();
        if (!Directory.Exists(folder))
        {
            return zones;
        }

        var zoneFolders = new Dictionary<string, string>();
        var spawnZones = new Dictionary<string, string>();
        foreach (var zoneFolder in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
        {
            var zoneErrors = new List<ZoneFileError>();
            var zone = ZoneFolder.Read(zoneFolder, zoneErrors);
            if (zoneErrors.Count == 0)
            {
                zoneErrors.AddRange(ZoneValidation.Check(zone));
                zoneErrors.AddRange(ZoneValidation.CheckPlacements(zone, key => props.TryGetValue(key, out var prop) ? prop : null, key => groups.TryGetValue(key, out var group) ? group : null));
            }

            var zoneFile = DisplayPath(Path.Combine(zoneFolder, ZoneFolder.ZoneFile));
            if (zoneErrors.Count == 0 && zoneFolders.TryGetValue(zone.Id, out var first))
            {
                errors.Add(new ContentError(zoneFile, 1, 1, $"'{zone.Id}' is already used by {first}."));
            }

            foreach (var spawn in zone.Chunks.SelectMany(c => c.Spawns))
            {
                if (spawnZones.TryGetValue(spawn.Id, out var other) && other != zone.Id)
                {
                    errors.Add(new ContentError(zoneFile, 1, 1, $"spawn '{spawn.Id}' is already used by {other}."));
                }

                spawnZones.TryAdd(spawn.Id, zone.Id);
            }

            errors.AddRange(zoneErrors.Select(e => new ContentError(DisplayPath(Path.Combine(zoneFolder, e.File)), 1, 1, e.Message)));
            if (zoneErrors.Count == 0)
            {
                warnings.AddRange(ZoneValidation.Blockout(zone, key => groups.TryGetValue(key, out var group) ? group : null)
                    .Select(w => new ContentError(DisplayPath(Path.Combine(zoneFolder, w.File)), 1, 1, w.Message, Warning: true)));

                // Group copies become their parts: the game loads plain placements.
                Groups.Expand(zone, key => groups.TryGetValue(key, out var group) ? group : null);
                zoneFolders.TryAdd(zone.Id, zoneFile);
                zones.Add(zone);
            }
        }

        return zones;
    }

    private static void AssignZoneNumbers(List<Zone> zones, IdRegistry registry)
    {
        var numbers = registry.Assign("zone", zones.Select(z => z.Id));
        var spawns = zones.SelectMany(z => z.Chunks).SelectMany(c => c.Spawns).ToList();
        var spawnNumbers = registry.Assign("spawn", spawns.Select(s => s.Id));
        foreach (var zone in zones)
        {
            zone.Number = numbers[zone.Id];
        }

        foreach (var spawn in spawns)
        {
            spawn.Number = spawnNumbers[spawn.Id];
        }

        zones.Sort((a, b) => a.Number.CompareTo(b.Number));
    }

    /// <summary>A JSON Schema per content type, keyed by kind, for editor autocompletion and checking.</summary>
    public IReadOnlyDictionary<string, JsonNode> Schemas() => _types.ToDictionary(t => t.Kind, t => t.Schema());

    /// <summary><paramref name="path"/> as errors show it: relative to the working folder when it's inside it.</summary>
    internal string DisplayPath(string path)
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    internal string ContentPath(string folder) => Path.Combine(_root, folder);

    /// <summary>An error at a TOML span, with 1-based line and column.</summary>
    internal static ContentError ErrorAt(string file, SourceSpan span, string message) =>
        new(file, span.Start.Line + 1, span.Start.Column + 1, message);

    /// <summary>A name in ids and string keys: a lowercase letter, then lowercase letters, digits and underscores.</summary>
    internal const string NamePattern = "[a-z][a-z0-9_]*";

    /// <summary>A dotted key as written, without quotes (<c>a."b"</c> is <c>a.b</c>).</summary>
    internal static string KeyText(KeySyntax key) => string.Join('.', KeyParts(key).Select(KeyText));

    /// <summary>The parts of a dotted key, in order.</summary>
    internal static IEnumerable<BareKeyOrStringValueSyntax> KeyParts(KeySyntax key) =>
        new[] { key.Key! }.Concat(key.DotKeys.Select(d => d.Key!));

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

    /// <summary>One content type's entries after reading, before they get their numbers.</summary>
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
                // TOML allows nan and inf, but no content number means either, and every comparison a range check
                // makes with NaN is false, so it would pass them.
                fileErrors.AddRange(document.Descendants()
                    .OfType<FloatValueSyntax>()
                    .Where(f => !double.IsFinite(f.Value))
                    .Select(f => ErrorAt(file, f.Span, $"{f.Token?.Text ?? f.Value.ToString()} isn't allowed: content numbers must be finite.")));
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
