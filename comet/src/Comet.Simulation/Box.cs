using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>An axis-aligned solid box, such as a block or a platform.</summary>
    public readonly struct Box
    {
        public Box(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public Vector3 Min { get; }

        public Vector3 Max { get; }

        public Vector3 Centre => (Min + Max) / 2;

        public Vector3 Size => Max - Min;

        /// <summary>A box standing on <paramref name="baseY"/>, centred on (x, z).</summary>
        public static Box Standing(float x, float z, float baseY, Vector3 size) => new Box(
            new Vector3(x - size.X / 2, baseY, z - size.Z / 2),
            new Vector3(x + size.X / 2, baseY + size.Y, z + size.Z / 2));
    }
}
