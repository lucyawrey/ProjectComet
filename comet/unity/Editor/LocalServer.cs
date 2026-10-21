using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Comet.Content;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Runs the game server (the Comet settings' server project) on this computer, so play mode and local playtests
    /// need no terminal. With Start with Play Mode on (each person's own choice, on by default), entering play mode
    /// starts it unless a game server already answers <c>/health</c> on the port: one started elsewhere (a script) is
    /// used as it is and never restarted or stopped from here. The editor's own server keeps running between plays,
    /// restarts when the content changes, and stops when the editor quits. Each person's <see cref="Options"/> can also
    /// serve the last web build, open it to the local network, and start bots (Comet › Server › Options…). It logs
    /// to <see cref="LogFile"/>. Which server it is (pids, start times, content) is kept in <c>Library/</c>, so the
    /// editor finds it again after a script reload, or after a restart if it crashed. Builds run in the background
    /// (Unity's progress bar, bottom right), so the editor and play mode carry on; play mode can't connect until the
    /// server answers.
    /// </summary>
    [InitializeOnLoad]
    public static class LocalServer
    {
        /// <summary>The server's log, relative to the Unity project.</summary>
        public const string LogFile = "Logs/game-server.log";

        private const string Menu = "Comet/Server/";
        // Editor preferences are shared by every project, so each key names this one by its folder.
        private static readonly string Prefix = $"Comet.LocalServer.{ContentHash.Of(System.Text.Encoding.UTF8.GetBytes(ContentBuild.ProjectRoot))}.";
        private static readonly string StartWithPlayKey = Prefix + "StartWithPlayMode";
        private static readonly string WebKey = Prefix + "Web";
        private static readonly string LanKey = Prefix + "Lan";
        private static readonly string BotsKey = Prefix + "Bots";
        // The progress item of a build under way, kept in the editor session so a script reload can pick it up.
        private static readonly string BuildingKey = Prefix + "Building";
        private const string StateFile = "Library/CometLocalServer.json";
        private const double StartTimeout = 60;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };

        static LocalServer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.quitting += () => Stop(quiet: true);
            ContentBuild.Copied += OnContentCopied;
            EditorApplication.delayCall += SharePort;

            // A start that was still waiting for the server to answer when the scripts reloaded (entering play mode does).
            if (Read() is { waiting: true } state && Alive(state.pid, state.startTicks))
            {
                Wait();
            }

            // A build the scripts reloaded during (saving a script does) lost what was waiting for it: build again.
            if (SessionState.GetInt(BuildingKey, 0) is var progress && progress != 0)
            {
                SessionState.EraseInt(BuildingKey);
                if (Progress.Exists(progress))
                {
                    Progress.Remove(progress);
                }

                EditorApplication.delayCall += () =>
                {
                    if (Settings() is { } settings)
                    {
                        Start(settings, build: true);
                    }
                };
            }
        }

        /// <summary>What the editor's server runs with: each person's own choices, kept in the editor's preferences.</summary>
        public struct Options
        {
            /// <summary>Serve the last web build from the server, so a browser can play.</summary>
            public bool Web;

            /// <summary>Listen on every network interface, so others on the local network can join.</summary>
            public bool Lan;

            /// <summary>How many bots to start once the server answers.</summary>
            public int Bots;
        }

        /// <summary>Whether entering play mode starts the server if none is running; on by default.</summary>
        public static bool StartWithPlayMode
        {
            get => EditorPrefs.GetBool(StartWithPlayKey, true);
            set => EditorPrefs.SetBool(StartWithPlayKey, value);
        }

        /// <summary>The options the next start uses.</summary>
        public static Options Chosen
        {
            get => new Options { Web = EditorPrefs.GetBool(WebKey, false), Lan = EditorPrefs.GetBool(LanKey, false), Bots = EditorPrefs.GetInt(BotsKey, 0) };
            set
            {
                EditorPrefs.SetBool(WebKey, value.Web);
                EditorPrefs.SetBool(LanKey, value.Lan);
                EditorPrefs.SetInt(BotsKey, Mathf.Clamp(value.Bots, 0, MaxBots));
            }
        }

        /// <summary>The most bots the editor starts.</summary>
        public const int MaxBots = 50;

        /// <summary>Whether the editor's own server is running.</summary>
        public static bool Running => Read() is { } state && Alive(state.pid, state.startTicks);

        /// <summary>Whether it's still starting: building, or launched and not yet answering.</summary>
        public static bool Starting => _building != null || (Read() is { waiting: true } state && Alive(state.pid, state.startTicks));

        /// <summary>Whether a start is building the server (or bots) in the background.</summary>
        public static bool Building => _building != null;

        // The server (and bots) being built in the background for a start, or null; and a count of stops, so a stop
        // during the build cancels its launch.
        private static Task<(Built Server, Built Bots)>? _building;
        private static int _stops;

        /// <summary>The options the running server started with, or null if the editor's server isn't running.</summary>
        public static Options? RunningWith =>
            Read() is { } state && Alive(state.pid, state.startTicks) ? new Options { Web = state.web, Lan = state.lan, Bots = state.bots } : (Options?)null;

        /// <summary>Whether a game server answers <c>/health</c> on the settings' port now (the editor's own or another).</summary>
        public static bool Answers(CometSettings settings)
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

        /// <summary>This computer's address on the local network (the interface with a gateway), or null if it has none.</summary>
        public static string? LanAddress()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => n.GetIPProperties())
                    .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                    .SelectMany(p => p.UnicastAddresses)
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString();
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        [MenuItem(Menu + "Start", priority = 40)]
        private static void StartMenu()
        {
            if (Settings() is { } settings && Answers(settings) is var answers && Free(settings, answers))
            {
                Start(settings, build: true, answers);
            }
        }

        [MenuItem(Menu + "Start", validate = true)]
        private static bool CanStart() => !Running && _building == null && Settings() != null;

        [MenuItem(Menu + "Stop", priority = 41)]
        private static void StopMenu() => Stop(quiet: false);

        [MenuItem(Menu + "Stop", validate = true)]
        private static bool CanStop() => Running;

        [MenuItem(Menu + "Restart", priority = 42)]
        private static void RestartMenu() => Restart();

        [MenuItem(Menu + "Restart", validate = true)]
        private static bool CanRestart() => Running && _building == null;

        [MenuItem(Menu + "Show Log", priority = 43)]
        public static void ShowLog() => EditorUtility.OpenWithDefaultApp(ContentBuild.ProjectPath(LogFile));

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

        /// <summary>Stops the editor's server and starts it again with the chosen options, building it first.</summary>
        public static void Restart()
        {
            if (Settings() is { } settings)
            {
                Stop(quiet: true);
                Start(settings, build: true);
            }
        }

        // Entering play mode: use the server that's running (restarting the editor's own if the content changed),
        // else start one; another program on the port stops play mode from starting.
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode || !StartWithPlayMode || Settings() is not { } settings)
            {
                return;
            }

            SharePort();
            var content = ContentHashNow();
            if (Read() is { } state && Alive(state.pid, state.startTicks))
            {
                if (content != null && content != state.content)
                {
                    Debug.Log("The content changed since the local game server started; restarting it.");
                    Stop(quiet: true);
                    Start(settings, build: false);
                }

                return;
            }

            // Asked once: each ask can take up to a second when something holds the port without answering.
            var answers = Answers(settings);
            if (!Free(settings, answers))
            {
                EditorApplication.isPlaying = false;
            }
            else if (!answers)
            {
                Start(settings, build: true, answers); // an incremental build is quick, and a pull may have changed the server
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
            if (Read() is { } state && Alive(state.pid, state.startTicks))
            {
                if (state.content != hash && !state.waiting)
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

        // Whether the port is free for the editor's server, or has a game server on it already (answers: one answered
        // just now); false (with the reason in the Console) if another program holds it.
        private static bool Free(CometSettings settings, bool answers)
        {
            if (answers)
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

            Debug.LogError($"Another program is using port {settings.serverPort}, so the local game server can't start there. Close it, or change the Comet settings' server port.");
            return false;
        }

        /// <summary>
        /// Starts the server with the chosen options, building it (and the bots, if any) first when
        /// <paramref name="build"/> or when it never was, then waits for it to answer in the background and starts the
        /// bots. False (with the reason in the Console) if it couldn't start. <paramref name="answers"/> passes on
        /// whether a server answered on the port just now, to save asking again.
        /// </summary>
        public static bool Start(CometSettings settings, bool build, bool? answers = null)
        {
            if (string.IsNullOrEmpty(settings.serverProject) || Running || _building != null || (answers ?? Answers(settings)))
            {
                return false;
            }

            if (ContentHashNow() == null)
            {
                Debug.LogError("There's no built content for the local game server yet: build it first (Comet › Content › Build).");
                return false;
            }

            var options = Chosen;
            var last = Read();
            var program = last?.program ?? "";
            var bots = last?.botsProgram ?? "";
            var buildServer = build || program.Length == 0 || !File.Exists(program);
            var buildBots = options.Bots > 0 && !string.IsNullOrEmpty(settings.botsProject) && (build || bots.Length == 0 || !File.Exists(bots));
            if (!buildServer && !buildBots)
            {
                return Launch(settings, options, program, bots);
            }

            // Built off the main thread, so the editor (and play mode) carry on meanwhile.
            var dotnet = ContentBuild.Dotnet();
            var root = ContentBuild.ProjectRoot;
            var serverProject = ContentBuild.ProjectPath(settings.serverProject);
            var botsProject = buildBots ? ContentBuild.ProjectPath(settings.botsProject) : "";
            var progress = Progress.Start("Local game server", buildBots ? "Building the game server and bots" : "Building the game server");
            var building = Task.Run(() => (
                buildServer ? Build(dotnet, root, serverProject) : new Built(program, null),
                buildBots ? Build(dotnet, root, botsProject) : new Built(bots, null)));
            _building = building;
            SessionState.SetInt(BuildingKey, progress);
            var stops = _stops;
            void Poll()
            {
                if (!building.IsCompleted)
                {
                    return;
                }

                EditorApplication.update -= Poll;
                _building = null;
                SessionState.EraseInt(BuildingKey);
                Progress.Remove(progress);
                var (server, botsBuilt) = building.Result;
                if (server.Error != null)
                {
                    Debug.LogError($"Couldn't build the game server ({serverProject}):\n{server.Error}");
                    return;
                }

                if (botsBuilt.Error != null)
                {
                    Debug.LogWarning($"Couldn't build the bots ({botsProject}), so the server starts without them:\n{botsBuilt.Error}");
                }

                if (stops != _stops || Running || Answers(settings))
                {
                    return; // stopped, or started elsewhere, meanwhile
                }

                Launch(settings, options, server.Program!, botsBuilt.Error == null ? botsBuilt.Program ?? "" : "");
            }

            EditorApplication.update += Poll;
            return true;
        }

        // Starts the built server with options, then waits for it to answer in the background; false (with the reason
        // in the Console) if it couldn't start.
        private static bool Launch(CometSettings settings, Options options, string program, string bots)
        {
            var content = ContentHashNow();
            if (content == null)
            {
                return false;
            }

            if (options.Bots > 0 && (bots.Length == 0 || !File.Exists(bots)))
            {
                Debug.LogWarning(string.IsNullOrEmpty(settings.botsProject) ? "The Comet settings name no bots project, so no bots start." : "Starting the server without bots.");
                options.Bots = 0;
            }

            var arguments = $"\"{program}\" --urls http://{(options.Lan ? "0.0.0.0" : "localhost")}:{settings.serverPort} \"--Comet:LogFile={ContentBuild.ProjectPath(LogFile)}\"";
            if (options.Web)
            {
                if (WebBuild(settings) is { } web)
                {
                    CopyContentInto(web);
                    arguments += " " + string.Format(settings.serverWebArgument, $"\"{web}\"");
                }
                else
                {
                    Debug.LogWarning("There's no web build to serve yet: make one in Comet › Server › Options…; starting the server without it.");
                    options.Web = false;
                }
            }

            if (options.Bots > 0)
            {
                arguments += " --Comet:Connections:MaxConnectionsPerAddress=1000"; // the bots all come from this computer
            }

            var log = ContentBuild.ProjectPath(LogFile);
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, "");

            // The built program runs directly: stopping `dotnet run` can leave the program it started running.
            var start = new ProcessStartInfo(ContentBuild.Dotnet(), $"{arguments} {settings.serverArguments}")
            {
                WorkingDirectory = ContentBuild.ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            try
            {
                using var process = Process.Start(start)!;
                Write(new State
                {
                    pid = process.Id,
                    startTicks = process.StartTime.ToUniversalTime().Ticks,
                    content = content,
                    program = program,
                    botsProgram = bots,
                    waiting = true,
                    web = options.Web,
                    lan = options.Lan,
                    bots = options.Bots,
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"Couldn't start the local game server ({start.FileName} {start.Arguments}): {e.Message}");
                return false;
            }

            Wait();
            return true;
        }

        /// <summary>Stops the editor's own server and its bots, if it's running; never a server started elsewhere.</summary>
        public static void Stop(bool quiet)
        {
            _stops++;
            if (Read() is not { } state)
            {
                return;
            }

            Kill(state.botsPid, state.botsStartTicks);
            if (Kill(state.pid, state.startTicks) && !quiet)
            {
                Debug.Log("Stopped the local game server.");
            }

            File.Delete(ContentBuild.ProjectPath(StateFile));
        }

        /// <summary>The web build folder (a full path), if it has a page; null otherwise.</summary>
        public static string? WebBuild(CometSettings settings)
        {
            if (string.IsNullOrEmpty(settings.webBuildFolder))
            {
                return null;
            }

            var folder = ContentBuild.ProjectPath(settings.webBuildFolder);
            return File.Exists(Path.Combine(folder, "index.html")) ? folder : null;
        }

        // The web build loads its content from its own StreamingAssets, so a content change needs only a copy, not a new build.
        private static void CopyContentInto(string web)
        {
            if (ContentBuild.Source is not { } source || !File.Exists(source))
            {
                return;
            }

            var target = Path.Combine(web, "StreamingAssets", Path.GetFileName(source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        // A build's program, or why there's none.
        private readonly struct Built
        {
            public Built(string? program, string? error)
            {
                Program = program;
                Error = error;
            }

            public string? Program { get; }

            public string? Error { get; }
        }

        // Builds a project with dotnet (run off the main thread: no Unity calls), returning the program built or the build's output.
        private static Built Build(string dotnet, string root, string project)
        {
            var start = new ProcessStartInfo(dotnet, $"build \"{project}\" -nologo -v quiet -t:Build -getProperty:TargetPath")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                var program = output.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0);
                return process.ExitCode != 0 || program == null || !File.Exists(program)
                    ? new Built(null, output + error.Result)
                    : new Built(program, null);
            }
            catch (Exception e)
            {
                return new Built(null, $"Couldn't run dotnet: {e.Message}. Is the .NET SDK installed?");
            }
        }

        // Waits in the background for the server just started to answer (then starts its bots), or to stop (saying
        // why, from its log).
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
                if (Read() is not { waiting: true } state)
                {
                    EditorApplication.update -= Poll; // stopped meanwhile
                    return;
                }

                if (!Alive(state.pid, state.startTicks))
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

                        // A content build finished while it started (which doesn't restart a starting server).
                        if (ContentHashNow() is { } now && now != state.content)
                        {
                            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                            {
                                Debug.Log("The content changed while the local game server started; restarting it.");
                                Stop(quiet: true);
                                if (!Start(settings, build: false, answers: false))
                                {
                                    Debug.LogError("The local game server didn't restart for the new content: start it again (Comet › Server › Start).");
                                }

                                return;
                            }

                            Debug.LogWarning("The content changed while the local game server started, so it plays the older content until play mode next starts.");
                        }

                        state.waiting = false;
                        StartBots(state, settings);
                        Write(state);
                        Debug.Log($"The local game server is running at {Describe(state, settings)}.");
                        return;
                    }

                    asking = null;
                }

                if (EditorApplication.timeSinceStartup > giveUp)
                {
                    EditorApplication.update -= Poll;
                    state.waiting = false;
                    Write(state);
                    Debug.LogWarning($"The local game server hasn't answered after {StartTimeout} s (Comet › Server › Show Log).");
                    return;
                }

                asking ??= Http.GetAsync($"http://localhost:{settings.serverPort}/health");
            }

            EditorApplication.update += Poll;
        }

        // Where people reach it: this computer, and others on the network with LAN on; the page with the web build.
        private static string Describe(State state, CometSettings settings)
        {
            var scheme = state.web ? "http://" : "";
            var here = $"{scheme}localhost:{settings.serverPort}";
            var bots = state.bots > 0 ? $", with {state.bots} bots" : "";
            return state.lan && LanAddress() is { } lan ? $"{here} (on the network: {scheme}{lan}:{settings.serverPort}){bots}" : here + bots;
        }

        private static void StartBots(State state, CometSettings settings)
        {
            if (state.bots <= 0 || string.IsNullOrEmpty(state.botsProgram))
            {
                return;
            }

            var arguments = settings.botsArguments
                .Replace("{url}", $"ws://localhost:{settings.serverPort}/ws")
                .Replace("{count}", state.bots.ToString());
            try
            {
                using var process = Process.Start(new ProcessStartInfo(ContentBuild.Dotnet(), $"\"{state.botsProgram}\" {arguments}")
                {
                    WorkingDirectory = ContentBuild.ProjectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })!;
                state.botsPid = process.Id;
                state.botsStartTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (Exception e)
            {
                Debug.LogError($"Couldn't start the bots: {e.Message}");
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

        // Tells play mode which port to join (LocalServerPort).
        private static void SharePort()
        {
            if (Settings() is { } settings)
            {
                SessionState.SetInt(LocalServerPort.Key, settings.serverPort);
            }
        }

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

        // The editor's own server and bots: their processes (pid and start time, so a reused pid isn't taken for
        // theirs), the content and options it started with, the programs run, and whether it's still starting.
        [Serializable]
        private sealed class State
        {
            public int pid;
            public long startTicks;
            public string content = "";
            public string program = "";
            public int botsPid;
            public long botsStartTicks;
            public string botsProgram = "";
            public bool waiting;
            public bool web;
            public bool lan;
            public int bots;
        }

        private static State? Read()
        {
            var path = ContentBuild.ProjectPath(StateFile);
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<State>(File.ReadAllText(path)) : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static void Write(State state) => File.WriteAllText(ContentBuild.ProjectPath(StateFile), JsonUtility.ToJson(state, true));

        // Stops a process the editor started, if it's still that one; whether it was running.
        private static bool Kill(int pid, long startTicks)
        {
            if (pid <= 0 || !Alive(pid, startTicks))
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                process.WaitForExit(3000);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                // It has stopped by itself meanwhile.
            }

            return true;
        }

        // Whether a process is still the one the editor started.
        private static bool Alive(int pid, long startTicks)
        {
            if (pid <= 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited && Math.Abs(process.StartTime.ToUniversalTime().Ticks - startTicks) < TimeSpan.TicksPerSecond;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
