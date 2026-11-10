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
    [InitializeOnLoad]
    public static partial class LocalServer
    {
        public const string LogFile = "Logs/game-server.log";

        public const string LoginLogFile = "Logs/login-server.log";

        private const string Menu = "Comet/Server/";

        private static readonly string Prefix = $"Comet.LocalServer.{ContentHash.Of(System.Text.Encoding.UTF8.GetBytes(ContentBuild.ProjectRoot))}.";
        private static readonly string StartWithPlayKey = Prefix + "StartWithPlayMode";
        private static readonly string WebKey = Prefix + "Web";
        private static readonly string LanKey = Prefix + "Lan";
        private static readonly string BotsKey = Prefix + "Bots";
        private static readonly string JoinOutsideKey = Prefix + "JoinOutsideServer";

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

        public struct Options
        {
            public bool Web;

            public bool Lan;

            public int Bots;
        }

        public static bool StartWithPlayMode
        {
            get => EditorPrefs.GetBool(StartWithPlayKey, true);
            set => EditorPrefs.SetBool(StartWithPlayKey, value);
        }

        public static bool JoinOutsideServer
        {
            get => EditorPrefs.GetBool(JoinOutsideKey, false);
            set => EditorPrefs.SetBool(JoinOutsideKey, value);
        }

        public static int Port(CometSettings settings) =>
            Read() is { } state && Alive(state.pid, state.startTicks) ? state.port
            : _building != null ? LocalServerPort.InEditor ?? settings.serverPort
            : settings.serverPort;

        public static Options Chosen
        {
            get =>
                new Options
                {
                    Web = EditorPrefs.GetBool(WebKey, false),
                    Lan = EditorPrefs.GetBool(LanKey, false),
                    Bots = EditorPrefs.GetInt(BotsKey, 0),
                };
            set
            {
                EditorPrefs.SetBool(WebKey, value.Web);
                EditorPrefs.SetBool(LanKey, value.Lan);
                EditorPrefs.SetInt(BotsKey, Mathf.Clamp(value.Bots, 0, MaxBots));
            }
        }

        public const int MaxBots = 50;

        public static bool Running => Read() is { } state && Alive(state.pid, state.startTicks);

        public static bool Starting => _building != null || (Read() is { waiting: true } state && Alive(state.pid, state.startTicks));

        public static bool Building => _building != null;

        private static Task<(Built Server, Built Bots, Built Login, Built Database)>? _building;
        private static int _stops;

        public static Options? RunningWith =>
            Read() is { } state && Alive(state.pid, state.startTicks)
                ? new Options
                {
                    Web = state.web,
                    Lan = state.lan,
                    Bots = state.bots,
                }
                : (Options?)null;

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

        public static string? LanAddress()
        {
            try
            {
                return NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => n.GetIPProperties())
                    .Where(p =>
                        p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                    )
                    .SelectMany(p => p.UnicastAddresses)
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    ?.ToString();
            }
            catch (NetworkInformationException)
            {
                return null;
            }
        }

        [MenuItem(Menu + "Start", priority = 40)]
        private static void StartMenu()
        {
            if (Settings() is not { } settings)
            {
                return;
            }

            if (JoinOutsideServer && Answers(settings))
            {
                UseOutside(settings);
            }
            else
            {
                Start(settings, build: true, answers: false);
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

        public static bool UseAccounts
        {
            get => LocalServerPort.UseAccounts;
            set
            {
                if (value == LocalServerPort.UseAccounts)
                {
                    return;
                }

                EditorPrefs.SetBool(LocalServerPort.UseAccountsKey, value);
                Stop(quiet: false);
                SharePort();
            }
        }

        [MenuItem(Menu + "Use Accounts", priority = 61)]
        private static void ToggleUseAccounts() => UseAccounts = !UseAccounts;

        [MenuItem(Menu + "Use Accounts", validate = true)]
        private static bool ShowUseAccounts()
        {
            UnityEditor.Menu.SetChecked(Menu + "Use Accounts", LocalServerPort.UseAccounts);
            return !EditorApplication.isPlaying;
        }

        [MenuItem(Menu + "Join Outside Server", priority = 62)]
        private static void ToggleJoinOutsideServer() => JoinOutsideServer = !JoinOutsideServer;

        [MenuItem(Menu + "Join Outside Server", validate = true)]
        private static bool ShowJoinOutsideServer()
        {
            UnityEditor.Menu.SetChecked(Menu + "Join Outside Server", JoinOutsideServer);
            return !EditorApplication.isPlaying;
        }

        private static bool WithAccounts(CometSettings settings) => LocalServerPort.UseAccounts && !string.IsNullOrEmpty(settings.loginProject);

        public static void Restart()
        {
            if (Settings() is { } settings)
            {
                Stop(quiet: true);
                Start(settings, build: true);
            }
        }

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

            if (JoinOutsideServer && Answers(settings))
            {
                UseOutside(settings);
                return;
            }

            Start(settings, build: true, answers: false);
        }

        private static void OnContentCopied(byte[] contentFile)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Settings() is not { } settings)
            {
                return;
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
            else if (JoinOutsideServer && Answers(settings) && PlayedContent(settings) is { } played && played != hash)
            {
                Debug.LogWarning(
                    $"The game server on port {settings.serverPort} was started outside the editor and plays older content; restart it to play the new content."
                );
            }
        }

        private static void UseOutside(CometSettings settings)
        {
            if (PlayedContent(settings) is { } played && ContentHashNow() is { } ours && played != ours)
            {
                Debug.LogWarning(
                    $"Using the game server on port {settings.serverPort}, started outside the editor; it plays different content than the editor has, so restart it to play the new content."
                );
            }
            else
            {
                Debug.Log($"Using the game server on port {settings.serverPort}, started outside the editor.");
            }
        }

        private static (int Server, int Login) PickPorts(CometSettings settings, bool accounts)
        {
            static int Pick(int wanted, int taken)
            {
                for (var port = wanted; port < wanted + 100 && port <= 65535; port++)
                {
                    if (port != taken && !PortInUse(port))
                    {
                        return port;
                    }
                }

                return wanted;
            }

            var server = Pick(settings.serverPort, accounts ? settings.loginPort : 0);
            var login = accounts ? Pick(settings.loginPort, server) : 0;
            if (server != settings.serverPort)
            {
                Debug.Log($"Port {settings.serverPort} is taken, so the local game server starts on port {server}.");
            }

            if (accounts && login != settings.loginPort)
            {
                Debug.Log($"Port {settings.loginPort} is taken, so the local Login server starts on port {login}.");
            }

            return (server, login);
        }

        public static bool Start(CometSettings settings, bool build, bool? answers = null)
        {
            if (string.IsNullOrEmpty(settings.serverProject) || Running || _building != null || (JoinOutsideServer && (answers ?? Answers(settings))))
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
            var login = last?.loginProgram ?? "";
            var accounts = WithAccounts(settings);
            var buildServer = build || program.Length == 0 || !File.Exists(program);
            var buildBots = options.Bots > 0 && !string.IsNullOrEmpty(settings.botsProject) && (build || bots.Length == 0 || !File.Exists(bots));
            var buildLogin = accounts && (build || login.Length == 0 || !File.Exists(login));

            if (last != null)
            {
                Stop(quiet: true);
            }

            var (serverPort, loginPort) = PickPorts(settings, accounts);
            SharePort(serverPort, loginPort);
            if (!buildServer && !buildBots && !accounts)
            {
                return Launch(settings, options, program, bots, "", "", serverPort, 0);
            }

            var dotnet = ContentBuild.Dotnet();
            var root = ContentBuild.ProjectRoot;
            var serverProject = ContentBuild.ProjectPath(settings.serverProject);
            var botsProject = buildBots ? ContentBuild.ProjectPath(settings.botsProject) : "";
            var loginProject = buildLogin ? ContentBuild.ProjectPath(settings.loginProject) : "";
            var databaseTool = accounts ? ContentBuild.ProjectPath(settings.databaseTool) : "";
            var port = settings.serverPort;
            var progress = Progress.Start("Local game server", buildBots ? "Building the servers and bots" : "Building the servers");
            var building = Task.Run(() =>
                (
                    buildServer ? Build(dotnet, root, serverProject) : new Built(program, null),
                    buildBots ? Build(dotnet, root, botsProject) : new Built(bots, null),
                    buildLogin ? Build(dotnet, root, loginProject) : new Built(accounts ? login : "", null),
                    accounts ? Database(dotnet, databaseTool, root, CheckoutRoot(root), port) : new Built("", null)
                )
            );
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
                LaunchBuilt(settings, options, building.Result, serverProject, botsProject, serverPort, loginPort, stops);
            }

            EditorApplication.update += Poll;
            return true;
        }

        private static void LaunchBuilt(
            CometSettings settings,
            Options options,
            (Built Server, Built Bots, Built Login, Built Database) built,
            string serverProject,
            string botsProject,
            int serverPort,
            int loginPort,
            int stops
        )
        {
            var (server, botsBuilt, loginBuilt, database) = built;
            if (server.Error != null)
            {
                Debug.LogError($"Couldn't build the game server ({serverProject}):\n{server.Error}");
                return;
            }

            if (loginBuilt.Error != null)
            {
                Debug.LogError($"Couldn't build the Login server ({settings.loginProject}):\n{loginBuilt.Error}");
                return;
            }

            if (database.Error != null)
            {
                Debug.LogError($"Couldn't start the development database ({settings.databaseTool}); is Docker running?\n{database.Error}");
                return;
            }

            if (botsBuilt.Error != null)
            {
                Debug.LogWarning($"Couldn't build the bots ({botsProject}), so the server starts without them:\n{botsBuilt.Error}");
            }

            if (stops != _stops || Running || (JoinOutsideServer && serverPort == settings.serverPort && Answers(settings)))
            {
                return;
            }

            Launch(
                settings,
                options,
                server.Program!,
                botsBuilt.Error == null ? botsBuilt.Program ?? "" : "",
                loginBuilt.Program ?? "",
                database.Program ?? "",
                serverPort,
                loginPort
            );
        }

        public static void Stop(bool quiet)
        {
            _stops++;
            if (Read() is not { } state)
            {
                return;
            }

            Kill(state.botsPid, state.botsStartTicks);
            Kill(state.loginPid, state.loginStartTicks);
            if (state.dev)
            {
                StopCleanly(state);
            }

            if (Kill(state.pid, state.startTicks) && !quiet)
            {
                Debug.Log("Stopped the local game server.");
            }

            File.Delete(ContentBuild.ProjectPath(StateFile));
        }

        private static void StopCleanly(State state)
        {
            if (!Alive(state.pid, state.startTicks))
            {
                return;
            }

            try
            {
                Task.Run(() => Http.PostAsync($"http://localhost:{state.port}/dev/stop", null)).Wait();
                using var process = Process.GetProcessById(state.pid);
                process.WaitForExit(5000);
            }
            catch (Exception e)
                when (e is AggregateException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone already, or not answering: Kill sees to it.
            }
        }

        public static string? WebBuild(CometSettings settings)
        {
            if (string.IsNullOrEmpty(settings.webBuildFolder))
            {
                return null;
            }

            var folder = ContentBuild.ProjectPath(settings.webBuildFolder);
            return File.Exists(Path.Combine(folder, "index.html")) ? folder : null;
        }

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
                    EditorApplication.update -= Poll;
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

                        if (ContentHashNow() is { } now && now != state.content)
                        {
                            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                            {
                                Debug.Log("The content changed while the local game server started; restarting it.");
                                Stop(quiet: true);
                                if (!Start(settings, build: false, answers: false))
                                {
                                    Debug.LogError(
                                        "The local game server didn't restart for the new content: start it again (Comet › Server › Start)."
                                    );
                                }

                                return;
                            }

                            Debug.LogWarning(
                                "The content changed while the local game server started, so it plays the older content until play mode next starts."
                            );
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

                asking ??= Http.GetAsync($"http://localhost:{state.port}/health");
            }

            EditorApplication.update += Poll;
        }

        private static string Describe(State state, CometSettings settings)
        {
            var scheme = state.web ? "http://" : "";
            var here = $"{scheme}localhost:{state.port}";
            var bots = state.bots > 0 ? $", with {state.bots} bots" : "";
            return state.lan && LanAddress() is { } lan ? $"{here} (on the network: {scheme}{lan}:{state.port}){bots}" : here + bots;
        }

        private static void StartBots(State state, CometSettings settings)
        {
            if (state.bots <= 0 || string.IsNullOrEmpty(state.botsProgram))
            {
                return;
            }

            var template =
                state.dev ? settings.botsDevArguments
                : state.loginPort > 0 ? settings.botsArguments
                : "";
            if (string.IsNullOrEmpty(template))
            {
                Debug.LogWarning(
                    state.dev
                        ? "The bots need accounts here: the Comet settings name no Bots Dev Arguments, so no bots start without Use Accounts."
                        : "The bots need a Login server, so no bots start: turn on Comet › Server › Use Accounts."
                );
                state.bots = 0;
                return;
            }

            var arguments = template
                .Replace("{url}", $"ws://localhost:{state.port}/ws")
                .Replace("{login}", $"http://localhost:{state.loginPort}")
                .Replace("{count}", state.bots.ToString());
            try
            {
                using var process = Process.Start(
                    new ProcessStartInfo(ContentBuild.Dotnet(), $"\"{state.botsProgram}\" {arguments}")
                    {
                        WorkingDirectory = ContentBuild.ProjectRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                )!;
                state.botsPid = process.Id;
                state.botsStartTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (Exception e)
            {
                Debug.LogError($"Couldn't start the bots: {e.Message}");
                state.bots = 0;
            }
        }

        [Serializable]
        private sealed class Info
        {
            public string contentHash = "";
        }

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

        // The checkout (the folder holding .git: a folder in the main checkout, a file in a worktree) that holds
        // the Unity project, which names its database as tools/run shapeland-dev does; the Unity project's own folder
        // is named the same in every checkout.
        private static string CheckoutRoot(string projectRoot) => LocalServerPort.CheckoutRoot(projectRoot);

        private static string? ContentHashNow() =>
            ContentBuild.Source is { } source && File.Exists(source) ? ContentHash.Of(File.ReadAllBytes(source)) : null;

        private static CometSettings? Settings() =>
            CometSettings.Find(quiet: true) is { } settings && !string.IsNullOrEmpty(settings.serverProject) ? settings : null;

        private static void SharePort()
        {
            if (Settings() is not { } settings || _building != null)
            {
                return;
            }

            if (Read() is { } state && Alive(state.pid, state.startTicks))
            {
                SharePort(state.port, state.loginPort);
            }
            else
            {
                SharePort(settings.serverPort, WithAccounts(settings) ? settings.loginPort : 0);
            }
        }

        private static void SharePort(int port, int loginPort)
        {
            SessionState.SetInt(LocalServerPort.Key, port);
            SessionState.SetInt(LocalServerPort.LoginKey, loginPort);
        }

        private static string Tail()
        {
            try
            {
                using var stream = new FileStream(
                    ContentBuild.ProjectPath(LogFile),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                var lines = new StreamReader(stream).ReadToEnd().Split('\n');
                return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 15)));
            }
            catch (IOException)
            {
                return "(no log)";
            }
        }
    }
}
