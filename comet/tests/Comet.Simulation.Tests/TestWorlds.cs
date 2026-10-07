using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

internal static class TestWorlds
{
    public static readonly MovementRules Rules = new();

    /// <summary>
    /// A 9×9-sample (16 m) heightmap with sample (0, 0) at the origin and 2 m spacing: flat at 1 m, except a hole at
    /// sample (6, 6) and a 1 m rise along x = 16. Plus a 2 m cube standing at (4, 4) and a 0.3 m step at (4, 10).
    /// </summary>
    public static CollisionWorld Create()
    {
        var map = new Heightmap { SizeX = 9, SizeZ = 9, Scale = 0.01f, Offset = 0, Samples = new ushort[81] };
        for (var z = 0; z < 9; z++)
        {
            for (var x = 0; x < 9; x++)
            {
                map.Samples[z * 9 + x] = (ushort)(x == 8 ? 200 : 100);
            }
        }

        map.Samples[6 * 9 + 6] = Heightmap.Hole;
        var terrain = new TerrainGround(map, 2, Vector3.Zero);
        Box[] boxes =
        [
            Box.Standing(4, 4, 1, new Vector3(2, 2, 2)),
            Box.Standing(4, 10, 1, new Vector3(2, 0.3f, 2)),
        ];
        return new CollisionWorld(terrain, boxes, killHeight: -20);
    }
}
