using System.Numerics;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;

namespace Comet.Client.Tests;

public class ClientSessionTests
{
    private readonly FakeTransport _transport = new();
    private readonly ClientSession _session;
    private readonly MessageWriter _server = new();

    public ClientSessionTests() => _session = new ClientSession(_transport, ProtocolSerializer.Options);

    private void ServerSends<T>(uint tick, ushort messageId, T message)
    {
        _server.BeginFrame(tick);
        _server.Write(messageId, message);
        _transport.Incoming.Enqueue(_server.WrittenSpan.ToArray());
    }

    private void Welcome(double now)
    {
        ServerSends(100, MessageIds.Welcome, new Welcome { EntityId = 7, TickRate = 30 });
        _session.Update(now);
    }

    private List<(uint Tick, ushort Id, ReadOnlyMemory<byte> Payload)> Sent()
    {
        var messages = new List<(uint, ushort, ReadOnlyMemory<byte>)>();
        foreach (var frame in _transport.Sent)
        {
            var reader = FrameReader.Create(frame);
            while (reader.TryReadNext(out var id, out var payload))
            {
                messages.Add((reader.Tick, id, payload));
            }
        }

        _transport.Sent.Clear();
        return messages;
    }

    [Fact]
    public void WelcomeStartsTheClockAndPings()
    {
        Welcome welcome = default;
        _session.WelcomeArrived += w => welcome = w;
        Assert.False(_session.Welcomed);
        Assert.Throws<InvalidOperationException>(() => _session.Clock);

        Welcome(now: 5);
        _session.Flush(5);

        Assert.True(_session.Welcomed);
        Assert.Equal(7u, _session.EntityId);
        Assert.Equal(7u, welcome.EntityId);
        var ping = Assert.Single(Sent());
        Assert.Equal(MessageIds.Ping, ping.Id);
        Assert.Equal(100u, ping.Tick);
        Assert.Equal(5_000_000, FrameReader.Decode<Ping>(ping.Payload).ClientTime);
    }

    [Fact]
    public void PingsEverySecondAndLearnsTheRoundTrip()
    {
        Welcome(now: 5);
        _session.Flush(5);
        Sent();

        ServerSends(102, MessageIds.Pong, new Pong { ClientTime = 5_000_000 });
        _session.Update(5.08);
        Assert.Equal(0.08, _session.Clock.RoundTrip, 6);

        _session.Update(5.5);
        _session.Flush(5.5);
        Assert.Empty(Sent());
        _session.Update(6);
        _session.Flush(6);
        Assert.Equal(MessageIds.Ping, Assert.Single(Sent()).Id);
    }

    [Fact]
    public void FlushSendsNothingWhenThereIsNothingToSay()
    {
        _session.Flush(0);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public void StatesFeedTheBufferOnceTheGameSpawnsTheEntity()
    {
        Welcome(now: 0);
        ServerSends(103, MessageIds.EntityState, new EntityState { EntityId = 9, X = 1, Tick = 103 });
        _session.Update(0.1);
        Assert.False(_session.Entities.Contains(9));

        _session.Entities.Spawn(9, 103, Vector3.Zero, 0);
        // Buffered by the state's own tick, not the tick of the frame that carried it.
        ServerSends(108, MessageIds.EntityState, new EntityState { EntityId = 9, X = 2, Tick = 105 });
        _session.Update(0.2);

        Assert.Equal(2, _session.StatesReceived);
        Assert.True(_session.Entities.TrySample(9, 104, out var pose));
        Assert.Equal(1, pose.Position.X, 4);
    }

    [Fact]
    public void StatesWaitingThroughALocalStallDontRaiseTheDelay()
    {
        Welcome(now: 5);
        ServerSends(102, MessageIds.Pong, new Pong { ClientTime = 5_000_000 });
        _session.Update(5.08);
        Assert.True(_session.Clock.Synced);
        _session.Entities.Spawn(9, 102, Vector3.Zero, 0);
        var target = _session.InterpolationDelay.Target;

        // A hidden tab: states kept arriving for 10 s, but the session only sees them all when it runs again.
        for (uint tick = 104; tick < 404; tick += 2)
        {
            ServerSends(tick, MessageIds.EntityState, new EntityState { EntityId = 9, X = tick / 100f, Tick = tick });
        }

        _session.Update(15.1);

        Assert.Equal(150, _session.StatesReceived);
        Assert.Equal(target, _session.InterpolationDelay.Target);
    }

    [Fact]
    public void DespawnRemovesTheEntity()
    {
        uint despawned = 0;
        _session.EntityDespawned += id => despawned = id;
        Welcome(now: 0);
        _session.Entities.Spawn(9, 100, Vector3.Zero, 0);

        ServerSends(101, MessageIds.EntityDespawn, new EntityDespawn { EntityId = 9 });
        _session.Update(0.1);

        Assert.Equal(9u, despawned);
        Assert.False(_session.Entities.Contains(9));
    }

    [Fact]
    public void ReportsEchoTheLastCorrection()
    {
        PositionCorrection received = default;
        _session.Corrected += c => received = c;
        Welcome(now: 0);
        _session.Flush(0);
        Sent();

        ServerSends(110, MessageIds.PositionCorrection, new PositionCorrection { Sequence = 3, Reason = CorrectionReason.SnapBack, X = 4 });
        _session.Update(0.1);
        _session.ReportPosition(new Vector3(4, 0, 0), Vector3.UnitX, 1);
        _session.Flush(0.1);

        Assert.Equal(3u, received.Sequence);
        var report = FrameReader.Decode<PositionReport>(Assert.Single(Sent()).Payload);
        Assert.Equal(3u, report.CorrectionSequence);
        Assert.Equal(4, report.X);
    }

    [Fact]
    public void StepsOfAFasterSimulationFallOnEveryOtherTick()
    {
        Welcome(now: 0);

        Assert.True(_session.TickOfStep(200, stepsPerSecond: 60, out var tick));
        Assert.Equal(100u, tick);
        Assert.False(_session.TickOfStep(201, stepsPerSecond: 60, out _));
        Assert.True(_session.TickOfStep(77, stepsPerSecond: 30, out tick));
        Assert.Equal(77u, tick);
        Assert.Throws<ArgumentException>(() => _session.TickOfStep(1, stepsPerSecond: 45, out _));
    }

    [Fact]
    public void ReportsAreStampedWithTheirStepsTick()
    {
        Welcome(now: 0);
        _session.Flush(0);
        Sent();

        // Two reports for different ticks before one flush go in two frames, each with its own tick.
        _session.ReportPosition(140, new Vector3(1, 0, 0), Vector3.UnitX, 0);
        _session.ReportPosition(142, new Vector3(2, 0, 0), Vector3.UnitX, 0);
        _session.Flush(0.1);
        var sent = Sent();
        Assert.Equal([140u, 142u], sent.Select(m => m.Tick));

        // Without a report, a frame is stamped with the estimate again.
        _session.Write(MessageIds.FirstGameMessage, new Pong { ClientTime = 1 });
        _session.Flush(0.1);
        Assert.NotEqual(142u, Assert.Single(Sent()).Tick);
    }

    [Fact]
    public void ReportsAreHeldWhilePingsGoUnansweredThenTheLatestIsSent()
    {
        // The upload stalls: pings at 0, 1, 2 and 3 s get no answer, so reports stop rather than queue up for the
        // server to receive all at once (the code review's S8).
        Welcome(now: 0);
        for (var i = 0; i <= 30; i++)
        {
            var now = i * 0.1;
            _session.Update(now);
            _session.ReportPosition((uint)(100 + 3 * i), new Vector3(i, 0, 0), Vector3.UnitX, 0);
            _session.Flush(now);
        }

        var reports = Sent().Where(m => m.Id == MessageIds.PositionReport).ToList();
        Assert.True(_session.ReportsHeld);
        Assert.InRange(reports.Count, 20, 22); // up to just past 2 s
        Assert.Equal(reports.Count - 1, FrameReader.Decode<PositionReport>(reports[^1].Payload).X);

        // The pongs arrive, in order: the latest held report goes out once, and reporting carries on.
        foreach (var sent in new[] { 0.0, 1, 2, 3 })
        {
            ServerSends(200, MessageIds.Pong, new Pong { ClientTime = (long)(sent * 1_000_000) });
        }

        _session.Update(3.1);
        _session.Flush(3.1);
        Assert.False(_session.ReportsHeld);
        var resumed = Assert.Single(Sent(), m => m.Id == MessageIds.PositionReport);
        Assert.Equal(30, FrameReader.Decode<PositionReport>(resumed.Payload).X);
        Assert.Equal(190u, resumed.Tick);
    }

    [Fact]
    public void AnAnsweredPingNeverHoldsReports()
    {
        // Times that don't fit microseconds exactly (frames at 60 Hz from an odd start): each pong answers the latest
        // ping, so a later pong a second late must not hold reports (the final review's finding).
        const double start = 5.123456789;
        Welcome(now: start);
        var lastPing = 0L;
        for (var frame = 0; frame < 6 * 60; frame++)
        {
            var now = start + frame / 60.0;
            _session.Update(now);
            _session.Flush(now);
            foreach (var (_, id, payload) in Sent())
            {
                if (id == MessageIds.Ping)
                {
                    lastPing = FrameReader.Decode<Ping>(payload).ClientTime;
                    if (frame < 3 * 60)
                    {
                        ServerSends(200, MessageIds.Pong, new Pong { ClientTime = lastPing });
                    }
                }
            }

            // From 3 s, pongs stop: reports are held only once that ping has waited 2 s (a frame either side is
            // left out, for rounding).
            var waited = now - (start + 3);
            if (Math.Abs(waited - ClientSession.ReportHoldSeconds) > 0.02)
            {
                Assert.True(waited > ClientSession.ReportHoldSeconds == _session.ReportsHeld, $"held: {_session.ReportsHeld} at {now - start:0.000} s");
            }
        }
    }

    [Fact]
    public void GameMessagesAreHandedOn()
    {
        const ushort chat = MessageIds.FirstGameMessage + 1;
        (ushort Id, uint Tick, int Length) seen = default;
        _session.GameMessage += (id, payload, tick) => seen = (id, tick, payload.Length);

        ServerSends(100, chat, new Pong { ClientTime = 1 });
        ServerSends(100, 40, new Pong { ClientTime = 1 }); // an unknown Comet message from a newer server is skipped
        _session.Update(0);

        Assert.Equal(chat, seen.Id);
        Assert.Equal(100u, seen.Tick);
        Assert.True(seen.Length > 0);
    }

    [Fact]
    public void AMalformedFrameClosesTheConnection()
    {
        _transport.Incoming.Enqueue([1, 2]);
        _session.Update(0);

        Assert.True(_transport.CloseRequested);
        Assert.NotNull(_session.Error);
    }

    private sealed class FakeTransport : IClientTransport
    {
        public Queue<byte[]> Incoming { get; } = new();

        public List<byte[]> Sent { get; } = [];

        public bool CloseRequested { get; private set; }

        public TransportState State => CloseRequested ? TransportState.Closed : TransportState.Open;

        public string? Error => null;

        public void Send(ReadOnlySpan<byte> frame) => Sent.Add(frame.ToArray());

        public bool TryReceive(out byte[] frame) => Incoming.TryDequeue(out frame!);

        public void Close() => CloseRequested = true;

        public void Dispose()
        {
        }
    }
}
