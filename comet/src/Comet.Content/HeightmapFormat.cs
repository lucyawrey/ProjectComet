using System.IO;
using System.Text;

namespace Comet.Content
{
    /// <summary>
    /// The raw heightmap file: magic <c>CHGT</c>, version, grid size, then little-endian uint16 samples.
    /// A plain <c>.r16</c> file differs only by the 10-byte header.
    /// </summary>
    public static class HeightmapFormat
    {
        /// <summary>The header's length in bytes.</summary>
        public const int HeaderSize = 10;

        public const ushort Version = 2;

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CHGT");

        /// <summary>Reads a heightmap file; throws <see cref="ContentException"/> if it's malformed or another version.</summary>
        public static Heightmap Read(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
            {
                throw new ContentException("Not a heightmap file (expected the magic 'CHGT').");
            }

            try
            {
                var version = reader.ReadUInt16();
                if (version != Version)
                {
                    throw new ContentException($"Unsupported heightmap version {version} (expected {Version}).");
                }

                var map = new Heightmap { SizeX = reader.ReadUInt16(), SizeZ = reader.ReadUInt16() };
                if (map.SizeX < 2 || map.SizeZ < 2)
                {
                    throw new ContentException($"Heightmap is {map.SizeX}×{map.SizeZ} samples; it needs at least 2×2.");
                }

                // An int overflows at 65535 × 65535 samples, and a header claiming more than the file holds mustn't allocate.
                var count = (long)map.SizeX * map.SizeZ;
                if (count > int.MaxValue || (stream.CanSeek && stream.Length - stream.Position < 2 * count))
                {
                    throw new ContentException("Heightmap file is truncated.");
                }

                map.Samples = new ushort[count];
                for (var i = 0; i < map.Samples.Length; i++)
                {
                    map.Samples[i] = reader.ReadUInt16();
                }

                if (stream.CanSeek && stream.Position != stream.Length)
                {
                    throw new ContentException($"Heightmap has {stream.Length - stream.Position} bytes after its {map.SizeX}×{map.SizeZ} samples.");
                }

                return map;
            }
            catch (EndOfStreamException e)
            {
                throw new ContentException("Heightmap file is truncated.", e);
            }
        }

        public static void Write(Stream stream, Heightmap map)
        {
            if (map.SizeX < 2 || map.SizeZ < 2 || map.SizeX > ushort.MaxValue || map.SizeZ > ushort.MaxValue)
            {
                throw new ContentException($"Heightmap is {map.SizeX}×{map.SizeZ} samples; each side must be 2 to {ushort.MaxValue}.");
            }

            if (map.Samples.Length != map.SizeX * map.SizeZ)
            {
                throw new ContentException($"Heightmap has {map.Samples.Length} samples; {map.SizeX}×{map.SizeZ} needs {map.SizeX * map.SizeZ}.");
            }

            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write((ushort)map.SizeX);
            writer.Write((ushort)map.SizeZ);
            foreach (var sample in map.Samples)
            {
                writer.Write(sample);
            }
        }
    }
}
