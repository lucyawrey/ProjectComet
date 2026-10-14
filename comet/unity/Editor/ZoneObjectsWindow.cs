using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Places assets and spawn points in a zone opened in the editor (Comet › Zones › Objects): a palette of the asset
    /// library, each placed at the Scene view's center on the ground there, then moved with Unity's own tools; and
    /// spawn points, edited in the Inspector.
    /// </summary>
    public sealed class ZoneObjectsWindow : EditorWindow
    {
        [SerializeField] private string spawnKey = "spawn.";
        private Vector2 _scroll;

        [MenuItem("Comet/Zones/Objects", priority = 21)]
        public static void Open() => GetWindow<ZoneObjectsWindow>("Zone Objects");

        private void OnGUI()
        {
            var editing = FindAnyObjectByType<ZoneEditing>();
            var settings = ZoneEditor.Settings(quiet: true);
            if (editing == null || settings == null || settings.assets == null)
            {
                EditorGUILayout.HelpBox(settings == null || settings.assets == null ? "This project's Zone Editor Settings need an asset library." : "Open a zone first: Comet › Zones › Open…", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Click an asset to place it at the Scene view's center, then move, turn and scale it with Unity's tools (hold Ctrl to snap). Saving checks scale limits and how far objects reach.", MessageType.None);
            const int size = 96;
            var perRow = Mathf.Max(1, (int)((position.width - 20) / (size + 4)));
            var entries = settings.assets.Entries;
            for (var i = 0; i < entries.Count; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var entry in entries.Skip(i).Take(perRow))
                {
                    var preview = entry.model != null ? AssetPreview.GetAssetPreview(entry.model) : null;
                    var label = entry.key.StartsWith("asset.") ? entry.key.Substring("asset.".Length) : entry.key;
                    var content = preview != null ? new GUIContent(label, preview, entry.key) : new GUIContent(label, entry.key);
                    if (GUILayout.Button(content, GUILayout.Width(size), GUILayout.Height(size)))
                    {
                        Selection.activeGameObject = ZoneEditor.Place(editing, entry.key, Drop(editing), settings);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            // Previews load in the background; keep repainting until they're in.
            if (AssetPreview.IsLoadingAssetPreviews())
            {
                Repaint();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Spawn points", EditorStyles.boldLabel);
            spawnKey = EditorGUILayout.TextField("Key", spawnKey);
            if (GUILayout.Button("Add a spawn point at the Scene view's center"))
            {
                if (!Comet.ZoneFiles.ZoneValidation.IsKey(spawnKey, "spawn") || editing.GetComponentsInChildren<ZoneSpawn>().Any(s => s.key == spawnKey))
                {
                    EditorUtility.DisplayDialog("New spawn point", "Pick a new key: 'spawn.' then lowercase letters, digits and underscores.", "OK");
                }
                else
                {
                    Selection.activeGameObject = ZoneEditor.AddSpawn(editing, spawnKey, Drop(editing), settings);
                }
            }

            foreach (var spawn in editing.GetComponentsInChildren<ZoneSpawn>())
            {
                if (GUILayout.Button($"{spawn.key}{(spawn.isDefault ? " (default)" : "")}", EditorStyles.linkLabel))
                {
                    Selection.activeGameObject = spawn.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // The Scene view's center, dropped onto the highest terrain under it, if any.
        private static Vector3 Drop(ZoneEditing editing)
        {
            var pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            var best = float.NegativeInfinity;
            foreach (var piece in ZoneTerrainEditing.For(editing).Terrain.Pieces)
            {
                if (piece.TryGetHeightAt(pivot.x, pivot.z, out var ground) && ground > best)
                {
                    best = ground;
                }
            }

            return float.IsNegativeInfinity(best) ? pivot : new Vector3(pivot.x, best, pivot.z);
        }
    }
}
