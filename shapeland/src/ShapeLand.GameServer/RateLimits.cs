using Comet.Server.Ticking;

namespace ShapeLand.GameServer;

public static class RateLimits
{
    public static TickLimiter Chat(int tickRate) => new(tickRate, burst: 3, secondsPerTake: 2);

    public static TickLimiter Join(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    public static TickLimiter Look(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    public static TickLimiter Rename(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    public static TickLimiter Return(int tickRate) => new(tickRate, burst: 2, secondsPerTake: 1);
}
