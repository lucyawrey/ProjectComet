using System;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// A chunk's terrain heights: 16-bit samples, row by row along Z. Sample <see cref="Hole"/> means no ground; a triangle exists
    /// only where all three of its samples are ground.
    /// </summary>
    [MessagePackObject]
    public sealed class Heightmap
    {
        public const ushort Hole = 0;

        [Key(0)]
        public int SizeX { get; set; }

        [Key(1)]
        public int SizeZ { get; set; }

        /// <summary>Meters per sample step.</summary>
        public const float Scale = 0.01f;

        /// <summary>The height in meters of sample value 0 (from <see cref="Zone.HeightBase"/>); not stored in the tile's file.</summary>
        [Key(2)]
        public float Offset { get; set; } = Zone.DefaultHeightBase;

        [Key(3)]
        public ushort[] Samples { get; set; } = Array.Empty<ushort>();

        public bool IsHole(int x, int z) => Samples[z * SizeX + x] == Hole;

        /// <summary>The height in meters at a grid point, or false where there's no ground.</summary>
        public bool TryGetHeight(int x, int z, out float height)
        {
            var sample = Samples[z * SizeX + x];
            height = sample * Scale + Offset;
            return sample != Hole;
        }

        /// <summary>The ground sample nearest a height in meters, clamped so it's never a hole.</summary>
        public ushort ToSample(float height) => (ushort)Math.Clamp(MathF.Round((height - Offset) / Scale), 1, ushort.MaxValue);
    }
}
