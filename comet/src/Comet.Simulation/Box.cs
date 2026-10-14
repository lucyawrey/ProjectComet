using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>An axis-aligned solid box, such as a block or a platform.</summary>
    public readonly struct Box
    {
        /// <summary>A box from corner <paramref name="min"/> to corner <paramref name="max"/>, in meters.</summary>
        public Box(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>The corner with the lowest X, Y and Z.</summary>
        public Vector3 Min { get; }

        /// <summary>The corner with the highest X, Y and Z.</summary>
        public Vector3 Max { get; }

        public Vector3 Center => (Min + Max) / 2;

        public Vector3 Size => Max - Min;

        /// <summary>A box standing on <paramref name="baseY"/>, centered on (x, z).</summary>
        public static Box Standing(float x, float z, float baseY, Vector3 size) => new Box(
            new Vector3(x - size.X / 2, baseY, z - size.Z / 2),
            new Vector3(x + size.X / 2, baseY + size.Y, z + size.Z / 2));
    }
}
