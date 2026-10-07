using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// Builds for step 2b's shared package check, runnable from the menu or in batch mode
    /// (<c>-executeMethod ShapeLand.Client.Editor.Builds.MacOS</c>). Output goes to the repository's <c>artifacts/</c>.
    /// </summary>
    public static class Builds
    {
        private const string CheckScene = "Assets/Scenes/SharedPackageCheck.unity";
        private const string TerrainMaterial = "Assets/Materials/Terrain.mat";
        private const string BlockMaterial = "Assets/Materials/Block.mat";
        private const string ShapeMaterial = "Assets/Materials/Shape.mat";
        private const string EyeMaterial = "Assets/Materials/Eyes.mat";
        private const string JoinScene = "Assets/Scenes/Join.unity";
        private const string GameScene = "Assets/Scenes/Game.unity";

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        [MenuItem("ShapeLand/Build/macOS (IL2CPP)")]
        public static void MacOS() => Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, "macos/ShapeLand.app");

        [MenuItem("ShapeLand/Build/Web")]
        public static void Web()
        {
            // Uncompressed for now, so any static file server can host the check; release builds will use Brotli.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, "web");
        }

        /// <summary>A web build of the bare join scene (step 3b's early browser check), connecting to localhost:5080.</summary>
        [MenuItem("ShapeLand/Build/Web (Join Scene)")]
        public static void WebJoin()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, "web-join", JoinScene);
        }

        /// <summary>Creates the check scene: a camera, a sun and the check component with the terrain material.</summary>
        [MenuItem("ShapeLand/Create Check Scene")]
        public static void CreateCheckScene()
        {
            Directory.CreateDirectory("Assets/Materials");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.45f, 0.62f, 0.3f) };
            material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, TerrainMaterial);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            camera.transform.SetPositionAndRotation(new Vector3(-40, 34, -40), Quaternion.Euler(30, 45, 0));

            AddSun();

            var check = new GameObject("Shared package check").AddComponent<SharedPackageCheck>();
            var serialized = new SerializedObject(check);
            serialized.FindProperty("terrainMaterial").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, CheckScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(CheckScene, true) };
            AssetDatabase.SaveAssets();
        }

        /// <summary>Creates the bare join scene (step 3b): a camera and the join client, which logs what the server says.</summary>
        [MenuItem("ShapeLand/Create Join Scene")]
        public static void CreateJoinScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.75f, 0.95f);

            new GameObject("Join client", typeof(Comet.Unity.CometConnection), typeof(JoinClient));

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, JoinScene);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Creates the game scene (step 3b, growing into the full client): a sun, the orbit camera, and the join
        /// client with the game view, which draws the island, the blocks and the player's own shape.
        /// </summary>
        [MenuItem("ShapeLand/Create Game Scene")]
        public static void CreateGameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera", typeof(Camera), typeof(OrbitCamera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            camera.transform.SetPositionAndRotation(new Vector3(0, 20, -30), Quaternion.Euler(30, 0, 0));
            AddSun();

            var view = new GameObject("Game", typeof(Comet.Unity.CometConnection), typeof(JoinClient), typeof(GameView)).GetComponent<GameView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("terrainMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterial);
            serialized.FindProperty("blockMaterial").objectReferenceValue = LitMaterial(BlockMaterial, new Color(0.62f, 0.55f, 0.48f));
            serialized.FindProperty("shapeMaterial").objectReferenceValue = LitMaterial(ShapeMaterial, Color.white);
            serialized.FindProperty("eyeMaterial").objectReferenceValue = LitMaterial(EyeMaterial, new Color(0.08f, 0.08f, 0.1f));
            serialized.FindProperty("orbitCamera").objectReferenceValue = camera.GetComponent<OrbitCamera>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, GameScene);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Copies the compiled content into StreamingAssets (gitignored), as the content build left it.</summary>
        [MenuItem("ShapeLand/Copy Content")]
        public static void CopyContent()
        {
            var source = Path.Combine(RepoRoot, "artifacts", "content", "shapeland", "content.bin");
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Run the content build first: dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build", source);
            }

            Directory.CreateDirectory(Application.streamingAssetsPath);
            File.Copy(source, Path.Combine(Application.streamingAssetsPath, "content.bin"), overwrite: true);
            AssetDatabase.Refresh();
        }

        private static void AddSun()
        {
            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);
        }

        // A plain URP Lit material, created once and kept as an asset.
        private static Material LitMaterial(string path, Color colour)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = colour };
                material.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        private static void Build(BuildTarget target, BuildTargetGroup group, string output, string scene = CheckScene)
        {
            CopyContent();
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group), ScriptingImplementation.IL2CPP);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                target = target,
                targetGroup = group,
                locationPathName = Path.Combine(RepoRoot, "artifacts", "unity", "shapeland", output),
            });

            Debug.Log($"Build {report.summary.result}: {report.summary.outputPath} ({report.summary.totalErrors} errors)");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
