using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comet.Content;
using MessagePack;

namespace ShapeLand.Shared.Content
{
    /// <summary>ShapeLand's compiled content, as loaded by the server and client.</summary>
    public sealed class ShapeLandContent
    {
        /// <summary>How the content file is encoded: Comet's content types plus ShapeLand's.</summary>
        public static readonly MessagePackSerializerOptions SerializerOptions = ContentSerializer.CreateOptions(ShapeLandResolver.Instance);

        /// <summary>Content made in memory (by the content build, or tests); <see cref="Fingerprint"/> is 0 until it's saved.</summary>
        public ShapeLandContent(IEnumerable<Shape> shapes, GameSettings settings, StringTable strings, IEnumerable<Prop> props, IEnumerable<Zone> zones, IEnumerable<Group>? groups = null)
        {
            Groups = new ContentTable<Group>("group", (groups ?? Enumerable.Empty<Group>()).OrderBy(g => g.Number));
            Settings = settings;
            Movement = settings.Rules();
            Props = new ContentTable<Prop>("prop", props.OrderBy(a => a.Number));
            Shapes = new ContentTable<Shape>("shape", shapes.OrderBy(s => s.Number));
            Strings = strings;
            Zones = new ContentTable<Zone>("zone", zones.OrderBy(z => z.Number));
        }

        /// <summary>The playable shapes, in number order.</summary>
        public ContentTable<Shape> Shapes { get; }

        /// <summary>The game-wide values (<c>content/settings/game.toml</c>).</summary>
        public GameSettings Settings { get; }

        /// <summary>The movement physics from <see cref="Settings"/>, as the motor and the server's checks take them.</summary>
        public Comet.Simulation.MovementRules Movement { get; }

        /// <summary>The interface text (<c>content/strings/en.toml</c>).</summary>
        public StringTable Strings { get; }

        /// <summary>What zones place (<c>content/props/</c>), with their collision, in number order.</summary>
        public ContentTable<Prop> Props { get; }

        /// <summary>The zones (<c>content/zones/</c>), in number order.</summary>
        public ContentTable<Zone> Zones { get; }

        /// <summary>The groups (<c>content/groups/</c>), in number order: the shapes' looks (zones hold theirs expanded).</summary>
        public ContentTable<Group> Groups { get; }

        /// <summary>The primitive kind of a shape's body (its look's first <c>body</c> part), or null if it has none.</summary>
        public PrimitiveKind? BodyKind(Shape shape) =>
            Groups.TryGet(shape.Look, out var look)
                ? Comet.Content.Groups.Expand(new Placement { Group = look.Id }, key => Groups.TryGet(key, out var g) ? g : null)
                    .FirstOrDefault(p => p.Role == "body")?.Primitive?.Kind
                : null;

        /// <summary>
        /// A fingerprint of the content file this was loaded from or saved to (0 for content never in a file). Joins
        /// carry it, so the server turns away a client whose content differs rather than judging it by other numbers.
        /// </summary>
        public ulong Fingerprint { get; private set; }

        /// <summary>Reads a content file; throws <see cref="ContentException"/> if it's corrupt or from an incompatible build.</summary>
        public static ShapeLandContent Load(Stream stream)
        {
            var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var data = bytes.ToArray();
            ContentFile file;
            try
            {
                file = MessagePackSerializer.Deserialize<ContentFile>(data, SerializerOptions);
            }
            catch (MessagePackSerializationException e)
            {
                throw new ContentException("The content file is corrupt or from an incompatible build.", e);
            }

            return new ShapeLandContent(file.Shapes, file.Settings, file.Strings, file.Props, file.Zones, file.Groups) { Fingerprint = FingerprintOf(data) };
        }

        /// <summary>Writes the content file and sets <see cref="Fingerprint"/> to match it.</summary>
        public void Save(Stream stream)
        {
            var data = MessagePackSerializer.Serialize(new ContentFile { Shapes = Shapes.All.ToArray(), Strings = Strings, Props = Props.All.ToArray(), Zones = Zones.All.ToArray(), Settings = Settings, Groups = Groups.All.ToArray() }, SerializerOptions);
            Fingerprint = FingerprintOf(data);
            stream.Write(data, 0, data.Length);
        }

        // 64-bit FNV-1a: plenty to tell two content builds apart, and the same on every platform.
        private static ulong FingerprintOf(byte[] data)
        {
            var hash = 14695981039346656037UL;
            foreach (var b in data)
            {
                hash = (hash ^ b) * 1099511628211UL;
            }

            return hash;
        }

        [MessagePackObject(AllowPrivate = true)]
        internal sealed class ContentFile
        {
            [Key(0)] public Shape[] Shapes { get; set; } = new Shape[0];

            [Key(2)] public StringTable Strings { get; set; } = new StringTable();

            [Key(3)] public Zone[] Zones { get; set; } = new Zone[0];

            [Key(4)] public Prop[] Props { get; set; } = new Prop[0];

            [Key(5)] public GameSettings Settings { get; set; } = new GameSettings();

            [Key(6)] public Group[] Groups { get; set; } = new Group[0];
        }
    }

    [GeneratedMessagePackResolver]
    internal partial class ShapeLandResolver
    {
    }
}
