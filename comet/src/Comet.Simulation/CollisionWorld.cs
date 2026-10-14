using System;
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

        /// <summary>A world of <paramref name="terrain"/> (if any) and <paramref name="boxes"/>, where falling below <paramref name="killHeight"/> meters is out of the world.</summary>
        public CollisionWorld(TerrainGround? terrain, IEnumerable<Box> boxes, float killHeight)
        {
            Terrain = terrain;
            _boxes = new List<Box>(boxes);
            KillHeight = killHeight;
        }

        /// <summary>The ground, or null for a world of boxes only.</summary>
        public TerrainGround? Terrain { get; }

        public IReadOnlyList<Box> Boxes => _boxes;

        /// <summary>Below this height a player has fallen out of the world.</summary>
        public float KillHeight { get; }

        /// <summary>
        /// The highest surface under a body's footprint that is at or below <paramref name="maxY"/>: terrain under its
        /// center (allowed up to <paramref name="terrainClimb"/> higher, for slopes), or the top of a box its footprint
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

        /// <summary>
        /// How far along a ray a sphere of <paramref name="radius"/> first touches a box, up to
        /// <paramref name="maxDistance"/> (boxes are widened by the radius, so corners count as a little larger).
        /// A ray starting inside a box hits at 0. Terrain is ignored. For keeping a camera out of blocks.
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, float radius, out float distance)
        {
            direction = Vector3.Normalize(direction);
            distance = maxDistance;
            var hit = false;
            foreach (var box in _boxes)
            {
                if (EntersBox(origin, direction, box.Min - new Vector3(radius), box.Max + new Vector3(radius), out var enter) && enter < distance)
                {
                    distance = enter;
                    hit = true;
                }
            }

            return hit;
        }

        // The slab test: where the ray is inside all three axis ranges at once, if it ever is ahead of the origin.
        private static bool EntersBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, out float enter)
        {
            enter = 0;
            var exit = float.MaxValue;
            return Slab(origin.X, direction.X, min.X, max.X, ref enter, ref exit)
                && Slab(origin.Y, direction.Y, min.Y, max.Y, ref enter, ref exit)
                && Slab(origin.Z, direction.Z, min.Z, max.Z, ref enter, ref exit);
        }

        // Narrows [enter, exit] to where the ray is between min and max on one axis; false once it's empty.
        private static bool Slab(float origin, float direction, float min, float max, ref float enter, ref float exit)
        {
            if (MathF.Abs(direction) < 1e-6f)
            {
                return origin >= min && origin <= max;
            }

            var t1 = (min - origin) / direction;
            var t2 = (max - origin) / direction;
            enter = MathF.Max(enter, MathF.Min(t1, t2));
            exit = MathF.Min(exit, MathF.Max(t1, t2));
            return enter <= exit;
        }

        private static bool OverlapsFootprint(Box box, Vector3 feet, float radius) =>
            feet.X + radius > box.Min.X && feet.X - radius < box.Max.X
            && feet.Z + radius > box.Min.Z && feet.Z - radius < box.Max.Z;
    }
}
