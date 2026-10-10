namespace Comet.Server.Movement;

/// <summary>
/// Stamps a player's entity states with the tick the player sent them at, so the network jitter between that
/// player and the server doesn't show as wobble on everyone else's screen. The tick comes from the report
/// frame's header (the client's estimate of the server tick), so it is bounded: no later than the tick the
/// report arrived, no more than <see cref="MaxLagSeconds"/> before it, and never before the previous stamp.
/// <para>
/// States sent to others carry that stamp shifted by the player's smoothed latency (<see cref="Broadcast"/>):
/// the median of arrival minus stamp over the last <see cref="SmoothingSeconds"/>, approached at no more than
/// <see cref="SlewRate"/>. The spacing between the player's states stays as they sent it, but their steady
/// latency no longer counts towards everyone else's interpolation delay; only their jitter and stalls do. A
/// broadcast stamp may be a little later than the tick its report arrived, by up to the shift.
/// </para>
/// </summary>
public sealed class StateStamp(int tickRate, double maxLagSeconds = StateStamp.MaxLagSeconds)
{
    public const double MaxLagSeconds = 0.5;

    /// <summary>How many seconds of reports the latency's median covers.</summary>
    public const double SmoothingSeconds = 5;

    /// <summary>How fast the shift may change, in ticks per tick, so the spacing of states never jumps.</summary>
    public const double SlewRate = 0.05;

    /// <summary>How far, in ticks, the smoothed shift may drift from the whole-tick shift in use before it changes.</summary>
    public const double ShiftHysteresis = 0.75;

    private readonly uint _maxLag = (uint)Math.Round(maxLagSeconds * tickRate);
    private readonly uint _window = (uint)Math.Round(SmoothingSeconds * tickRate);
    private readonly Queue<(uint Arrival, int Lag)> _lags = new();
    private int[] _sorted = new int[64];
    private double _shift = double.NaN;
    private long _wholeShift;
    private uint _lastArrival;

    /// <summary>The last stamp given, the sender's: for checking their movement.</summary>
    public uint Last { get; private set; }

    /// <summary>The last stamp for states sent to others: <see cref="Last"/> shifted by the smoothed latency.</summary>
    public uint Broadcast { get; private set; }

    /// <summary>Broadcast stamps given so far, and how many of them were held back to keep stamps from going backwards.</summary>
    public int Broadcasts { get; private set; }

    public int Clamped { get; private set; }

    /// <summary>The smoothed latency the broadcast stamps are shifted by, in ticks (0 before the first report).</summary>
    public double Shift => double.IsNaN(_shift) ? 0 : _shift;

    /// <summary>The stamp for a state reported in a frame headed <paramref name="clientTick"/> that arrived at <paramref name="arrivalTick"/>.</summary>
    public uint Stamp(uint clientTick, uint arrivalTick)
    {
        var earliest = Math.Max(arrivalTick > _maxLag ? arrivalTick - _maxLag : 0, Last);
        Last = Math.Clamp(clientTick, Math.Min(earliest, arrivalTick), arrivalTick);

        // The broadcast stamp starts from the sender's own tick, bounded only to within the maximum lag of
        // arrival either way, not from Last: a report that crosses faster than the client's estimate assumes
        // (half its best round trip) arrives before its own stamp, and holding it to arrival broke the sender's
        // spacing, a hitch for everyone drawing them. Nor is it held to arrival afterwards (a report arriving
        // faster than the median would lose a tick, drawn at double speed as a jump); a stamp a little ahead is
        // simply drawn when the render tick reaches it.
        var own = Math.Clamp((long)clientTick, (long)arrivalTick - _maxLag, (long)arrivalTick + _maxLag);
        UpdateShift((int)(arrivalTick - own), arrivalTick);
        // Stamps are whole ticks, so the shift is too; it changes only once the smoothed shift is well past the
        // one in use, since rounding a shift that hovers near a half would move every few stamps by a tick.
        if (Math.Abs(_shift - _wholeShift) > ShiftHysteresis)
        {
            _wholeShift = (long)Math.Round(_shift);
        }

        var shifted = (uint)Math.Max(0, own + _wholeShift);
        Broadcast = Math.Max(shifted, Broadcast);
        Broadcasts++;
        Clamped += Broadcast != shifted ? 1 : 0;
        return Last;
    }

    /// <summary>Stamps a position the server set itself (a spawn or respawn) with the server's own tick.</summary>
    public uint StampServer(uint tick)
    {
        Last = Math.Max(Last, tick);
        Broadcast = Math.Max(Broadcast, tick);
        return Last;
    }

    private void UpdateShift(int lag, uint arrivalTick)
    {
        _lags.Enqueue((arrivalTick, lag));
        while (_lags.Count > 1 && arrivalTick - _lags.Peek().Arrival > _window)
        {
            _lags.Dequeue();
        }

        if (_sorted.Length < _lags.Count)
        {
            _sorted = new int[_lags.Count * 2];
        }

        var count = 0;
        foreach (var sample in _lags)
        {
            _sorted[count++] = sample.Lag;
        }

        Array.Sort(_sorted, 0, count);
        double median = _sorted[count / 2];
        if (double.IsNaN(_shift))
        {
            _shift = median;
        }
        else
        {
            var step = SlewRate * (arrivalTick - _lastArrival);
            _shift += Math.Clamp(median - _shift, -step, step);
        }

        _lastArrival = arrivalTick;
    }
}
