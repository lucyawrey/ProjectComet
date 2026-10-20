using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Comet.Content;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Runs the game server (the Comet settings' server project) on this computer for play mode, so play mode needs no
    /// terminal. With Comet › Server › Start with Play Mode on (each person's own choice, on by default), entering play
    /// mode starts it unless a game server already answers <c>/health</c> on the port: one started elsewhere (a
    /// script) is used as it is and never restarted or stopped from here. The editor's own server keeps running
    /// between plays, restarts when the content changes, and stops when the editor quits. It logs to
    /// <see cref="LogFile"/>. Which server it is (which pid, started when, with which content) is kept in
    /// <c>Library/</c>, so the editor finds it again after a script reload, or after a restart if it crashed.
    /// </summary>
    [InitializeOnLoad]
    public static class LocalServer
    {
        /// <summary>The server's log, relative to the Unity project.</summary>
        public const string LogFile = "Logs/game-server.log";

        private const string Menu = "Comet/Server/";
        private const string StartWithPlayKey = "Comet.LocalServer.StartWithPlayMode";
        private const string StateFile = "Library/CometLocalServer.txt";
        private const double StartTimeout = 60;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };

        static LocalServer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.quitting += () => Stop(quiet: true);
            ContentBuild.Copied += OnContentCopied;

            // A start that was still waiting for the server to answer when the scripts reloaded (entering play mode does).
            if (Read() is { Waiting: true } state && Alive(state))
            {
                Wait();
            }
        }

        /// <summary>Whether entering play mode starts the server if none is running; this person's choice, on by default.</summary>
        public static bool StartWithPlayMode
        {
            get => EditorPrefs.GetBool(StartWithPlayKey, true);
            set => EditorPrefs.SetBool(StartWithPlayKey, value);
        }

        /// <summary>Whether the editor's own server is running.</summary>
        public static bool Running => Read() is { } state && Alive(state);

        [MenuItem(Menu + "Start", priority = 40)]
        private static void StartMenu()
        {
            if (Settings() is { } settings && Free(settings))
            {
                Start(settings, build: true);
            }
        }

        [MenuItem(Menu + "Start", validate = true)]
        private static bool CanStart() => !Running && Settings() != null;

        [MenuItem(Menu + "Stop", priority = 41)]
        private static void StopMenu() => Stop(quiet: false);

        [MenuItem(Menu + "Restart", priority = 42)]
        private static void RestartMenu()
        {
            if (Settings() is { } settings)
            {
                Stop(quiet: true);
                Start(settings, build: true);
            }
        }

        [MenuItem(Menu + "Stop", validate = true)]
        private static bool CanStop() => Running;

        [MenuItem(Menu + "Restart", validate = true)]
        private static bool CanRestart() => Running;

        [MenuItem(Menu + "Show Log", priority = 43)]
        private static void ShowLog() => EditorUtility.OpenWithDefaultApp(ContentBuild.ProjectPath(LogFile));

        [MenuItem(Menu + "Show Log", validate = true)]
        private static bool CanShowLog() => File.Exists(ContentBuild.ProjectPath(LogFile));

        [MenuItem(Menu + "Start with Play Mode", priority = 60)]
        private static void ToggleStartWithPlayMode() => StartWithPlayMode = !StartWithPlayMode;

        [MenuItem(Menu + "Start with Play Mode", validate = true)]
        private static bool ShowStartWithPlayMode()
        {
            UnityEditor.Menu.SetChecked(Menu + "Start with Play Mode", StartWithPlayMode);
            return true;
        }

        // Entering play mode: use the server that's running (restarting the editor's own if the content changed),
        // else start one; another program on the port stops play mode from starting.
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode || !StartWithPlayMode || Settings() is not { } settings)
            {
                return;
            }

            var content = ContentHashNow();
            if (Read() is { } state && Alive(state))
            {
                if (content != null && content != state.Content)
                {
                    Debug.Log("The content changed since the local game server started; restarting it.");
                    Stop(quiet: true);
                    Start(settings, build: false);
                }

                return;
            }

            if (!Free(settings))
            {
                EditorApplication.isPlaying = false;
            }
            else if (!Answers(settings))
            {
                Start(settings, build: true);
            }
        }

        // After a content build (or a copy, as the editor loads): restart the editor's own server if its content is
        // older; say so for one started elsewhere.
        private static void OnContentCopied(byte[] contentFile)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Settings() is not { } settings)
            {
                return; // entering play mode checks for itself
            }

            var hash = ContentHash.Of(contentFile);
            if (Read() is { } state && Alive(state))
            {
                if (state.Content != hash && !state.Waiting)
                {
                    Debug.Log("Restarting the local game server for the new content.");
                    Stop(quiet: true);
                    Start(settings, build: false);
                }
            }
            else if (Answers(settings) && PlayedContent(settings) is { } played && played != hash)
            {
                Debug.LogWarning($"The game server on port {settings.serverPort} was started outside the editor and plays older content; restart it to play the new content.");
            }
        }

        // Whether the port is free for the editor's server, or has a game server on it already; false (with the
        // reason in the Console) if another program holds it.
        private static bool Free(CometSettings settings)
        {
            if (Answers(settings))
            {
                if (PlayedContent(settings) is { } played && ContentHashNow() is { } ours && played != ours)
                {
                    Debug.LogWarning($"Using the game server on port {settings.serverPort}, started outside the editor; it plays different content than the editor has, so restart it to play the new content.");
                }
                else
                {
                    Debug.Log($"Using the game server on port {settings.serverPort}, started outside the editor.");
                }

                return true;
            }

            if (!PortInUse(settings.serverPort))
            {
                return true;
            }

            Debug.LogError($"Another program is using port {settings.serverPort}, so the local game server can't start there. Close it, or change the Comet settings' server port (and the port the game joins).");
            return false;
        }

        /// <summary>
        /// Builds the server (when <paramref name="build"/>, or when it never was) and starts it, then waits for it to
        /// answer in the background. False (with the reason in the Console) if it couldn't start.
        /// </summary>
        public static bool Start(CometSettings settings, bool build)
        {
            if (string.IsNullOrEmpty(settings.serverProject) || Running || Answers(settings))
            {
                return false;
            }

            var content = ContentHashNow();
            if (content == null)
            {
                Debug.LogError("There's no built content for the local game server yet: build it first (Comet › Content › Build).");
                return false;
            }

            var program = Read()?.Program;
            if (build || program == null || !File.Exists(program))
            {
                program = Build(settings);
                if (program == null)
                {
                    return false;
                }
            }

            var log = ContentBuild.ProjectPath(LogFile);
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, "");

            // The built program runs directly: stopping `dotnet run` can leave the program it started running.
            var start = new ProcessStartInfo(ContentBuild.Dotnet(), $"\"{program}\" --urls http://localhost:{settings.serverPort} \"--Comet:LogFile={log}\" {settings.serverArguments}")
            {
                WorkingDirectory = ContentBuild.ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            try
            {
                using var process = Process.Start(start)!;
                Write(new State { Pid = process.Id, StartTicks = process.StartTime.ToUniversalTime().Ticks, Content = content, Program = program, Waiting = true });
            }
            catch (Exception e)
            {
                Debug.LogError($"Couldn't start the local game server ({start.FileName} {start.Arguments}): {e.Message}");
                return false;
            }

            Wait();
            return true;
        }

        /// <summary>Stops the editor's own server, if it's running; never one started elsewhere.</summary>
        public static void Stop(bool quiet)
        {
            if (Read() is not { } state)
            {
                return;
            }

            if (Alive(state))
            {
                try
                {
                    using var process = Process.GetProcessById(state.Pid);
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
                {
                    // It has stopped by itself meanwhile.
                }

                if (!quiet)
                {
                    Debug.Log("Stopped the local game server.");
                }
            }

            File.Delete(ContentBuild.ProjectPath(StateFile));
        }

        // Builds the server project, returning the program built, or null (with the build's output in the Console).
        private static string? Build(CometSettings settings)
        {
            var project = ContentBuild.ProjectPath(settings.serverProject);
            var start = new ProcessStartInfo(ContentBuild.Dotnet(), $"build \"{project}\" -nologo -v quiet -t:Build -getProperty:TargetPath")
            {
                WorkingDirectory = ContentBuild.ProjectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            EditorUtility.DisplayProgressBar("Local game server", "Building the game server…", 0.5f);
            try
            {
                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                var program = output.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0);
                if (process.ExitCode != 0 || program == null || !File.Exists(program))
                {
                    Debug.LogError($"Couldn't build the local game server ({project}):\n{output}{error.Result}");
                    return null;
                }

                return program;
            }
            catch (Exception e)
            {
                Debug.LogError($"Couldn't run dotnet to build the local game server: {e.Message}. Is the .NET SDK installed?");
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Waits in the background for the server just started to answer, or to stop (saying why, from its log).
        private static void Wait()
        {
            var settings = Settings();
            if (settings == null)
            {
                return;
            }

            var giveUp = EditorApplication.timeSinceStartup + StartTimeout;
            Task<HttpResponseMessage>? asking = null;
            void Poll()
            {
                if (Read() is not { Waiting: true } state)
                {
                    EditorApplication.update -= Poll; // stopped meanwhile
                    return;
                }

                if (!Alive(state))
                {
                    EditorApplication.update -= Poll;
                    File.Delete(ContentBuild.ProjectPath(StateFile));
                    Debug.LogError($"The local game server stopped while starting (Comet › Server › Show Log):\n{Tail()}");
                    return;
                }

                if (asking is { IsCompleted: true })
                {
                    if (asking.Status == TaskStatus.RanToCompletion && asking.Result.IsSuccessStatusCode)
                    {
                        EditorApplication.update -= Poll;
                        state.Waiting = false;
                        Write(state);
                        Debug.Log($"The local game server is running at localhost:{settings.serverPort}.");
                        return;
                    }

                    asking = null;
                }

                if (EditorApplication.timeSinceStartup > giveUp)
                {
                    EditorApplication.update -= Poll;
                    state.Waiting = false;
                    Write(state);
                    Debug.LogWarning($"The local game server hasn't answered after {StartTimeout} s (Comet › Server › Show Log).");
                    return;
                }

                asking ??= Http.GetAsync($"http://localhost:{settings.serverPort}/health");
            }

            EditorApplication.update += Poll;
        }

        // Whether a game server answers /health on the port now.
        private static bool Answers(CometSettings settings)
        {
            try
            {
                return Task.Run(() => Http.GetAsync($"http://localhost:{settings.serverPort}/health")).Result.IsSuccessStatusCode;
            }
            catch (AggregateException)
            {
                return false;
            }
        }

        [Serializable]
        private sealed class Info
        {
            public string contentHash = "";
        }

        // The content hash the server on the port says it plays (its /info), or null if it doesn't say.
        private static string? PlayedContent(CometSettings settings)
        {
            try
            {
                var text = Task.Run(() => Http.GetStringAsync($"http://localhost:{settings.serverPort}/info")).Result;
                var hash = JsonUtility.FromJson<Info>(text)?.contentHash;
                return string.IsNullOrEmpty(hash) ? null : hash;
            }
            catch (Exception e) when (e is AggregateException or ArgumentException)
            {
                return null;
            }
        }

        private static bool PortInUse(int port)
        {
            try
            {
                using var client = new TcpClient();
                return client.ConnectAsync("localhost", port).Wait(500) && client.Connected;
            }
            catch (Exception e) when (e is SocketException or AggregateException)
            {
                return false;
            }
        }

        // The built content's hash now, or null if there's none.
        private static string? ContentHashNow() =>
            ContentBuild.Source is { } source && File.Exists(source) ? ContentHash.Of(File.ReadAllBytes(source)) : null;

        private static CometSettings? Settings() =>
            CometSettings.Find(quiet: true) is { } settings && !string.IsNullOrEmpty(settings.serverProject) ? settings : null;

        // The last lines of the server's log.
        private static string Tail()
        {
            try
            {
                using var stream = new FileStream(ContentBuild.ProjectPath(LogFile), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var lines = new StreamReader(stream).ReadToEnd().Split('\n');
                return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 15)));
            }
            catch (IOException)
            {
                return "(no log)";
            }
        }

        // The editor's own server: its process (pid and start time, so a reused pid isn't taken for it), the content
        // it started with, the program run, and whether it's still starting.
        private sealed class State
        {
            public int Pid;
            public long StartTicks;
            public string Content = "";
            public string Program = "";
            public bool Waiting;
        }

        private static State? Read()
        {
            var path = ContentBuild.ProjectPath(StateFile);
            if (!File.Exists(path))
            {
                return null;
            }

            var lines = File.ReadAllLines(path);
            return lines.Length >= 5 && int.TryParse(lines[0], out var pid) && long.TryParse(lines[1], out var ticks)
                ? new State { Pid = pid, StartTicks = ticks, Content = lines[2], Program = lines[3], Waiting = lines[4] == "waiting" }
                : null;
        }

        private static void Write(State state) =>
            File.WriteAllText(ContentBuild.ProjectPath(StateFile),
                new StringBuilder().AppendLine(state.Pid.ToString()).AppendLine(state.StartTicks.ToString()).AppendLine(state.Content)
                    .AppendLine(state.Program).AppendLine(state.Waiting ? "waiting" : "running").ToString());

        // Whether the process the state names is still the one the editor started.
        private static bool Alive(State state)
        {
            try
            {
                using var process = Process.GetProcessById(state.Pid);
                return !process.HasExited && Math.Abs(process.StartTime.ToUniversalTime().Ticks - state.StartTicks) < TimeSpan.TicksPerSecond;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
