using Comet.Server.Ticking;

namespace ShapeLand.GameServer;

public static class RateLimits
{
    public static TickLimiter Chat(int tickRate) => new(tickRate, burst: 3, secondsPerTake: 2);

    /// <summary>Far more than a retrying player needs; each join checks a ticket and loads a character.</summary>
    public static TickLimiter Join(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    /// <summary>Each look change is a save and a spawn everyone sees.</summary>
    public static TickLimiter Look(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    /// <summary>Each rename is a database read.</summary>
    public static TickLimiter Rename(int tickRate) => new(tickRate, burst: 5, secondsPerTake: 1);

    /// <summary>Each return is a teleport everyone sees.</summary>
    public static TickLimiter Return(int tickRate) => new(tickRate, burst: 2, secondsPerTake: 1);
}
