using System.Diagnostics;

namespace StackBench.Bots;

/// <summary>The measurement window, in Stopwatch timestamps: a warmup after start, then the duration.</summary>
public sealed class BenchWindow(BotOptions options)
{
    public long Start { get; } = Stopwatch.GetTimestamp() + Seconds(options.WarmupSeconds);

    public long End => Start + Seconds(options.DurationSeconds);

    public bool IsMeasuring(long timestamp) => timestamp >= Start && timestamp < End;

    public bool HasEnded(long timestamp) => timestamp >= End;

    private static long Seconds(int seconds) => seconds * Stopwatch.Frequency;
}
