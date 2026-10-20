using Comet.Content;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// An asset, a primitive or a group copy placed in a zone opened in the editor (or in a group being edited): the
    /// object's transform is the placement's. A primitive's mesh is built at its size: the scale tool edits
    /// <see cref="size"/>, and any other scale is folded into it once a resize ends. A group copy's parts are drawn
    /// under it by the editor (<see cref="GroupDrawer"/>), unsaved, and clicking one selects the copy.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [SelectionBase]
    public sealed class ZonePlacement : MonoBehaviour
    {
        [Tooltip("The asset's key (asset.prop); empty for a primitive or a group copy.")]
        public string asset = "";

        [Tooltip("The group this is a copy of (group.tree); empty for an asset or a primitive. Copies look like their group: edit the group to change them all.")]
        public string group = "";

        [Tooltip("Whether this places a primitive, built in code, instead of an asset.")]
        public bool isPrimitive;

        [Tooltip("An optional name, unique in the zone, for game code to find it by (a spawn point's marker has the spawn's key).")]
        public string placementName = "";

        [Tooltip("Blockout: a stand-in for finished geometry, drawn in a dev look and reported by the content build.")]
        public bool blockout;

        [Tooltip("The primitive's kind.")]
        public PrimitiveKind kind;

        [Tooltip("The primitive's size in meters; a regular solid's scale (at 1, 1, 1 it holds as much as a 1 m cube).")]
        public Vector3 size = Vector3.one;

        [Tooltip("A round primitive's sides, or 0 to take them from the polygon budget.")]
        [Min(0)] public int sides;

        [Tooltip("How far a box's edges are cut back, in meters.")]
        [Min(0)] public float bevel;

        [Tooltip("A stairway's steps, or 0 for steps about 0.25 m high.")]
        [Min(0)] public int steps;

        [Tooltip("A doorway's opening, width and height in meters.")]
        public Vector2 opening;

        [Tooltip("An arch's thickness: how wide its legs and band are, in meters.")]
        [Min(0)] public float thickness;

        // Drawn by the editor's inspector as one color field with a reset button: editing it gives the placement its
        // own color, resetting goes back to the default (its asset's, else the game's).
        [HideInInspector] public bool ownColor;
        [HideInInspector] public Color color = Color.gray;
        [SerializeField, HideInInspector] private Color defaultColor = Color.gray;

        /// <summary>How finely round primitives are cut while editing: the game's, set by the editor (Comet › Zones › Polygon Budget).</summary>
        public static PolygonBudget Budget { get; set; } = new PolygonBudget();

        /// <summary>Draws a group copy's parts under it; set by the editor, which reads the groups.</summary>
        public static System.Action<ZonePlacement>? GroupDrawer { get; set; }

        /// <summary>Whether this is a group copy.</summary>
        public bool IsGroup => !string.IsNullOrEmpty(group);

        /// <summary>The primitive placed, or null for an asset.</summary>
        public Primitive? Primitive => isPrimitive
            ? new Primitive
            {
                Kind = kind, Sides = sides > 0 ? sides : (int?)null, Bevel = bevel, Steps = steps,
                OpeningWidth = opening.x, OpeningHeight = opening.y, Thickness = thickness,
            }
            : null;

        // The mesh shown while a resize is dragged: refilled at each size rather than cached, then dropped.
        private Mesh? _dragMesh;

        /// <summary>Shows the mesh at its size while a resize is dragged, without keeping a mesh for every size passed.</summary>
        public void PreviewResize()
        {
            if (!isPrimitive || !TryGetComponent<MeshFilter>(out var filter))
            {
                return;
            }

            if (_dragMesh == null)
            {
                _dragMesh = new Mesh { name = "Resizing", hideFlags = HideFlags.DontSave };
            }

            var builder = new PrimitiveMeshes.Builder();
            builder.Add(Comet.Simulation.PrimitiveMesher.Build(Primitive!, new System.Numerics.Vector3(size.x, size.y, size.z), Budget), System.Numerics.Matrix4x4.Identity, 0xFFFFFF);
            _dragMesh.Clear();
            builder.Fill(_dragMesh);
            filter.sharedMesh = _dragMesh;
        }

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

            DropDragMesh();
        }

        private void DropDragMesh()
        {
            if (_dragMesh != null)
            {
                DestroyImmediate(_dragMesh);
                _dragMesh = null;
            }
        }

        private void OnDestroy() => DropDragMesh();

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

        /// <summary>Draws the object in its color (property blocks aren't saved with the scene); a group copy's parts keep their own.</summary>
        public void ApplyColor()
        {
            if (!IsGroup)
            {
                PlacedColors.Apply(gameObject, ownColor ? color : defaultColor);
            }
        }

        private void OnEnable()
        {
            Rebuild();
            ApplyColor();
            if (IsGroup)
            {
                GroupDrawer?.Invoke(this);
            }
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
