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
        public static readonly MessagePackSerializerOptions SerializerOptions = ContentSerializer.CreateOptions(ShapeLandResolver.Instance);

        public ShapeLandContent(IEnumerable<Shape> shapes, Heightmap terrain, StringTable strings)
        {
            Shapes = new ContentTable<Shape>("shape", shapes.OrderBy(s => s.Number));
            Terrain = terrain;
            Strings = strings;
        }

        public ContentTable<Shape> Shapes { get; }

        /// <summary>The interface text (<c>content/strings/en.toml</c>).</summary>
        public StringTable Strings { get; }

        /// <summary>The test zone's terrain, until zone files exist.</summary>
        public Heightmap Terrain { get; }

        /// <summary>
        /// A fingerprint of the content file this was loaded from or saved to (0 for content never in a file). Joins
        /// carry it, so the server turns away a client whose content differs rather than judging it by other numbers.
        /// </summary>
        public ulong Fingerprint { get; private set; }

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

            return new ShapeLandContent(file.Shapes, file.Terrain, file.Strings) { Fingerprint = FingerprintOf(data) };
        }

        public void Save(Stream stream)
        {
            var data = MessagePackSerializer.Serialize(new ContentFile { Shapes = Shapes.All.ToArray(), Terrain = Terrain, Strings = Strings }, SerializerOptions);
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

            [Key(1)] public Heightmap Terrain { get; set; } = new Heightmap();

            [Key(2)] public StringTable Strings { get; set; } = new StringTable();
        }
    }

    [GeneratedMessagePackResolver]
    internal partial class ShapeLandResolver
    {
    }
}
