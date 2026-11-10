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
    [EditorTool("Zone Place")]
    public sealed class ZonePlaceTool : EditorTool
    {
        [SerializeField]
        private string placeable = "";

        [SerializeField]
        private string adding = "";

        [SerializeField]
        private string addingId = "";

        [SerializeField]
        private string groupId = "group.";

        [SerializeField]
        private Color color = Color.clear;

        [SerializeField]
        private bool blockout;

        private GUIContent? _icon;
        private Vector3? _hover;

        internal static ZonePlaceTool? Instance { get; private set; }

        [MenuItem("Comet/Zones/Place Tool", priority = 21)]
        public static void Select() => ToolManager.SetActiveTool<ZonePlaceTool>();

        [MenuItem("Comet/Zones/Place Tool", true)]
        private static bool CanSelect() => Editing != null;

        public override GUIContent toolbarIcon =>
            _icon ??= new GUIContent(
                EditorGUIUtility.IconContent("Prefab Icon").image,
                "Zone Place: place primitives, props, spawn points, teleporters, triggers and signs in the opened zone"
            );

        public override bool IsAvailable() => Editing != null;

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

        private static List<(Mesh Mesh, Matrix4x4 Matrix)> Parts(GameObject model, Vector3 scale) =>
            model
                .GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null && !PlacedProps.IsCollision(f))
                .Select(f => (f.sharedMesh, Matrix4x4.Scale(scale) * model.transform.worldToLocalMatrix * f.transform.localToWorldMatrix))
                .ToList();

        private static List<(Mesh Mesh, Matrix4x4 Matrix)> Parts(string placeable, CometSettings settings, Vector3? size = null)
        {
            if (ZoneEditor.PrimitiveNamed(placeable) is { } kind)
            {
                var (primitive, starting) = ZoneEditor.Starting(kind);
                var at = size ?? starting;
                var mesh = PrimitiveMeshes.For(
                    new Placement { Primitive = primitive, Scale = new System.Numerics.Vector3(at.x, at.y, at.z) },
                    settings.Budget
                );
                return new List<(Mesh, Matrix4x4)> { (mesh, Matrix4x4.identity) };
            }

            if (settings.props == null || !settings.props.TryGet(placeable, out var entry) || entry.model == null)
            {
                return new List<(Mesh, Matrix4x4)>();
            }

            var bounds = entry.bounds.size;
            var scale = size is { } s
                ? new Vector3(s.x / Mathf.Max(bounds.x, 0.01f), s.y / Mathf.Max(bounds.y, 0.01f), s.z / Mathf.Max(bounds.z, 0.01f))
                : Vector3.one;
            return Parts(entry.model, scale);
        }

        private Texture2D? Thumbnail(string key, List<(Mesh Mesh, Matrix4x4 Matrix)> parts, Material material, Color color, int size) =>
            Thumbnail(key, parts.Select(p => (p.Mesh, p.Matrix, color)).ToList(), material, color, size);

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
                    bounds.Encapsulate(
                        matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1)))
                    );
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

        internal void PanelGui(float width)
        {
            var editing = Editing;
            var settings = CometSettings.Find(quiet: true);
            if (editing == null || settings == null || settings.props == null)
            {
                EditorGUILayout.HelpBox(
                    settings == null || settings.props == null
                        ? "This project's Comet Settings need a prop library."
                        : "Open a zone first: Comet › Zones › Open…",
                    MessageType.Info
                );
                return;
            }

            EditorGUIUtility.labelWidth = 40;
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
            if (editing.IsGroup)
            {
                GroupBanner(editing, settings);
            }

            if (color.a == 0)
            {
                color = settings.placedColor;
            }

            ColorRow(settings);
            blockout = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "Place as blockout",
                    "What's placed is blockout: drawn with the blockout grid and listed by the content build until it's finished or swapped for a prop."
                ),
                blockout
            );
            EditorGUILayout.Space();

            var perRow = Mathf.Max(1, (int)(width / (ButtonSize + 4)));
            var budget = settings.Budget;
            var budgetKey = $"{budget.FacetLength}/{budget.MinSides}/{budget.MaxSides}";
            CatalogGui(editing, settings, perRow, budgetKey);
            GroupActionsGui(editing, settings);

            if (AssetPreview.IsLoadingAssetPreviews())
            {
                SceneView.RepaintAll();
            }

            if (editing.IsGroup)
            {
                EditorGUILayout.HelpBox(
                    !string.IsNullOrEmpty(placeable)
                        ? "Click in the scene to place it. Copies of this group show what's saved."
                        : "Pick a primitive, a prop or a group, then click in the scene to place it.",
                    MessageType.None
                );
                EditorGUILayout.EndVertical();
                return;
            }

            TriggersGui(editing, settings, budgetKey);
            SignsGui(editing, settings, budgetKey);
            EditorGUILayout.HelpBox(
                adding.Length > 0 || !string.IsNullOrEmpty(placeable)
                    ? "Click in the scene to place it, in the picked color; hold Alt to orbit as usual. Move, turn and scale with Unity's tools after (W, E, R; hold Ctrl to snap). Saving checks scale limits and how far objects reach."
                    : "Pick a primitive, a prop, a group, or a new spawn point, teleporter, trigger or sign, then click in the scene to place it.",
                MessageType.None
            );
            EditorGUILayout.EndVertical();
        }

        private const int ButtonSize = 64;

        private static void GroupBanner(ZoneEditing editing, CometSettings settings)
        {
            EditorGUILayout.HelpBox(
                $"Editing {editing.groupId}: its pivot is the scene's origin. Saving (Ctrl+S) updates every copy.",
                MessageType.Info
            );
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

        private void CatalogGui(ZoneEditing editing, CometSettings settings, int perRow, string budgetKey)
        {
            EditorGUILayout.LabelField("Primitives", EditorStyles.boldLabel);
            Grid(
                PrimitiveKinds
                    .All.Select(PrimitiveKinds.Name)
                    .Select(kind =>
                        (
                            kind,
                            kind,
                            settings.placedMaterial != null
                                ? Thumbnail($"{kind} {budgetKey}", Parts(kind, settings), settings.placedMaterial, color, ButtonSize * 2)
                                : null
                        )
                    )
                    .ToList(),
                perRow
            );

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Props", EditorStyles.boldLabel);
            Grid(
                settings
                    .props!.Entries.Select(entry =>
                    {
                        var label =
                            !string.IsNullOrEmpty(entry.displayName) ? entry.displayName
                            : entry.id.StartsWith("prop.") ? entry.id.Substring("prop.".Length)
                            : entry.id;
                        var preview =
                            entry.model == null ? null
                            : settings.placedMaterial != null
                                ? Thumbnail(entry.id, Parts(entry.model, Vector3.one), settings.placedMaterial, color, ButtonSize * 2)
                            : AssetPreview.GetAssetPreview(entry.model);
                        return (entry.id, label, preview);
                    })
                    .ToList(),
                perRow
            );

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
            Grid(
                ZoneGroups
                    .All.Where(g => g.Id != editing.groupId)
                    .Select(g =>
                    {
                        var label = g.DisplayName ?? g.Id.Substring("group.".Length);
                        var picture =
                            settings.placedMaterial != null
                                ? Thumbnail(
                                    $"{g.Id} {ZoneGroups.Version} {budgetKey}",
                                    ZoneGroups.Meshes(g.Id, settings),
                                    settings.placedMaterial,
                                    Color.clear,
                                    ButtonSize * 2
                                )
                                : null;
                        return (g.Id, label, picture);
                    })
                    .ToList(),
                perRow
            );
        }

        private void Grid(List<(string Key, string Label, Texture2D? Picture)> items, int perRow)
        {
            for (var i = 0; i < items.Count; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var (item, label, picture) in items.Skip(i).Take(perRow))
                {
                    var content = picture != null ? new GUIContent(picture, label) : new GUIContent(label, item);
                    var picked = adding.Length == 0 && placeable == item;
                    if (GUILayout.Toggle(picked, content, GUI.skin.button, GUILayout.Width(ButtonSize), GUILayout.Height(ButtonSize)) && !picked)
                    {
                        placeable = item;
                        adding = "";
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void GroupActionsGui(ZoneEditing editing, CometSettings settings)
        {
            if (adding.Length == 0 && placeable.StartsWith("group.", StringComparison.Ordinal) && ZoneGroups.Find(placeable) != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button($"Edit {placeable}"))
                {
                    ZoneGroups.OpenLater(placeable, settings);
                }

                if (GUILayout.Button("Delete…", GUILayout.Width(70)))
                {
                    var id = placeable;
                    EditorApplication.delayCall += () => ZoneGroups.Delete(id, editing, settings);
                    placeable = "";
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            groupId = EditorGUILayout.TextField(groupId);
            if (GUILayout.Button("Save selection as group", GUILayout.Width(160)))
            {
                ZoneGroups.SaveSelection(editing, groupId, settings);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void TriggersGui(ZoneEditing editing, CometSettings settings, string budgetKey)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Spawn points, teleporters and triggers", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            Adder(editing, settings, budgetKey, "spawn", "New spawn point", settings.spawnPad, settings.spawnPadSize, settings.spawnPadColor);
            Adder(
                editing,
                settings,
                budgetKey,
                "teleporter",
                "New teleporter",
                settings.teleporterPad,
                settings.teleporterPadSize,
                settings.teleporterPadColor
            );
            Adder(editing, settings, budgetKey, "trigger", "New trigger", "", Vector3.zero, Color.white);
            EditorGUILayout.EndHorizontal();
            if (adding.Length > 0 && adding != "sign")
            {
                EditorGUILayout.LabelField("ID");
                addingId = EditorGUILayout.TextField(addingId);
            }

            foreach (var point in editing.GetComponentsInChildren<ZoneTrigger>(true).OrderBy(t => t.id, StringComparer.Ordinal))
            {
                if (GUILayout.Button(point.id, EditorStyles.linkLabel))
                {
                    Selection.activeGameObject = point.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
        }

        private void SignsGui(ZoneEditing editing, CometSettings settings, string budgetKey)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Signs", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            Adder(editing, settings, budgetKey, "sign", "New sign", "", Vector3.zero, Color.white);
            EditorGUILayout.EndHorizontal();
            foreach (var sign in editing.GetComponentsInChildren<ZoneSign>(true))
            {
                var on =
                    sign.transform.parent != null && sign.transform.parent.GetComponent<ZoneTrigger>() is { } trigger && trigger.Sign == sign
                        ? $" ({trigger.id})"
                        : "";
                if (GUILayout.Button(sign.text.Split('\n')[0] + on, EditorStyles.linkLabel))
                {
                    Selection.activeGameObject = sign.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
        }

        private void Adder(
            ZoneEditing editing,
            CometSettings settings,
            string budgetKey,
            string kind,
            string label,
            string pad,
            Vector3 padSize,
            Color padColor
        )
        {
            var picture =
                settings.placedMaterial != null && !string.IsNullOrEmpty(pad)
                    ? Thumbnail(
                        $"{kind} {pad} {padSize} {budgetKey}",
                        Parts(pad, settings, padSize),
                        settings.placedMaterial,
                        padColor,
                        ButtonSize * 2
                    )
                    : null;
            var content = picture != null ? new GUIContent(picture, label) : new GUIContent(label);
            var picked = adding == kind;
            if (GUILayout.Toggle(picked, content, GUI.skin.button, GUILayout.Width(ButtonSize), GUILayout.Height(ButtonSize)) != picked)
            {
                adding = picked ? "" : kind;
                var prefix = $"{kind}.{ZoneName(editing)}.";
                var typed = addingId.Split('.') is { Length: 3 } parts ? parts[2] : "";
                addingId = prefix + typed;
            }
        }

        private void ColorRow(CometSettings settings)
        {
            EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
            const int swatch = 18,
                perRow = 12;
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
            if (editing == null || settings == null || (adding.Length == 0 && string.IsNullOrEmpty(placeable)))
            {
                return;
            }

            var e = Event.current;
            if (e.alt)
            {
                return;
            }

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
                Handles.DrawWireDisc(at, Vector3.up, adding.Length > 0 ? 1 : 0.5f);
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
            if (adding.Length == 0)
            {
                var placed = ZoneEditor.Place(editing, placeable, at, settings, color);
                if (blockout && placed.GetComponent<ZonePlacement>() is { } placement)
                {
                    placement.blockout = true;
                    placement.ApplyColor();
                    if (placement.IsGroup)
                    {
                        ZoneGroups.Draw(placement);
                    }
                }

                Selection.activeGameObject = placed;
                return;
            }

            if (adding == "sign")
            {
                Selection.activeGameObject = ZoneEditor.AddSign(editing, at + Vector3.up * 1.5f).gameObject;
                return;
            }

            var id = addingId;
            var prefix = $"{adding}.{ZoneName(editing)}.";
            if (
                !id.StartsWith(prefix, StringComparison.Ordinal)
                || !Comet.ZoneFiles.ZoneValidation.IsZonedId(id, adding)
                || editing.GetComponentsInChildren<ZoneTrigger>(true).Any(t => t.id == id)
            )
            {
                EditorUtility.DisplayDialog(
                    $"New {adding}",
                    $"Pick a new id: '{prefix}' then lowercase letters, digits and underscores, starting with a letter.",
                    "OK"
                );
                return;
            }

            Selection.activeGameObject = (
                adding switch
                {
                    "spawn" => (ZoneTrigger)ZoneEditor.AddTrigger<ZoneSpawn>(editing, id, at, settings),
                    "teleporter" => ZoneEditor.AddTrigger<ZoneTeleporter>(editing, id, at, settings),
                    _ => ZoneEditor.AddTrigger<ZoneToggleTrigger>(editing, id, at, settings),
                }
            ).gameObject;
        }

        private static string ZoneName(ZoneEditing editing) =>
            editing.name.StartsWith("Zone zone.", StringComparison.Ordinal) ? editing.name.Substring("Zone zone.".Length) : "";

        private static Vector3? Drop(ZoneEditing editing, Vector2 mouse)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var best = TerrainHit(editing, ray);
            var picked = HandleUtility.PickGameObject(mouse, false);
            var placed = picked != null ? picked.GetComponentInParent<ZonePlacement>(true) : null;
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

        private static Vector3? TerrainHit(ZoneEditing editing, Ray ray)
        {
            var pieces = ZoneTerrainEditing.For(editing).Terrain.Pieces;
            bool Below(Vector3 p) => pieces.Any(piece => piece.TryGetHeightAt(p.x, p.z, out var ground) && p.y <= ground);

            const float step = 0.25f,
                far = 1000;
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
            var width = Mathf.Max(minSize.x, size.x) - 12;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Width(width), GUILayout.Height(Mathf.Max(minSize.y, size.y) - 34));
            ZonePlaceTool.Instance?.PanelGui(width - 20);
            EditorGUILayout.EndScrollView();
        }
    }
}
