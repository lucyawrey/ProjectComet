namespace Comet.Server.Movement;

/// <summary>
/// Stamps a player's entity states with the tick the player sent them at, so the network jitter between that
/// player and the server doesn't show as wobble on everyone else's screen. The tick comes from the report
/// frame's header (the client's estimate of the server tick), so it is bounded: no later than the tick the
/// report arrived, no more than <see cref="MaxLagSeconds"/> before it, and never before the previous stamp.
/// </summary>
public sealed class StateStamp(int tickRate, double maxLagSeconds = StateStamp.MaxLagSeconds)
{
    public const double MaxLagSeconds = 0.5;

    private readonly uint _maxLag = (uint)Math.Round(maxLagSeconds * tickRate);

    /// <summary>The last stamp given.</summary>
    public uint Last { get; private set; }

    /// <summary>The stamp for a state reported in a frame headed <paramref name="clientTick"/> that arrived at <paramref name="arrivalTick"/>.</summary>
    public uint Stamp(uint clientTick, uint arrivalTick)
    {
        var earliest = Math.Max(arrivalTick > _maxLag ? arrivalTick - _maxLag : 0, Last);
        Last = Math.Clamp(clientTick, Math.Min(earliest, arrivalTick), arrivalTick);
        return Last;
    }

    /// <summary>Stamps a position the server set itself (a spawn or respawn) with the server's own tick.</summary>
    public uint StampServer(uint tick)
    {
        Last = Math.Max(Last, tick);
        return Last;
    }
}
