namespace Comet.Server.Ticking;

public sealed class TickLimiter(int tickRate, int burst, float secondsPerTake)
{
    private float _tokens = burst;
    private uint _lastTick;

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
