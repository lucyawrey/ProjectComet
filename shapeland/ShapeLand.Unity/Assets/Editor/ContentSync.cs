using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Comet.Unity;
using Comet.Unity.Editor;
using ShapeLand.Shared.Content;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// Keeps the content that play mode and the play-mode tests read (StreamingAssets/content.bin, gitignored) the
    /// same as the content build's output: copied whenever it differs, when the editor loads its scripts and as play
    /// mode starts. The asset library (<see cref="AssetLibrary"/>) follows the content's asset entries at the same
    /// times. Saving a zone in the zone editor runs the content build and copies its output; otherwise the content
    /// build still has to run (tools/shapeland-dev.sh does).
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
            ZoneEditor.Saved += _ => BuildContent();
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

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static Process building;
        private static bool buildAgain;

        /// <summary>Runs the content build in the background, then copies its output in; a save during a build queues one more.</summary>
        public static void BuildContent()
        {
            if (building != null)
            {
                buildAgain = true;
                return;
            }

            var start = new ProcessStartInfo(Dotnet(), "run --project shapeland/src/ShapeLand.ContentBuild -- build")
            {
                WorkingDirectory = RepoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var output = new System.Text.StringBuilder();
            try
            {
                building = new Process { StartInfo = start };
                building.OutputDataReceived += (_, e) => { lock (output) { output.AppendLine(e.Data); } };
                building.ErrorDataReceived += (_, e) => { lock (output) { output.AppendLine(e.Data); } };
                building.Start();
                building.BeginOutputReadLine();
                building.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                building = null;
                Debug.LogError($"Couldn't run the content build ({start.FileName} {start.Arguments}): {e.Message}");
                return;
            }

            var progress = Progress.Start("Content build", "Building ShapeLand's content from its files");
            void Poll()
            {
                if (!building.HasExited)
                {
                    return;
                }

                EditorApplication.update -= Poll;
                building.WaitForExit();
                var failed = building.ExitCode != 0;
                building.Dispose();
                building = null;
                Progress.Finish(progress, failed ? Progress.Status.Failed : Progress.Status.Succeeded);
                if (failed)
                {
                    lock (output)
                    {
                        Debug.LogError($"The content build failed:\n{output}");
                    }
                }
                else
                {
                    Sync();
                    AssetDatabase.Refresh();
                    Debug.Log("Built the content and copied it in. A running game server needs a restart to use it.");
                }

                if (buildAgain)
                {
                    buildAgain = false;
                    BuildContent();
                }
            }

            EditorApplication.update += Poll;
        }

        // The editor may not have the shell's PATH (opened from Unity Hub, say), so look where installers put dotnet too.
        private static string Dotnet()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var known = new[]
            {
                Path.Combine(home, ".dotnet", "dotnet"),
                "/usr/local/share/dotnet/dotnet",
                "/opt/homebrew/bin/dotnet",
                "/usr/share/dotnet/dotnet",
                "/usr/bin/dotnet",
            };
            var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(dir => Path.Combine(dir, "dotnet"));
            return onPath.Concat(known).FirstOrDefault(File.Exists) ?? "dotnet";
        }

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
            RefreshZoneEditorSettings(content);
            return true;
        }

        // The zone editor's settings that come from the game's content: terrain colors, the body height and spawn markers.
        private static void RefreshZoneEditorSettings(byte[] contentFile)
        {
            var settings = AssetDatabase.LoadAssetAtPath<Comet.Unity.Editor.ZoneEditorSettings>("Assets/Settings/ZoneEditorSettings.asset");
            if (settings == null)
            {
                return;
            }

            ShapeLandContent content;
            try
            {
                content = ShapeLandContent.Load(new MemoryStream(contentFile));
            }
            catch (Comet.Content.ContentException)
            {
                return; // RefreshLibrary has said so
            }

            var game = content.Settings;
            var ground = GameView.Color(game.World.GroundColor);
            var skirt = GameView.Color(game.World.SkirtColor);
            if (settings.groundColor == ground && settings.skirtColor == skirt && settings.bodyHeight == game.Movement.BodyHeight
                && settings.spawnMarkerAsset == game.Spawns.MarkerAsset && settings.spawnMarkerHeight == game.Spawns.MarkerHeight)
            {
                return;
            }

            settings.groundColor = ground;
            settings.skirtColor = skirt;
            settings.bodyHeight = game.Movement.BodyHeight;
            settings.spawnMarkerAsset = game.Spawns.MarkerAsset;
            settings.spawnMarkerHeight = game.Spawns.MarkerHeight;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log("Updated the zone editor settings from the content.");
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

                var min = new Vector3(asset.BoundsMin.X, asset.BoundsMin.Y, asset.BoundsMin.Z);
                var max = new Vector3(asset.BoundsMax.X, asset.BoundsMax.Y, asset.BoundsMax.Z);
                return new AssetLibrary.Entry { key = asset.Id, model = model, uniformScale = asset.UniformScale, bounds = new Bounds((min + max) / 2, max - min) };
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
