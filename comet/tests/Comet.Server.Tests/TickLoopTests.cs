using System.Diagnostics;
using Comet.Server.Ticking;

namespace Comet.Server.Tests;

public class TickLoopTests
{
    [Fact]
    public async Task KeepsTheTickRateWithoutCatchingUp()
    {
        var starts = new List<long>();
        var loop = new TickLoop(
            tickRate: 50,
            tick =>
            {
                starts.Add(Stopwatch.GetTimestamp());
                if (tick == 3)
                {
                    Thread.Sleep(300);
                }
            }
        );

        loop.Start();
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        loop.Stop();

        var intervals = starts.Zip(starts.Skip(1), (a, b) => Stopwatch.GetElapsedTime(a, b).TotalMilliseconds).Order().ToList();
        Assert.InRange(intervals[intervals.Count / 2], 15, 25);

        var afterSlowTick = starts[3];
        Assert.True(starts.Count(t => t >= afterSlowTick && Stopwatch.GetElapsedTime(afterSlowTick, t).TotalMilliseconds < 190) <= 12);
    }

    [Fact]
    public async Task AFailingTickStopsTheLoopAndReportsIt()
    {
        var ticks = 0;
        var loop = new TickLoop(
            tickRate: 100,
            tick =>
            {
                Interlocked.Increment(ref ticks);
                if (tick == 3)
                {
                    throw new InvalidOperationException("boom");
                }
            }
        );
        var failed = new TaskCompletionSource<(uint, Exception)>();
        loop.Failed += (tick, e) => failed.TrySetResult((tick, e));

        loop.Start();
        var (at, error) = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        loop.Stop();

        Assert.Equal(3u, at);
        Assert.Equal("boom", error.Message);
        Assert.Equal(3, ticks);
    }
}
