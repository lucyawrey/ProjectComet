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

        public ShapeLandContent(IEnumerable<Shape> shapes, Heightmap terrain)
        {
            Shapes = new ContentTable<Shape>("shape", shapes.OrderBy(s => s.Number));
            Terrain = terrain;
        }

        public ContentTable<Shape> Shapes { get; }

        /// <summary>The test zone's terrain, until zone files exist.</summary>
        public Heightmap Terrain { get; }

        public static ShapeLandContent Load(Stream stream)
        {
            ContentFile file;
            try
            {
                file = MessagePackSerializer.Deserialize<ContentFile>(stream, SerializerOptions);
            }
            catch (MessagePackSerializationException e)
            {
                throw new ContentException("The content file is corrupt or from an incompatible build.", e);
            }

            return new ShapeLandContent(file.Shapes, file.Terrain);
        }

        public void Save(Stream stream) => MessagePackSerializer.Serialize(
            stream,
            new ContentFile { Shapes = Shapes.All.ToArray(), Terrain = Terrain },
            SerializerOptions);

        [MessagePackObject(AllowPrivate = true)]
        internal sealed class ContentFile
        {
            [Key(0)] public Shape[] Shapes { get; set; } = new Shape[0];

            [Key(1)] public Heightmap Terrain { get; set; } = new Heightmap();
        }
    }

    [GeneratedMessagePackResolver]
    internal partial class ShapeLandResolver
    {
    }
}
