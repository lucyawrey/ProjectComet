using Comet.Content;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// An asset or a primitive placed in a zone opened in the editor: the object's transform is the placement's. A
    /// primitive's mesh is built at its size, so its scale is folded into <see cref="size"/> once a resize ends.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class ZonePlacement : MonoBehaviour
    {
        [Tooltip("The asset's key (asset.prop); empty for a primitive.")]
        public string asset = "";

        [Tooltip("Whether this places a primitive, built in code, instead of an asset.")]
        public bool isPrimitive;

        [Tooltip("The primitive's kind.")]
        public PrimitiveKind kind;

        [Tooltip("The primitive's size in meters (at 1, 1, 1 a regular solid is 1 m tall).")]
        public Vector3 size = Vector3.one;

        [Tooltip("A round primitive's sides, or 0 to take them from the polygon budget.")]
        [Min(0)] public int sides;

        [Tooltip("How far a box's edges are cut back, in meters.")]
        [Min(0)] public float bevel;

        [Tooltip("A stairway's steps, or 0 for steps about 0.25 m high.")]
        [Min(0)] public int steps;

        [Tooltip("A wall's or arch's opening, width and height in meters (0 for none).")]
        public Vector2 opening;

        [Tooltip("Blockout: a stand-in for finished geometry, drawn in a dev look and reported by the content build.")]
        public bool blockout;

        [Tooltip("An optional name, unique in the zone, for game code to find it by (a spawn point's marker has the spawn's key).")]
        public string placementName = "";

        // Drawn by the editor's inspector as one color field with a reset button: editing it gives the placement its
        // own color, resetting goes back to the default (its asset's, else the game's).
        [HideInInspector] public bool ownColor;
        [HideInInspector] public Color color = Color.gray;
        [SerializeField, HideInInspector] private Color defaultColor = Color.gray;

        /// <summary>How finely round primitives are cut while editing: the game's, set by the editor (Comet › Zones › Polygon Budget).</summary>
        public static PolygonBudget Budget { get; set; } = new PolygonBudget();

        /// <summary>The primitive placed, or null for an asset.</summary>
        public Primitive? Primitive => isPrimitive
            ? new Primitive
            {
                Kind = kind, Sides = sides > 0 ? sides : (int?)null, Bevel = bevel, Steps = steps,
                OpeningWidth = opening.x, OpeningHeight = opening.y,
            }
            : null;

        /// <summary>Builds a primitive's mesh again at its size (the mesh isn't saved with the scene).</summary>
        public void Rebuild()
        {
            if (!isPrimitive || this == null)
            {
                return;
            }

            var mesh = PrimitiveMeshes.For(new Placement { Primitive = Primitive, Scale = new System.Numerics.Vector3(size.x, size.y, size.z) }, Budget);
            if (TryGetComponent<MeshFilter>(out var filter))
            {
                filter.sharedMesh = mesh;
            }

            // Lets Unity's surface snapping land other objects on it.
            if (TryGetComponent<MeshCollider>(out var collider))
            {
                collider.sharedMesh = mesh;
            }
        }

        /// <summary>The color it's drawn in without its own.</summary>
        public Color DefaultColor => defaultColor;

        /// <summary>Sets the default: its asset's color, else the game's.</summary>
        public void SetDefaultColor(Color value)
        {
            defaultColor = value;
            if (!ownColor)
            {
                color = defaultColor;
            }

            ApplyColor();
        }

        /// <summary>Draws the object in its color (property blocks aren't saved with the scene).</summary>
        public void ApplyColor() => PlacedColors.Apply(gameObject, ownColor ? color : defaultColor);

        private void OnEnable()
        {
            Rebuild();
            ApplyColor();
        }

        private void OnValidate()
        {
            ApplyColor();
#if UNITY_EDITOR
            // Meshes can't be swapped in OnValidate itself.
            UnityEditor.EditorApplication.delayCall += Rebuild;
#endif
        }
    }
}
