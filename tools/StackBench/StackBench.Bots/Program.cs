using System.Diagnostics;
using StackBench.Bots;
using StackBench.Metrics;

// Stack benchmark bots: many WebSocket connections from one process, each reporting positions
// and measuring round trips and gaps between updates. See .claude/design/prototype.md.
BotOptions options;
try
{
    options = BotOptions.Parse(args);
}
catch (Exception e) when (e is ArgumentException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(BotOptions.Usage);
    return 2;
}

var window = new BenchWindow(options);
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

Console.WriteLine($"{options.Bots} bots → {options.Url}; measuring for {options.DurationSeconds} s after a {options.WarmupSeconds} s warmup");

var bots = Enumerable.Range(0, options.Bots).Select(i => new Bot(i, options, window)).ToArray();
var rampDelay = TimeSpan.FromSeconds((double)options.RampSeconds / options.Bots);
var running = new List<Task>(bots.Length);
foreach (var bot in bots)
{
    running.Add(Task.Run(() => bot.RunAsync(stop.Token)));
    await Task.Delay(rampDelay);
}

// Stop shortly after the window ends, so the last gaps are recorded.
var untilEnd = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), window.End);
stop.CancelAfter(untilEnd + TimeSpan.FromSeconds(1));
await Task.WhenAll(running);

var roundTrip = new LatencyRecorder();
var updateGap = new LatencyRecorder();
foreach (var bot in bots)
{
    roundTrip.Add(bot.RoundTrip);
    updateGap.Add(bot.UpdateGap);
}
var seconds = (double)options.DurationSeconds;
var result = new BotsResult
{
    Bots = options.Bots,
    WindowSeconds = seconds,
    RoundTrip = roundTrip.Summarize(),
    UpdateGap = updateGap.Summarize(),
    BytesReceivedPerBotPerSecond = Math.Round(bots.Sum(b => b.BytesReceived) / (seconds * options.Bots)),
    BytesSentPerBotPerSecond = Math.Round(bots.Sum(b => b.BytesSent) / (seconds * options.Bots)),
    ConnectionFailures = bots.Count(b => b.Failed),
};

Console.WriteLine($"Results written to {ResultFiles.Write(options.ResultsPath, result)}");
return 0;
