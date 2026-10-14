using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// A game's settings for Comet's editor tools: how to build its content, where its zones are and how to draw them.
    /// Each game's Unity project has one (Create › Comet › Settings); the tools find it. A game may fill some in from
    /// its content.
    /// </summary>
    [CreateAssetMenu(menuName = "Comet/Settings")]
    public sealed class CometSettings : ScriptableObject
    {
        [Tooltip("The game's content build project, relative to the Unity project: run with 'dotnet run -- build'.")]
        public string contentBuildProject = "";

        [Tooltip("The content build's output file, relative to the Unity project: copied into StreamingAssets under the same name.")]
        public string contentFile = "";

        [Tooltip("The game's zones folder, relative to the Unity project (each zone is a folder in it).")]
        public string zonesFolder = "../content/zones";

        [Tooltip("Where opened zones keep their working scenes, relative to the Unity project: a folder the game's .gitignore ignores, since the zone's files are what's kept.")]
        public string scenesFolder = "Assets/ZoneEditing";

        [Tooltip("The scene play mode always starts in, whatever scene is open: the game's own, which loads its zones from content.")]
        public SceneAsset? playScene;

        [Tooltip("The models for asset keys.")]
        public AssetLibrary? assets;

        [Tooltip("Terrain's material: Comet/Vertex Color Lit, since its colors are vertex colors.")]
        public Material? terrainMaterial;

        [Tooltip("If set, placed objects are drawn in it instead of their models' own materials.")]
        public Material? placedMaterial;

        [Tooltip("The color of terrain no one has painted.")]
        public Color groundColor = new Color32(0x73, 0x9e, 0x4d, 0xff);

        [Tooltip("The color of terrain's skirts: the walls and bottoms where its ground ends.")]
        public Color skirtColor = new Color32(0x7a, 0x5c, 0x3e, 0xff);

        [Tooltip("The game's body height, in meters: the terrain tools warn where two pieces' ground is closer than this.")]
        public float bodyHeight = 1;

        [Tooltip("If set, each new spawn point gets a marker: this asset, placed flattened under it and named with its key.")]
        public string spawnMarkerAsset = "";

        [Tooltip("How tall a new spawn point's marker is, in meters, sunk halfway into the ground; it can be moved and scaled after.")]
        public float spawnMarkerHeight = 0.4f;

        private void OnValidate() => ZoneEditorScenes.SetPlayScene(this);

        // Searching the asset database takes about 10 ms, and the zone tools ask on every Scene view event, so the
        // answer is kept until it's unloaded (as a new scene does) or the project's assets change.
        private static CometSettings? _found;

        [InitializeOnLoadMethod]
        private static void ForgetOnProjectChange() => EditorApplication.projectChanged += () => _found = null;

        /// <summary>The project's settings, or null (with a dialog unless <paramref name="quiet"/>) if it has none.</summary>
        public static CometSettings? Find(bool quiet)
        {
            if (_found != null)
            {
                return _found;
            }

            var found = AssetDatabase.FindAssets($"t:{nameof(CometSettings)}");
            if (found.Length == 0)
            {
                if (!quiet)
                {
                    EditorUtility.DisplayDialog("No Comet settings", "This project has no Comet Settings asset. Make one with Create › Comet › Settings.", "OK");
                }

                return null;
            }

            return _found = AssetDatabase.LoadAssetAtPath<CometSettings>(AssetDatabase.GUIDToAssetPath(found[0]));
        }
    }
}
