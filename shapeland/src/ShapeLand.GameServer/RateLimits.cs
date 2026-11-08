using Comet.Server.Ticking;

namespace ShapeLand.GameServer;

public static class RateLimits
{
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
}
