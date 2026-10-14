using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// The Scene view's place tool for a zone opened in the editor (in the Tools overlay, or Comet › Zones › Place
    /// Tool): pick an asset or a spawn point in its panel, then click in the scene to place it on whatever is under
    /// the mouse. Placed objects are moved, turned and scaled with Unity's own tools; spawn points are edited in the
    /// Inspector.
    /// </summary>
    [EditorTool("Zone Place")]
    public sealed class ZonePlaceTool : EditorTool
    {
        [SerializeField] private string asset;
        [SerializeField] private bool spawn;
        [SerializeField] private string spawnKey = "spawn.";

        private GUIContent _icon;
        private Vector3? _hover;

        /// <summary>The tool's one instance, for its panel.</summary>
        internal static ZonePlaceTool Instance { get; private set; }

        [MenuItem("Comet/Zones/Place Tool", priority = 21)]
        public static void Select() => ToolManager.SetActiveTool<ZonePlaceTool>();

        [MenuItem("Comet/Zones/Place Tool", true)]
        private static bool CanSelect() => Editing != null;

        public override GUIContent toolbarIcon => _icon ??= new GUIContent(EditorGUIUtility.IconContent("Prefab Icon").image, "Zone Place: place assets and spawn points in the opened zone");

        public override bool IsAvailable() => Editing != null;

        private void OnEnable() => Instance = this;

        private static ZoneEditing Editing => FindAnyObjectByType<ZoneEditing>();

        /// <summary>The tool's panel in the Scene view.</summary>
        internal void PanelGui()
        {
            var editing = Editing;
            var settings = CometSettings.Find(quiet: true);
            if (editing == null || settings == null || settings.assets == null)
            {
                EditorGUILayout.HelpBox(settings == null || settings.assets == null ? "This project's Comet Settings need an asset library." : "Open a zone first: Comet › Zones › Open…", MessageType.Info);
                return;
            }

            EditorGUIUtility.labelWidth = 40;
            EditorGUILayout.BeginVertical(GUILayout.Width(280));
            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);
            const int size = 64, perRow = 4;
            var entries = settings.assets.Entries;
            for (var i = 0; i < entries.Count; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var entry in entries.Skip(i).Take(perRow))
                {
                    var preview = entry.model != null ? AssetPreview.GetAssetPreview(entry.model) : null;
                    var label = entry.key.StartsWith("asset.") ? entry.key.Substring("asset.".Length) : entry.key;
                    var content = preview != null ? new GUIContent(preview, entry.key) : new GUIContent(label, entry.key);
                    var picked = !spawn && asset == entry.key;
                    if (GUILayout.Toggle(picked, content, GUI.skin.button, GUILayout.Width(size), GUILayout.Height(size)) && !picked)
                    {
                        asset = entry.key;
                        spawn = false;
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            // Previews load in the background; keep repainting until they're in.
            if (AssetPreview.IsLoadingAssetPreviews())
            {
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Spawn points", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var spawnPicked = GUILayout.Toggle(spawn, "New spawn point", GUI.skin.button, GUILayout.Width(120));
            if (spawnPicked != spawn)
            {
                spawn = spawnPicked;
            }

            spawnKey = EditorGUILayout.TextField(spawnKey);
            EditorGUILayout.EndHorizontal();
            foreach (var point in editing.GetComponentsInChildren<ZoneSpawn>())
            {
                if (GUILayout.Button($"{point.key}{(point.isDefault ? " (default)" : "")}", EditorStyles.linkLabel))
                {
                    Selection.activeGameObject = point.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }

            EditorGUILayout.HelpBox(spawn || !string.IsNullOrEmpty(asset)
                ? "Click in the scene to place it; hold Alt to orbit as usual. Move, turn and scale with Unity's tools after (W, E, R; hold Ctrl to snap). Saving checks scale limits and how far objects reach."
                : "Pick an asset or a new spawn point, then click in the scene to place it.", MessageType.None);
            EditorGUILayout.EndVertical();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            var editing = Editing;
            var settings = CometSettings.Find(quiet: true);
            if (editing == null || settings == null || (!spawn && string.IsNullOrEmpty(asset)))
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

            if (e.type == EventType.MouseMove || e.type == EventType.Layout)
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
                Selection.activeGameObject = ZoneEditor.Place(editing, asset, at, settings);
                return;
            }

            if (!Comet.ZoneFiles.ZoneValidation.IsKey(spawnKey, "spawn") || editing.GetComponentsInChildren<ZoneSpawn>().Any(s => s.key == spawnKey))
            {
                EditorUtility.DisplayDialog("New spawn point", "Pick a new key: 'spawn.' then lowercase letters, digits and underscores.", "OK");
                return;
            }

            Selection.activeGameObject = ZoneEditor.AddSpawn(editing, spawnKey, at, settings);
        }

        // Where the mouse points: on whatever the scene has under it (terrain or placed objects), else on the highest
        // terrain piece along the ray, else on the ground plane.
        private static Vector3? Drop(ZoneEditing editing, Vector2 mouse)
        {
            if (HandleUtility.PlaceObject(mouse, out var hit, out _))
            {
                return hit;
            }

            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            const float step = 0.25f, far = 1000;
            var pieces = ZoneTerrainEditing.For(editing).Terrain.Pieces;
            for (var t = step; t < far; t += step)
            {
                var point = ray.GetPoint(t);
                foreach (var piece in pieces)
                {
                    if (piece.TryGetHeightAt(point.x, point.z, out var ground) && point.y <= ground)
                    {
                        return new Vector3(point.x, ground, point.z);
                    }
                }
            }

            if (Mathf.Abs(ray.direction.y) > 1e-4f)
            {
                var t = -ray.origin.y / ray.direction.y;
                return t > 0 ? ray.GetPoint(t) : (Vector3?)null;
            }

            return null;
        }
    }

    /// <summary>The place tool's panel, shown in the Scene view while the tool is picked.</summary>
    [Overlay(typeof(SceneView), "comet-zone-place", "Zone Place")]
    internal sealed class ZonePlacePanel : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(ZonePlaceTool) && ZonePlaceTool.Instance != null;

        public override void OnGUI() => ZonePlaceTool.Instance?.PanelGui();
    }
}
