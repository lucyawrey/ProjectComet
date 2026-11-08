using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Builds a game's content and keeps the copy that play mode and its tests read (StreamingAssets, git ignored) the
    /// same as the build's output: copied after each build, when scripts load and as play mode starts.
    /// </summary>
    [InitializeOnLoad]
    public static class ContentBuild
    {
        private static Process? building;
        private static bool buildAgain;

        static ContentBuild()
        {
            // After the editor has finished loading, so a game's handlers can find its assets.
            EditorApplication.delayCall += () => Copy();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                {
                    Copy();
                }
            };
            ZoneEditor.Saved += _ => Build();
        }

        /// <summary>Raised with the content after each copy, whether or not it changed.</summary>
        public static event Action<byte[]>? Copied;

        /// <summary>The content build's output, as a full path; null if the settings don't name one.</summary>
        public static string? Source
        {
            get
            {
                var settings = CometSettings.Find(quiet: true);
                return settings == null || string.IsNullOrEmpty(settings.contentFile) ? null : ProjectPath(settings.contentFile);
            }
        }

        [MenuItem("Comet/Content/Build", priority = 20)]
        private static void BuildMenu() => Build();

        /// <summary>A build asked for during another runs after it.</summary>
        public static void Build()
        {
            var settings = CometSettings.Find(quiet: false);
            if (settings == null || string.IsNullOrEmpty(settings.contentBuildProject))
            {
                Debug.LogWarning("The Comet settings name no content build project, so the content wasn't built.");
                return;
            }

            if (building != null)
            {
                buildAgain = true;
                return;
            }

            var start = new ProcessStartInfo(Dotnet(), $"run --project \"{ProjectPath(settings.contentBuildProject)}\" -- build")
            {
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var output = new StringBuilder();
            try
            {
                building = new Process { StartInfo = start };
                building.OutputDataReceived += (_, e) =>
                {
                    lock (output)
                    {
                        output.AppendLine(e.Data);
                    }
                };
                building.ErrorDataReceived += (_, e) =>
                {
                    lock (output)
                    {
                        output.AppendLine(e.Data);
                    }
                };
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

            var progress = Progress.Start("Content build", "Building the content from its files");
            void Poll()
            {
                if (!building!.HasExited)
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
                else if (Copy())
                {
                    AssetDatabase.Refresh();
                    Debug.Log("Built the content and copied it in.");
                }

                // Warnings (such as blockout placements) don't stop the build, but are worth seeing.
                if (!failed)
                {
                    lock (output)
                    {
                        foreach (var line in output.ToString().Split('\n').Where(l => l.Contains(": warning: ")))
                        {
                            Debug.LogWarning($"Content build: {line.Trim()}");
                        }
                    }
                }

                if (buildAgain)
                {
                    buildAgain = false;
                    Build();
                }
            }

            EditorApplication.update += Poll;
        }

        /// <summary>Returns false if there is no build output yet.</summary>
        public static bool Copy()
        {
            var source = Source;
            if (source == null || !File.Exists(source))
            {
                return false;
            }

            var content = File.ReadAllBytes(source);
            var target = Path.Combine(Application.streamingAssetsPath, Path.GetFileName(source));
            if (!File.Exists(target) || !content.AsSpan().SequenceEqual(File.ReadAllBytes(target)))
            {
                Directory.CreateDirectory(Application.streamingAssetsPath);
                File.WriteAllBytes(target, content);
                Debug.Log($"Copied the content build's {Path.GetFileName(source)} into StreamingAssets.");
            }

            Copied?.Invoke(content);
            return true;
        }

        // The editor may lack the shell's PATH (opened from Unity Hub, say), so also look where installers put dotnet.
        internal static string Dotnet()
        {
            var windows = Application.platform == RuntimePlatform.WindowsEditor;
            var name = windows ? "dotnet.exe" : "dotnet";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var known = windows
                ? new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", name),
                    Path.Combine(home, ".dotnet", name),
                }
                : new[]
                {
                    Path.Combine(home, ".dotnet", name),
                    "/usr/local/share/dotnet/dotnet",
                    "/opt/homebrew/bin/dotnet",
                    "/usr/share/dotnet/dotnet",
                    "/usr/bin/dotnet",
                };
            var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator)
                .Where(dir => dir.Length > 0)
                .Select(dir => Path.Combine(dir, name));
            return onPath.Concat(known).FirstOrDefault(File.Exists) ?? "dotnet";
        }

        internal static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        internal static string ProjectPath(string relative) => Path.GetFullPath(Path.Combine(ProjectRoot, relative));
    }
}
