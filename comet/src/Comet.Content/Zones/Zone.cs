using System.Collections.Generic;
using System.Numerics;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// A zone: its terrain pieces, borders, entrances and transition rooms, and the chunks it owns. Authored as a
    /// folder of files (a zone TOML, a TOML per chunk and binary tiles), compiled whole into the content file.
    /// </summary>
    [MessagePackObject]
    public sealed class Zone : IContentEntry
    {
        [Key(0)] public int Number { get; set; }

        /// <summary>The zone's key (<c>zone.meadow</c>).</summary>
        [Key(1)] public string Id { get; set; } = "";

        /// <summary>
        /// For a zone on its own local grid (a dungeon), the region chunk its chunk (0, 0) sits on, so its scenery can
        /// show in the open world's distant LODs; null for open zones, which are on the region grid already.
        /// </summary>
        [Key(2)] public ChunkCoord? WorldAnchor { get; set; }

        /// <summary>The terrain pieces, with their zone-wide settings; tiles in chunks refer to them by name.</summary>
        [Key(3)] public TerrainPiece[] Pieces { get; set; } = new TerrainPiece[0];

        /// <summary>The chunk edges where this zone meets a neighbor.</summary>
        [Key(4)] public ZoneBorder[] Borders { get; set; } = new ZoneBorder[0];

        /// <summary>Doorways into other zones, such as a dungeon's mouth.</summary>
        [Key(5)] public ZoneEntrance[] Entrances { get; set; } = new ZoneEntrance[0];

        /// <summary>Loading rooms: areas a party gathers in before an entrance closes behind them.</summary>
        [Key(6)] public TransitionRoom[] TransitionRooms { get; set; } = new TransitionRoom[0];

        /// <summary>The chunks the zone owns, each with its terrain tiles, placements, spawns and volumes.</summary>
        [Key(7)] public ZoneChunk[] Chunks { get; set; } = new ZoneChunk[0];
    }

    /// <summary>A chunk's position on the grid (<see cref="ZoneGrid"/>).</summary>
    [MessagePackObject]
    public readonly struct ChunkCoord : System.IEquatable<ChunkCoord>
    {
        [SerializationConstructor]
        public ChunkCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        [Key(0)] public int X { get; }

        [Key(1)] public int Z { get; }

        /// <summary>The chunk holding a world position (edges belong to the chunk on their high side).</summary>
        public static ChunkCoord Containing(Vector3 position) => new ChunkCoord(
            (int)System.MathF.Floor(position.X / ZoneGrid.ChunkSize),
            (int)System.MathF.Floor(position.Z / ZoneGrid.ChunkSize));

        /// <summary>The world position of the chunk's low corner, at height 0.</summary>
        [IgnoreMember]
        public Vector3 Origin => new Vector3(X * ZoneGrid.ChunkSize, 0, Z * ZoneGrid.ChunkSize);

        public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;

        public override bool Equals(object? obj) => obj is ChunkCoord other && Equals(other);

        public override int GetHashCode() => X * 397 ^ Z;

        public override string ToString() => $"{X}_{Z}";
    }

    /// <summary>A terrain piece's zone-wide settings. Its heights are stored in tiles, one per chunk it covers.</summary>
    [MessagePackObject]
    public sealed class TerrainPiece
    {
        /// <summary>The piece's name, unique in the zone (<c>main</c>, <c>sky_island</c>).</summary>
        [Key(0)] public string Name { get; set; } = "";

        /// <summary>Where the piece's skirts end when it has no underside map: a flat bottom this many meters below its lowest ground.</summary>
        [Key(1)] public float SkirtDepth { get; set; } = 4;

        /// <summary>Whether the piece's outer edge gets skirts (off for main ground that nothing sees below).</summary>
        [Key(2)] public bool RimSkirts { get; set; } = true;

        /// <summary>Whether the edges of holes inside the piece get skirts (chasm walls).</summary>
        [Key(3)] public bool HoleSkirts { get; set; } = true;

        /// <summary>The color of the piece's unpainted underside (its skirt walls and bottom) as 0xRRGGBB, if not the game's.</summary>
        [Key(4)] public uint? UnderColor { get; set; }

        /// <summary>The color of the piece's unpainted ground as 0xRRGGBB, if not the game's.</summary>
        [Key(5)] public uint? GroundColor { get; set; }
    }

    /// <summary>One chunk of a zone: the terrain tiles, placed objects, spawns and volumes whose pivots are in it.</summary>
    [MessagePackObject]
    public sealed class ZoneChunk
    {
        [Key(0)] public ChunkCoord Coord { get; set; }

        [Key(1)] public TerrainTile[] Tiles { get; set; } = new TerrainTile[0];

        [Key(2)] public Placement[] Placements { get; set; } = new Placement[0];

        [Key(3)] public SpawnPoint[] Spawns { get; set; } = new SpawnPoint[0];

        [Key(4)] public CollisionVolume[] Volumes { get; set; } = new CollisionVolume[0];
    }

    /// <summary>
    /// A terrain piece's part of one chunk: a heightmap whose sample (0, 0) sits at sample
    /// (<see cref="OriginX"/>, <see cref="OriginZ"/>) of the chunk, with an optional underside and colors on the same grid.
    /// </summary>
    [MessagePackObject]
    public sealed class TerrainTile
    {
        /// <summary>The <see cref="TerrainPiece.Name"/> of the piece this tile belongs to.</summary>
        [Key(0)] public string Piece { get; set; } = "";

        /// <summary>The chunk sample column of the tile's first column (0 to <see cref="ZoneGrid.ChunkSize"/>).</summary>
        [Key(1)] public int OriginX { get; set; }

        /// <summary>The chunk sample row of the tile's first row.</summary>
        [Key(2)] public int OriginZ { get; set; }

        /// <summary>The ground's heights; holes mark where the piece has no ground.</summary>
        [Key(3)] public Heightmap Heights { get; set; } = new Heightmap();

        /// <summary>The bottom of the piece's skirts, if it has a painted underside rather than a flat bottom.</summary>
        [Key(4)] public Heightmap? Underside { get; set; }

        /// <summary>Painted colors, if any.</summary>
        [Key(5)] public ColorMap? Colors { get; set; }

        /// <summary>Painted colors of the underside and the skirt walls above it, if any; only with an <see cref="Underside"/>.</summary>
        [Key(6)] public ColorMap? UndersideColors { get; set; }
    }

    /// <summary>
    /// A prop, a primitive or a group placed in a chunk (or a part of a group): free position and full rotation,
    /// with uniform or non-uniform scale (a group's copies scale evenly). A primitive's scale is its size in meters, and
    /// its mesh is built at that size (<see cref="Matrix"/>). The content build expands groups into their parts.
    /// </summary>
    [MessagePackObject]
    public sealed class Placement
    {
        /// <summary>The prop's key (<c>prop.rock</c>), or empty for a primitive or a group.</summary>
        [Key(0)] public string Prop { get; set; } = "";

        /// <summary>The pivot's world position, in meters; it lies in the placement's chunk.</summary>
        [Key(1)] public Vector3 Position { get; set; }

        /// <summary>Rotation as Euler angles in degrees, applied in Unity's order (Z, then X, then Y).</summary>
        [Key(2)] public Vector3 Rotation { get; set; }

        [Key(3)] public Vector3 Scale { get; set; } = Vector3.One;

        /// <summary>
        /// An optional name, unique in the zone, for game code to find this placement by (in ShapeLand, a spawn
        /// point's marker is named with the spawn's key); null for most.
        /// </summary>
        [Key(4)] public string? Name { get; set; }

        /// <summary>The placement's own color as 0xRRGGBB, or null for its prop's.</summary>
        [Key(5)] public uint? Color { get; set; }

        /// <summary>The primitive placed, or null for a prop.</summary>
        [Key(6)] public Primitive? Primitive { get; set; }

        /// <summary>Whether this is blockout: a stand-in for finished geometry, drawn in a dev look while a level is built.</summary>
        [Key(7)] public bool Blockout { get; set; }

        /// <summary>The group placed (<c>group.tree</c>), or null for a prop or a primitive.</summary>
        [Key(8)] public string? Group { get; set; }

        /// <summary>
        /// A group part's role (<c>body</c>, <c>eyes</c>), for game code to find or color the parts that play it; null
        /// for most, and only parts of groups have one.
        /// </summary>
        [Key(9)] public string? Role { get; set; }

        /// <summary>
        /// A group copy's colors by role, as 0xRRGGBB: its parts with a listed role, however deeply nested, take that
        /// color in place of their own (an outer copy's color wins); null for most.
        /// </summary>
        [Key(10)] public Dictionary<string, uint>? Colors { get; set; }

        /// <summary>
        /// The placement's transform from its mesh's space to the world: for a prop, scale, then rotation, then
        /// position; for a primitive, whose mesh is built at its size, rotation then position.
        /// </summary>
        public Matrix4x4 Matrix() =>
            (Primitive is null ? Matrix4x4.CreateScale(Scale) : Matrix4x4.Identity) * Matrix4x4.CreateFromQuaternion(ZoneGrid.Rotation(Rotation)) * Matrix4x4.CreateTranslation(Position);
    }

    /// <summary>How a player faces on arriving at a spawn point.</summary>
    public enum SpawnFacing
    {
        /// <summary>Toward <see cref="SpawnPoint.Yaw"/>.</summary>
        Direction,
        /// <summary>Toward the spawn point's center (for a scattered arrival).</summary>
        TowardPoint,
        Random,
    }

    /// <summary>A place players arrive: a random spot with ground within <see cref="Radius"/> of the point.</summary>
    [MessagePackObject]
    public sealed class SpawnPoint : IContentEntry
    {
        [Key(0)] public int Number { get; set; }

        /// <summary>The spawn's key (<c>spawn.meadow_gate</c>), unique across zones.</summary>
        [Key(1)] public string Id { get; set; } = "";

        [Key(2)] public Vector3 Position { get; set; }

        /// <summary>How far from the point arrivals scatter, in meters.</summary>
        [Key(3)] public float Radius { get; set; }

        [Key(4)] public SpawnFacing Facing { get; set; }

        /// <summary>The facing for <see cref="SpawnFacing.Direction"/>, in degrees clockwise from +Z.</summary>
        [Key(5)] public float Yaw { get; set; }

        /// <summary>Whether this is the zone's default spawn (exactly one per zone with spawns).</summary>
        [Key(6)] public bool IsDefault { get; set; }
    }

    /// <summary>What a collision volume does.</summary>
    public enum VolumeKind
    {
        /// <summary>Blocks movement, unseen (an invisible wall).</summary>
        Wall,
        Water,
    }

    /// <summary>A hand-placed box for special cases, such as an invisible wall or water.</summary>
    [MessagePackObject]
    public sealed class CollisionVolume
    {
        [Key(0)] public VolumeKind Kind { get; set; }

        /// <summary>The box's center, in world meters.</summary>
        [Key(1)] public Vector3 Position { get; set; }

        /// <summary>Euler angles in degrees, as for <see cref="Placement.Rotation"/>.</summary>
        [Key(2)] public Vector3 Rotation { get; set; }

        /// <summary>The box's full size along its own axes, in meters.</summary>
        [Key(3)] public Vector3 Size { get; set; }
    }

    /// <summary>A side of a chunk.</summary>
    public enum ChunkSide
    {
        /// <summary>The +Z edge.</summary>
        North,
        /// <summary>The +X edge.</summary>
        East,
        South,
        West,
    }

    /// <summary>One chunk edge where this zone meets another; a border is the list of its edges.</summary>
    [MessagePackObject]
    public sealed class ZoneBorder
    {
        /// <summary>The chunk on this zone's side.</summary>
        [Key(0)] public ChunkCoord Chunk { get; set; }

        [Key(1)] public ChunkSide Side { get; set; }

        /// <summary>The neighboring zone's key.</summary>
        [Key(2)] public string Zone { get; set; } = "";
    }

    /// <summary>A doorway into another zone: walking into its box takes a player to the other zone's matching entrance.</summary>
    [MessagePackObject]
    public sealed class ZoneEntrance
    {
        /// <summary>The entrance's name, unique in the zone.</summary>
        [Key(0)] public string Name { get; set; } = "";

        /// <summary>The doorway box's center, in world meters.</summary>
        [Key(1)] public Vector3 Position { get; set; }

        /// <summary>The box's turn about the vertical axis, in degrees clockwise from +Z.</summary>
        [Key(2)] public float Yaw { get; set; }

        [Key(3)] public Vector3 Size { get; set; }

        /// <summary>The zone it leads to.</summary>
        [Key(4)] public string ToZone { get; set; } = "";

        /// <summary>The entrance in that zone where players arrive.</summary>
        [Key(5)] public string ToEntrance { get; set; } = "";
    }

    /// <summary>A loading room: an area a party gathers in; once one member goes on, <see cref="Entrance"/> closes.</summary>
    [MessagePackObject]
    public sealed class TransitionRoom
    {
        /// <summary>The room's name, unique in the zone.</summary>
        [Key(0)] public string Name { get; set; } = "";

        /// <summary>The room's box center, in world meters.</summary>
        [Key(1)] public Vector3 Position { get; set; }

        /// <summary>The box's turn about the vertical axis, in degrees clockwise from +Z.</summary>
        [Key(2)] public float Yaw { get; set; }

        [Key(3)] public Vector3 Size { get; set; }

        /// <summary>The name of the entrance into the room, which closes once the party has gone on.</summary>
        [Key(4)] public string Entrance { get; set; } = "";
    }
}
