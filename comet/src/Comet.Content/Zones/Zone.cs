using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MessagePack;

namespace Comet.Content
{
    [MessagePackObject]
    public sealed class Zone : IContentEntry
    {
        [Key(0)]
        public int Number { get; set; }

        [Key(1)]
        public string Id { get; set; } = "";

        /// <summary>
        /// For a zone on its own local grid (a dungeon), the region chunk its chunk (0, 0) sits on, so its
        /// scenery shows in distant LODs; null for open zones.
        /// </summary>
        [Key(2)]
        public ChunkCoord? WorldAnchor { get; set; }

        [Key(3)]
        public TerrainPiece[] Pieces { get; set; } = Array.Empty<TerrainPiece>();

        [Key(4)]
        public ZoneBorder[] Borders { get; set; } = Array.Empty<ZoneBorder>();

        [Key(5)]
        public ZoneEntrance[] Entrances { get; set; } = Array.Empty<ZoneEntrance>();

        [Key(6)]
        public TransitionRoom[] TransitionRooms { get; set; } = Array.Empty<TransitionRoom>();

        [Key(7)]
        public ZoneChunk[] Chunks { get; set; } = Array.Empty<ZoneChunk>();

        /// <summary>This zone's own height in meters below which a fall is out of the world; null uses the game's.</summary>
        [Key(8)]
        public float? KillHeight { get; set; }

        public const float DefaultHeightBase = -100f;

        /// <summary>
        /// The height in meters of terrain sample value 0, so ground covers this + 0.01 m up to this + 655.35 m (<see cref="Heightmap.Scale"/>).
        /// </summary>
        [Key(9)]
        public float HeightBase { get; set; } = DefaultHeightBase;
    }

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

    [MessagePackObject]
    public sealed class TerrainPiece
    {
        [Key(0)]
        public string Name { get; set; } = "";

        /// <summary>Where the piece's skirts end when it has no underside map: a flat bottom this many meters below its lowest ground.</summary>
        [Key(1)]
        public float SkirtDepth { get; set; } = 4;

        [Key(2)]
        public bool RimSkirts { get; set; } = true;

        [Key(3)]
        public bool HoleSkirts { get; set; } = true;

        /// <summary>The color of the piece's unpainted underside (its skirt walls and bottom) as 0xRRGGBB, if not the game's.</summary>
        [Key(4)]
        public uint? UnderColor { get; set; }

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
        [Key(0)]
        public string Piece { get; set; } = "";

        /// <summary>The chunk sample column of the tile's first column (0 to <see cref="ZoneGrid.ChunkSize"/>).</summary>
        [Key(1)]
        public int OriginX { get; set; }

        [Key(2)]
        public int OriginZ { get; set; }

        [Key(3)]
        public Heightmap Heights { get; set; } = new Heightmap();

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

        [Key(1)]
        public Vector3 Position { get; set; }

        /// <summary>Rotation as Euler angles in degrees, applied in Unity's order (Z, then X, then Y).</summary>
        [Key(2)]
        public Vector3 Rotation { get; set; }

        [Key(3)]
        public Vector3 Scale { get; set; } = Vector3.One;

        [Key(4)]
        public string? Tag { get; set; }

        /// <summary>The placement's own color as 0xRRGGBB, or null for its prop's.</summary>
        [Key(5)]
        public uint? Color { get; set; }

        /// <summary>The primitive placed, or null for a prop.</summary>
        [Key(6)]
        public Primitive? Primitive { get; set; }

        [Key(7)]
        public bool Blockout { get; set; }

        /// <summary>The group placed (<c>group.tree</c>), or null for a prop or a primitive.</summary>
        [Key(8)]
        public string? Group { get; set; }

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

        public Matrix4x4 Matrix() =>
            (Primitive is null ? Matrix4x4.CreateScale(Scale) : Matrix4x4.Identity)
            * Matrix4x4.CreateFromQuaternion(ZoneGrid.Rotation(Rotation))
            * Matrix4x4.CreateTranslation(Position);
    }

    public enum SpawnFacing
    {
        Direction,

        TowardPoint,
        Random,
    }

    public enum Rearm
    {
        StepOff,

        Repeat,
    }

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

        [Key(1)]
        public string Id { get; set; } = "";

        [Key(2)]
        public TriggerVolume Volume { get; set; } = new TriggerVolume();

        [Key(3)]
        public SpawnFacing Facing { get; set; }
    }

    [MessagePackObject]
    public sealed class Teleporter : IContentEntry
    {
        [Key(0)]
        public int Number { get; set; }

        [Key(1)]
        public string Id { get; set; } = "";

        [Key(2)]
        public TriggerVolume Volume { get; set; } = new TriggerVolume();

        [Key(3)]
        public SpawnFacing Facing { get; set; }

        [Key(4)]
        public string Target { get; set; } = "";

        [Key(5)]
        public uint? ChargeColor { get; set; }
    }

    [MessagePackObject]
    public sealed class Trigger : IContentEntry
    {
        [Key(0)]
        public int Number { get; set; }

        [Key(1)]
        public string Id { get; set; } = "";

        [Key(2)]
        public TriggerVolume Volume { get; set; } = new TriggerVolume();

        [Key(3)]
        public ToggleEffect[] Effects { get; set; } = Array.Empty<ToggleEffect>();

        [Key(4)]
        public uint? ChargeColor { get; set; }
    }

    [MessagePackObject]
    public sealed class Sign
    {
        public const int MaxText = 200;

        /// <summary>The farthest a sign can be seen, in meters.</summary>
        public const float MaxShowDistance = 200;

        public const float MinFontSize = 8,
            MaxFontSize = 48;

        /// <summary>The smallest and largest text heights of world-sized signs, in meters.</summary>
        public const float MinTextHeight = 0.05f,
            MaxTextHeight = 5;

        [Key(0)]
        public Vector3 Position { get; set; }

        [Key(1)]
        public string Text { get; set; } = "";

        [Key(2)]
        public uint TextColor { get; set; } = 0xFFFFFF;

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

        [Key(8)]
        public bool ScreenSized { get; set; }

        /// <summary>A world-sized sign's text size: the height of its font, in meters.</summary>
        [Key(9)]
        public float TextHeight { get; set; } = 0.3f;

        [Key(10)]
        public bool Background { get; set; }

        [Key(11)]
        public bool Off { get; set; }
    }

    [MessagePackObject]
    public sealed class ToggleEffect
    {
        [Key(0)]
        public string Tag { get; set; } = "";

        /// <summary>Seconds until they flip back, or 0 to stay. Firing again before then restarts the wait instead of flipping.</summary>
        [Key(1)]
        public float RevertSeconds { get; set; }
    }

    public enum VolumeKind
    {
        Wall,
        Water,
    }

    [MessagePackObject]
    public sealed class CollisionVolume
    {
        [Key(0)]
        public VolumeKind Kind { get; set; }

        /// <summary>The box's center, in world meters.</summary>
        [Key(1)]
        public Vector3 Position { get; set; }

        [Key(2)]
        public Vector3 Rotation { get; set; }

        [Key(3)]
        public Vector3 Size { get; set; }
    }

    public enum ChunkSide
    {
        /// <summary>The +Z edge.</summary>
        North,

        /// <summary>The +X edge.</summary>
        East,
        South,
        West,
    }

    [MessagePackObject]
    public sealed class ZoneBorder
    {
        [Key(0)]
        public ChunkCoord Chunk { get; set; }

        [Key(1)]
        public ChunkSide Side { get; set; }

        [Key(2)]
        public string Zone { get; set; } = "";
    }

    [MessagePackObject]
    public sealed class ZoneEntrance
    {
        [Key(0)]
        public string Name { get; set; } = "";

        [Key(1)]
        public Vector3 Position { get; set; }

        [Key(2)]
        public float Yaw { get; set; }

        [Key(3)]
        public Vector3 Size { get; set; }

        [Key(4)]
        public string ToZone { get; set; } = "";

        [Key(5)]
        public string ToEntrance { get; set; } = "";
    }

    [MessagePackObject]
    public sealed class TransitionRoom
    {
        [Key(0)]
        public string Name { get; set; } = "";

        [Key(1)]
        public Vector3 Position { get; set; }

        [Key(2)]
        public float Yaw { get; set; }

        [Key(3)]
        public Vector3 Size { get; set; }

        [Key(4)]
        public string Entrance { get; set; } = "";
    }
}
