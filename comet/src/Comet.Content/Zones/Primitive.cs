using MessagePack;

namespace Comet.Content
{
    /// <summary>Each stands on its pivot (bottom center) and at scale 1 fits a 1 m box, so scale is size in meters.</summary>
    public enum PrimitiveKind
    {
        Box,

        /// <summary>A ramp: its slope rises toward +X, with a vertical face at +X.</summary>
        Wedge,

        Cylinder,

        Cone,

        Sphere,

        /// <summary>Steps climbing toward +X.</summary>
        Stairs,

        /// <summary>A plain wall: a box with its face toward -Z.</summary>
        Wall,

        Doorway,

        Arch,

        // At scale 1 each regular solid holds as much as a 1 m cube; a uniform scale keeps them regular.

        /// <summary>A regular tetrahedron standing on a face, with a corner at the back and a face toward +Z.</summary>
        Tetrahedron,

        /// <summary>A regular octahedron standing on a tip, its middle square to the axes.</summary>
        Octahedron,

        Dodecahedron,

        Icosahedron,
    }

    public static class PrimitiveKinds
    {
        public static readonly PrimitiveKind[] All = (PrimitiveKind[])System.Enum.GetValues(typeof(PrimitiveKind));

        public static string Name(PrimitiveKind kind)
        {
            var text = new System.Text.StringBuilder();
            foreach (var c in kind.ToString())
            {
                if (char.IsUpper(c) && text.Length > 0)
                {
                    text.Append('_');
                }

                text.Append(char.ToLowerInvariant(c));
            }

            return text.ToString();
        }

        public static PrimitiveKind? Named(string name)
        {
            foreach (var kind in All)
            {
                if (Name(kind) == name)
                {
                    return kind;
                }
            }

            return null;
        }
    }

    [MessagePackObject]
    public sealed class Primitive
    {
        [Key(0)]
        public PrimitiveKind Kind { get; set; }

        /// <summary>A fixed number of sides for a round kind (including an arch's curve), or null for the polygon budget's.</summary>
        [Key(1)]
        public int? Sides { get; set; }

        /// <summary>A box's bevel: how far its edges are cut back, in meters (0 for sharp edges).</summary>
        [Key(2)]
        public float Bevel { get; set; }

        /// <summary>A stairway's number of steps, or 0 for steps about 0.25 m high (at most <see cref="MaxSteps"/>).</summary>
        [Key(3)]
        public int Steps { get; set; }

        public const int MaxSteps = 256;

        /// <summary>A doorway's opening width and height, in meters.</summary>
        [Key(4)]
        public float OpeningWidth { get; set; }

        [Key(5)]
        public float OpeningHeight { get; set; }

        /// <summary>An arch's thickness (its legs and band), in meters.</summary>
        [Key(6)]
        public float Thickness { get; set; }
    }

    [MessagePackObject]
    public sealed class PolygonBudget
    {
        /// <summary>About how long each facet is, in meters.</summary>
        [Key(0)]
        public float FacetLength { get; set; } = 0.5f;

        [Key(1)]
        public int MinSides { get; set; } = 6;

        [Key(2)]
        public int MaxSides { get; set; } = 48;

        /// <summary>The sides of a circle of <paramref name="circumference"/> meters.</summary>
        public int Sides(float circumference)
        {
            var sides = (int)System.MathF.Round(circumference / System.MathF.Max(FacetLength, 0.01f));
            return System.Math.Clamp(sides, MinSides, MaxSides);
        }
    }
}
