namespace Comet.Client.Tests;

public class InterpolationDelayTests
{
    private const int TickRate = 30;

    /// <summary>Feeds samples every 0.1 s from <paramref name="from"/> to <paramref name="to"/>, advancing each 1/60 s.</summary>
    private static void Run(InterpolationDelay delay, double from, double to, Func<int, double>? needed)
    {
        var i = 0;
        for (var now = from; now < to; now += 1.0 / 60)
        {
            if (needed != null && Math.Abs(now / 0.1 - Math.Round(now / 0.1)) < 1e-6)
            {
                delay.AddSample(needed(i++), now);
            }

            delay.Advance(now);
        }
    }

    [Fact]
    public void StartsAtTwoReportIntervals()
    {
        var delay = new InterpolationDelay(TickRate, reportInterval: 0.1);

        Assert.Equal(6, delay.Ticks, 6);
        Assert.Equal(0.2, delay.Seconds, 6);
    }

    [Fact]
    public void NeverGoesUnderTheFloor()
    {
        var delay = new InterpolationDelay(TickRate, reportInterval: 0.1);
        Run(delay, 0, 5, _ => 1);

        Assert.Equal(6, delay.Ticks, 6);
    }

    [Fact]
    public void TargetsTheHighPercentilePlusAMargin()
    {
        var delay = new InterpolationDelay(TickRate, reportInterval: 0.1);
        // One sample in ten needs 15 ticks, the rest 5: the 95th percentile is 15, plus one tick.
        Run(delay, 0, 5, i => i % 10 == 0 ? 15 : 5);

        Assert.Equal(16, delay.Target, 6);
    }

    [Fact]
    public void GrowsSlowlySoTheRenderTickNeverGoesBack()
    {
        var delay = new InterpolationDelay(TickRate, reportInterval: 0.1);
        delay.Advance(0);
        delay.AddSample(30, 0);
        delay.Advance(0.25);

        var before = delay.Ticks;
        delay.Advance(1.25);
        // At most 4% of a second's 30 ticks.
        Assert.Equal(before + 1.2, delay.Ticks, 6);

        // The render tick (receive tick minus the delay) still moves forwards at 96% speed.
        Assert.True(TickRate * 1.0 - (delay.Ticks - before) > 0);
    }

    [Fact]
    public void ShrinksMoreSlowlyThanItGrows()
    {
        var delay = new InterpolationDelay(TickRate, reportInterval: 0.1);
        Run(delay, 0, 30, _ => 20);
        Assert.Equal(21, delay.Ticks, 6);

        // Nothing needs more than the floor now; the old samples leave the window, and the delay falls at 2%.
        Run(delay, 30, 33, _ => 1);
        Assert.Equal(6, delay.Target, 6);
        Assert.InRange(delay.Ticks, 21 - 3 * TickRate * 0.02 - 0.01, 21 - (3 - 2.25) * TickRate * 0.02 + 0.01);
    }

    [Fact]
    public void KeepsItsTargetWhileNobodyMoves()
    {
        var delay = new InterpolationDelay(TickRate, reportInterval: 0.1);
        Run(delay, 0, 30, _ => 12);
        Run(delay, 30, 40, null);

        Assert.Equal(13, delay.Target, 6);
        Assert.Equal(13, delay.Ticks, 6);
    }
}
