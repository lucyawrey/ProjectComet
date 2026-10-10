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

    [Fact]
    public void BroadcastsShiftedByASteadyLatency()
    {
        // Sent every 2 ticks, arriving 6 ticks later: others get the arrival timeline, evenly spaced.
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

        // A resend holds four reports back; they arrive together. Their spacing is kept, shifted as before.
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

        // The player's route gets 9 ticks longer and stays so: the shift follows at the slew rate, never jumping.
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

        // One report arrives a tick sooner than the usual 2: still two ticks after the last, a tick past its arrival.
        _stamp.Stamp(clientTick: 162, arrivalTick: 163);
        Assert.Equal(164u, _stamp.Broadcast);
        _stamp.Stamp(clientTick: 164, arrivalTick: 166);
        Assert.Equal(166u, _stamp.Broadcast);
        Assert.Equal(0, _stamp.Clamped);
    }

    [Fact]
    public void BroadcastsNeverGoBackwards()
    {
        _stamp.Stamp(clientTick: 100, arrivalTick: 110);
        Assert.Equal(110u, _stamp.Broadcast);
        // A stamp from before the last is held at the last.
        _stamp.Stamp(clientTick: 96, arrivalTick: 111);
        Assert.Equal(110u, _stamp.Broadcast);
        // A server position from before the last broadcast keeps the last.
        _stamp.StampServer(105);
        Assert.Equal(110u, _stamp.Broadcast);
        Assert.Equal(130u, _stamp.StampServer(130));
        Assert.Equal(130u, _stamp.Broadcast);
    }
}
