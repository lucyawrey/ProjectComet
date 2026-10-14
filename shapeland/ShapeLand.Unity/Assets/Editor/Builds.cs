using System;
using System.IO;
using System.Linq;
using System.Text;
using Comet.Unity.Editor;
using ShapeLand.Shared.Content;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// ShapeLand's builds of the Game scene, runnable from the ShapeLand menu or in batch mode
    /// (<c>tools/unity-batch.sh -- -buildTarget WebGL -executeMethod ShapeLand.Client.Editor.Builds.Web -quit</c>).
    /// Output goes to the repository's <c>artifacts/unity/shapeland/</c>.
    /// </summary>
    public static class Builds
    {
        private const string GameScene = "Assets/Scenes/Game.unity";

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        /// <summary>
        /// A desktop build. A development build, so the speed-cheat key (hold C) works for seeing a snap-back
        /// blend; the server field on the join screen picks the server.
        /// </summary>
        [MenuItem("ShapeLand/Build/macOS")]
        public static void MacOS() => Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, "macos/ShapeLand.app", BuildOptions.Development);

        /// <summary>The Linux counterpart of <see cref="MacOS"/>.</summary>
        [MenuItem("ShapeLand/Build/Linux")]
        public static void Linux() => Build(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, "linux/ShapeLand", BuildOptions.Development);

        /// <summary>The web build, as a release build, on ShapeLand's own page; <c>tools/shapeland-dev.sh</c> serves it from the game server.</summary>
        [MenuItem("ShapeLand/Build/Web")]
        public static void Web()
        {
            // Brotli, about a quarter of the size: uncompressed, the page took minutes to load through a lossy
            // link. With the decompression fallback, the loader unpacks files a server sent without
            // Content-Encoding (GitHub Pages can't set it; browsers accept br only over HTTPS); the game server
            // sends it to browsers that ask for br.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            // ShapeLand's own page (Assets/WebGLTemplates/ShapeLand) in place of Unity's.
            PlayerSettings.WebGL.template = "PROJECT:ShapeLand";
            // WebGPU where the browser has it, else WebGL 2 (chosen by the page as it loads). WebGPU needs HTTPS or
            // localhost; the net stats panel (F3) shows which one is running.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.WebGPU, GraphicsDeviceType.OpenGLES3 });
            var report = Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, "web");
            if (report?.summary.result == BuildResult.Succeeded)
            {
                FillPageText(Path.Combine(report.summary.outputPath, "index.html"));
            }
        }

        // Copies the compiled content into StreamingAssets (gitignored), as the content build left it; Comet does this as
        // play mode starts too (ContentBuild). Builds fail early when there's no content.
        private static void CopyContent()
        {
            if (!ContentBuild.Copy())
            {
                throw new FileNotFoundException("Run the content build first: Comet › Content › Build, or dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build", ContentBuild.Source);
            }

            AssetDatabase.Refresh();
        }

        // Puts the page's text (page.* in the content's string table) into the built page, in place of the
        // template's marker, so the page needs no download of its own to show it. Done to the build's output rather
        // than through a template variable, which Unity would save into the project settings.
        private static void FillPageText(string page)
        {
            const string marker = "/* PAGE_TEXT */ {}";
            var html = File.ReadAllText(page);
            if (!html.Contains(marker))
            {
                throw new InvalidOperationException($"The page has no {marker} to put its text in: {page}");
            }

            File.WriteAllText(page, html.Replace(marker, PageText()));
        }

        // The page's strings as a JSON object, by key.
        private static string PageText()
        {
            ShapeLandContent content;
            using (var stream = File.OpenRead(ContentBuild.Source))
            {
                content = ShapeLandContent.Load(stream);
            }

            var pairs = content.Strings.Entries.Where(e => e.Key.StartsWith("page.")).OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => $"{Json(e.Key)}: {Json(e.Value)}");
            return "{ " + string.Join(", ", pairs) + " }";
        }

        private static string Json(string text)
        {
            var result = new StringBuilder("\"");
            foreach (var c in text)
            {
                result.Append(c switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '<' => "\\u003c", // never "</script>" inside the page's script
                    _ when c < ' ' => $"\\u{(int)c:x4}",
                    _ => c.ToString(),
                });
            }

            return result.Append('"').ToString();
        }

        private static BuildReport Build(BuildTarget target, BuildTargetGroup group, string output, BuildOptions options = BuildOptions.None)
        {
            // Switching platforms inside a batch run builds with the starting platform's shader setup: a web build
            // from a Linux-targeted editor failed every draw. Batch runs start on the target (-buildTarget) instead.
            if (Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget != target)
            {
                Debug.LogError($"Build Failed: the editor is on {EditorUserBuildSettings.activeBuildTarget}; start it with -buildTarget for {target}");
                EditorApplication.Exit(1);
                return null;
            }

            CopyContent();

            // The web client loads its content over plain HTTP from whichever server served the page, such as
            // the game server on another machine on the network; Unity otherwise allows that only from localhost.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            // Desktop builds open in a window (the mockups' 16:10 shape), so several clients fit on one screen.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group), ScriptingImplementation.IL2CPP);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GameScene },
                target = target,
                targetGroup = group,
                locationPathName = Path.Combine(RepoRoot, "artifacts", "unity", "shapeland", output),
                options = options,
            });

            Debug.Log($"Build {report.summary.result}: {report.summary.outputPath} ({report.summary.totalErrors} errors)");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }

            return report;
        }
    }
}
