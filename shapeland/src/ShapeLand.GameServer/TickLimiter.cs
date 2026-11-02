namespace ShapeLand.GameServer;

/// <summary>
/// One player's rate limit for an action: a burst of a few, then one every <c>secondsPerTake</c>.
/// Counted in server ticks. Tick thread only.
/// </summary>
public sealed class TickLimiter(int tickRate, int burst, float secondsPerTake)
{
    private float _tokens = burst;
    private uint _lastTick;

    /// <summary>Chat: three lines at once, then one every two seconds.</summary>
    public static TickLimiter Chat(int tickRate) => new(tickRate, burst: 3, secondsPerTake: 2);

    /// <summary>Join attempts: far more than a player retrying a failed join needs; each one checks a ticket and loads a character.</summary>
    public static TickLimiter Join(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    /// <summary>Look changes: a few at once, then one a second (each is a save and a spawn everyone sees).</summary>
    public static TickLimiter Look(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    /// <summary>Renames: a few at once, then one a second (each is a database read).</summary>
    public static TickLimiter Rename(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    /// <summary>Returns to the spawn point: a couple at once, then one a second (each one is a teleport everyone sees).</summary>
    public static TickLimiter Return(int tickRate) => new(tickRate, burst: 2, secondsPerTake: 1);

    /// <summary>Takes one action at <paramref name="tick"/> if the limit allows it.</summary>
    public bool TryTake(uint tick)
    {
        if (_lastTick != 0)
        {
            _tokens = MathF.Min(burst, _tokens + (tick - _lastTick) / (tickRate * secondsPerTake));
        }

        _lastTick = tick;
        if (_tokens < 1)
        {
            return false;
        }

        _tokens--;
        return true;
    }
}
