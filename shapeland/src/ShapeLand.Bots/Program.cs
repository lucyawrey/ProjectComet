using ShapeLand.Bots;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;

// ShapeLand bots for phase 0: honest wanderers plus optional cheaters that slide too fast.
//   dotnet run --project shapeland/src/ShapeLand.Bots -- [--url ws://localhost:5080/ws] [--count 5] [--cheaters 1] [--seconds 0] [--seed 1] [--content <repo>/artifacts/content/shapeland/content.bin]
//     [--warmup 10] [--results file.json] [--meta key=value ...] [--prefix Bot]
// With --warmup or --results, bots record network numbers (as the F3 overlay shows them) after the warmup and
// print a summary at the end (NetReport); --results also writes them to a file, with each --meta pair in it.
var url = new Uri(Arg("--url", "ws://localhost:5080/ws"));
var count = int.Parse(Arg("--count", "5"));
var cheaters = int.Parse(Arg("--cheaters", "0"));
var seconds = double.Parse(Arg("--seconds", "0"));
var seed = int.Parse(Arg("--seed", "1"));
var contentFile = Arg("--content", "");
var resultsFile = Arg("--results", "");
var prefix = Arg("--prefix", "Bot"); // honest bots' names, so two groups can share a server
var warmup = double.Parse(Arg("--warmup", resultsFile != "" ? "10" : "-1"));
var meta = args.Select((arg, i) => (arg, i)).Where(a => a.arg == "--meta" && a.i + 1 < args.Length)
    .Select(a => args[a.i + 1].Split('=', 2)).ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "");

ShapeLandContent content;
using (var stream = File.OpenRead(contentFile != "" ? contentFile : Path.Combine(FindRepoRoot(), "artifacts", "content", "shapeland", "content.bin")))
{
    content = ShapeLandContent.Load(stream);
}

var world = ShapeLandWorld.Create(content);
var bots = Enumerable.Range(1, count)
    .Select(i => new Bot($"{prefix} {i:00}", seed * 1000 + i, new BotSettings { WarmupSeconds = warmup }, content, world))
    .Concat(Enumerable.Range(1, cheaters).Select(i => new Bot($"{(prefix == "Bot" ? "Cheater" : prefix + " cheater")} {i:00}", seed * 1000 + 500 + i, new BotSettings { SpeedCheat = 2.5f }, content, world)))
    .ToList();
var isCheater = bots.Select((_, i) => i >= count).ToList();

using var stop = seconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(seconds)) : new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
Console.WriteLine($"{bots.Count} bots joining {url}; Ctrl+C to stop.");
await Task.WhenAll(bots.Select(async (bot, i) =>
{
    await Task.Delay(i * 100); // don't all join in the same instant
    await bot.RunAsync(url, stop.Token);
}));

Console.WriteLine($"{"Bot",-12} {"Joined",-7} {"Seen",5} {"States",7} {"Chat in/out",12} {"Snap-backs",11} {"Respawns",9}");
foreach (var bot in bots)
{
    var joined = bot.Failed ? "failed" : bot.Rejected is { } reason ? reason.ToString() : bot.Joined ? "yes" : "no";
    Console.WriteLine($"{bot.Name,-12} {joined,-7} {bot.SeenPlayers.Count,5} {bot.StatesReceived,7} {$"{bot.ChatsReceived}/{bot.ChatsSent}",12} {bot.SnapBacks,11} {bot.Respawns,9}");
}

if (warmup >= 0)
{
    var report = NetReport.Create(bots, isCheater, new NetReport.RunSettings(url.ToString(), count, cheaters, seconds, seed, warmup), meta);
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

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ProjectComet.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Can't find the repository root (ProjectComet.slnx).");
}
