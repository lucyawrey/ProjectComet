using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// A chunk's terrain heights: a grid of 16-bit samples, row by row along Z, each turned into metres by
    /// <see cref="Scale"/> and <see cref="Offset"/>. Sample value <see cref="Hole"/> means no ground (open sky, such as
    /// past a floating island's edge): a triangle of terrain exists only where all three of its samples are ground.
    /// Stored on disk in the raw format (<see cref="HeightmapFormat"/>).
    /// </summary>
    [MessagePackObject]
    public sealed class Heightmap
    {
        /// <summary>The sample value for no ground.</summary>
        public const ushort Hole = 0;

        /// <summary>Samples along X.</summary>
        [Key(0)] public int SizeX { get; set; }

        /// <summary>Samples along Z.</summary>
        [Key(1)] public int SizeZ { get; set; }

        /// <summary>Metres per sample step.</summary>
        [Key(2)] public float Scale { get; set; }

        /// <summary>The height in metres of sample value 0; ground samples start at 1.</summary>
        [Key(3)] public float Offset { get; set; }

        /// <summary><see cref="SizeX"/> × <see cref="SizeZ"/> samples, row by row along Z.</summary>
        [Key(4)] public ushort[] Samples { get; set; } = new ushort[0];

        public bool IsHole(int x, int z) => Samples[z * SizeX + x] == Hole;

        /// <summary>The height in metres at a grid point, or false where there's no ground.</summary>
        public bool TryGetHeight(int x, int z, out float height)
        {
            var sample = Samples[z * SizeX + x];
            height = sample * Scale + Offset;
            return sample != Hole;
        }
    }
}
