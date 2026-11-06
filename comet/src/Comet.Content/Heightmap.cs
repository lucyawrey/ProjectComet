using System;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// A chunk's terrain heights: a grid of 16-bit samples, row by row along Z, each turned into meters by
    /// <see cref="Scale"/> and the zone's <see cref="Offset"/>. Sample value <see cref="Hole"/> means no ground (open sky, such as
    /// past a floating island's edge): a triangle of terrain exists only where all three of its samples are ground.
    /// Stored on disk in the raw format (<see cref="HeightmapFormat"/>).
    /// </summary>
    [MessagePackObject]
    public sealed class Heightmap
    {
        /// <summary>The sample value for no ground.</summary>
        public const ushort Hole = 0;

        /// <summary>Samples along X.</summary>
        [Key(0)]
        public int SizeX { get; set; }

        /// <summary>Samples along Z.</summary>
        [Key(1)]
        public int SizeZ { get; set; }

        /// <summary>Meters per sample step, the same for all terrain.</summary>
        public const float Scale = 0.01f;

        /// <summary>
        /// The height in meters of sample value 0, from the zone's <see cref="Zone.HeightBase"/>; ground samples
        /// start at 1. Not stored in the tile's file.
        /// </summary>
        [Key(2)]
        public float Offset { get; set; } = Zone.DefaultHeightBase;

        /// <summary><see cref="SizeX"/> × <see cref="SizeZ"/> samples, row by row along Z.</summary>
        [Key(3)]
        public ushort[] Samples { get; set; } = Array.Empty<ushort>();

        /// <summary>True where the sample at column <paramref name="x"/>, row <paramref name="z"/> has no ground.</summary>
        public bool IsHole(int x, int z) => Samples[z * SizeX + x] == Hole;

        /// <summary>The height in meters at a grid point, or false where there's no ground.</summary>
        public bool TryGetHeight(int x, int z, out float height)
        {
            var sample = Samples[z * SizeX + x];
            height = sample * Scale + Offset;
            return sample != Hole;
        }

        /// <summary>The ground sample nearest a height in meters, clamped to this map's range so it's never a hole.</summary>
        public ushort ToSample(float height) => (ushort)Math.Clamp(MathF.Round((height - Offset) / Scale), 1, ushort.MaxValue);
    }
}
