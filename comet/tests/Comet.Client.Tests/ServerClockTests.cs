namespace Comet.Client.Tests;

public class ServerClockTests
{
    private const int TickRate = 30;

    /// <summary>A pong from a server whose tick at time t is t × 30 + 1000, with the reply leaving halfway through the round trip.</summary>
    private static void Pong(ServerClock clock, double sentAt, double roundTrip, double offsetSeconds = 0)
    {
        var tick = (uint)Math.Floor((sentAt + roundTrip / 2 + offsetSeconds) * TickRate + 1000);
        clock.OnPong(tick, sentAt, sentAt + roundTrip);
    }

    private static double TrueTick(double now) => now * TickRate + 1000;

    [Fact]
    public void FirstPongSetsBothTimelines()
    {
        var clock = new ServerClock(TickRate);
        Pong(clock, sentAt: 10, roundTrip: 0.1);

        Assert.True(clock.Synced);
        Assert.Equal(0.1, clock.RoundTrip, 6);
        Assert.Equal(TrueTick(10.1), clock.ServerTick(10.1), 0.6);
        // Frames arriving now left the server half a round trip ago.
        Assert.Equal(TrueTick(10.05), clock.ReceiveTick(10.1), 0.6);
    }

    [Fact]
    public void FramesGiveARoughEstimateBeforeAnyPong()
    {
        var clock = new ServerClock(TickRate);
        clock.OnFrame(500, now: 2);

        Assert.False(clock.Synced);
        Assert.Equal(500.5 + TickRate, clock.ServerTick(3), 6);
    }

    [Fact]
    public void TrustsTheLowestRoundTrip()
    {
        var clock = new ServerClock(TickRate);
        Pong(clock, sentAt: 0, roundTrip: 0.05);

        // Later pings are delayed one way only (queueing), which would skew a plain average.
        for (var i = 1; i < 9; i++)
        {
            clock.OnPong((uint)Math.Floor(TrueTick(i + 0.025)), i, i + 0.05 + 0.2);
        }

        Assert.Equal(0.05, clock.RoundTrip, 6);
        clock.Advance(9);
        Assert.Equal(TrueTick(9), clock.ServerTick(9), 0.6);
    }

    [Fact]
    public void ForgetsSamplesOlderThanTheWindow()
    {
        var clock = new ServerClock(TickRate);
        Pong(clock, sentAt: 0, roundTrip: 0.02);
        for (var i = 1; i <= ServerClock.SampleCount; i++)
        {
            Pong(clock, sentAt: i, roundTrip: 0.1);
        }

        Assert.Equal(0.1, clock.RoundTrip, 6);
    }

    [Fact]
    public void SlewsSmallDifferences()
    {
        var clock = new ServerClock(TickRate);
        clock.Advance(0);
        Pong(clock, sentAt: 0, roundTrip: 0.05);
        var before = clock.ServerTick(1);

        // The server turns out to be 0.1 s (3 ticks) ahead; with a lower round trip, so it's trusted.
        Pong(clock, sentAt: 0.5, roundTrip: 0.04, offsetSeconds: 0.1);
        clock.Advance(1);
        var moved = clock.ServerTick(1) - before;
        Assert.InRange(moved, 0.1, 1 * TickRate * ServerClock.MaxSlew + 1e-9);

        clock.Advance(4);
        Assert.Equal(TrueTick(4) + 0.1 * TickRate, clock.ServerTick(4), 0.6);
    }

    [Fact]
    public void JumpsLargeDifferences()
    {
        var clock = new ServerClock(TickRate);
        clock.Advance(0);
        Pong(clock, sentAt: 0, roundTrip: 0.05);
        Pong(clock, sentAt: 0.5, roundTrip: 0.04, offsetSeconds: 1);
        clock.Advance(0.6);

        Assert.Equal(TrueTick(0.6) + TickRate, clock.ServerTick(0.6), 0.6);
    }
}
