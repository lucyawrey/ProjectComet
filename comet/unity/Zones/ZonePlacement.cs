using System.Collections.Generic;
using Comet.Content;
using UnityEngine;

namespace Comet.Unity
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class ZonePlacement : MonoBehaviour
    {
        [Tooltip("The prop's id (prop.rock); empty for a primitive or a group copy.")]
        public string prop = "";

        [Tooltip(
            "The group this is a copy of (group.tree); empty for a prop or a primitive. Copies look like their group: edit the group to change them all."
        )]
        public string group = "";

        [Tooltip("Whether this places a primitive, built in code, instead of a prop.")]
        public bool isPrimitive;

        [Tooltip("An optional tag for triggers to flip it by (and game code to find it by); several placements may share one to flip together.")]
        public string placementTag = "";

        [Tooltip("Blockout: a stand-in for finished geometry, drawn in a dev look and reported by the content build.")]
        public bool blockout;

        [Tooltip(
            "A group part's role (body, leaves), for game code to find or color it by, and for copies to recolor it by; empty for most. Only a group's parts have one."
        )]
        public string role = "";

        [Tooltip("The primitive's kind.")]
        public PrimitiveKind kind;

        [Tooltip("The primitive's size in meters; a regular solid's scale (at 1, 1, 1 it holds as much as a 1 m cube).")]
        public Vector3 size = Vector3.one;

        [Tooltip("A round primitive's sides, or 0 to take them from the polygon budget.")]
        [Min(0)]
        public int sides;

        [Tooltip("How far a box's edges are cut back, in meters.")]
        [Min(0)]
        public float bevel;

        [Tooltip("A stairway's steps, or 0 for steps about 0.25 m high.")]
        [Min(0)]
        public int steps;

        [Tooltip("A doorway's opening, width and height in meters.")]
        public Vector2 opening;

        [Tooltip("An arch's thickness: how wide its legs and band are, in meters.")]
        [Min(0)]
        public float thickness;

        [HideInInspector]
        public bool ownColor;

        [HideInInspector]
        public Color color = Color.gray;

        [SerializeField, HideInInspector]
        private Color defaultColor = Color.gray;

        [System.Serializable]
        public struct RoleColor
        {
            public string role;
            public Color color;
        }

        [HideInInspector]
        public List<RoleColor> roleColors = new List<RoleColor>();

        public static PolygonBudget Budget { get; set; } = new PolygonBudget();

        public static System.Func<bool, Material?>? MaterialFor { get; set; }

        public static System.Action<ZonePlacement>? GroupDrawer { get; set; }

        public bool IsGroup => !string.IsNullOrEmpty(group);

        [SerializeField, HideInInspector]
        private bool editingInPlace;

        public bool EditingInPlace
        {
            get => editingInPlace;
            set
            {
                editingInPlace = value;
                SyncPickRoot();
            }
        }

        public bool IsLoose => transform.parent == null || transform.parent.GetComponentInParent<ZonePlacement>(true) == null;

        public Primitive? Primitive =>
            isPrimitive
                ? new Primitive
                {
                    Kind = kind,
                    Sides = sides > 0 ? sides : (int?)null,
                    Bevel = bevel,
                    Steps = steps,
                    OpeningWidth = opening.x,
                    OpeningHeight = opening.y,
                    Thickness = thickness,
                }
                : null;

        private Mesh? _dragMesh;

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
            builder.Add(
                Comet.Simulation.PrimitiveMesher.Build(Primitive!, new System.Numerics.Vector3(size.x, size.y, size.z), Budget),
                System.Numerics.Matrix4x4.Identity,
                0xFFFFFF
            );
            _dragMesh.Clear();
            builder.Fill(_dragMesh);
            filter.sharedMesh = _dragMesh;
        }

        public void Rebuild()
        {
            if (!isPrimitive || this == null)
            {
                return;
            }

            var mesh = PrimitiveMeshes.For(
                new Placement { Primitive = Primitive, Scale = new System.Numerics.Vector3(size.x, size.y, size.z) },
                Budget
            );
            if (TryGetComponent<MeshFilter>(out var filter))
            {
                filter.sharedMesh = mesh;
            }

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

        public Color DefaultColor => defaultColor;

        public void SetDefaultColor(Color value)
        {
            defaultColor = value;
            if (!ownColor)
            {
                color = defaultColor;
            }

            ApplyColor();
        }

        public void ApplyColor()
        {
            if (IsGroup)
            {
                return;
            }

            if (MaterialFor?.Invoke(blockout) is { } material)
            {
                PlacedProps.SetMaterial(gameObject, material);
            }

            PlacedColors.Apply(gameObject, ownColor ? color : defaultColor);
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += SyncPickRoot;
#endif
            Rebuild();
            ApplyColor();
            if (IsGroup && !editingInPlace)
            {
                GroupDrawer?.Invoke(this);
            }
        }

        private void OnValidate()
        {
            ApplyColor();
            if (IsGroup && !editingInPlace)
            {
                GroupDrawer?.Invoke(this);
            }

#if UNITY_EDITOR
            // Meshes can't be swapped in OnValidate itself.
            UnityEditor.EditorApplication.delayCall += Rebuild;
            UnityEditor.EditorApplication.delayCall += SyncPickRoot;
#endif
        }

        private void SyncPickRoot()
        {
            if (this == null || Application.isPlaying)
            {
                return;
            }

            var wanted = IsGroup ? !editingInPlace : !isPrimitive;
            var root = GetComponent<ZonePickRoot>();
            if (wanted && root == null)
            {
                gameObject.AddComponent<ZonePickRoot>().hideFlags = HideFlags.HideInInspector;
            }
            else if (!wanted && root != null)
            {
                DestroyImmediate(root);
            }
        }
    }
}
