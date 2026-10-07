namespace ShapeLand.GameServer;

/// <summary>
/// One player's chat rate limit: a burst of a few messages, then one every <see cref="SecondsPerMessage"/>.
/// Counted in server ticks. Tick thread only.
/// </summary>
public sealed class ChatLimiter(int tickRate)
{
    public const int Burst = 3;
    public const float SecondsPerMessage = 2;

    private float _tokens = Burst;
    private uint _lastTick;

    public bool TryTake(uint tick)
    {
        if (_lastTick != 0)
        {
            _tokens = MathF.Min(Burst, _tokens + (tick - _lastTick) / (tickRate * SecondsPerMessage));
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
