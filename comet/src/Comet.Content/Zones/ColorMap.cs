using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// Painted terrain colors: one color per height sample, on the same grid as the heightmap tile it belongs to,
    /// blended across each triangle. Stored on disk in its raw format (<see cref="ColorMapFormat"/>).
    /// </summary>
    [MessagePackObject]
    public sealed class ColorMap
    {
        /// <summary>Samples along X.</summary>
        [Key(0)] public int SizeX { get; set; }

        /// <summary>Samples along Z.</summary>
        [Key(1)] public int SizeZ { get; set; }

        /// <summary><see cref="SizeX"/> × <see cref="SizeZ"/> colors as 0xRRGGBB, row by row along Z.</summary>
        [Key(2)] public uint[] Colors { get; set; } = new uint[0];

        /// <summary>The color at column <paramref name="x"/>, row <paramref name="z"/>, as 0xRRGGBB.</summary>
        public uint this[int x, int z] => Colors[z * SizeX + x];
    }
}
