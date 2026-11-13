namespace Comet.Server.Movement;

public sealed class StateStamp
{
    public const double MaxLagSeconds = 0.5;

    public const double SmoothingSeconds = 5;

    /// <summary>How fast the shift may change, in ticks per tick, so the spacing of states never jumps.</summary>
    public const double SlewRate = 0.05;

    /// <summary>How far, in ticks, the smoothed shift may drift from the whole-tick shift in use before it changes.</summary>
    public const double ShiftHysteresis = 0.75;

    private readonly uint _maxLag;
    private readonly uint _window;
    private readonly Queue<(uint Arrival, int Lag)> _lags = new();

    private readonly int[] _lagCounts;
    private double _shift = double.NaN;
    private long _wholeShift;
    private uint _lastArrival;

    public StateStamp(int tickRate, double maxLagSeconds = MaxLagSeconds)
    {
        _maxLag = (uint)Math.Round(maxLagSeconds * tickRate);
        _window = (uint)Math.Round(SmoothingSeconds * tickRate);
        _lagCounts = new int[2 * _maxLag + 1];
    }

    public uint Last { get; private set; }

    public uint Broadcast { get; private set; }

    public int Broadcasts { get; private set; }

    public int Clamped { get; private set; }

    /// <summary>The smoothed latency the broadcast stamps are shifted by, in ticks (0 before the first report).</summary>
    public double Shift => double.IsNaN(_shift) ? 0 : _shift;

    public uint Stamp(uint clientTick, uint arrivalTick)
    {
        var earliest = Math.Max(arrivalTick > _maxLag ? arrivalTick - _maxLag : 0, Last);
        Last = Math.Clamp(clientTick, Math.Min(earliest, arrivalTick), arrivalTick);

        // Starts from the sender's own tick, within the maximum lag of arrival, not from Last: a report can arrive before its stamp,
        // and holding it to arrival broke the sender's spacing. Not held to arrival afterwards either: that would lose a tick, drawn as a jump.
        var own = Math.Clamp((long)clientTick, (long)arrivalTick - _maxLag, (long)arrivalTick + _maxLag);
        UpdateShift((int)(arrivalTick - own), arrivalTick);
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

    public uint StampServer(uint tick)
    {
        Last = Math.Max(Last, tick);
        Broadcast = Math.Max(Broadcast, tick);
        return Last;
    }

    private void UpdateShift(int lag, uint arrivalTick)
    {
        _lags.Enqueue((arrivalTick, lag));
        _lagCounts[lag + _maxLag]++;
        while (_lags.Count > 1 && arrivalTick - _lags.Peek().Arrival > _window)
        {
            _lagCounts[_lags.Dequeue().Lag + _maxLag]--;
        }

        var below = 0;
        var bin = 0;
        while (below + _lagCounts[bin] <= _lags.Count / 2)
        {
            below += _lagCounts[bin++];
        }

        double median = bin - (int)_maxLag;
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
