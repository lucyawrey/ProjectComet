using System.IO;
using System.Text;

namespace Comet.Content
{
    /// <summary>
    /// The raw color map file: the magic <c>CCOL</c>, a format version, the grid size in samples, then one color
    /// per sample as three bytes (red, green, blue), row by row along Z.
    /// </summary>
    public static class ColorMapFormat
    {
        /// <summary>The header's length in bytes, before the colors.</summary>
        public const int HeaderSize = 10;
        /// <summary>The format version this code reads and writes.</summary>
        public const ushort Version = 1;

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CCOL");

        /// <summary>Reads a color map file; throws <see cref="ContentException"/> if it's malformed or another version.</summary>
        public static ColorMap Read(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
            {
                throw new ContentException("Not a color map file (expected the magic 'CCOL').");
            }

            try
            {
                var version = reader.ReadUInt16();
                if (version != Version)
                {
                    throw new ContentException($"Unsupported color map version {version} (expected {Version}).");
                }

                var map = new ColorMap { SizeX = reader.ReadUInt16(), SizeZ = reader.ReadUInt16() };
                var count = (long)map.SizeX * map.SizeZ;
                if (map.SizeX < 1 || map.SizeZ < 1)
                {
                    throw new ContentException($"Color map is {map.SizeX}×{map.SizeZ} samples; it needs at least one.");
                }

                if (stream.CanSeek && stream.Length - stream.Position < 3 * count)
                {
                    throw new ContentException("Color map file is truncated.");
                }

                map.Colors = new uint[count];
                for (var i = 0; i < map.Colors.Length; i++)
                {
                    var rgb = reader.ReadBytes(3);
                    if (rgb.Length != 3)
                    {
                        throw new ContentException("Color map file is truncated.");
                    }

                    map.Colors[i] = (uint)(rgb[0] << 16 | rgb[1] << 8 | rgb[2]);
                }

                if (stream.CanSeek && stream.Position != stream.Length)
                {
                    throw new ContentException($"Color map has {stream.Length - stream.Position} bytes after its {map.SizeX}×{map.SizeZ} colors.");
                }

                return map;
            }
            catch (EndOfStreamException e)
            {
                throw new ContentException("Color map file is truncated.", e);
            }
        }

        /// <summary>Writes <paramref name="map"/> as a color map file.</summary>
        public static void Write(Stream stream, ColorMap map)
        {
            if (map.SizeX < 1 || map.SizeZ < 1 || map.SizeX > ushort.MaxValue || map.SizeZ > ushort.MaxValue)
            {
                throw new ContentException($"Color map is {map.SizeX}×{map.SizeZ} samples; each side must be 1 to {ushort.MaxValue}.");
            }

            if (map.Colors.Length != map.SizeX * map.SizeZ)
            {
                throw new ContentException($"Color map has {map.Colors.Length} colors; {map.SizeX}×{map.SizeZ} needs {map.SizeX * map.SizeZ}.");
            }

            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write((ushort)map.SizeX);
            writer.Write((ushort)map.SizeZ);
            foreach (var color in map.Colors)
            {
                writer.Write((byte)(color >> 16));
                writer.Write((byte)(color >> 8));
                writer.Write((byte)color);
            }
        }
    }
}
