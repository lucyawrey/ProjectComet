using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

public class TerrainMesherTests
{
    private const uint Grass = 0x55aa33, Earth = 0x775533;

    // A piece of flat ground at `height` meters: `samples` rows of text, '#' for ground and '.' for a hole, row 0 at
    // world sample z = 0, all in chunk (0, 0) unless split.
    private static Zone Piece(float height, bool rim, bool holes, params string[] samples) => Zone(
        new TerrainPiece { Name = "main", SkirtDepth = height + 4, RimSkirts = rim, HoleSkirts = holes },
        Tile("main", 0, 0, 0, 0, height, samples));

    private static Zone Zone(TerrainPiece piece, params (ChunkCoord Chunk, TerrainTile Tile)[] tiles) => Zone([piece], tiles);

    private static Zone Zone(TerrainPiece[] pieces, params (ChunkCoord Chunk, TerrainTile Tile)[] tiles) => new()
    {
        Id = "zone.test",
        Pieces = pieces,
        Chunks = tiles.GroupBy(t => t.Chunk).Select(g => new ZoneChunk { Coord = g.Key, Tiles = g.Select(t => t.Tile).ToArray() }).ToArray(),
    };

    private static (ChunkCoord, TerrainTile) Tile(string piece, int chunkX, int chunkZ, int originX, int originZ, float height, params string[] samples)
    {
        int sizeX = samples[0].Length, sizeZ = samples.Length;
        var map = new Heightmap { SizeX = sizeX, SizeZ = sizeZ, Scale = 0.25f, Offset = -100 };
        map.Samples = new ushort[sizeX * sizeZ];
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                map.Samples[z * sizeX + x] = samples[z][x] == '#' ? (ushort)((height + 100) / 0.25f) : Heightmap.Hole;
            }
        }

        return (new ChunkCoord(chunkX, chunkZ), new TerrainTile { Piece = piece, OriginX = originX, OriginZ = originZ, Heights = map });
    }

    private static Dictionary<ChunkCoord, TerrainMesh> Mesh(Zone zone) => TerrainMesher.Build(new ZoneTerrain(zone), Grass, Earth);

    private static IEnumerable<(Vector3 A, Vector3 B, Vector3 C, uint Color)> Triangles(TerrainMesh mesh) =>
        Enumerable.Range(0, mesh.TriangleCount).Select(i => (mesh.Positions[3 * i], mesh.Positions[3 * i + 1], mesh.Positions[3 * i + 2], mesh.Colors[3 * i]));

    // Unity's normal for a clockwise triangle.
    private static Vector3 Normal((Vector3 A, Vector3 B, Vector3 C, uint) t) => Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A));

    [Fact]
    public void GroundFacesUpAndBottomsFaceDown()
    {
        var mesh = Mesh(Piece(2, rim: true, holes: true, "###", "###", "###"))[new ChunkCoord(0, 0)];
        var triangles = Triangles(mesh).ToList();

        var top = triangles.Where(t => t.A.Y == 2 && t.B.Y == 2 && t.C.Y == 2).ToList();
        var bottom = triangles.Where(t => t.A.Y == -4 && t.B.Y == -4 && t.C.Y == -4).ToList();
        Assert.Equal(8, top.Count);
        Assert.All(top, t => Assert.Equal(Vector3.UnitY, Normal(t)));
        Assert.All(top, t => Assert.Equal(Grass, t.Color));
        Assert.Equal(8, bottom.Count);
        Assert.All(bottom, t => Assert.Equal(-Vector3.UnitY, Normal(t)));
        // 8 edges around the outside, a wall of two triangles each.
        Assert.Equal(8 + 8 + 16, triangles.Count);
    }

    [Fact]
    public void APiecesOwnColorsReplaceTheGames()
    {
        const uint Stone = 0x888888;
        const uint Sand = 0xd8c690;
        var zone = Piece(2, rim: true, holes: true, "###", "###", "###");
        zone.Pieces[0].UnderColor = Stone;
        zone.Pieces[0].GroundColor = Sand;

        var triangles = Triangles(Mesh(zone)[new ChunkCoord(0, 0)]).ToList();

        Assert.All(triangles.Where(t => t.A.Y < 2 || t.B.Y < 2 || t.C.Y < 2), t => Assert.Equal(Stone, t.Color));
        Assert.All(triangles.Where(t => t.A.Y == 2 && t.B.Y == 2 && t.C.Y == 2), t => Assert.Equal(Sand, t.Color));
    }

    [Fact]
    public void UndersideColorsPaintTheBottomAndTheWallsAboveIt()
    {
        const uint Moss = 0x336633;
        var zone = Piece(2, rim: true, holes: true, "##", "##");
        var tile = zone.Chunks[0].Tiles[0];
        tile.Underside = new Heightmap { SizeX = 2, SizeZ = 2, Scale = 0.25f, Offset = -100, Samples = Enumerable.Repeat((ushort)(96 / 0.25f), 4).ToArray() };
        tile.UndersideColors = new ColorMap { SizeX = 2, SizeZ = 2, Colors = [Moss, Moss, Moss, Moss] };

        var triangles = Triangles(Mesh(zone)[new ChunkCoord(0, 0)]).ToList();

        var below = triangles.Where(t => t.A.Y < 2 || t.B.Y < 2 || t.C.Y < 2).ToList();
        Assert.NotEmpty(below);
        Assert.All(below, t => Assert.Equal(Moss, t.Color));
    }

    [Fact]
    public void WallsFaceOutward()
    {
        var mesh = Mesh(Piece(2, rim: true, holes: true, "###", "###", "###"))[new ChunkCoord(0, 0)];

        var walls = Triangles(mesh).Where(t => MathF.Abs(Normal(t).Y) < 0.01f).ToList();

        Assert.Equal(16, walls.Count);
        foreach (var wall in walls)
        {
            var middle = (wall.A + wall.B + wall.C) / 3 - new Vector3(1, 0, 1);
            Assert.True(Vector3.Dot(Normal(wall), new Vector3(middle.X, 0, middle.Z)) > 0, $"wall at {middle} faces in");
            Assert.Equal(Earth, wall.Color);
        }
    }

    [Fact]
    public void RimAndHoleSkirtsAreSeparate()
    {
        // A 5×5 piece with a hole at its center sample: the six triangles touching it are gone, leaving a hole edged
        // by six edges, inside a rim of sixteen.
        string[] samples = ["#####", "#####", "##.##", "#####", "#####"];
        int Walls(bool rim, bool holes) =>
            Triangles(Mesh(Piece(2, rim, holes, samples))[new ChunkCoord(0, 0)]).Count(t => MathF.Abs(Normal(t).Y) < 0.01f) / 2;

        Assert.Equal(16 + 6, Walls(rim: true, holes: true));
        Assert.Equal(16, Walls(rim: true, holes: false));
        Assert.Equal(6, Walls(rim: false, holes: true));
        Assert.Equal(0, Walls(rim: false, holes: false));
    }

    [Fact]
    public void PiecesAreCutAtChunkBordersWithoutWallsBetween()
    {
        // Two tiles meeting at world x = 0: chunk -1's last column is chunk 0's first.
        var zone = Zone(
            new TerrainPiece { Name = "main", SkirtDepth = 6, RimSkirts = false, HoleSkirts = true },
            Tile("main", -1, 0, 62, 0, 2, "###", "###"),
            Tile("main", 0, 0, 0, 0, 2, "###", "###"));

        var meshes = Mesh(zone);

        Assert.Equal(4, meshes[new ChunkCoord(-1, 0)].TriangleCount);
        Assert.Equal(4, meshes[new ChunkCoord(0, 0)].TriangleCount);
        // Positions are relative to their chunk: chunk -1's tile starts 62 m into it.
        Assert.Equal(62, meshes[new ChunkCoord(-1, 0)].Positions.Min(p => p.X));
        Assert.Equal(64, meshes[new ChunkCoord(-1, 0)].Positions.Max(p => p.X));
        Assert.Equal(0, meshes[new ChunkCoord(0, 0)].Positions.Min(p => p.X));
    }

    [Fact]
    public void MeshingSomeChunksGivesTheSameMeshesAsMeshingAll()
    {
        // A hole on chunk 0's side of the border, so walls are part of what has to match.
        var zone = Zone(
            new TerrainPiece { Name = "main", SkirtDepth = 6, RimSkirts = true, HoleSkirts = true },
            Tile("main", -1, 0, 62, 0, 2, "###", "###", "###"),
            Tile("main", 0, 0, 0, 0, 2, "###", "#.#", "###"));
        var all = Mesh(zone);

        var some = TerrainMesher.Build(new ZoneTerrain(zone), Grass, Earth, new HashSet<ChunkCoord> { new(0, 0) });

        Assert.Equal([new ChunkCoord(0, 0)], some.Keys);
        Assert.Equal(all[new ChunkCoord(0, 0)].Positions, some[new ChunkCoord(0, 0)].Positions);
        Assert.Equal(all[new ChunkCoord(0, 0)].Colors, some[new ChunkCoord(0, 0)].Colors);
    }

    [Fact]
    public void BottomsFollowTheUndersideMap()
    {
        var zone = Piece(2, rim: true, holes: true, "##", "##");
        var tile = zone.Chunks[0].Tiles[0];
        tile.Underside = new Heightmap { SizeX = 2, SizeZ = 2, Scale = 0.25f, Offset = -100, Samples = [(ushort)(90 / 0.25f), (ushort)(90 / 0.25f), (ushort)(90 / 0.25f), (ushort)(90 / 0.25f)] };

        var lowest = Mesh(zone)[new ChunkCoord(0, 0)].Positions.Min(p => p.Y);

        Assert.Equal(-10, lowest);
    }

    [Fact]
    public void WallsStopOnTerrainBelow()
    {
        // A small plateau piece standing on main ground at 0 m; its bottom is far below, but its walls stop on the ground.
        var zone = Zone(
            [new TerrainPiece { Name = "main", RimSkirts = false, HoleSkirts = false }, new TerrainPiece { Name = "plateau", SkirtDepth = 23, RimSkirts = true }],
            Tile("main", 0, 0, 0, 0, 0, "#####", "#####", "#####", "#####", "#####"),
            Tile("plateau", 0, 0, 1, 1, 3, "###", "###", "###"));

        var walls = Triangles(Mesh(zone)[new ChunkCoord(0, 0)]).Where(t => MathF.Abs(Normal(t).Y) < 0.01f).ToList();

        Assert.Equal(16, walls.Count);
        Assert.All(walls, w => Assert.Equal(0, MathF.Min(w.A.Y, MathF.Min(w.B.Y, w.C.Y))));
    }

    [Fact]
    public void PaintedColorsColorTheGround()
    {
        var zone = Piece(2, rim: false, holes: false, "##", "##");
        zone.Chunks[0].Tiles[0].Colors = new ColorMap { SizeX = 2, SizeZ = 2, Colors = [0x111111, 0x222222, 0x333333, 0x444444] };

        var mesh = Mesh(zone)[new ChunkCoord(0, 0)];

        // Each corner takes its sample's color: (1, 1) is the fourth.
        var corner = mesh.Positions.IndexOf(new Vector3(1, 2, 1));
        Assert.Equal(0x444444u, mesh.Colors[corner]);
    }
}
