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
    public class Builds : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // Switching platforms in a batch run keeps the starting platform's shader setup (a web build from a Linux editor failed every draw),
            // so a batch run starts on the profile's platform instead.
            if (Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget != report.summary.platform)
            {
                throw new BuildFailedException(
                    $"The editor is on {EditorUserBuildSettings.activeBuildTarget}; start it on {report.summary.platform} (-activeBuildProfile)"
                );
            }

            if (!ContentBuild.Copy())
            {
                throw new BuildFailedException(
                    $"Run the content build first: Comet › Content › Build, or dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build ({ContentBuild.Source})"
                );
            }

            AssetDatabase.Refresh();

            // A fast web build is uncompressed for this build only; delayCall puts the project's setting back after it ends, even a failed one.
            if (report.summary.platform == BuildTarget.WebGL && File.Exists(FastWebMarker))
            {
                File.Delete(FastWebMarker);
                var compression = PlayerSettings.WebGL.compressionFormat;
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                _restoreCompression = compression;
                EditorApplication.delayCall += RestoreCompression;
            }
        }

        public const string FastWebMarker = "Library/ShapeLand/fast-web-build";

        private static WebGLCompressionFormat? _restoreCompression;

        private static void RestoreCompression()
        {
            if (_restoreCompression is { } compression)
            {
                PlayerSettings.WebGL.compressionFormat = compression;
                _restoreCompression = null;
                AssetDatabase.SaveAssets(); // the build saved the project settings uncompressed; save them back
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.WebGL)
            {
                RestoreCompression();
                FillPageText(Path.Combine(report.summary.outputPath, "index.html"));
            }
        }

        private static void FillPageText(string page)
        {
            const string marker = "{\"PAGE_TEXT\": true}";
            var html = File.ReadAllText(page);
            if (!html.Contains(marker))
            {
                throw new BuildFailedException($"The page has no {marker} to put its text in: {page}");
            }

            File.WriteAllText(page, html.Replace(marker, PageText()));
        }

        private static string PageText()
        {
            ShapeLandContent content;
            using (var stream = File.OpenRead(ContentBuild.Source))
            {
                content = ShapeLandContent.Load(stream);
            }

            var pairs = content
                .Strings.Entries.Where(e => e.Key.StartsWith("page."))
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => $"{Json(e.Key)}: {Json(e.Value)}");
            return "{ " + string.Join(", ", pairs) + " }";
        }

        private static string Json(string text)
        {
            var result = new StringBuilder("\"");
            foreach (var c in text)
            {
                result.Append(
                    c switch
                    {
                        '"' => "\\\"",
                        '\\' => "\\\\",
                        '<' => "\\u003c", // never "</script>" inside the page's script
                        _ when c < ' ' => $"\\u{(int)c:x4}",
                        _ => c.ToString(),
                    }
                );
            }

            return result.Append('"').ToString();
        }
    }
}
