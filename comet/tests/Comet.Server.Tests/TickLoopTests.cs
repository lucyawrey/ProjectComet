using Comet.Server.Ticking;

namespace Comet.Server.Tests;

public class TickLoopTests
{
    [Fact]
    public async Task RunsAtTheTickRate()
    {
        var ticks = 0;
        var loop = new TickLoop(tickRate: 100, _ => Interlocked.Increment(ref ticks));

        loop.Start();
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        loop.Stop();

        Assert.InRange(ticks, 95, 105);
    }

    [Fact]
    public async Task OverrunningTickDoesNotCatchUp()
    {
        var ticks = 0;
        var loop = new TickLoop(tickRate: 100, tick =>
        {
            Interlocked.Increment(ref ticks);
            if (tick == 1)
            {
                Thread.Sleep(200);
            }
        });

        loop.Start();
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        loop.Stop();

        // 200 ms lost to tick 1, then ~30 ticks in the remaining 300 ms; catching up would give ~50.
        Assert.InRange(ticks, 25, 36);
    }

    [Fact]
    public async Task AFailingTickStopsTheLoopAndReportsIt()
    {
        var ticks = 0;
        var loop = new TickLoop(tickRate: 100, tick =>
        {
            Interlocked.Increment(ref ticks);
            if (tick == 3)
            {
                throw new InvalidOperationException("boom");
            }
        });
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
