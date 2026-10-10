using System.Text.Json;
using Comet.Client;

namespace ShapeLand.Bots;

/// <summary>
/// What a bot run measured under a network profile, for 3c's tuning: a summary printed at the end and, with
/// <c>--results</c>, a JSON file with the summary, the run's settings and every bot's samples. Network numbers
/// come from honest bots only, after the warmup; cheaters only show whether they were caught.
/// </summary>
public sealed class NetReport
{
    public required Dictionary<string, string> Meta { get; init; }
    public required RunSettings Run { get; init; }
    public required TuningSettings Tuning { get; init; }
    public required Summary Totals { get; init; }
    public required List<BotResult> Bots { get; init; }

    public sealed record RunSettings(string Url, int Bots, int Cheaters, double Seconds, int Seed, double WarmupSeconds);

    /// <summary>The client's tuning numbers this run used; the server's (validation margins) go by the commit in Meta.</summary>
    public sealed record TuningSettings(
        double DelayWindowSeconds, double DelayPercentile, double DelayGrowRate, double DelayShrinkRate,
        double DelayMarginMs, double DelayFloorMs, double DelayHoldSeconds, double IdleGapMs);

    public sealed record Summary(
        int HonestSnapBacks, int Respawns, int CheatersCaught, int Cheaters,
        double PingMedianMs, double PingP95Ms, double BestPingMedianMs,
        double DelayMedianMs, double DelayP5Ms, double DelayP95Ms, double TargetP5Ms, double TargetP95Ms,
        double TargetSwingMsPerSecond, double DelaySwingMsPerSecond, double HoldPercent, double HoldMeanMs, double HeldMsPerMove, long MoveStates);

    public sealed record BotResult(
        string Name, bool Cheater, bool Joined, int SnapBacks, int Respawns, long MoveStates, long Holds,
        double HeldSeconds, List<NetSample> Samples);

    public static NetReport Create(IReadOnlyList<Bot> bots, IReadOnlyList<bool> cheaters, RunSettings run, Dictionary<string, string> meta)
    {
        var results = bots.Select((bot, i) => new BotResult(
            bot.Name, cheaters[i], bot.Joined, bot.SnapBacks, bot.Respawns, bot.MoveStates, bot.Holds, bot.HeldSeconds, bot.NetSamples)).ToList();
        var honest = results.Where(r => !r.Cheater).ToList();
        var samples = honest.SelectMany(r => r.Samples).ToList();
        var moves = honest.Sum(r => r.MoveStates);
        var holds = honest.Sum(r => r.Holds);

        // How fast the target moves, averaged over every honest bot's consecutive samples.
        var swings = honest.SelectMany(r => r.Samples.Zip(r.Samples.Skip(1), (a, b) => Math.Abs(b.TargetMs - a.TargetMs) / (b.Seconds - a.Seconds))).ToList();
        var delaySwings = honest.SelectMany(r => r.Samples.Zip(r.Samples.Skip(1), (a, b) => Math.Abs(b.DelayMs - a.DelayMs) / (b.Seconds - a.Seconds))).ToList();

        var delay = new InterpolationDelay(30);
        var tuning = new TuningSettings(
            InterpolationDelay.WindowSeconds, InterpolationDelay.Percentile, InterpolationDelay.GrowRate, InterpolationDelay.ShrinkRate,
            delay.MarginTicks / delay.TickRate * 1000, delay.FloorTicks / delay.TickRate * 1000, InterpolationDelay.HoldSeconds,
            new RemoteEntities(30).IdleGapTicks / 30 * 1000);

        var summary = new Summary(
            honest.Sum(r => r.SnapBacks), results.Sum(r => r.Respawns),
            results.Count(r => r.Cheater && r.SnapBacks > 0), results.Count(r => r.Cheater),
            Percentile(samples.Select(s => s.PingMs), 0.5), Percentile(samples.Select(s => s.PingMs), 0.95),
            Percentile(samples.Select(s => s.BestPingMs), 0.5),
            Percentile(samples.Select(s => s.DelayMs), 0.5), Percentile(samples.Select(s => s.DelayMs), 0.05), Percentile(samples.Select(s => s.DelayMs), 0.95),
            Percentile(samples.Select(s => s.TargetMs), 0.05), Percentile(samples.Select(s => s.TargetMs), 0.95),
            swings.Count > 0 ? swings.Average() : 0,
            delaySwings.Count > 0 ? delaySwings.Average() : 0,
            moves > 0 ? 100.0 * holds / moves : 0,
            holds > 0 ? honest.Sum(r => r.HeldSeconds) / holds * 1000 : 0,
            moves > 0 ? honest.Sum(r => r.HeldSeconds) / moves * 1000 : 0,
            moves);

        return new NetReport { Meta = meta, Run = run, Tuning = tuning, Totals = summary, Bots = results };
    }

    public void Print()
    {
        var s = Totals;
        Console.WriteLine();
        Console.WriteLine($"Network results{(Meta.Count > 0 ? " (" + string.Join(", ", Meta.Select(m => $"{m.Key} {m.Value}")) + ")" : "")}, after a {Run.WarmupSeconds:0} s warmup:");
        Console.WriteLine($"  Snap-backs     {s.HonestSnapBacks} on honest bots; cheaters caught {s.CheatersCaught} of {s.Cheaters}; respawns {s.Respawns}");
        Console.WriteLine($"  Ping           median {s.PingMedianMs:0} ms, 95th percentile {s.PingP95Ms:0}; best median {s.BestPingMedianMs:0}");
        Console.WriteLine($"  Delay          median {s.DelayMedianMs:0} ms, 5th to 95th percentile {s.DelayP5Ms:0}-{s.DelayP95Ms:0}, moving {s.DelaySwingMsPerSecond:0} ms/s on average");
        Console.WriteLine($"  Target         5th to 95th percentile {s.TargetP5Ms:0}-{s.TargetP95Ms:0} ms, moving {s.TargetSwingMsPerSecond:0} ms/s on average");
        Console.WriteLine($"  Holds          {s.HoldPercent:0.0}% of {s.MoveStates} moves, {s.HoldMeanMs:0} ms each, {s.HeldMsPerMove:0.00} ms held per move");
    }

    public void Write(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private static double Percentile(IEnumerable<double> values, double fraction)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(fraction * sorted.Length))];
    }
}
