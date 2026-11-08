namespace Comet.Server.Ticking;

/// <summary>
/// One player's rate limit for an action: a burst of a few, then one every <c>secondsPerTake</c>.
/// Counted in server ticks. Tick thread only.
/// </summary>
public sealed class TickLimiter(int tickRate, int burst, float secondsPerTake)
{
    private float _tokens = burst;
    private uint _lastTick;

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
