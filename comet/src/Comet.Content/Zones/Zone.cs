using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// A zone: its terrain pieces, borders, entrances, transition rooms and chunks, authored as a folder of
    /// files and compiled whole into the content file.
    /// </summary>
    [MessagePackObject]
    public sealed class Zone : IContentEntry
    {
        [Key(0)]
        public int Number { get; set; }

        /// <summary>The zone's id (<c>zone.meadow</c>).</summary>
        [Key(1)]
        public string Id { get; set; } = "";

        /// <summary>
        /// For a zone on its own local grid (a dungeon), the region chunk its chunk (0, 0) sits on, so its
        /// scenery shows in distant LODs; null for open zones.
        /// </summary>
        [Key(2)]
        public ChunkCoord? WorldAnchor { get; set; }

        /// <summary>The terrain pieces, with their zone-wide settings; tiles in chunks refer to them by name.</summary>
        [Key(3)]
        public TerrainPiece[] Pieces { get; set; } = Array.Empty<TerrainPiece>();

        /// <summary>The chunk edges where this zone meets a neighbor.</summary>
        [Key(4)]
        public ZoneBorder[] Borders { get; set; } = Array.Empty<ZoneBorder>();

        /// <summary>Doorways into other zones, such as a dungeon's mouth.</summary>
        [Key(5)]
        public ZoneEntrance[] Entrances { get; set; } = Array.Empty<ZoneEntrance>();

        /// <summary>Loading rooms: areas a party gathers in before an entrance closes behind them.</summary>
        [Key(6)]
        public TransitionRoom[] TransitionRooms { get; set; } = Array.Empty<TransitionRoom>();

        /// <summary>The chunks the zone owns.</summary>
        [Key(7)]
        public ZoneChunk[] Chunks { get; set; } = Array.Empty<ZoneChunk>();

        /// <summary>This zone's own height in meters below which a fall is out of the world; null uses the game's.</summary>
        [Key(8)]
        public float? KillHeight { get; set; }

        /// <summary>The height base when a zone sets none.</summary>
        public const float DefaultHeightBase = -100f;

        /// <summary>
        /// The height in meters of terrain sample value 0, so ground covers this + 0.01 m up to this + 655.35 m (<see cref="Heightmap.Scale"/>).
        /// </summary>
        [Key(9)]
        public float HeightBase { get; set; } = DefaultHeightBase;
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

        [Key(0)]
        public int X { get; }

        [Key(1)]
        public int Z { get; }

        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);

        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);

        /// <summary>The chunk holding a world position (edges belong to the chunk on their high side).</summary>
        public static ChunkCoord Containing(Vector3 position) =>
            new ChunkCoord((int)System.MathF.Floor(position.X / ZoneGrid.ChunkSize), (int)System.MathF.Floor(position.Z / ZoneGrid.ChunkSize));

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
        [Key(0)]
        public string Name { get; set; } = "";

        /// <summary>Where the piece's skirts end when it has no underside map: a flat bottom this many meters below its lowest ground.</summary>
        [Key(1)]
        public float SkirtDepth { get; set; } = 4;

        /// <summary>Whether the piece's outer edge gets skirts (off for main ground that nothing sees below).</summary>
        [Key(2)]
        public bool RimSkirts { get; set; } = true;

        /// <summary>Whether the edges of holes inside the piece get skirts (chasm walls).</summary>
        [Key(3)]
        public bool HoleSkirts { get; set; } = true;

        /// <summary>The color of the piece's unpainted underside (its skirt walls and bottom) as 0xRRGGBB, if not the game's.</summary>
        [Key(4)]
        public uint? UnderColor { get; set; }

        /// <summary>The color of the piece's unpainted ground as 0xRRGGBB, if not the game's.</summary>
        [Key(5)]
        public uint? GroundColor { get; set; }
    }

    /// <summary>One chunk of a zone: the terrain tiles, placed objects, spawns and volumes whose pivots are in it.</summary>
    [MessagePackObject]
    public sealed class ZoneChunk
    {
        [Key(0)]
        public ChunkCoord Coord { get; set; }

        [Key(1)]
        public TerrainTile[] Tiles { get; set; } = Array.Empty<TerrainTile>();

        [Key(2)]
        public Placement[] Placements { get; set; } = Array.Empty<Placement>();

        [Key(3)]
        public SpawnPoint[] Spawns { get; set; } = Array.Empty<SpawnPoint>();

        [Key(4)]
        public CollisionVolume[] Volumes { get; set; } = Array.Empty<CollisionVolume>();

        [Key(5)]
        public Teleporter[] Teleporters { get; set; } = Array.Empty<Teleporter>();

        [Key(6)]
        public Trigger[] Triggers { get; set; } = Array.Empty<Trigger>();

        [Key(7)]
        public Sign[] Signs { get; set; } = Array.Empty<Sign>();

        /// <summary>
        /// Every sign in the chunk with the world point it's shown over: its own signs, then those on its
        /// spawns, teleporters and triggers.
        /// </summary>
        public IEnumerable<(Vector3 Point, Sign Sign)> AllSigns()
        {
            foreach (var sign in Signs.Where(s => !s.Off))
            {
                yield return (sign.Position, sign);
            }

            foreach (var volume in Spawns.Select(s => s.Volume).Concat(Teleporters.Select(t => t.Volume)).Concat(Triggers.Select(t => t.Volume)))
            {
                if (volume.Sign is { Off: false } sign)
                {
                    yield return (volume.ToWorld(sign.Position), sign);
                }
            }
        }
    }

    /// <summary>
    /// A terrain piece's part of one chunk: a heightmap whose sample (0, 0) sits at chunk sample (<see cref="OriginX"/>, <see cref="OriginZ"/>).
    /// </summary>
    [MessagePackObject]
    public sealed class TerrainTile
    {
        /// <summary>The <see cref="TerrainPiece.Name"/> of the piece this tile belongs to.</summary>
        [Key(0)]
        public string Piece { get; set; } = "";

        /// <summary>The chunk sample column of the tile's first column (0 to <see cref="ZoneGrid.ChunkSize"/>).</summary>
        [Key(1)]
        public int OriginX { get; set; }

        [Key(2)]
        public int OriginZ { get; set; }

        /// <summary>The ground's heights; holes mark where the piece has no ground.</summary>
        [Key(3)]
        public Heightmap Heights { get; set; } = new Heightmap();

        /// <summary>The bottom of the piece's skirts, if it has a painted underside rather than a flat bottom.</summary>
        [Key(4)]
        public Heightmap? Underside { get; set; }

        [Key(5)]
        public ColorMap? Colors { get; set; }

        /// <summary>Painted colors of the underside and the skirt walls above it, if any; only with an <see cref="Underside"/>.</summary>
        [Key(6)]
        public ColorMap? UndersideColors { get; set; }
    }

    /// <summary>
    /// A prop, primitive or group placed in a chunk (or a group's part). A primitive's scale is its size in
    /// meters, and its mesh is built at that size.
    /// </summary>
    [MessagePackObject]
    public sealed class Placement
    {
        /// <summary>The prop's id (<c>prop.rock</c>), or empty for a primitive or a group.</summary>
        [Key(0)]
        public string Prop { get; set; } = "";

        /// <summary>The pivot's world position, in meters; it lies in the placement's chunk.</summary>
        [Key(1)]
        public Vector3 Position { get; set; }

        /// <summary>Rotation as Euler angles in degrees, applied in Unity's order (Z, then X, then Y).</summary>
        [Key(2)]
        public Vector3 Rotation { get; set; }

        [Key(3)]
        public Vector3 Scale { get; set; } = Vector3.One;

        /// <summary>A tag for triggers' toggle effects to find this placement by; several may share one, to toggle together. Null for most.</summary>
        [Key(4)]
        public string? Tag { get; set; }

        /// <summary>The placement's own color as 0xRRGGBB, or null for its prop's.</summary>
        [Key(5)]
        public uint? Color { get; set; }

        /// <summary>The primitive placed, or null for a prop.</summary>
        [Key(6)]
        public Primitive? Primitive { get; set; }

        /// <summary>Whether this is blockout: a stand-in for finished geometry, drawn in a dev look while a level is built.</summary>
        [Key(7)]
        public bool Blockout { get; set; }

        /// <summary>The group placed (<c>group.tree</c>), or null for a prop or a primitive.</summary>
        [Key(8)]
        public string? Group { get; set; }

        /// <summary>A group part's role (<c>body</c>, <c>eyes</c>), for game code to find or color the parts that play it; null for most.</summary>
        [Key(9)]
        public string? Role { get; set; }

        /// <summary>
        /// A group copy's colors by role (0xRRGGBB): parts with a listed role, however deeply nested, take
        /// it in place of their own; an outer copy's wins. Null for most.
        /// </summary>
        [Key(10)]
        public Dictionary<string, uint>? Colors { get; set; }

        /// <summary>Whether the placement starts off: hidden and not solid until a trigger turns it on.</summary>
        [Key(11)]
        public bool Off { get; set; }

        /// <summary>
        /// The transform from mesh space to the world: prop scale, rotation, position; primitive (built at
        /// size) rotation, position.
        /// </summary>
        public Matrix4x4 Matrix() =>
            (Primitive is null ? Matrix4x4.CreateScale(Scale) : Matrix4x4.Identity)
            * Matrix4x4.CreateFromQuaternion(ZoneGrid.Rotation(Rotation))
            * Matrix4x4.CreateTranslation(Position);
    }

    /// <summary>How a player faces on arriving at a spawn point or teleporter.</summary>
    public enum SpawnFacing
    {
        /// <summary>The way the trigger volume faces (its yaw).</summary>
        Direction,

        /// <summary>Toward the volume's center (for a scattered arrival).</summary>
        TowardPoint,
        Random,
    }

    /// <summary>When a trigger fires again after firing.</summary>
    public enum Rearm
    {
        /// <summary>Not until whoever it fired for has stepped out of it.</summary>
        StepOff,

        /// <summary>Again every stand time while still inside it.</summary>
        Repeat,
    }

    /// <summary>
    /// What spawn points, teleporters and triggers share: an invisible box, cylinder or sphere that fires once a player's feet have
    /// been inside for its stand time, with an optional solid pad under it.
    /// </summary>
    [MessagePackObject]
    public sealed class TriggerVolume
    {
        /// <summary>The volume's kind: a box, a cylinder round around Y, or a sphere (an ellipsoid when its size is uneven).</summary>
        [Key(0)]
        public PrimitiveKind Kind { get; set; } = PrimitiveKind.Box;

        /// <summary>The volume's bottom center, in world meters; it lies in its chunk.</summary>
        [Key(1)]
        public Vector3 Position { get; set; }

        /// <summary>The volume's turn about the vertical axis, in degrees clockwise from +Z.</summary>
        [Key(2)]
        public float Yaw { get; set; }

        /// <summary>The volume's full size along its own axes, in meters.</summary>
        [Key(3)]
        public Vector3 Size { get; set; } = Vector3.One;

        /// <summary>How long a player stands inside before it fires, in seconds (0 fires at once).</summary>
        [Key(4)]
        public float StandSeconds { get; set; }

        [Key(5)]
        public Rearm Rearm { get; set; }

        /// <summary>The solid primitive drawn under the volume (in world space, never toggled), or null for none.</summary>
        [Key(6)]
        public Placement? Pad { get; set; }

        /// <summary>A sign shown with it (its position from the volume's bottom center, turned with its yaw), or null for none.</summary>
        [Key(7)]
        public Sign? Sign { get; set; }

        /// <summary>A point in the volume's own space (from its bottom center, turned with its yaw) in world meters.</summary>
        public Vector3 ToWorld(Vector3 local)
        {
            var radians = Yaw * MathF.PI / 180;
            var (sin, cos) = (MathF.Sin(radians), MathF.Cos(radians));
            return Position + new Vector3(local.X * cos + local.Z * sin, local.Y, -local.X * sin + local.Z * cos);
        }
    }

    /// <summary>A place players arrive: a random spot in its volume's footprint. Its id names its zone (<c>spawn.meadow.gate</c>).</summary>
    [MessagePackObject]
    public sealed class SpawnPoint : IContentEntry
    {
        [Key(0)]
        public int Number { get; set; }

        /// <summary>The spawn's id (<c>spawn.meadow.gate</c>), naming its zone.</summary>
        [Key(1)]
        public string Id { get; set; } = "";

        [Key(2)]
        public TriggerVolume Volume { get; set; } = new TriggerVolume();

        [Key(3)]
        public SpawnFacing Facing { get; set; }
    }

    /// <summary>
    /// A pad that sends players to another teleporter, arriving as at a spawn point. Its id names its zone
    /// (<c>teleporter.meadow.to_cave</c>).
    /// </summary>
    [MessagePackObject]
    public sealed class Teleporter : IContentEntry
    {
        [Key(0)]
        public int Number { get; set; }

        /// <summary>The teleporter's id (<c>teleporter.meadow.to_cave</c>), naming its zone.</summary>
        [Key(1)]
        public string Id { get; set; } = "";

        [Key(2)]
        public TriggerVolume Volume { get; set; } = new TriggerVolume();

        /// <summary>How players arriving here face.</summary>
        [Key(3)]
        public SpawnFacing Facing { get; set; }

        /// <summary>The teleporter it sends players to (<c>teleporter.cave.to_meadow</c>), in any zone.</summary>
        [Key(4)]
        public string Target { get; set; } = "";

        /// <summary>The color its pad turns toward while a player stands in it, as 0xRRGGBB, or null to leave it to the game.</summary>
        [Key(5)]
        public uint? ChargeColor { get; set; }
    }

    /// <summary>A trigger with toggle effects, such as a plate that opens a door. Its id names its zone (<c>trigger.meadow.gate_plate</c>).</summary>
    [MessagePackObject]
    public sealed class Trigger : IContentEntry
    {
        [Key(0)]
        public int Number { get; set; }

        /// <summary>The trigger's id (<c>trigger.meadow.gate_plate</c>), naming its zone.</summary>
        [Key(1)]
        public string Id { get; set; } = "";

        [Key(2)]
        public TriggerVolume Volume { get; set; } = new TriggerVolume();

        [Key(3)]
        public ToggleEffect[] Effects { get; set; } = Array.Empty<ToggleEffect>();

        /// <summary>The color its pad turns toward while a player stands in it, as 0xRRGGBB, or null to leave it to the game.</summary>
        [Key(4)]
        public uint? ChargeColor { get; set; }
    }

    /// <summary>Text shown over a point, such as a label by a teleporter. How it's drawn is game policy.</summary>
    [MessagePackObject]
    public sealed class Sign
    {
        /// <summary>The most characters a sign's text has.</summary>
        public const int MaxText = 200;

        /// <summary>The farthest a sign can be seen, in meters.</summary>
        public const float MaxShowDistance = 200;

        /// <summary>The smallest and largest font sizes of screen-sized signs, in the game's interface pixels.</summary>
        public const float MinFontSize = 8,
            MaxFontSize = 48;

        /// <summary>The smallest and largest text heights of world-sized signs, in meters.</summary>
        public const float MinTextHeight = 0.05f,
            MaxTextHeight = 5;

        /// <summary>The point the text is shown over, in world meters; a trigger's sign is placed from its volume's bottom center instead.</summary>
        [Key(0)]
        public Vector3 Position { get; set; }

        /// <summary>The text, plain (line breaks allowed).</summary>
        [Key(1)]
        public string Text { get; set; } = "";

        /// <summary>The text's color, as 0xRRGGBB.</summary>
        [Key(2)]
        public uint TextColor { get; set; } = 0xFFFFFF;

        /// <summary>The color of the panel behind the text, as 0xRRGGBB; kept while there's no panel.</summary>
        [Key(3)]
        public uint BackgroundColor { get; set; }

        /// <summary>How opaque the panel is, from 0 to 1.</summary>
        [Key(4)]
        public float BackgroundOpacity { get; set; } = 1;

        /// <summary>How far away it's shown, in meters.</summary>
        [Key(5)]
        public float ShowDistance { get; set; } = 45;

        /// <summary>A screen-sized sign's text size, in the game's interface pixels.</summary>
        [Key(6)]
        public float FontSize { get; set; } = 13;

        /// <summary>
        /// Whether the panel has a little arrow under it pointing at the point, as speech bubbles do; only
        /// with <see cref="Background"/>.
        /// </summary>
        [Key(7)]
        public bool Arrow { get; set; }

        /// <summary>Whether it's one size on screen at any distance (like a name tag) rather than sized in the world.</summary>
        [Key(8)]
        public bool ScreenSized { get; set; }

        /// <summary>A world-sized sign's text size: the height of its font, in meters.</summary>
        [Key(9)]
        public float TextHeight { get; set; } = 0.3f;

        /// <summary>Whether there's a panel behind the text (and so the arrow, if it has one).</summary>
        [Key(10)]
        public bool Background { get; set; }

        /// <summary>Whether it's turned off: kept in the zone but not shown (left out of <see cref="ZoneChunk.AllSigns"/>).</summary>
        [Key(11)]
        public bool Off { get; set; }
    }

    /// <summary>Flips every placement with a tag on or off, and optionally back after a time.</summary>
    [MessagePackObject]
    public sealed class ToggleEffect
    {
        [Key(0)]
        public string Tag { get; set; } = "";

        /// <summary>Seconds until they flip back, or 0 to stay. Firing again before then restarts the wait instead of flipping.</summary>
        [Key(1)]
        public float RevertSeconds { get; set; }
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
        [Key(0)]
        public VolumeKind Kind { get; set; }

        /// <summary>The box's center, in world meters.</summary>
        [Key(1)]
        public Vector3 Position { get; set; }

        /// <summary>Euler angles in degrees, as for <see cref="Placement.Rotation"/>.</summary>
        [Key(2)]
        public Vector3 Rotation { get; set; }

        /// <summary>The box's full size along its own axes, in meters.</summary>
        [Key(3)]
        public Vector3 Size { get; set; }
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
        [Key(0)]
        public ChunkCoord Chunk { get; set; }

        [Key(1)]
        public ChunkSide Side { get; set; }

        /// <summary>The neighboring zone's id.</summary>
        [Key(2)]
        public string Zone { get; set; } = "";
    }

    /// <summary>A doorway into another zone: walking into its box takes a player to the other zone's matching entrance.</summary>
    [MessagePackObject]
    public sealed class ZoneEntrance
    {
        /// <summary>The entrance's name, unique in the zone.</summary>
        [Key(0)]
        public string Name { get; set; } = "";

        /// <summary>The doorway box's center, in world meters.</summary>
        [Key(1)]
        public Vector3 Position { get; set; }

        /// <summary>The box's turn about the vertical axis, in degrees clockwise from +Z.</summary>
        [Key(2)]
        public float Yaw { get; set; }

        [Key(3)]
        public Vector3 Size { get; set; }

        [Key(4)]
        public string ToZone { get; set; } = "";

        /// <summary>The entrance in that zone where players arrive.</summary>
        [Key(5)]
        public string ToEntrance { get; set; } = "";
    }

    /// <summary>A loading room: an area a party gathers in; once one member goes on, <see cref="Entrance"/> closes.</summary>
    [MessagePackObject]
    public sealed class TransitionRoom
    {
        /// <summary>The room's name, unique in the zone.</summary>
        [Key(0)]
        public string Name { get; set; } = "";

        /// <summary>The room's box center, in world meters.</summary>
        [Key(1)]
        public Vector3 Position { get; set; }

        /// <summary>The box's turn about the vertical axis, in degrees clockwise from +Z.</summary>
        [Key(2)]
        public float Yaw { get; set; }

        [Key(3)]
        public Vector3 Size { get; set; }

        /// <summary>The name of the entrance into the room, which closes once the party has gone on.</summary>
        [Key(4)]
        public string Entrance { get; set; } = "";
    }
}
