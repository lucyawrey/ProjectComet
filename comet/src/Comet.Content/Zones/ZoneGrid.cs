using System;
using System.Numerics;

namespace Comet.Content
{
    /// <summary>
    /// The fixed world grid every zone uses: square chunks with terrain samples every meter. Chunk (x, z) covers
    /// world X from <c>x * ChunkSize</c> to <c>(x + 1) * ChunkSize</c>, and likewise Z.
    /// </summary>
    public static class ZoneGrid
    {
        /// <summary>A chunk's side, in meters.</summary>
        public const int ChunkSize = 64;

        /// <summary>Meters between terrain samples.</summary>
        public const float SampleSpacing = 1f;

        /// <summary>
        /// Samples along a chunk's side, edges included: a chunk's last row and column repeat its neighbors' first,
        /// so the terrain meets without seams.
        /// </summary>
        public const int ChunkSamples = ChunkSize + 1;

        /// <summary>
        /// The rotation for Euler angles in degrees, applied in Unity's order: Z, then X, then Y. A positive turn about
        /// Y takes +Z toward +X, as in Unity.
        /// </summary>
        public static Quaternion Rotation(Vector3 eulerDegrees)
        {
            const float toRadians = MathF.PI / 180;
            return Quaternion.CreateFromYawPitchRoll(eulerDegrees.Y * toRadians, eulerDegrees.X * toRadians, eulerDegrees.Z * toRadians);
        }

        /// <summary>A rotation as Euler angles in degrees, the inverse of <see cref="Rotation"/>: X from -90 to 90, Y and Z from -180 to 180.</summary>
        public static Vector3 Euler(Quaternion rotation)
        {
            const float toDegrees = 180 / MathF.PI;
            var q = Quaternion.Normalize(rotation);
            var sinX = Math.Clamp(2 * (q.W * q.X - q.Y * q.Z), -1f, 1f);
            var x = MathF.Asin(sinX);
            float y, z;
            if (MathF.Abs(sinX) > 0.99999f)
            {
                // Facing straight up or down, Y and Z turn about the same axis: it's all in Y.
                y = MathF.Atan2(2 * (q.W * q.Y - q.X * q.Z), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
                z = 0;
            }
            else
            {
                y = MathF.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
                z = MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.X * q.X + q.Z * q.Z));
            }

            return new Vector3(x, y, z) * toDegrees;
        }
    }
}
