using ShapeLand.Bots;
using ShapeLand.Shared;
using ShapeLand.Shared.Content;

// ShapeLand bots: honest wanderers plus optional cheaters. Each bot is a guest account on the Login server, with its
// token kept in --tokens so later runs play the same accounts.
//   dotnet run --project shapeland/src/ShapeLand.Bots -- [--dev] [--login http://localhost:5090] [--tokens <folder>] [--server ws://server:5080/ws] [--count 5] [--cheaters 1] [--seconds 0] [--seed 1] [--content <repo>/artifacts/content/shapeland/content.bin]
//     [--warmup 10] [--results file.json] [--meta key=value ...] [--prefix Bot] [--chat [30]] [--cheat-speed 2.5] [--cheat-jump 1]
// --server replaces the game server address in the bots' tickets (Docker). --dev joins the editor's server by name
// at --server, without accounts. --cheat-speed and --cheat-jump are multipliers; small values make subtle cheaters.
// --chat is the average seconds between lines. --warmup or --results records network numbers (see NetReport).
var login = new Uri(Arg("--login", "http://localhost:5090"));
var tokens = Arg("--tokens", "");
var serverOverride = Arg("--server", "") is { Length: > 0 } server ? new Uri(server) : null;
var count = int.Parse(Arg("--count", "5"));
var cheaters = int.Parse(Arg("--cheaters", "0"));
var cheatSpeed = float.Parse(Arg("--cheat-speed", "2.5"), System.Globalization.CultureInfo.InvariantCulture);
var cheatJump = float.Parse(Arg("--cheat-jump", "1"), System.Globalization.CultureInfo.InvariantCulture);
var seconds = double.Parse(Arg("--seconds", "0"));
var seed = int.Parse(Arg("--seed", "1"));
var contentFile = Arg("--content", "");
var resultsFile = Arg("--results", "");
var chatIndex = Array.IndexOf(args, "--chat");
var chat =
    chatIndex < 0 ? 0
    : chatIndex + 1 < args.Length && double.TryParse(args[chatIndex + 1], out var every) ? every
    : 30;
var prefix = Arg("--prefix", "Bot"); // honest bots' names, so two groups can share a server
var warmup = double.Parse(Arg("--warmup", resultsFile != "" ? "10" : "-1"));
var meta = args.Select((arg, i) => (arg, i))
    .Where(a => a.arg == "--meta" && a.i + 1 < args.Length)
    .Select(a => args[a.i + 1].Split('=', 2))
    .ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "");

ShapeLandContent content;
using (var stream = File.OpenRead(contentFile != "" ? contentFile : RepoPaths.DefaultContentFile(".")))
{
    content = ShapeLandContent.Load(stream);
}

if (tokens == "")
{
    tokens = Path.Combine(RepoPaths.Root("."), "artifacts", "bot-tokens");
}

var dev = args.Contains("--dev");
if (dev && serverOverride == null)
{
    Console.Error.WriteLine("--dev needs --server: the game server to join by name.");
    Environment.ExitCode = 2;
    return;
}

var worlds = new ZoneWorlds(content);
Bot NewBot(string name, int botSeed, BotSettings settings) =>
    new(
        name,
        botSeed,
        settings,
        content,
        worlds,
        dev ? new DevBotAccount(name, serverOverride!, content, botSeed) : new LoginBotAccount(name, login, tokens, content, botSeed, serverOverride)
    );
var bots = Enumerable
    .Range(1, count)
    .Select(i => NewBot($"{prefix}{i:0000}", seed * 1000 + i, new BotSettings { WarmupSeconds = warmup, ChatEverySeconds = chat }))
    .Concat(
        Enumerable
            .Range(1, cheaters)
            .Select(i =>
                NewBot(
                    $"{(prefix == "Bot" ? "Cheater" : prefix + "-cheater")}{i:00}",
                    seed * 1000 + 500 + i,
                    new BotSettings
                    {
                        SpeedCheat = cheatSpeed,
                        JumpCheat = cheatJump,
                        ChatEverySeconds = chat,
                    }
                )
            )
    )
    .ToList();
var isCheater = bots.Select((_, i) => i >= count).ToList();

using var stop = seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
Console.WriteLine(
    dev ? $"{bots.Count} bots joining {serverOverride} by name; Ctrl+C to stop." : $"{bots.Count} bots signing in at {login}; Ctrl+C to stop."
);
await Task.WhenAll(
    bots.Select(
        async (bot, i) =>
        {
            await Task.Delay(i * 100); // don't all join in the same instant
            await bot.RunAsync(stop.Token);
        }
    )
);

Console.WriteLine($"{"Bot", -16} {"Joined", -7} {"Seen", 5} {"States", 7} {"Chat in/out", 12} {"Snap-backs", 11} {"Respawns", 9}");
foreach (var bot in bots)
{
    var joined =
        bot.LoginFailure is { } refused ? refused
        : bot.Rejected is { } reason ? reason.ToString()
        : bot.DroppedFor is { } drop ? drop.ToString()
        : bot.Failed ? "failed"
        : bot.Joined ? "yes"
        : "no";
    Console.WriteLine(
        $"{bot.Name, -16} {joined, -7} {bot.SeenPlayers.Count, 5} {bot.StatesReceived, 7} {$"{bot.ChatsReceived}/{bot.ChatsSent}", 12} {bot.SnapBacks, 11} {bot.Respawns, 9}"
    );
}

if (warmup >= 0)
{
    var report = NetReport.Create(
        bots,
        isCheater,
        new NetReport.RunSettings(dev ? serverOverride!.ToString() : login.ToString(), count, cheaters, seconds, seed, warmup),
        meta
    );
    report.Print();
    if (resultsFile != "")
    {
        report.Write(resultsFile);
        Console.WriteLine($"  Results in {resultsFile}");
    }
}

string Arg(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}
