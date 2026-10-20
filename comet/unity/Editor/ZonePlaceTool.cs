using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Content;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// The Scene view's place tool for a zone opened in the editor (in the Tools overlay, or Comet › Zones › Place
    /// Tool): pick a primitive, a prop or a spawn point in its panel, then click in the scene to place it on whatever is under
    /// the mouse. Placed objects are moved, turned and scaled with Unity's own tools; spawn points are edited in the
    /// Inspector.
    /// </summary>
    [EditorTool("Zone Place")]
    public sealed class ZonePlaceTool : EditorTool
    {
        [SerializeField] private string placeable = "";
        [SerializeField] private bool spawn;
        [SerializeField] private string spawnKey = "spawn.";
        [SerializeField] private string groupKey = "group.";
        [SerializeField] private Color color = Color.clear; // clear until first shown: then the game's placed color
        [SerializeField] private bool blockout;

        private GUIContent? _icon;
        private Vector3? _hover;

        /// <summary>The tool's one instance, for its panel.</summary>
        internal static ZonePlaceTool? Instance { get; private set; }

        [MenuItem("Comet/Zones/Place Tool", priority = 21)]
        public static void Select() => ToolManager.SetActiveTool<ZonePlaceTool>();

        [MenuItem("Comet/Zones/Place Tool", true)]
        private static bool CanSelect() => Editing != null;

        public override GUIContent toolbarIcon => _icon ??= new GUIContent(EditorGUIUtility.IconContent("Prefab Icon").image, "Zone Place: place primitives, props and spawn points in the opened zone");

        public override bool IsAvailable() => Editing != null;

        // The panel's pictures of each model and primitive, drawn in the placed objects' material and the picked color
        // (Unity's own previews use the models' imported materials, which show purple where their shaders aren't
        // ready). Keyed by what's drawn, its color and, for primitives, the polygon budget.
        private readonly Dictionary<(string, Color), Texture2D> _thumbnails = new Dictionary<(string, Color), Texture2D>();

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            foreach (var texture in _thumbnails.Values)
            {
                DestroyImmediate(texture);
            }

            _thumbnails.Clear();
        }

        // A model's meshes, in its own space, without its collision objects.
        private static List<(Mesh Mesh, Matrix4x4 Matrix)> Parts(GameObject model, Vector3 scale) =>
            model.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null && !f.name.EndsWith(Prop.CollisionSuffix, StringComparison.Ordinal))
                .Select(f => (f.sharedMesh, Matrix4x4.Scale(scale) * model.transform.worldToLocalMatrix * f.transform.localToWorldMatrix))
                .ToList();

        // What a placeable name (a prop key or a primitive's kind) draws at a scale, or nothing if it has no model.
        private static List<(Mesh Mesh, Matrix4x4 Matrix)> Parts(string placeable, CometSettings settings, Vector3? size = null)
        {
            if (ZoneEditor.PrimitiveNamed(placeable) is { } kind)
            {
                var (primitive, starting) = ZoneEditor.Starting(kind);
                var at = size ?? starting;
                var mesh = PrimitiveMeshes.For(new Placement { Primitive = primitive, Scale = new System.Numerics.Vector3(at.x, at.y, at.z) }, settings.Budget);
                return new List<(Mesh, Matrix4x4)> { (mesh, Matrix4x4.identity) };
            }

            if (settings.props == null || !settings.props.TryGet(placeable, out var entry) || entry.model == null)
            {
                return new List<(Mesh, Matrix4x4)>();
            }

            var bounds = entry.bounds.size;
            var scale = size is { } s ? new Vector3(s.x / Mathf.Max(bounds.x, 0.01f), s.y / Mathf.Max(bounds.y, 0.01f), s.z / Mathf.Max(bounds.z, 0.01f)) : Vector3.one;
            return Parts(entry.model, scale);
        }

        private Texture2D? Thumbnail(string key, List<(Mesh Mesh, Matrix4x4 Matrix)> parts, Material material, Color color, int size) =>
            Thumbnail(key, parts.Select(p => (p.Mesh, p.Matrix, color)).ToList(), material, color, size);

        // A picture of meshes each in its own color; cached by key and `color` (the picked one, for what follows it).
        private Texture2D? Thumbnail(string key, List<(Mesh Mesh, Matrix4x4 Matrix, Color Color)> parts, Material material, Color color, int size)
        {
            if (_thumbnails.TryGetValue((key, color), out var cached) && cached != null)
            {
                return cached;
            }

            if (parts.Count == 0)
            {
                return null;
            }

            var bounds = new Bounds(parts[0].Matrix.MultiplyPoint3x4(parts[0].Mesh.bounds.center), Vector3.zero);
            foreach (var (mesh, matrix, _) in parts)
            {
                var b = mesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    bounds.Encapsulate(matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1))));
                }
            }

            var block = new MaterialPropertyBlock();

            var preview = new PreviewRenderUtility();
            try
            {
                var camera = preview.camera;
                var radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
                camera.fieldOfView = 30;
                camera.transform.rotation = Quaternion.Euler(25, -35, 0);
                camera.transform.position = bounds.center - camera.transform.forward * (radius / Mathf.Sin(15 * Mathf.Deg2Rad));
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = radius * 10;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40, -30, 0);
                preview.ambientColor = new Color(0.35f, 0.35f, 0.35f);
                preview.BeginStaticPreview(new Rect(0, 0, size, size));
                // Drawn twice: the first picture rendered after the editor starts comes out unlit.
                for (var pass = 0; pass < 2; pass++)
                {
                    foreach (var (mesh, matrix, partColor) in parts)
                    {
                        block.SetColor("_BaseColor", partColor);
                        for (var sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            preview.DrawMesh(mesh, matrix, material, sub, block);
                        }
                    }

                    camera.Render();
                }

                var texture = preview.EndStaticPreview();
                texture.hideFlags = HideFlags.HideAndDontSave;
                _thumbnails[(key, color)] = texture;
                return texture;
            }
            finally
            {
                preview.Cleanup();
            }
        }

        private static ZoneEditing Editing => FindAnyObjectByType<ZoneEditing>();

        /// <summary>The tool's panel in the Scene view.</summary>
        internal void PanelGui(float width)
        {
            var editing = Editing;
            var settings = CometSettings.Find(quiet: true);
            if (editing == null || settings == null || settings.props == null)
            {
                EditorGUILayout.HelpBox(settings == null || settings.props == null ? "This project's Comet Settings need a prop library." : "Open a zone first: Comet › Zones › Open…", MessageType.Info);
                return;
            }

            EditorGUIUtility.labelWidth = 40;
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
            if (editing.IsGroup)
            {
                EditorGUILayout.HelpBox($"Editing {editing.groupKey}: its pivot is the scene's origin. Saving (Ctrl+S) updates every copy.", MessageType.Info);
                var back = ZoneGroups.ReturnFolder;
                if (back.Length > 0 && GUILayout.Button($"Back to {System.IO.Path.GetFileName(back)}"))
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        {
                            ZoneEditor.Open(back, settings);
                        }
                    };
                }

                EditorGUILayout.Space();
            }

            if (color.a == 0)
            {
                color = settings.placedColor;
            }

            ColorRow(settings);
            blockout = EditorGUILayout.ToggleLeft(new GUIContent("Place as blockout", "What's placed is blockout: drawn with the blockout grid and listed by the content build until it's finished or swapped for a prop."), blockout);
            EditorGUILayout.Space();

            // As many buttons to a row as the panel's width fits.
            const int size = 64;
            var perRow = Mathf.Max(1, (int)(width / (size + 4)));
            var budget = settings.Budget;
            var budgetKey = $"{budget.FacetLength}/{budget.MinSides}/{budget.MaxSides}";
            void Grid(List<(string Key, string Label, Texture2D? Picture)> items)
            {
                for (var i = 0; i < items.Count; i += perRow)
                {
                    EditorGUILayout.BeginHorizontal();
                    foreach (var (key, label, picture) in items.Skip(i).Take(perRow))
                    {
                        var content = picture != null ? new GUIContent(picture, label) : new GUIContent(label, key);
                        var picked = !spawn && placeable == key;
                        if (GUILayout.Toggle(picked, content, GUI.skin.button, GUILayout.Width(size), GUILayout.Height(size)) && !picked)
                        {
                            placeable = key;
                            spawn = false;
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.LabelField("Primitives", EditorStyles.boldLabel);
            Grid(PrimitiveKinds.All.Select(PrimitiveKinds.Name).Select(kind =>
                (kind, kind, settings.placedMaterial != null ? Thumbnail($"{kind} {budgetKey}", Parts(kind, settings), settings.placedMaterial, color, size * 2) : null)).ToList());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Props", EditorStyles.boldLabel);
            Grid(settings.props.Entries.Select(entry =>
            {
                var label = entry.key.StartsWith("prop.") ? entry.key.Substring("prop.".Length) : entry.key;
                var preview = entry.model == null ? null
                    : settings.placedMaterial != null ? Thumbnail(entry.key, Parts(entry.model, Vector3.one), settings.placedMaterial, color, size * 2)
                    : AssetPreview.GetAssetPreview(entry.model);
                return (entry.key, label, preview);
            }).ToList());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
            Grid(ZoneGroups.All.Where(g => g.Id != editing.groupKey).Select(g =>
            {
                var label = g.Id.Substring("group.".Length);
                var picture = settings.placedMaterial != null ? Thumbnail($"{g.Id} {ZoneGroups.Version} {budgetKey}", ZoneGroups.Meshes(g.Id, settings), settings.placedMaterial, Color.clear, size * 2) : null;
                return (g.Id, label, picture);
            }).ToList());
            if (!spawn && placeable.StartsWith("group.", StringComparison.Ordinal) && ZoneGroups.Find(placeable) != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button($"Edit {placeable}"))
                {
                    ZoneGroups.OpenLater(placeable, settings);
                }

                if (GUILayout.Button("Delete…", GUILayout.Width(70)))
                {
                    var key = placeable;
                    EditorApplication.delayCall += () => ZoneGroups.Delete(key, editing, settings); // after this GUI pass
                    placeable = "";
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            groupKey = EditorGUILayout.TextField(groupKey);
            if (GUILayout.Button("Save selection as group", GUILayout.Width(160)))
            {
                ZoneGroups.SaveSelection(editing, groupKey, settings);
            }

            EditorGUILayout.EndHorizontal();

            // Previews load in the background; keep repainting until they're in.
            if (AssetPreview.IsLoadingAssetPreviews())
            {
                SceneView.RepaintAll();
            }

            if (editing.IsGroup)
            {
                EditorGUILayout.HelpBox(!string.IsNullOrEmpty(placeable)
                    ? "Click in the scene to place it. Copies of this group show what's saved." : "Pick a primitive, a prop or a group, then click in the scene to place it.", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Spawn points", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            // Its picture is the spawn marker, flattened as a new spawn's is, in the markers' color.
            var marker = settings.placedMaterial != null && !string.IsNullOrEmpty(settings.spawnMarker)
                ? Thumbnail($"spawn {settings.spawnMarker} {settings.spawnMarkerHeight} {budgetKey}",
                    Parts(settings.spawnMarker, settings, new Vector3(2 * ZoneSpawn.DefaultRadius, settings.spawnMarkerHeight, 2 * ZoneSpawn.DefaultRadius)),
                    settings.placedMaterial, settings.spawnMarkerColor, size * 2)
                : null;
            var spawnContent = marker != null ? new GUIContent(marker, "New spawn point") : new GUIContent("New spawn point");
            var spawnPicked = GUILayout.Toggle(spawn, spawnContent, GUI.skin.button, GUILayout.Width(marker != null ? size : 120), GUILayout.Height(marker != null ? size : 20));
            if (spawnPicked != spawn)
            {
                spawn = spawnPicked;
            }

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("New spawn point's key");
            spawnKey = EditorGUILayout.TextField(spawnKey);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            foreach (var point in editing.GetComponentsInChildren<ZoneSpawn>())
            {
                if (GUILayout.Button($"{point.key}{(point.isDefault ? " (default)" : "")}", EditorStyles.linkLabel))
                {
                    Selection.activeGameObject = point.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }

            EditorGUILayout.HelpBox(spawn || !string.IsNullOrEmpty(placeable)
                ? "Click in the scene to place it, in the picked color; hold Alt to orbit as usual. Move, turn and scale with Unity's tools after (W, E, R; hold Ctrl to snap). Saving checks scale limits and how far objects reach."
                : "Pick a primitive, a prop, a group or a new spawn point, then click in the scene to place it.", MessageType.None);
            EditorGUILayout.EndVertical();
        }

        // The color things are placed in: the settings' swatches, and a picker for any other.
        private void ColorRow(CometSettings settings)
        {
            EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
            const int swatch = 18, perRow = 12;
            for (var i = 0; i < settings.swatches.Count; i += perRow)
            {
                var row = GUILayoutUtility.GetRect(perRow * (swatch + 2), swatch + 2, GUILayout.ExpandWidth(false));
                foreach (var (option, k) in settings.swatches.Skip(i).Take(perRow).Select((c, k) => (c, k)))
                {
                    var r = new Rect(row.x + k * (swatch + 2) + 1, row.y + 1, swatch, swatch);
                    if (option == color)
                    {
                        EditorGUI.DrawRect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), Color.white);
                    }

                    EditorGUI.DrawRect(r, option);
                    if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                    {
                        color = option;
                        Event.current.Use();
                    }
                }
            }

            color = EditorGUILayout.ColorField(GUIContent.none, color, false, false, false, GUILayout.Width(perRow * (swatch + 2)));
        }

        public override void OnToolGUI(EditorWindow window)
        {
            var editing = Editing;
            var settings = CometSettings.Find(quiet: true);
            if (editing == null || settings == null || (!spawn && string.IsNullOrEmpty(placeable)))
            {
                return;
            }

            var e = Event.current;
            if (e.alt)
            {
                return;
            }

            // Take the left mouse button from the scene's selection while the tool is picked.
            var control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(control);
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                _hover = Drop(editing, e.mousePosition);
            }

            if (_hover is { } at)
            {
                Handles.color = Color.white;
                Handles.DrawWireDisc(at, Vector3.up, spawn ? 2 : 0.5f);
            }

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && _hover != null:
                    Place(editing, _hover.Value, settings);
                    e.Use();
                    break;
                case EventType.MouseMove:
                    window.Repaint();
                    break;
            }
        }

        private void Place(ZoneEditing editing, Vector3 at, CometSettings settings)
        {
            if (!spawn)
            {
                var placed = ZoneEditor.Place(editing, placeable, at, settings, color);
                if (blockout && placed.GetComponent<ZonePlacement>() is { } placement)
                {
                    placement.blockout = true;
                    placement.ApplyColor();
                    if (placement.IsGroup)
                    {
                        ZoneGroups.Redraw(placement);
                    }
                }

                Selection.activeGameObject = placed;
                return;
            }

            if (!Comet.ZoneFiles.ZoneValidation.IsKey(spawnKey, "spawn") || editing.GetComponentsInChildren<ZoneSpawn>().Any(s => s.key == spawnKey))
            {
                EditorUtility.DisplayDialog("New spawn point", "Pick a new key: 'spawn.' then lowercase letters, digits and underscores.", "OK");
                return;
            }

            Selection.activeGameObject = ZoneEditor.AddSpawn(editing, spawnKey, at, settings);
        }

        // Where the mouse points: on top of the terrain, or of a placed object if one is nearer along the ray (on its
        // bounding box's top), else on the ground plane.
        private static Vector3? Drop(ZoneEditing editing, Vector2 mouse)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var best = TerrainHit(editing, ray);
            var picked = HandleUtility.PickGameObject(mouse, false);
            var placed = picked != null ? picked.GetComponentInParent<ZonePlacement>() : null;
            if (placed != null)
            {
                var renderers = placed.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers.Skip(1))
                    {
                        bounds.Encapsulate(r.bounds);
                    }

                    if (bounds.IntersectRay(ray, out var distance) && (best == null || distance < Vector3.Distance(ray.origin, best.Value)))
                    {
                        var hit = ray.GetPoint(distance);
                        best = new Vector3(hit.x, bounds.max.y, hit.z);
                    }
                }
            }

            if (best == null && Mathf.Abs(ray.direction.y) > 1e-4f)
            {
                var t = -ray.origin.y / ray.direction.y;
                best = t > 0 ? ray.GetPoint(t) : (Vector3?)null;
            }

            return best;
        }

        // Where the ray first meets any terrain piece's ground: marching a quarter meter at a time, then narrowing in.
        private static Vector3? TerrainHit(ZoneEditing editing, Ray ray)
        {
            var pieces = ZoneTerrainEditing.For(editing).Terrain.Pieces;
            bool Below(Vector3 p) => pieces.Any(piece => piece.TryGetHeightAt(p.x, p.z, out var ground) && p.y <= ground);

            const float step = 0.25f, far = 1000;
            var previous = ray.origin;
            for (var t = step; t < far; t += step)
            {
                var point = ray.GetPoint(t);
                if (Below(point))
                {
                    var (a, b) = (previous, point);
                    for (var i = 0; i < 10; i++)
                    {
                        var middle = (a + b) / 2;
                        if (Below(middle))
                        {
                            b = middle;
                        }
                        else
                        {
                            a = middle;
                        }
                    }

                    return b;
                }

                previous = point;
            }

            return null;
        }
    }

    /// <summary>
    /// The place tool's panel, shown in the Scene view while the tool is picked. Drag its edge to resize it; its
    /// contents scroll when they don't fit.
    /// </summary>
    [Overlay(typeof(SceneView), "comet-zone-place", "Zone Place")]
    internal sealed class ZonePlacePanel : IMGUIOverlay, ITransientOverlay
    {
        private Vector2 _scroll;

        public bool visible => ToolManager.activeToolType == typeof(ZonePlaceTool) && ZonePlaceTool.Instance != null;

        public override void OnCreated()
        {
            minSize = new Vector2(300, 160);
            maxSize = new Vector2(900, 2000);
            if (size.y < minSize.y)
            {
                size = new Vector2(310, 520);
            }
        }

        public override void OnGUI()
        {
            // The overlay's size less its title bar and margins.
            var width = Mathf.Max(minSize.x, size.x) - 12;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Width(width), GUILayout.Height(Mathf.Max(minSize.y, size.y) - 34));
            ZonePlaceTool.Instance?.PanelGui(width - 20);
            EditorGUILayout.EndScrollView();
        }
    }
}
