using System.Numerics;
using Comet.Content;

namespace Comet.Simulation
{
    /// <summary>
    /// A heightmap placed in the world: the ground height under any point, interpolated across the same
    /// triangles the terrain mesh uses, so client and server agree on where the ground is. Each grid cell is
    /// split along the same diagonal: corners (x, z), (x, z+1), (x+1, z) form one triangle and
    /// (x+1, z), (x, z+1), (x+1, z+1) the other. A triangle with a hole corner isn't ground.
    /// </summary>
    public sealed class TerrainGround
    {
        public TerrainGround(Heightmap map, float spacing, Vector3 origin)
        {
            Map = map;
            Spacing = spacing;
            Origin = origin;
        }

        public Heightmap Map { get; }

        /// <summary>Metres between samples.</summary>
        public float Spacing { get; }

        /// <summary>Where sample (0, 0) sits in the world, at height 0.</summary>
        public Vector3 Origin { get; }

        /// <summary>A heightmap centred on <paramref name="centre"/> horizontally.</summary>
        public static TerrainGround Centred(Heightmap map, float spacing, Vector3 centre) => new TerrainGround(
            map,
            spacing,
            new Vector3(centre.X - (map.SizeX - 1) * spacing / 2, centre.Y, centre.Z - (map.SizeZ - 1) * spacing / 2));

        /// <summary>The ground height at a world point, or false over a hole or outside the map.</summary>
        public bool TryGetHeight(float x, float z, out float height)
        {
            height = 0;
            var gx = (x - Origin.X) / Spacing;
            var gz = (z - Origin.Z) / Spacing;
            if (gx < 0 || gz < 0 || gx > Map.SizeX - 1 || gz > Map.SizeZ - 1)
            {
                return false;
            }

            // On the far edges, use the last cell.
            var cx = System.Math.Min((int)gx, Map.SizeX - 2);
            var cz = System.Math.Min((int)gz, Map.SizeZ - 2);
            var u = gx - cx;
            var v = gz - cz;

            if (u + v <= 1)
            {
                if (!Map.TryGetHeight(cx, cz, out var h00) || !Map.TryGetHeight(cx + 1, cz, out var h10) || !Map.TryGetHeight(cx, cz + 1, out var h01))
                {
                    return false;
                }

                height = h00 + u * (h10 - h00) + v * (h01 - h00) + Origin.Y;
                return true;
            }

            if (!Map.TryGetHeight(cx + 1, cz + 1, out var h11) || !Map.TryGetHeight(cx + 1, cz, out var h10b) || !Map.TryGetHeight(cx, cz + 1, out var h01b))
            {
                return false;
            }

            height = h11 + (1 - u) * (h01b - h11) + (1 - v) * (h10b - h11) + Origin.Y;
            return true;
        }
    }
}
