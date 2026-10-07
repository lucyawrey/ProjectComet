using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// A chunk's terrain heights: a grid of 16-bit samples, row by row along Z, each turned into metres by
    /// <see cref="Scale"/> and <see cref="Offset"/>. Stored on disk in the raw format (<see cref="HeightmapFormat"/>).
    /// </summary>
    [MessagePackObject]
    public sealed class Heightmap
    {
        /// <summary>Samples along X.</summary>
        [Key(0)] public int SizeX { get; set; }

        /// <summary>Samples along Z.</summary>
        [Key(1)] public int SizeZ { get; set; }

        /// <summary>Metres per sample step.</summary>
        [Key(2)] public float Scale { get; set; }

        /// <summary>The height in metres of sample 0.</summary>
        [Key(3)] public float Offset { get; set; }

        /// <summary><see cref="SizeX"/> × <see cref="SizeZ"/> samples, row by row along Z.</summary>
        [Key(4)] public ushort[] Samples { get; set; } = new ushort[0];

        /// <summary>The height in metres at a grid point.</summary>
        public float HeightAt(int x, int z) => Samples[z * SizeX + x] * Scale + Offset;
    }
}
