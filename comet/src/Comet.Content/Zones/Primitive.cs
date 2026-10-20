using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// The shapes Comet builds in code, for blocking out levels and for simple geometry. Each stands on its pivot (the
    /// bottom center) and at a placement scale of 1 fits a 1 m box, so a placement's scale is its size in meters.
    /// </summary>
    public enum PrimitiveKind
    {
        Box,

        /// <summary>A ramp: its slope rises toward +X, with a vertical face at +X.</summary>
        Wedge,

        /// <summary>Round around Y; sides from the polygon budget unless fixed.</summary>
        Cylinder,

        /// <summary>Round around Y, to a point at the top; sides from the polygon budget unless fixed (4 sides make a square pyramid).</summary>
        Cone,

        /// <summary>Sides and rings from the polygon budget unless fixed.</summary>
        Sphere,

        /// <summary>Steps climbing toward +X.</summary>
        Stairs,

        /// <summary>A wall (its face toward -Z) with a door-shaped opening at the bottom center.</summary>
        Wall,

        /// <summary>A wall with a round-topped opening at the bottom center; the curve's sides come from the polygon budget.</summary>
        Arch,

        // The regular solids other than the cube (a box), each standing as a player's shape does: 1 m tall at a scale of
        // 1, regular at a uniform scale, and as wide as that makes it.

        /// <summary>A regular tetrahedron standing on a face, with a corner at the back and a face toward +Z.</summary>
        Tetrahedron,

        /// <summary>A regular octahedron standing on a tip, its middle square to the axes.</summary>
        Octahedron,

        /// <summary>A regular dodecahedron standing on a face.</summary>
        Dodecahedron,

        /// <summary>A regular icosahedron standing on a face.</summary>
        Icosahedron,
    }

    /// <summary>Primitive kinds by the names zone files and the editor use for them (<c>box</c>, <c>wedge</c>…).</summary>
    public static class PrimitiveKinds
    {
        /// <summary>Every kind, in order.</summary>
        public static readonly PrimitiveKind[] All = (PrimitiveKind[])System.Enum.GetValues(typeof(PrimitiveKind));

        /// <summary><paramref name="kind"/>'s name: its enum name in snake case.</summary>
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

        /// <summary>The kind named <paramref name="name"/>, or null if none is.</summary>
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

    /// <summary>A primitive's kind and the values that shape it beyond its size (the placement's scale).</summary>
    [MessagePackObject]
    public sealed class Primitive
    {
        [Key(0)] public PrimitiveKind Kind { get; set; }

        /// <summary>
        /// A fixed number of sides for a round kind (a cylinder, cone or sphere around Y, or an arch's curve), or null to
        /// take them from the polygon budget.
        /// </summary>
        [Key(1)] public int? Sides { get; set; }

        /// <summary>A box's bevel: how far its edges are cut back, in meters (0 for sharp edges).</summary>
        [Key(2)] public float Bevel { get; set; }

        /// <summary>A stairway's number of steps, or 0 for steps about 0.25 m high.</summary>
        [Key(3)] public int Steps { get; set; }

        /// <summary>A wall's or arch's opening: its width and its height (an arch's to the top of its curve), in meters.</summary>
        [Key(4)] public float OpeningWidth { get; set; }

        [Key(5)] public float OpeningHeight { get; set; }

        /// <summary>Whether two primitives build the same mesh at the same size.</summary>
        public bool SameAs(Primitive other) =>
            Kind == other.Kind && Sides == other.Sides && Bevel == other.Bevel && Steps == other.Steps &&
            OpeningWidth == other.OpeningWidth && OpeningHeight == other.OpeningHeight;
    }

    /// <summary>
    /// How finely round primitives are cut, the same for all of a game's primitives so large and small ones look
    /// equally faceted: sides are about <see cref="FacetLength"/> long, within the limits.
    /// </summary>
    [MessagePackObject]
    public sealed class PolygonBudget
    {
        /// <summary>About how long each facet is around a round primitive, in meters.</summary>
        [Key(0)] public float FacetLength { get; set; } = 0.5f;

        /// <summary>The fewest sides a round primitive has, however small.</summary>
        [Key(1)] public int MinSides { get; set; } = 6;

        /// <summary>The most sides a round primitive has, however large.</summary>
        [Key(2)] public int MaxSides { get; set; } = 48;

        /// <summary>The sides of a circle of <paramref name="circumference"/> meters.</summary>
        public int Sides(float circumference)
        {
            var sides = (int)System.MathF.Round(circumference / System.MathF.Max(FacetLength, 0.01f));
            return System.Math.Clamp(sides, MinSides, MaxSides);
        }
    }
}
