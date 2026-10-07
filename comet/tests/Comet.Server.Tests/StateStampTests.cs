using Comet.Server.Movement;

namespace Comet.Server.Tests;

public class StateStampTests
{
    private readonly StateStamp _stamp = new(tickRate: 30); // at most 15 ticks of lag

    [Fact]
    public void UsesTheSendersTick()
    {
        Assert.Equal(98u, _stamp.Stamp(clientTick: 98, arrivalTick: 100));
        Assert.Equal(101u, _stamp.Stamp(clientTick: 101, arrivalTick: 104));
    }

    [Fact]
    public void IsNeverLaterThanArrival()
    {
        Assert.Equal(100u, _stamp.Stamp(clientTick: 500, arrivalTick: 100));
    }

    [Fact]
    public void IsNeverMoreThanTheMaximumLagEarly()
    {
        Assert.Equal(85u, _stamp.Stamp(clientTick: 10, arrivalTick: 100));
        Assert.Equal(0u, new StateStamp(30).Stamp(clientTick: 0, arrivalTick: 5));
    }

    [Fact]
    public void NeverGoesBackwards()
    {
        _stamp.Stamp(clientTick: 99, arrivalTick: 100);
        Assert.Equal(99u, _stamp.Stamp(clientTick: 95, arrivalTick: 101));
    }

    [Fact]
    public void ServerPositionsUseTheServerTick()
    {
        _stamp.Stamp(clientTick: 90, arrivalTick: 100);
        Assert.Equal(120u, _stamp.StampServer(120));
        Assert.Equal(120u, _stamp.Stamp(clientTick: 110, arrivalTick: 125));
    }
}
