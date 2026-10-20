using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Comet › Server › Local Server…: the editor's local game server (<see cref="LocalServer"/>) and each person's
    /// options for it: serving the last web build, opening it to the local network, and bots, so people can playtest
    /// levels together without a terminal. Makes the web build too, when the editor is on the Web platform.
    /// </summary>
    internal sealed class LocalServerWindow : EditorWindow
    {
        private const double StatusEvery = 1; // seconds between checks of who's on the port (an HTTP request)
        private const double WebBuildEvery = 10; // seconds between looks for files newer than the web build

        private double _checkedAt = -StatusEvery;
        private double _webCheckedAt = -WebBuildEvery;
        private bool _answers;
        private DateTime? _webBuiltAt;
        private string? _newerSource;

        [MenuItem("Comet/Server/Local Server…", priority = 20)]
        private static void Open() => GetWindow<LocalServerWindow>("Local Server");

        private void OnEnable()
        {
            _checkedAt = -StatusEvery;
            _webCheckedAt = -WebBuildEvery;
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.timeSinceStartup - _checkedAt >= StatusEvery)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            var settings = CometSettings.Find(quiet: true);
            if (settings == null || string.IsNullOrEmpty(settings.serverProject))
            {
                EditorGUILayout.HelpBox("The Comet settings name no server project, so there's no local game server.", MessageType.Info);
                return;
            }

            if (EditorApplication.timeSinceStartup - _checkedAt >= StatusEvery)
            {
                _checkedAt = EditorApplication.timeSinceStartup;
                _answers = LocalServer.Answers(settings);
            }

            if (EditorApplication.timeSinceStartup - _webCheckedAt >= WebBuildEvery)
            {
                _webCheckedAt = EditorApplication.timeSinceStartup;
                CheckWebBuild(settings);
            }

            Status(settings);
            EditorGUILayout.Space();
            OptionsGUI();
            EditorGUILayout.Space();
            WebBuildGUI(settings);
        }

        private void Status(CometSettings settings)
        {
            var running = LocalServer.RunningWith;
            var port = settings.serverPort;
            if (LocalServer.Starting)
            {
                EditorGUILayout.HelpBox($"Starting on port {port}…", MessageType.None);
            }
            else if (running is { } with)
            {
                var scheme = with.Web ? "http://" : "";
                var lines = $"Running on port {port}, started by the editor{(with.Bots > 0 ? $", with {with.Bots} bots" : "")}.\nThis computer: {scheme}localhost:{port}";
                if (with.Lan)
                {
                    lines += LocalServer.LanAddress() is { } lan ? $"\nOthers on the network: {scheme}{lan}:{port}" : "\nOthers on the network: (no network address found)";
                }

                EditorGUILayout.HelpBox(lines, MessageType.Info);
                var chosen = LocalServer.Chosen;
                if (chosen.Web != with.Web || chosen.Lan != with.Lan || chosen.Bots != with.Bots)
                {
                    EditorGUILayout.HelpBox("The options changed: restart to use them.", MessageType.Warning);
                }
            }
            else if (_answers)
            {
                EditorGUILayout.HelpBox($"A game server started outside the editor is running on port {port}. Play mode uses it; the editor doesn't stop or restart it.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"Stopped. Port {port}.", MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(running != null || _answers))
            {
                if (GUILayout.Button("Start"))
                {
                    LocalServer.Start(settings, build: true);
                    _checkedAt = -StatusEvery;
                }
            }

            using (new EditorGUI.DisabledScope(running == null))
            {
                if (GUILayout.Button("Stop"))
                {
                    LocalServer.Stop(quiet: false);
                    _checkedAt = -StatusEvery;
                }

                if (GUILayout.Button("Restart"))
                {
                    LocalServer.Restart();
                    _checkedAt = -StatusEvery;
                }
            }

            using (new EditorGUI.DisabledScope(!File.Exists(ContentBuild.ProjectPath(LocalServer.LogFile))))
            {
                if (GUILayout.Button("Show Log"))
                {
                    LocalServer.ShowLog();
                }
            }

            using (new EditorGUI.DisabledScope(running is not { Web: true }))
            {
                if (GUILayout.Button("Open Page"))
                {
                    Application.OpenURL($"http://localhost:{port}/");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void OptionsGUI()
        {
            EditorGUILayout.LabelField("Options (yours, on this computer)", EditorStyles.boldLabel);
            var options = LocalServer.Chosen;
            EditorGUI.BeginChangeCheck();
            options.Web = EditorGUILayout.Toggle(new GUIContent("Serve the web build", "Serve the last web build, so a browser can play: open the page at the server's address."), options.Web);
            options.Lan = EditorGUILayout.Toggle(new GUIContent("Open to the network", "Listen on every network interface, so others on this local network can join (a firewall may ask first)."), options.Lan);
            options.Bots = EditorGUILayout.IntSlider(new GUIContent("Bots", "How many bots join once the server is up."), options.Bots, 0, LocalServer.MaxBots);
            if (EditorGUI.EndChangeCheck())
            {
                LocalServer.Chosen = options;
            }

            LocalServer.StartWithPlayMode = EditorGUILayout.Toggle(new GUIContent("Start with play mode", "Entering play mode starts the server if none is running on the port."), LocalServer.StartWithPlayMode);
        }

        private void WebBuildGUI(CometSettings settings)
        {
            EditorGUILayout.LabelField("Web build", EditorStyles.boldLabel);
            if (string.IsNullOrEmpty(settings.webBuildFolder))
            {
                EditorGUILayout.HelpBox("The Comet settings name no web build folder.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField(_webBuiltAt is { } at ? $"Made {Ago(at)}." : "None yet.");
            if (_newerSource != null)
            {
                EditorGUILayout.HelpBox($"The game changed since ({_newerSource}): make a new web build to play it in a browser. Content changes need none: the server copies the content in.", MessageType.Warning);
            }

            var onWeb = EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL;
            if (!onWeb)
            {
                EditorGUILayout.HelpBox($"The editor is on {EditorUserBuildSettings.activeBuildTarget}; the web build needs the Web platform. Switching reimports the project, which takes a while.", MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!onWeb || settings.webBuildProfile == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Make Web Build"))
                {
                    MakeWebBuild(settings);
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUI.DisabledScope(onWeb || settings.webBuildProfile == null))
            {
                if (GUILayout.Button("Switch to Web…")
                    && EditorUtility.DisplayDialog("Switch to Web", "Switch the editor to the Web platform? Unity reimports the project's assets for it, which can take several minutes.", "Switch", "Cancel"))
                {
                    BuildProfile.SetActiveBuildProfile(settings.webBuildProfile);
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // Builds the web player into the settings' folder with the web build profile; the server serves the folder as
        // it is, so pages pick it up on reload.
        private void MakeWebBuild(CometSettings settings)
        {
            var folder = ContentBuild.ProjectPath(settings.webBuildFolder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
            {
                buildProfile = settings.webBuildProfile,
                locationPathName = folder,
                options = BuildOptions.None,
            });
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                File.SetLastWriteTimeUtc(Path.Combine(folder, "index.html"), DateTime.UtcNow);
                Debug.Log($"Made the web build in {settings.webBuildFolder}.");
            }
            else
            {
                Debug.LogError($"The web build failed ({report.summary.result}); see the Console.");
            }

            _webCheckedAt = -WebBuildEvery;
        }

        // When the web build was made, and the first thing it's made from that changed since (as the dev script checks),
        // leaving out the zone editor's working scenes, which aren't in builds.
        private void CheckWebBuild(CometSettings settings)
        {
            _webBuiltAt = null;
            _newerSource = null;
            if (LocalServer.WebBuild(settings) is not { } web)
            {
                return;
            }

            var page = File.GetLastWriteTimeUtc(Path.Combine(web, "index.html"));
            _webBuiltAt = page;
            var root = ContentBuild.ProjectRoot;
            var scenes = ContentBuild.ProjectPath(settings.scenesFolder) + Path.DirectorySeparatorChar;
            var folders = new[] { "Assets", "Packages", "ProjectSettings" }.Select(f => Path.Combine(root, f))
                .Concat(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Where(p => p.source == UnityEditor.PackageManager.PackageSource.Local)
                    .Select(p => p.resolvedPath));
            foreach (var folder in folders.Where(Directory.Exists))
            {
                var newer = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(f => !f.EndsWith(".meta", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}StreamingAssets{Path.DirectorySeparatorChar}")
                        && !f.StartsWith(scenes, StringComparison.Ordinal)
                        && File.GetLastWriteTimeUtc(f) > page);
                if (newer != null)
                {
                    _newerSource = Path.GetRelativePath(root, newer);
                    return;
                }
            }
        }

        private static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            return span.TotalMinutes < 1 ? "just now"
                : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
                : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
                : $"{(int)span.TotalDays} days ago";
        }
    }
}
