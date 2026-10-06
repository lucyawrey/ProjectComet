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
var cpuAtStart = CpuAtAsync(window.Start);
var cpuAtEnd = CpuAtAsync(window.End);
var running = new List<Task>(bots.Length);
var launched = 0;
var progress = ReportProgressAsync(stop.Token);
foreach (var bot in bots)
{
    running.Add(Task.Run(() => bot.RunAsync(stop.Token)));
    Interlocked.Increment(ref launched);
    await Task.Delay(rampDelay);
}

// Stop shortly after the window ends, so the last gaps are recorded.
var untilEnd = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), window.End);
stop.CancelAfter(untilEnd + TimeSpan.FromSeconds(1));
await Task.WhenAll(running);
await progress;

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
    CpuCores = Math.Round((await cpuAtEnd - await cpuAtStart).TotalSeconds / seconds, 3),
    ProcessorCount = Environment.ProcessorCount,
};

Console.WriteLine($"Results written to {ResultFiles.Write(options.ResultsPath, result)}");
return 0;

// Prints where the run is every 30 s, so a long run shows it's alive. Reads nothing that's measured.
async Task ReportProgressAsync(CancellationToken token)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
    try
    {
        while (await timer.WaitForNextTickAsync(token))
        {
            var now = Stopwatch.GetTimestamp();
            if (window.HasEnded(now))
            {
                return;
            }
            var phase = window.IsMeasuring(now)
                ? $"measuring {Clock(Stopwatch.GetElapsedTime(window.Start, now))} / {Clock(TimeSpan.FromSeconds(options.DurationSeconds))}"
                : $"warmup {Clock(TimeSpan.FromSeconds(options.WarmupSeconds) - Stopwatch.GetElapsedTime(now, window.Start))} / {Clock(TimeSpan.FromSeconds(options.WarmupSeconds))}";
            Console.WriteLine($"{phase} · {Volatile.Read(ref launched)} bots started, {bots.Count(b => b.Failed)} failed");
        }
    }
    catch (OperationCanceledException)
    {
    }
}

// The process's CPU time at a Stopwatch timestamp, for measuring over the window.
static async Task<TimeSpan> CpuAtAsync(long timestamp)
{
    var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), timestamp);
    if (wait > TimeSpan.Zero)
    {
        await Task.Delay(wait);
    }
    using var process = Process.GetCurrentProcess();
    return process.TotalProcessorTime;
}

static string Clock(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";
