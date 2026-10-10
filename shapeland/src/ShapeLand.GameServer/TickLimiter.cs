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

    /// <summary>Join attempts: far more than a player retrying a taken name needs; each one scans every player.</summary>
    public static TickLimiter Join(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

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
