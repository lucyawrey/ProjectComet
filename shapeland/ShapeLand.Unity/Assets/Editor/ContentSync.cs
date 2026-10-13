using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// Keeps the content that play mode and the play-mode tests read (StreamingAssets/content.bin, gitignored) the
    /// same as the content build's output: copied whenever it differs, when the editor loads its scripts and as play
    /// mode starts. The content build itself still has to run (tools/shapeland-dev.sh does).
    /// </summary>
    [InitializeOnLoad]
    public static class ContentSync
    {
        static ContentSync()
        {
            Sync();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                {
                    Sync();
                }
            };
        }

        public static string Source => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "artifacts", "content", "shapeland", "content.bin"));

        private static string Target => Path.Combine(Application.streamingAssetsPath, "content.bin");

        /// <summary>Copies the content build's output if it differs from StreamingAssets' copy; false if there is none yet.</summary>
        public static bool Sync()
        {
            if (!File.Exists(Source))
            {
                return false;
            }

            var content = File.ReadAllBytes(Source);
            if (File.Exists(Target) && Same(content, File.ReadAllBytes(Target)))
            {
                return true;
            }

            Directory.CreateDirectory(Application.streamingAssetsPath);
            File.WriteAllBytes(Target, content);
            Debug.Log("Copied the content build's content.bin into StreamingAssets.");
            return true;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
