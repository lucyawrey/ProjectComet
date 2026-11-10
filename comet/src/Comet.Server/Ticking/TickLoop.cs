using System.Diagnostics;

namespace Comet.Server.Ticking;

public readonly record struct TickTiming(uint Tick, TimeSpan Lateness, TimeSpan Work);

/// <summary>
/// Runs the simulation at a fixed rate on a dedicated thread: sleeps until shortly before each deadline, then spins
/// (the OS often wakes sleeping threads late). A tick that overruns delays the next one; there are no catch-up ticks.
/// </summary>
public sealed class TickLoop
{
    private static readonly TimeSpan SpinWindow = TimeSpan.FromMilliseconds(1);

    private readonly Action<uint> _onTick;
    private readonly Thread _thread;
    private volatile bool _stopping;
    private uint _tick;

    public TickLoop(int tickRate, Action<uint> onTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        TickRate = tickRate;
        Period = TimeSpan.FromSeconds(1.0 / tickRate);
        _onTick = onTick;
        _thread = new Thread(Run)
        {
            Name = "Tick loop",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
    }

    /// <summary>Ticks per second.</summary>
    public int TickRate { get; }

    public TimeSpan Period { get; }

    public uint CurrentTick => Volatile.Read(ref _tick);

    public event Action<TickTiming>? TickCompleted;

    /// <summary>
    /// Called on the tick thread if a tick throws; the loop has stopped and the game's state can't be trusted, so the server
    /// should log and stop. With no handler, the exception ends the process.
    /// </summary>
    public event Action<uint, Exception>? Failed;

    public void Start() => _thread.Start();

    public void Stop()
    {
        _stopping = true;
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join();
        }
    }

    private void Run()
    {
        var period = Period.TotalSeconds * Stopwatch.Frequency;
        var spinWindow = (long)(SpinWindow.TotalSeconds * Stopwatch.Frequency);
        var start = Stopwatch.GetTimestamp();
        var scheduled = (double)start;

        while (!_stopping)
        {
            var tickStart = Stopwatch.GetTimestamp();
            var tick = Volatile.Read(ref _tick) + 1;
            Volatile.Write(ref _tick, tick);

            try
            {
                _onTick(tick);
            }
            catch (Exception e) when (Failed is not null)
            {
                _stopping = true;
                Failed(tick, e);
                return;
            }

            var tickEnd = Stopwatch.GetTimestamp();
            TickCompleted?.Invoke(
                new TickTiming(
                    tick,
                    Stopwatch.GetElapsedTime((long)scheduled, Math.Max(tickStart, (long)scheduled)),
                    Stopwatch.GetElapsedTime(tickStart, tickEnd)
                )
            );

            scheduled += period;
            var now = Stopwatch.GetTimestamp();
            if (now >= scheduled)
            {
                scheduled = now;
                continue;
            }

            WaitUntil((long)scheduled, spinWindow);
        }
    }

    private static void WaitUntil(long deadline, long spinWindow)
    {
        var sleepUntil = deadline - spinWindow;
        var now = Stopwatch.GetTimestamp();
        if (now < sleepUntil)
        {
            var sleep = Stopwatch.GetElapsedTime(now, sleepUntil);
            if (sleep >= TimeSpan.FromMilliseconds(1))
            {
                Thread.Sleep(sleep);
            }
        }
        while (Stopwatch.GetTimestamp() < deadline)
        {
            Thread.SpinWait(20);
        }
    }
}
