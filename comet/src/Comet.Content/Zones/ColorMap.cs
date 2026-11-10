using System;
using MessagePack;

namespace Comet.Content
{
    [MessagePackObject]
    public sealed class ColorMap
    {
        [Key(0)]
        public int SizeX { get; set; }

        [Key(1)]
        public int SizeZ { get; set; }

        /// <summary><see cref="SizeX"/> × <see cref="SizeZ"/> colors as 0xRRGGBB, row by row along Z.</summary>
        [Key(2)]
        public uint[] Colors { get; set; } = Array.Empty<uint>();

        public uint this[int x, int z] => Colors[z * SizeX + x];
    }
}
