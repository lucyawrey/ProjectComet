using Comet.Server.Ticking;

namespace Comet.Server.Tests;

public class TickLimiterTests
{
    [Fact]
    public void AllowsABurstThenOneEveryPeriod()
    {
        var limiter = new TickLimiter(tickRate: 30, burst: 3, secondsPerTake: 2);

        Assert.True(limiter.TryTake(100));
        Assert.True(limiter.TryTake(100));
        Assert.True(limiter.TryTake(100));
        Assert.False(limiter.TryTake(101));
        Assert.True(limiter.TryTake(161));
        Assert.False(limiter.TryTake(162));
    }
}
