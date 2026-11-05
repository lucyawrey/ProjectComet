namespace Comet.Data;

/// <summary>
/// Makes Snowflake-style 64-bit row IDs: 42 bits of milliseconds since <see cref="Epoch"/> (about 139 years), 10 bits
/// of node number (one per server process, from config) and 11 bits of sequence (2,048 IDs per millisecond per node).
/// The sign bit stays 0, so IDs fit PostgreSQL's <c>bigint</c>. IDs from one generator only grow; IDs from different
/// nodes never collide. Thread-safe.
/// </summary>
public sealed class SnowflakeIds
{
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public const int TimeBits = 42;
    public const int NodeBits = 10;
    public const int SequenceBits = 11;
    public const int MaxNode = (1 << NodeBits) - 1;
    private const long MaxSequence = (1 << SequenceBits) - 1;
    private const long MaxTime = (1L << TimeBits) - 1;

    /// <summary>How far the clock may step back before <see cref="Next"/> gives up instead of waiting.</summary>
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

    /// <summary>
    /// The next ID. If this millisecond's 2,048 are used up, or the clock stepped back by up to
    /// <see cref="MaxClockStepBack"/>, it waits for the clock; a bigger step back throws.
    /// </summary>
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
                    time = WaitUntil(_lastTime + 1); // this millisecond is used up
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

    /// <summary>When an ID was made, to the millisecond.</summary>
    public static DateTimeOffset TimeOf(long id) => Epoch.AddMilliseconds(id >> (NodeBits + SequenceBits));

    /// <summary>The node that made an ID.</summary>
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
