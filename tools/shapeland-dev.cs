// Runs ShapeLand locally for development: by default the web client served from the game server, for testing
// in a browser without opening the Unity editor; with --no-web, just the server, for the editor's play mode or
// a desktop build. Builds the content, makes the Unity web build in batch mode (uncompressed, for speed, into
// artifacts/unity/shapeland/web-dev; skipped when nothing it uses changed since the last one), starts PostgreSQL (the
// shared comet-postgres container, tools/run comet-postgres, with a database for this checkout and port), the Login
// server (on the game server's port + 10) and the game server, optionally starts bots, and opens the page. Then it
// watches the content sources and restarts the servers when they change. Ctrl+C stops everything but PostgreSQL.
// Docker is needed.
//
//   tools/run shapeland-dev [--rebuild] [--no-build] [--no-web] [--bots N] [--cheaters N] [--port 5080] [--lan] [--no-open]
//                           [--net PROFILE] [--bots-net PROFILE] [--bots-chat] [--no-watch] [--spawn KEY]
//
//   --rebuild      make the Unity web build even if it looks up to date
//   --no-build     use the last web build as it is (content still rebuilds)
//   --no-web       no web build or page: just the server, for the Unity editor's play mode or a desktop build.
//                  Rerun it after changing server code. After a content change, restart play mode (which picks up
//                  the new content), so client and server use the same numbers
//   --bots N       start N honest bots (default 0): guest accounts named Bot0001 and on, their tokens kept in
//                  artifacts/bot-tokens so later runs reuse them
//   --cheaters N   start N speed-cheating bots (default 0)
//   --bots-chat    let the bots chat now and then (they stay quiet otherwise)
//   --port P       the game server's port (default 5080); the Login server takes P + 10
//   --lan          listen on every network interface, so other devices on the network can join
//   --no-open      don't open the page in a browser
//   --net P        run the server and bots in Docker with simulated network conditions on the server's link, so
//                  every client gets them (shapeland/docker): none, good (about 80 ms round trip, 1% loss),
//                  bad (about 200 ms, 2% loss) or awful (about 200 ms, 5% loss). Desktop builds join it too.
//   --bots-net P   with --net, a profile the bots get on top of it (default none), e.g. for high-ping bots
//   --no-watch     don't watch the content sources. Without it, saving a change under shapeland/content/ (content
//                  numbers, zones saved from Comet › Zones) rebuilds the content and restarts the server and bots;
//                  clients rejoin by themselves (a web page reloads first for the new content; play mode needs a restart)
//   --spawn KEY    start every player at this spawn point, e.g. spawn.tests.start, to try a level in any zone
//                  (default: each at their shape's spawn)
//
// The profiles are in tools/shared/NetProfiles.cs. On Linux, Docker needs the traffic-shaping kernel modules loaded
// (README.md, Requirements).
// With the Unity editor open on the project, the web build runs in it (tools/run unity-batch); it must be on the Web
// profile. ShapeLand only: Crystal's page comes from the Login server.
#:project shared/Tools.Shared.csproj
#:property PublishAot=false
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

args = Tool.Arguments(args);

var project = Path.Combine(Tool.Repo, "shapeland", "ShapeLand.Unity");

// Its own folder, since these builds skip compression (Builds.cs) and shapeland-deploy publishes artifacts/unity/shapeland/web.
// That way a playtest build never goes out by mistake.
var web = Path.Combine(Tool.Repo, "artifacts", "unity", "shapeland", "web-dev");
var content = Path.Combine(Tool.Repo, "artifacts", "content", "shapeland", "content.bin");
var bin = Path.Combine(Tool.Repo, "artifacts", "bin");

bool rebuild = false,
    build = true,
    serveWeb = true,
    openPage = true,
    botsChat = false,
    watch = true,
    lan = false;
int bots = 0,
    cheaters = 0,
    port = 5080;
string? net = null;
string botsNet = "none",
    spawn = "";
var options = new Tool.Args(args);
while (options.More)
{
    switch (options.Take())
    {
        case "--rebuild":
            rebuild = true;
            break;
        case "--no-build":
            build = false;
            break;
        case "--no-web":
            serveWeb = false;
            openPage = false;
            break;
        case "--bots":
            bots = options.Number("--bots");
            break;
        case "--cheaters":
            cheaters = options.Number("--cheaters");
            break;
        case "--port":
            port = options.Number("--port");
            break;
        case "--lan":
            lan = true;
            break;
        case "--no-open":
            openPage = false;
            break;
        case "--net":
            net = options.Value("--net");
            break;
        case "--bots-net":
            botsNet = options.Value("--bots-net");
            break;
        case "--bots-chat":
            botsChat = true;
            break;
        case "--no-watch":
            watch = false;
            break;
        case "--spawn":
            spawn = options.Value("--spawn");
            break;
        case "-h" or "--help":
            Tool.Usage();
            break;
        case var other:
            throw Tool.Fail($"Unknown option {other} (see --help)", 2);
    }
}

var compose = new Dictionary<string, string?>();
if (net != null)
{
    foreach (var (key, value) in NetProfiles.Settings("NETEM", net).Concat(NetProfiles.Settings("BOTS_NETEM", botsNet)))
        compose[key] = value;
    NetProfiles.CheckModules();
}
else if (botsNet != "none")
    throw Tool.Fail("--bots-net needs --net.", 2);

var loginPort = port + 10;
Console.WriteLine("== PostgreSQL");
var database = ToolOutput("comet-postgres", "name", Tool.Repo, port.ToString());
var connection = ToolOutput("comet-postgres", "database", database);
Console.WriteLine($"   Database {database} in comet-postgres");

var contentChecked = DateTime.UtcNow;
Console.WriteLine("== Content");
if (!TryBuildContent())
    throw Tool.Fail("The content build failed.");

var webRoot = serveWeb ? web : null;
var page = Path.Combine(web, "index.html");
if (serveWeb && build && (rebuild || Stale()))
{
    Console.WriteLine("== Unity web build (a few minutes)");
    var marker = Path.Combine(project, "Library", "ShapeLand", "fast-web-build");
    Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
    File.WriteAllText(marker, "");
    var code = Tool.RunTool("unity-batch", "-l", "Logs/web.log", "build", "Web", web);
    File.Delete(marker);
    if (code != 0)
        throw Tool.Fail("The Unity web build failed.");
    if (!File.Exists(page))
        throw Tool.Fail($"The Unity build made no page at {page} (log: {project}/Logs/web.log)");
    File.SetLastWriteTimeUtc(page, DateTime.UtcNow);
}
else if (serveWeb && !File.Exists(page))
    throw Tool.Fail("No web build yet; run without --no-build.");
else if (serveWeb)
    Console.WriteLine("== Unity web build is up to date");
CopyContent();

Console.WriteLine("== Login and game servers");
Tool.Quietly("dotnet", "build", "-v", "quiet", "-nologo", Path.Combine(Tool.Repo, "shapeland", "src", "ShapeLand.LoginServer"));
Tool.Quietly("dotnet", "build", "-v", "quiet", "-nologo", Path.Combine(Tool.Repo, "shapeland", "src", "ShapeLand.GameServer"));
if (bots + cheaters > 0)
    Tool.Quietly("dotnet", "build", "-v", "quiet", "-nologo", Path.Combine(Tool.Repo, "shapeland", "src", "ShapeLand.Bots"));
string[] composeFile = ["compose", "-f", Path.Combine(Tool.Repo, "shapeland", "docker", "compose.yaml")];
if (net != null)
{
    Console.WriteLine("== Docker image");
    Tool.Quietly("docker", [.. composeFile, "build", "-q"]);
}

var url = $"http://localhost:{port}/";
var loginUrl = $"http://localhost:{loginPort}";
if (Tool.Answers($"{loginUrl}/", success: false))
    throw Tool.Fail($"Something is already using port {loginPort}, the Login server's (the game server's + 10); stop it or use --port.");
if (Tool.Answers($"{url}health"))
    throw Tool.Fail(
        $"A game server is already running on port {port}: another run of this tool, or the Unity editor's\n"
            + "(stop that one with Comet › Server › Stop). Stop it or use --port."
    );
if (Tool.Answers(url, success: false))
    throw Tool.Fail($"Something else is already using port {port}; stop it or use --port.");

var host = lan ? "0.0.0.0" : "localhost";
var publicHost = lan ? LocalAddress() ?? "localhost" : "localhost";

Process? server = null,
    login = null,
    botsProcess = null;
Tool.OnStop(StopServers);
StartServers();

Console.WriteLine(
    serveWeb
        ? $"== Playing at {url} (Ctrl+C stops){(spawn != "" ? $", starting at {spawn}" : "")}{(net != null ? $", network profile {net}" : "")}"
        : $"== Server at localhost:{port}, for the editor's play mode (Ctrl+C stops){(spawn != "" ? $", starting at {spawn}" : "")}{(net != null ? $", network profile {net}" : "")}"
);
Console.WriteLine($"   Login server at {loginUrl}, database {database}");
if (lan)
    Console.WriteLine($"   Other devices: http://{publicHost}:{port}/");
if (watch)
    Console.WriteLine("   Watching shapeland/content/: saving a change there rebuilds the content and restarts the server.");
if (openPage)
    Tool.OpenInBrowser(url);

if (!watch)
{
    server!.WaitForExit();
    return;
}

// Polls rather than using a file watcher, which misses changes on some file systems; folders count too, since theirs change on add or remove.
while (!server!.HasExited)
{
    Thread.Sleep(1000);
    if (!ContentChangedSince(contentChecked))
        continue;
    Thread.Sleep(500);
    contentChecked = DateTime.UtcNow;
    Console.WriteLine("== Content changed; rebuilding");
    if (!TryBuildContent())
    {
        Console.WriteLine("== The content build failed; the server keeps the old content. Fix it and save again.");
        continue;
    }
    CopyContent();
    Console.WriteLine("== Restarting the Login and game servers");
    StopServers();
    StartServers();
    Console.WriteLine(
        serveWeb
            ? "== Restarted with the new content. Browsers reload and rejoin by themselves."
            : "== Restarted with the new content. Restart play mode to play with it."
    );
}
throw Tool.Fail("The game server stopped.");

string ToolOutput(string tool, params string[] toolArgs)
{
    var (code, text) = Tool.Capture("dotnet", ["run", "--file", Path.Combine(Tool.Repo, "tools", tool + ".cs"), "--", .. toolArgs]);
    return code == 0 ? text.Trim() : throw Tool.Fail($"{Tool.Command(tool)} {string.Join(' ', toolArgs)} failed.");
}

bool TryBuildContent() =>
    Tool.Run("dotnet", "run", "--project", Path.Combine(Tool.Repo, "shapeland", "src", "ShapeLand.ContentBuild"), "--", "build") == 0;

bool Stale()
{
    if (!File.Exists(page))
        return true;
    var built = File.GetLastWriteTimeUtc(page);
    string[] sources =
    [
        Path.Combine(project, "Assets"),
        Path.Combine(project, "Packages"),
        Path.Combine(project, "ProjectSettings"),
        .. new[] { "Comet.Client", "Comet.Content", "Comet.Protocol", "Comet.Simulation" }.Select(p => Path.Combine(Tool.Repo, "comet", "src", p)),
        Path.Combine(Tool.Repo, "comet", "unity"),
        Path.Combine(Tool.Repo, "shapeland", "src", "ShapeLand.Shared"),
        Path.Combine(Tool.Repo, "shapeland", "content", "strings"),
    ];
    var streaming = Path.DirectorySeparatorChar + "StreamingAssets" + Path.DirectorySeparatorChar;
    return sources
        .Where(Directory.Exists)
        .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
        .Any(f => !f.EndsWith(".meta", StringComparison.Ordinal) && !f.Contains(streaming) && File.GetLastWriteTimeUtc(f) > built);
}

bool ContentChangedSince(DateTime time)
{
    var folder = Path.Combine(Tool.Repo, "shapeland", "content");
    try
    {
        return Directory
            .EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Append(folder)
            .Any(p => File.GetLastWriteTimeUtc(p) > time);
    }
    catch (IOException)
    {
        return false;
    }
}

void CopyContent()
{
    if (webRoot == null)
        return;
    var copy = Path.Combine(webRoot, "StreamingAssets", "content.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
    if (!File.Exists(copy) || !File.ReadAllBytes(copy).AsSpan().SequenceEqual(File.ReadAllBytes(content)))
        File.Copy(content, copy, overwrite: true);
}

static string? LocalAddress()
{
    try
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect("1.1.1.1", 53);
        return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
    }
    catch (SocketException)
    {
        return null;
    }
}

void StartServers()
{
    var bind = lan ? "0.0.0.0" : "127.0.0.1";
    if (net != null)
    {
        var env = new Dictionary<string, string?>(compose)
        {
            ["SHAPELAND_WEBROOT"] = webRoot != null ? "/app/web" : "",
            ["SHAPELAND_PUBLISH"] = $"{bind}:{port}",
            ["LOGIN_PUBLISH"] = $"{bind}:{loginPort}",
            ["BOTS_COUNT"] = bots.ToString(),
            ["BOTS_CHEATERS"] = cheaters.ToString(),
            ["BOTS_CHAT"] = botsChat ? "30" : "",
            ["SHAPELAND_START_SPAWN"] = spawn,
            ["SHAPELAND_DATABASE"] = NetProfiles.ForContainers(connection),
            ["SHAPELAND_LOGIN_URL"] = $"http://{publicHost}:{loginPort}",
            ["SHAPELAND_GAME_ADDRESS"] = $"ws://{publicHost}:{port}/ws",
            ["SHAPELAND_PAGE"] = $"http://{publicHost}:{port}",
            ["SHAPELAND_USER"] = NetProfiles.HostUser(),
        };
        compose = env;
        server = Tool.Start(Tool.Info("docker", [.. composeFile, "up", "--no-log-prefix", "login", "server"], env: env));
    }
    else
    {
        login = Tool.Start(
            Tool.Info(
                "dotnet",
                [
                    Path.Combine(bin, "ShapeLand.LoginServer", "debug", "ShapeLand.LoginServer.dll"),
                    "--urls",
                    $"http://{host}:{loginPort}",
                    $"--Login:ConnectionString={connection}",
                    $"--Login:AllowedOrigins:0=http://localhost:{port}",
                    $"--Login:AllowedOrigins:1=http://{publicHost}:{port}",
                    "--Login:RateLimits:ExemptAddresses:0=127.0.0.1",
                    "--Login:RateLimits:ExemptAddresses:1=::1",
                    $"--ShapeLand:GameServer:Address=ws://{publicHost}:{port}/ws",
                ]
            )
        );
        if (!Tool.WaitFor(() => login.HasExited || Tool.Answers($"{loginUrl}/api/health"), 60, TimeSpan.FromMilliseconds(500)) || login.HasExited)
            throw Tool.Fail("The Login server stopped.");

        server = Tool.Start(
            Tool.Info(
                "dotnet",
                [
                    Path.Combine(bin, "ShapeLand.GameServer", "debug", "ShapeLand.GameServer.dll"),
                    "--urls",
                    $"http://{host}:{port}",
                    $"--ShapeLand:WebRoot={webRoot ?? ""}",
                    $"--ShapeLand:StartSpawn={spawn}",
                    $"--ShapeLand:ConnectionString={connection}",
                    $"--ShapeLand:LoginUrl=http://{publicHost}:{loginPort}",
                    "--Comet:Connections:MaxConnectionsPerAddress=1000",
                ]
            )
        );
    }

    Tool.WaitFor(() => server.HasExited || Tool.Answers($"{url}health"), 60, TimeSpan.FromMilliseconds(500));
    if (server.HasExited)
        throw Tool.Fail("The game server stopped.");
    if (!Tool.Answers($"{url}health"))
        throw Tool.Fail($"The game server didn't start at {url}.");

    if (bots + cheaters > 0 && net != null)
        Tool.Quietly(Tool.Info("docker", [.. composeFile, "--profile", "bots", "up", "-d", "bots"], env: compose));
    else if (bots + cheaters > 0)
    {
        var info = Tool.Info(
            "dotnet",
            [
                Path.Combine(bin, "ShapeLand.Bots", "debug", "ShapeLand.Bots.dll"),
                "--login",
                loginUrl,
                "--count",
                bots.ToString(),
                "--cheaters",
                cheaters.ToString(),
                .. botsChat ? new[] { "--chat" } : [],
            ]
        );
        info.RedirectStandardOutput = true;
        botsProcess = Tool.Start(info);
        botsProcess.OutputDataReceived += (_, _) => { };
        botsProcess.BeginOutputReadLine();
    }
}

void StopServers()
{
    if (net != null)
        Tool.Capture(Tool.Info("docker", [.. composeFile, "--profile", "bots", "down", "-t", "2"], env: compose), withErrors: true);
    foreach (var process in new[] { botsProcess, server, login })
        if (process != null)
            Tool.Stop(process);
    (server, login, botsProcess) = (null, null, null);
}
