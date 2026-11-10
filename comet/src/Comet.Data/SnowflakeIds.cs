namespace Comet.Data;

/// <summary>42 bits of milliseconds since <see cref="Epoch"/>, 10 of node, 11 of sequence; sign bit 0 for <c>bigint</c>. Thread-safe.</summary>
public sealed class SnowflakeIds
{
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public const int TimeBits = 42;
    public const int NodeBits = 10;
    public const int SequenceBits = 11;
    public const int MaxNode = (1 << NodeBits) - 1;
    private const long MaxSequence = (1 << SequenceBits) - 1;
    private const long MaxTime = (1L << TimeBits) - 1;

    public static readonly TimeSpan MaxClockStepBack = TimeSpan.FromSeconds(1);

    private readonly int _node;
    private readonly TimeProvider _clock;
    private readonly Lock _lock = new();
    private long _lastTime = -1;
    private long _sequence;

    public SnowflakeIds(int node, TimeProvider clock)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(node, MaxNode);
        _node = node;
        _clock = clock;
    }

    /// <summary>Waits if this millisecond's IDs are used up or the clock stepped back up to <see cref="MaxClockStepBack"/>; more throws.</summary>
    public long Next()
    {
        lock (_lock)
        {
            var time = Now();
            if (time < _lastTime)
            {
                if (_lastTime - time > (long)MaxClockStepBack.TotalMilliseconds)
                {
                    throw new InvalidOperationException($"The clock stepped back {_lastTime - time} ms; refusing to make IDs that could repeat.");
                }

                time = WaitUntil(_lastTime);
            }

            if (time == _lastTime)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                {
                    time = WaitUntil(_lastTime + 1);
                }
            }
            else
            {
                _sequence = 0;
            }

            _lastTime = time;
            return (time << (NodeBits + SequenceBits)) | ((long)_node << SequenceBits) | _sequence;
        }
    }

    public static DateTimeOffset TimeOf(long id) => Epoch.AddMilliseconds(id >> (NodeBits + SequenceBits));

    public static int NodeOf(long id) => (int)((id >> SequenceBits) & MaxNode);

    private long Now()
    {
        var time = (long)(_clock.GetUtcNow() - Epoch).TotalMilliseconds;
        if (time < 0 || time > MaxTime)
        {
            throw new InvalidOperationException($"The clock ({_clock.GetUtcNow():O}) is outside the ID range.");
        }

        return time;
    }

    private long WaitUntil(long time)
    {
        var spin = new SpinWait();
        long now;
        while ((now = Now()) < time)
        {
            spin.SpinOnce();
        }

        return now;
    }
}
