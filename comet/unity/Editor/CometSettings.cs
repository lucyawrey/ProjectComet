using Comet.Content;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

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

        [Tooltip("The game's groups folder, relative to the Unity project (each group is a .toml file in it).")]
        public string groupsFolder = "../content/groups";

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

        [Tooltip("The color of terrain no one has painted, for pieces without their own.")]
        public Color groundColor = new Color32(0x73, 0x9e, 0x4d, 0xff);

        [Tooltip("The color of terrain's unpainted undersides (the walls and bottoms where its ground ends), for pieces without their own.")]
        [FormerlySerializedAs("skirtColor")]
        public Color underColor = new Color32(0x7a, 0x5c, 0x3e, 0xff);

        [Tooltip("The color of placed objects whose asset has none, unless they have their own.")]
        public Color placedColor = new Color32(0x9e, 0x8c, 0x7a, 0xff);

        [Tooltip("The game's body height, in meters: the terrain tools warn where two pieces' ground is closer than this.")]
        public float bodyHeight = 1;

        [Tooltip("If set, each new spawn point gets a marker: this asset (asset.prop) or primitive (cylinder), placed flattened under it and named with its key.")]
        public string spawnMarkerAsset = "";

        [Tooltip("How tall a new spawn point's marker is, in meters, sunk halfway into the ground; it can be moved and scaled after.")]
        public float spawnMarkerHeight = 0.4f;

        [Tooltip("The color spawn markers are drawn in, as the game draws a marker that isn't the player's.")]
        public Color spawnMarkerColor = Color.white;

        [Tooltip("The colors the place tool offers to place things in, beside its color picker.")]
        public System.Collections.Generic.List<Color> swatches = new System.Collections.Generic.List<Color>
        {
            new Color32(0xf2, 0xf2, 0xf2, 0xff), new Color32(0x9e, 0x9e, 0x9e, 0xff), new Color32(0x4a, 0x4a, 0x4a, 0xff),
            new Color32(0x9e, 0x8c, 0x7a, 0xff), new Color32(0x7a, 0x5c, 0x3e, 0xff), new Color32(0xe5, 0x39, 0x35, 0xff),
            new Color32(0xfb, 0x8c, 0x00, 0xff), new Color32(0xfd, 0xd8, 0x35, 0xff), new Color32(0x43, 0xa0, 0x47, 0xff),
            new Color32(0x1e, 0x88, 0xe5, 0xff), new Color32(0x8e, 0x24, 0xaa, 0xff), new Color32(0xec, 0x40, 0x7a, 0xff),
        };

        [Header("Local game server (Comet › Server)")]
        [Tooltip("The game server project, relative to the Unity project: built and run on this computer for play mode. Empty: no local server.")]
        public string serverProject = "";

        [Tooltip("The port the local game server listens on; the game's client joins localhost at this port.")]
        [Min(1)] public int serverPort = 5080;

        [Tooltip("More command-line arguments for the local game server, such as --ShapeLand:Zone=zone.tests.")]
        public string serverArguments = "";

        [Tooltip("The game's web build folder, relative to the Unity project: where Make Web Build puts it and the local server serves it from.")]
        public string webBuildFolder = "";

        [Tooltip("The build profile Make Web Build uses.")]
        public UnityEditor.Build.Profile.BuildProfile? webBuildProfile;

        [Tooltip("The server argument that serves a web build: {0} is its folder.")]
        public string serverWebArgument = "";

        [Tooltip("The bots project, relative to the Unity project, for the local server's bots; empty: no bots.")]
        public string botsProject = "";

        [Tooltip("The bots' arguments: {url} is the server's WebSocket URL and {count} how many to start.")]
        public string botsArguments = "--url {url} --count {count}";

        [Header("Polygon budget (Comet › Zones › Polygon Budget)")]
        [Tooltip("About how long each side of a round primitive is, in meters.")]
        [Min(0.01f)] public float facetLength = 0.5f;

        [Tooltip("The fewest sides a round primitive has.")]
        [Min(3)] public int minSides = 6;

        [Tooltip("The most sides a round primitive has.")]
        [Min(3)] public int maxSides = 48;

        [Tooltip("The game's settings file holding the budget, relative to the Unity project, where the budget window saves it.")]
        public string budgetFile = "";

        [Tooltip("The table in that file holding the budget's facet_length, min_sides and max_sides.")]
        public string budgetTable = "primitives";

        /// <summary>The polygon budget these settings hold.</summary>
        public PolygonBudget Budget => new PolygonBudget { FacetLength = facetLength, MinSides = minSides, MaxSides = Mathf.Max(minSides, maxSides) };

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
