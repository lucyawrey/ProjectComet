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
}
