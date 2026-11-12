using Comet.Server.Movement;

namespace Comet.Server.Tests;

public class StateStampTests
{
    private readonly StateStamp _stamp = new(tickRate: 30);

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

    [Fact]
    public void BroadcastsShiftedByASteadyLatency()
    {
        for (uint sent = 100; sent <= 140; sent += 2)
        {
            Assert.Equal(sent, _stamp.Stamp(clientTick: sent, arrivalTick: sent + 6));
            Assert.Equal(sent + 6, _stamp.Broadcast);
        }
    }

    [Fact]
    public void AStallDoesntMoveTheShift()
    {
        for (uint sent = 100; sent <= 160; sent += 2)
        {
            _stamp.Stamp(clientTick: sent, arrivalTick: sent + 6);
        }

        for (uint sent = 162; sent <= 168; sent += 2)
        {
            _stamp.Stamp(clientTick: sent, arrivalTick: 176);
            Assert.Equal(sent + 6, _stamp.Broadcast);
        }

        Assert.Equal(6, _stamp.Shift, 6);
    }

    [Fact]
    public void ALatencyChangeIsFollowedSlowly()
    {
        for (uint sent = 100; sent <= 200; sent += 2)
        {
            _stamp.Stamp(clientTick: sent, arrivalTick: sent + 3);
        }

        var shifts = new List<double>();
        for (uint sent = 202; sent <= 600; sent += 2)
        {
            _stamp.Stamp(clientTick: sent, arrivalTick: sent + 12);
            shifts.Add(_stamp.Shift);
        }

        Assert.All(shifts.Zip(shifts.Skip(1)), pair => Assert.InRange(pair.Second - pair.First, 0, StateStamp.SlewRate * 2 + 1e-9));
        Assert.Equal(12, shifts[^1], 6);
    }

    [Fact]
    public void BroadcastsKeepTheSendersSpacingWhenAReportArrivesEarly()
    {
        for (uint sent = 100; sent <= 160; sent += 2)
        {
            _stamp.Stamp(clientTick: sent, arrivalTick: sent + 2);
        }

        _stamp.Stamp(clientTick: 162, arrivalTick: 163);
        Assert.Equal(164u, _stamp.Broadcast);
        _stamp.Stamp(clientTick: 164, arrivalTick: 166);
        Assert.Equal(166u, _stamp.Broadcast);
        Assert.Equal(0, _stamp.Clamped);
    }

    [Fact]
    public void BroadcastsKeepTheSendersSpacingWhenAReportArrivesBeforeItsStamp()
    {
        for (uint sent = 100; sent <= 160; sent += 2)
        {
            _stamp.Stamp(clientTick: sent, arrivalTick: sent - 1);
        }

        Assert.Equal(161u, _stamp.Stamp(clientTick: 162, arrivalTick: 161));
        Assert.Equal(161u, _stamp.Broadcast);
        _stamp.Stamp(clientTick: 164, arrivalTick: 162);
        Assert.Equal(163u, _stamp.Broadcast);
        Assert.Equal(0, _stamp.Clamped);
    }

    [Fact]
    public void AShiftHoveringBetweenTwoTicksRarelyMovesTheStamps()
    {
        var random = new Random(1);
        var moved = 0;
        var last = 0u;
        for (uint sent = 100; sent <= 2100; sent += 2)
        {
            _stamp.Stamp(clientTick: sent, arrivalTick: sent + 2 + (uint)random.Next(2));
            moved += sent > 100 && _stamp.Broadcast - last != 2 ? 1 : 0;
            last = _stamp.Broadcast;
        }

        Assert.InRange(moved, 0, 8);
    }

    [Fact]
    public void BroadcastsNeverGoBackwards()
    {
        _stamp.Stamp(clientTick: 100, arrivalTick: 110);
        Assert.Equal(110u, _stamp.Broadcast);
        _stamp.Stamp(clientTick: 96, arrivalTick: 111);
        Assert.Equal(110u, _stamp.Broadcast);
        _stamp.StampServer(105);
        Assert.Equal(110u, _stamp.Broadcast);
        Assert.Equal(130u, _stamp.StampServer(130));
        Assert.Equal(130u, _stamp.Broadcast);
    }
}
