// Runs ShapeLand's game server under a simulated network profile with headless bots, for tuning the netcode, and
// prints what they measured: false snap-backs on honest bots, cheaters caught, ping, the interpolation delay
// and its target, and holds; then the server's side (rule violations, and airborne players gone silent whose
// fall it finished, which reach the bot as snap-backs too). Everything also goes to a results file stamped with the profile and commit.
//
//   tools/run shapeland-netrun PROFILE [--seconds 180] [--bots 10] [--cheaters 1] [--cheat-speed 2.5] [--cheat-jump 1]
//                              [--seed 1] [--warmup 10]
//                              [--bots-net PROFILE] [--far N] [--far-net bad] [--port 5091]
//
//   PROFILE        none, good, bad or awful (tools/shared/NetProfiles.cs), on the server's link
//   --seconds S    how long the bots run, warmup included (default 180)
//   --bots N       honest bots, whose network numbers are recorded (default 10)
//   --cheaters N   speed-cheating bots, which should all be caught (default 1)
//   --cheat-speed F, --cheat-jump F
//                  how much faster cheaters move and jump than allowed (default 2.5 and 1); small values make
//                  subtle cheaters, for tuning the tolerances
//   --seed N       the bots' seed, so runs before and after a change are comparable (default 1)
//   --warmup S     seconds after joining before numbers are recorded, while the delay settles (default 10)
//   --bots-net P   a profile the bots get on top, e.g. for high-ping bots; they then run in their own container
//   --far N        also N far players: honest bots named "Far", in their own container with --far-net on top,
//                  to see how they affect the others (the summary is the others'; theirs follows)
//   --far-net P    the far players' extra profile (default bad)
//   --port P       the server's port on this machine (default 5091, so a tools/run shapeland-dev run can go on)
//
// The bots run on this machine and reach the server through its published port, as a browser does, unless
// --bots-net is given. Results go to artifacts/netruns/<time>-<profile>.json, with the server's log beside it.
#:project shared/Tools.Shared.csproj
using System.Globalization;
using System.Text.RegularExpressions;

args = Tool.Arguments(args);

var options = new Tool.Args(args);
if (!options.More || options.Peek.StartsWith('-')) Tool.Usage(options.More && options.Peek is "-h" or "--help" ? 0 : 2);
var profile = options.Take();
int seconds = 180, bots = 10, cheaters = 1, seed = 1, warmup = 10, far = 0, port = 5091;
string cheatSpeed = "2.5", cheatJump = "1", botsNet = "none", farNet = "bad";
while (options.More)
{
    switch (options.Take())
    {
        case "--seconds": seconds = options.Number("--seconds"); break;
        case "--bots": bots = options.Number("--bots"); break;
        case "--cheaters": cheaters = options.Number("--cheaters"); break;
        case "--cheat-speed": cheatSpeed = options.Value("--cheat-speed"); break;
        case "--cheat-jump": cheatJump = options.Value("--cheat-jump"); break;
        case "--seed": seed = options.Number("--seed"); break;
        case "--warmup": warmup = options.Number("--warmup"); break;
        case "--bots-net": botsNet = options.Value("--bots-net"); break;
        case "--far": far = options.Number("--far"); break;
        case "--far-net": farNet = options.Value("--far-net"); break;
        case "--port": port = options.Number("--port"); break;
        case "-h" or "--help": Tool.Usage(); break;
        case var other: throw Tool.Fail($"Unknown option {other} (see --help)", 2);
    }
}

var env = NetProfiles.Settings("NETEM", profile);
if (far > 0 && botsNet != "none") throw Tool.Fail("--far needs the bots on this machine (no --bots-net).", 2);
foreach (var (key, value) in NetProfiles.Settings("BOTS_NETEM", far > 0 ? farNet : botsNet)) env[key] = value;
NetProfiles.CheckModules();

var commit = Tool.Output("git", "-C", Tool.Repo, "rev-parse", "--short", "HEAD");
if (Tool.Output("git", "-C", Tool.Repo, "status", "--porcelain") != "") commit += "-dirty";
var runs = Path.Combine(Tool.Repo, "artifacts", "netruns");
Directory.CreateDirectory(runs);
var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{profile}" + (botsNet != "none" ? $"-bots-{botsNet}" : "") + (far > 0 ? $"-far-{far}-{farNet}" : "");
var results = Path.Combine(runs, name + ".json");
var serverLog = Path.Combine(runs, name + ".server.log");

Console.WriteLine("== Building");
Tool.Quietly("dotnet", "run", "--project", Path.Combine(Tool.Repo, "shapeland", "src", "ShapeLand.ContentBuild"), "--", "build");
foreach (var program in new[] { "ShapeLand.LoginServer", "ShapeLand.GameServer", "ShapeLand.Bots" })
    Tool.Quietly("dotnet", "build", "-v", "quiet", "-nologo", Path.Combine(Tool.Repo, "shapeland", "src", program));

// Its own Compose project, so a tools/run shapeland-dev run alongside keeps its containers. No web build.
string[] compose = ["compose", "-p", "shapeland-netrun", "-f", Path.Combine(Tool.Repo, "shapeland", "docker", "compose.yaml")];
// Each run starts from a fresh database and fresh bot accounts (comet-postgres, tools/run comet-postgres).
var (code, connection) = Tool.Capture("dotnet", "run", "--file", Path.Combine(Tool.Repo, "tools", "comet-postgres.cs"), "--", "fresh", $"shapeland_netrun_{port}");
if (code != 0) throw Tool.Fail("Couldn't make the run's database (is Docker running?).");
var tokens = Path.Combine(Tool.Repo, "artifacts", "bot-tokens", "net");
if (Directory.Exists(tokens)) Directory.Delete(tokens, recursive: true);
Directory.CreateDirectory(tokens);
env["SHAPELAND_PUBLISH"] = $"127.0.0.1:{port}";
env["SHAPELAND_WEBROOT"] = "";
env["SHAPELAND_WEB_DIR"] = Path.Combine(Tool.Repo, "shapeland", "docker");
env["SHAPELAND_DATABASE"] = NetProfiles.ForContainers(connection.Trim());
env["LOGIN_PUBLISH"] = $"127.0.0.1:{port + 10}";
env["SHAPELAND_GAME_ADDRESS"] = $"ws://127.0.0.1:{port}/ws";
env["SHAPELAND_USER"] = NetProfiles.HostUser();

int Docker(params string[] dockerArgs) => Tool.Run(Tool.Info("docker", dockerArgs, env: env));
void Quiet(params string[] dockerArgs) => Tool.Capture(Tool.Info("docker", dockerArgs, env: env), withErrors: true);

// Ctrl+C or a kill stops the run there, after cleaning up.
Tool.OnStop(() =>
{
    File.WriteAllText(serverLog, Tool.Capture(Tool.Info("docker", [.. compose, "logs", "-t", "--no-log-prefix", "server"], env: env), withErrors: true).Output);
    Quiet("rm", "-f", "shapeland-netrun-bots", "shapeland-netrun-far");
    Quiet([.. compose, "--profile", "bots", "down", "-t", "2"]);
    Summarize(File.ReadAllLines(serverLog));
});

Console.WriteLine($"== Server under {profile}");
Tool.Quietly(Tool.Info("docker", [.. compose, "build", "-q", "server"], env: env));
var (upCode, upOutput) = Tool.Capture(Tool.Info("docker", [.. compose, "up", "-d", "--wait", "login", "server"], env: env), withErrors: true);
if (upCode != 0)
{
    Console.Error.WriteLine(upOutput.TrimEnd());
    throw Tool.Fail("The servers didn't start; their logs are in " + serverLog + " (the server's) and docker compose logs.");
}

Console.WriteLine($"== {bots} bots and {cheaters} cheaters for {seconds} s (warmup {warmup} s)");
List<string> botArgs = ["--count", bots.ToString(), "--cheaters", cheaters.ToString(), "--cheat-speed", cheatSpeed,
    "--cheat-jump", cheatJump, "--seconds", seconds.ToString(), "--seed", seed.ToString(), "--warmup", warmup.ToString(),
    "--meta", $"cheatSpeed={cheatSpeed}", "--meta", $"cheatJump={cheatJump}", "--meta", $"profile={profile}",
    "--meta", $"botsNet={botsNet}", "--meta", $"commit={commit}"];
string[] inContainer = ["dotnet", "/app/bin/ShapeLand.Bots/debug/ShapeLand.Bots.dll", "--login", "http://login:5090",
    "--server", "ws://server:5080/ws", "--tokens", "/app/bot-tokens", "--content", "/app/content/content.bin"];
if (far > 0)
{
    // The far players run alongside, in their own container behind --far-net, and are read afterwards.
    botArgs.AddRange(["--meta", $"far={far}", "--meta", $"farNet={farNet}"]);
    Quiet([.. compose, "--profile", "bots", "run", "-d", "--name", "shapeland-netrun-far", "bots", .. inContainer,
        "--prefix", "Far", "--count", far.ToString(), "--cheaters", "0", "--seconds", seconds.ToString(),
        "--seed", (seed + 7).ToString(), "--warmup", warmup.ToString(), "--results", "/tmp/results.json"]);
}
if (botsNet == "none")
    Tool.Run("dotnet", [Path.Combine(Tool.Repo, "artifacts", "bin", "ShapeLand.Bots", "debug", "ShapeLand.Bots.dll"),
        "--login", $"http://127.0.0.1:{port + 10}", "--tokens", tokens, .. botArgs, "--results", results]);
else
{
    // In the bots' container, behind their own netem as well; the results file is copied out afterwards.
    Quiet("rm", "-f", "shapeland-netrun-bots");
    Docker([.. compose, "--profile", "bots", "run", "--name", "shapeland-netrun-bots", "-T", "bots", .. inContainer, .. botArgs, "--results", "/tmp/results.json"]);
    Quiet("cp", "shapeland-netrun-bots:/tmp/results.json", results);
    Quiet("rm", "shapeland-netrun-bots");
    Console.WriteLine($"  Results in {results}");
}
if (far > 0)
{
    Quiet("wait", "shapeland-netrun-far");
    Console.WriteLine($"\n== The far players' own view ({profile} plus {farNet})");
    var farLog = Tool.Capture(Tool.Info("docker", ["logs", "shapeland-netrun-far"]), withErrors: true).Output.Split('\n');
    foreach (var line in farLog.SkipWhile(l => !l.StartsWith("Network results")).Where(l => !l.Contains("Results in")))
        Console.WriteLine(line);
    Quiet("cp", "shapeland-netrun-far:/tmp/results.json", Path.Combine(runs, name + ".far.json"));
    Console.WriteLine($"  Results in {Path.Combine(runs, name + ".far.json")}");
}

// What the server saw, from each player's line on leaving: rule violations, and falls it finished itself; and how
// much of the movement tolerances honest players needed (MovementHeadroom), the worst of them: banked movement in
// seconds for each speed factor, height over an exact jump's apex, and late arc start.
static void Summarize(string[] log)
{
    static bool IsCheater(string player) => player.Contains("Cheater") || player.Contains("-cheater");
    int honestViolations = 0, honestFalls = 0, cheaterViolations = 0, farViolations = 0, farFalls = 0;
    bool left = false, farSeen = false;
    foreach (Match m in log.Select(l => Regex.Match(l, @"(\S+) left \(.*?\); (\d+) movement violations, (\d+) falls finished")).Where(m => m.Success))
    {
        left = true;
        var (player, violations, falls) = (m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        if (IsCheater(player)) cheaterViolations += violations;
        else if (Regex.IsMatch(player, @"Far\d")) { farViolations += violations; farFalls += falls; farSeen = true; }
        else { honestViolations += violations; honestFalls += falls; }
    }
    if (left)
        Console.WriteLine($"  Server         honest bots: {honestViolations} violations, {honestFalls} falls finished by the server; cheaters: {cheaterViolations} violations");
    if (farSeen)
        Console.WriteLine($"  Server         far players: {farViolations} violations, {farFalls} falls finished by the server");

    var bursts = new Dictionary<string, double>(); // in the order the server logs them
    double? rise = null, air = null;
    foreach (Match m in log.Select(l => Regex.Match(l, @"(\S+) headroom: burst seconds (.*?); rise over apex (\S+) m; air slack (\S+) s")).Where(m => m.Success))
    {
        if (IsCheater(m.Groups[1].Value)) continue;
        foreach (var pair in m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var (key, value) = (pair.Split('=')[0], double.Parse(pair.Split('=')[1], CultureInfo.InvariantCulture));
            bursts[key] = bursts.TryGetValue(key, out var worst) ? Math.Max(worst, value) : value;
        }
        rise = Math.Max(rise ?? double.MinValue, double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
        air = Math.Max(air ?? double.MinValue, double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
    }
    if (rise != null)
        Console.WriteLine(FormattableString.Invariant(
            $"  Headroom       honest worst: burst s{string.Concat(bursts.Select(b => $" {b.Key} {b.Value:F3}"))}; rise over apex {rise:F3} m; air slack {air:F3} s"));
}
