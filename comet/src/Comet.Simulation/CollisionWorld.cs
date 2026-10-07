using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>
    /// What players stand on and bump into: optional terrain plus solid boxes, and a kill height below which
    /// a player has fallen out of the world.
    /// </summary>
    public sealed class CollisionWorld
    {
        private readonly List<Box> _boxes;

        public CollisionWorld(TerrainGround? terrain, IEnumerable<Box> boxes, float killHeight)
        {
            Terrain = terrain;
            _boxes = new List<Box>(boxes);
            KillHeight = killHeight;
        }

        public TerrainGround? Terrain { get; }

        public IReadOnlyList<Box> Boxes => _boxes;

        /// <summary>Below this height a player has fallen out of the world.</summary>
        public float KillHeight { get; }

        /// <summary>
        /// The highest surface under a body's footprint that is at or below <paramref name="maxY"/>: terrain under its
        /// centre (allowed up to <paramref name="terrainClimb"/> higher, for slopes), or the top of a box its footprint
        /// overlaps. False if there is none (over a hole, past the edge).
        /// </summary>
        public bool TryGetGround(Vector3 feet, float radius, float maxY, out float height, float terrainClimb = 0)
        {
            var found = false;
            height = float.MinValue;
            if (Terrain != null && Terrain.TryGetHeight(feet.X, feet.Z, out var terrain) && terrain <= maxY + terrainClimb)
            {
                height = terrain;
                found = true;
            }

            foreach (var box in _boxes)
            {
                if (box.Max.Y <= maxY && box.Max.Y > height && OverlapsFootprint(box, feet, radius))
                {
                    height = box.Max.Y;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Whether a body standing at <paramref name="feet"/> overlaps a box.</summary>
        public bool IsBlocked(Vector3 feet, float radius, float bodyHeight)
        {
            const float epsilon = 0.001f;
            foreach (var box in _boxes)
            {
                if (OverlapsFootprint(box, feet, radius - epsilon)
                    && feet.Y < box.Max.Y - epsilon
                    && feet.Y + bodyHeight > box.Min.Y + epsilon)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverlapsFootprint(Box box, Vector3 feet, float radius) =>
            feet.X + radius > box.Min.X && feet.X - radius < box.Max.X
            && feet.Z + radius > box.Min.Z && feet.Z - radius < box.Max.Z;
    }
}
