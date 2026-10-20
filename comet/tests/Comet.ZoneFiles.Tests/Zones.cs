using System.Numerics;
using Comet.Content;

namespace Comet.ZoneFiles.Tests;

// Test zones, and a throwaway folder to save them in.
internal static class Zones
{
    /// <summary>A flat tile of <paramref name="size"/> samples at <paramref name="height"/> meters.</summary>
    public static Heightmap Flat(int sizeX, int sizeZ, float height) => new()
    {
        SizeX = sizeX,
        SizeZ = sizeZ,
        Scale = 0.25f,
        Offset = -100f,
        Samples = Enumerable.Repeat((ushort)((height + 100f) / 0.25f), sizeX * sizeZ).ToArray(),
    };

    /// <summary>
    /// A zone of two chunks side by side along X, with one piece whose tiles meet on the shared edge, a placement,
    /// a default spawn and a volume.
    /// </summary>
    public static Zone Sample() => new()
    {
        Id = "zone.meadow",
        Pieces = [new TerrainPiece { Name = "main", SkirtDepth = 12, RimSkirts = false }],
        Borders = [new ZoneBorder { Chunk = new ChunkCoord(0, 0), Side = ChunkSide.North, Zone = "zone.hills" }],
        Entrances = [new ZoneEntrance { Name = "cave", Position = new Vector3(10, 2, 10), Size = new Vector3(4, 4, 2), ToZone = "zone.cave", ToEntrance = "mouth" }],
        TransitionRooms = [new TransitionRoom { Name = "hall", Position = new Vector3(20, 2, 20), Size = new Vector3(8, 4, 8), Entrance = "cave" }],
        Chunks =
        [
            new ZoneChunk
            {
                Coord = new ChunkCoord(-1, 0),
                Tiles = [new TerrainTile { Piece = "main", OriginX = 32, OriginZ = 0, Heights = Flat(33, 65, 4) }],
                Placements = [new Placement { Prop = "prop.cube", Position = new Vector3(-10.5f, 4, 3.25f), Rotation = new Vector3(0, 45, 0), Scale = new Vector3(2, 1, 2) }],
            },
            new ZoneChunk
            {
                Coord = new ChunkCoord(0, 0),
                Tiles =
                [
                    new TerrainTile
                    {
                        Piece = "main",
                        Heights = Flat(17, 65, 4),
                        Colors = new ColorMap { SizeX = 17, SizeZ = 65, Colors = Enumerable.Repeat(0x66aa33u, 17 * 65).ToArray() },
                    },
                ],
                Spawns = [new SpawnPoint { Id = "spawn.meadow_gate", Position = new Vector3(5, 4, 5), Radius = 3, Facing = SpawnFacing.Direction, Yaw = 90, IsDefault = true }],
                Volumes = [new CollisionVolume { Kind = VolumeKind.Wall, Position = new Vector3(8, 6, 8), Size = new Vector3(1, 4, 6) }],
            },
        ],
    };
}

internal sealed class TempFolder : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "comet-zone-" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Root);

    public string PathOf(string relative) => Path.Combine(Root, relative);

    public void Write(string relative, string text)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
