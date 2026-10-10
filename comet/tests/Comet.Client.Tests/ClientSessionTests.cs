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
