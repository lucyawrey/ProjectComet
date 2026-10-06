using HdrHistogram;

namespace StackBench.Metrics;

/// <summary>
/// Records durations into an HdrHistogram (microsecond resolution, 3 significant digits, up to
/// one minute). Recording doesn't allocate. Not thread-safe: use one per thread or bot, then merge.
/// </summary>
public sealed class LatencyRecorder
{
    private static readonly long Highest = (long)TimeSpan.FromMinutes(1).TotalMicroseconds;

    private readonly LongHistogram _histogram = new(Highest, numberOfSignificantValueDigits: 3);

    public void Record(TimeSpan duration) =>
        _histogram.RecordValue(Math.Clamp((long)duration.TotalMicroseconds, 0, Highest));

    public void Add(LatencyRecorder other) => _histogram.Add(other._histogram);

    public void Reset() => _histogram.Reset();

    public LatencySummary Summarize()
    {
        var count = _histogram.TotalCount;
        if (count == 0)
        {
            return new LatencySummary(0, 0, 0, 0, 0);
        }
        return new LatencySummary(
            count,
            ToMs(_histogram.GetMean()),
            ToMs(_histogram.GetValueAtPercentile(50)),
            ToMs(_histogram.GetValueAtPercentile(99)),
            ToMs(_histogram.GetMaxValue()));
    }

    private static double ToMs(double microseconds) => Math.Round(microseconds / 1000.0, 3);
}

/// <summary>Durations in milliseconds.</summary>
public sealed record LatencySummary(long Count, double MeanMs, double MedianMs, double P99Ms, double MaxMs);
