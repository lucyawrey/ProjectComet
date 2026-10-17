using System;
using System.IO;
using System.Linq;
using System.Text;
using Comet.Unity.Editor;
using ShapeLand.Shared.Content;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// ShapeLand's own steps in every build of the Game scene. The builds themselves are the build profiles in
    /// <c>Assets/Settings/Build Profiles/</c> (File › Build Profiles); the player settings they need (the web build's
    /// Brotli with the decompression fallback, ShapeLand's page template, WebGPU then WebGL 2, IL2CPP, plain HTTP to
    /// the game server, a 1280×800 window on desktop) are saved in the project settings. Scripts build a profile with
    /// <c>tools/unity-batch.sh</c>, into the repository's <c>artifacts/unity/shapeland/</c>.
    /// </summary>
    public class Builds : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // Copies the compiled content into StreamingAssets (gitignored), as the content build left it; Comet does this as
        // play mode starts too (ContentBuild). Builds fail early when there's no content.
        public void OnPreprocessBuild(BuildReport report)
        {
            // Switching platforms inside a batch run builds with the starting platform's shader setup: a web build
            // from a Linux-targeted editor failed every draw. Batch runs start on the profile's platform instead.
            if (Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget != report.summary.platform)
            {
                throw new BuildFailedException($"The editor is on {EditorUserBuildSettings.activeBuildTarget}; start it on {report.summary.platform} (-activeBuildProfile)");
            }

            if (!ContentBuild.Copy())
            {
                throw new BuildFailedException($"Run the content build first: Comet › Content › Build, or dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build ({ContentBuild.Source})");
            }

            AssetDatabase.Refresh();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.WebGL)
            {
                FillPageText(Path.Combine(report.summary.outputPath, "index.html"));
            }
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
                throw new BuildFailedException($"The page has no {marker} to put its text in: {page}");
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
    }
}
