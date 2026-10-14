using System.IO;
using System.Linq;
using ShapeLand.Shared.Content;
using UnityEditor;
using UnityEngine;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// Keeps the content that play mode and the play-mode tests read (StreamingAssets/content.bin, gitignored) the
    /// same as the content build's output: copied whenever it differs, when the editor loads its scripts and as play
    /// mode starts. The asset library (<see cref="AssetLibrary"/>) follows the content's asset entries at the same
    /// times. The content build itself still has to run (tools/shapeland-dev.sh does).
    /// </summary>
    [InitializeOnLoad]
    public static class ContentSync
    {
        /// <summary>Where the asset library lives; the game scene refers to it.</summary>
        public const string LibraryPath = "Assets/Models/AssetLibrary.asset";

        static ContentSync()
        {
            // After the editor has finished loading, so the models can be found.
            EditorApplication.delayCall += () => Sync();
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
            if (!File.Exists(Target) || !Same(content, File.ReadAllBytes(Target)))
            {
                Directory.CreateDirectory(Application.streamingAssetsPath);
                File.WriteAllBytes(Target, content);
                Debug.Log("Copied the content build's content.bin into StreamingAssets.");
            }

            RefreshLibrary(content);
            return true;
        }

        // Points each asset key at the model its entry names (a path under Assets/), saving only if something changed.
        private static void RefreshLibrary(byte[] contentFile)
        {
            ShapeLandContent content;
            try
            {
                content = ShapeLandContent.Load(new MemoryStream(contentFile));
            }
            catch (Comet.Content.ContentException e)
            {
                Debug.LogWarning($"Can't read the content to update the asset library: {e.Message}");
                return;
            }

            var library = AssetDatabase.LoadAssetAtPath<AssetLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AssetLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var entries = content.Assets.All.Select(asset =>
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + asset.Model);
                if (model == null)
                {
                    Debug.LogWarning($"{asset.Id}'s model Assets/{asset.Model} isn't in the project.");
                }

                return new AssetLibrary.Entry { key = asset.Id, model = model };
            }).ToList();
            if (entries.SequenceEqual(library.Entries))
            {
                return;
            }

            library.SetEntries(entries);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            Debug.Log($"Updated the asset library ({entries.Count} assets).");
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
