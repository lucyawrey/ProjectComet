using Comet.Protocol.Framing;
using Comet.Protocol.Messages;

namespace Comet.Protocol.Tests;

public class FrameTests
{
    [Fact]
    public void RoundTripsTickAndMessages()
    {
        var writer = new MessageWriter();
        writer.BeginFrame(tick: 123_456);
        writer.Write(MessageIds.EntityState, new EntityState { EntityId = 7, X = 1.5f, Y = -2f, Z = 3f, Facing = 0.25f });
        writer.Write(MessageIds.Pong, new Pong { ClientTime = 42 });

        var reader = FrameReader.Create(writer.WrittenMemory);

        Assert.Equal(123_456u, reader.Tick);
        Assert.True(reader.TryReadNext(out var id, out var payload));
        Assert.Equal(MessageIds.EntityState, id);
        var state = FrameReader.Decode<EntityState>(payload);
        Assert.Equal(7u, state.EntityId);
        Assert.Equal(1.5f, state.X);
        Assert.Equal(0.25f, state.Facing);
        Assert.True(reader.TryReadNext(out id, out payload));
        Assert.Equal(MessageIds.Pong, id);
        Assert.Equal(42, FrameReader.Decode<Pong>(payload).ClientTime);
        Assert.False(reader.TryReadNext(out _, out _));
        Assert.Equal(2, writer.MessageCount);
    }

    [Fact]
    public void EntityStateFitsTheBudget()
    {
        // About 10 updates in ~300 bytes per frame (prototype.md).
        var writer = new MessageWriter();
        writer.Clear();

        writer.Write(MessageIds.EntityState, new EntityState { EntityId = 299, X = 123.456f, Y = 7.89f, Z = -98.7f, Facing = 3.1f });

        Assert.InRange(writer.Length, 1, 30);
    }

    [Fact]
    public void ShiftsPayloadsOf128BytesOrMore()
    {
        var writer = new MessageWriter(initialCapacity: 8);
        writer.BeginFrame(tick: 1);
        var big = new byte[300];
        new Random(1).NextBytes(big);
        writer.Write(99, big);
        writer.Write(MessageIds.Pong, new Pong { ClientTime = -5 });

        var reader = FrameReader.Create(writer.WrittenMemory);

        Assert.True(reader.TryReadNext(out var id, out var payload));
        Assert.Equal(99, id);
        Assert.Equal(big, FrameReader.Decode<byte[]>(payload));
        Assert.True(reader.TryReadNext(out id, out payload));
        Assert.Equal(-5, FrameReader.Decode<Pong>(payload).ClientTime);
    }

    [Fact]
    public void SkipsUnknownMessages()
    {
        var writer = new MessageWriter();
        writer.BeginFrame(tick: 1);
        writer.Write(500, "a message type this reader doesn't know");
        writer.Write(MessageIds.Ping, new Ping { ClientTime = 9 });

        var reader = FrameReader.Create(writer.WrittenMemory);
        ushort id;
        ReadOnlyMemory<byte> payload;
        do
        {
            Assert.True(reader.TryReadNext(out id, out payload));
        }
        while (id != MessageIds.Ping);

        Assert.Equal(9, FrameReader.Decode<Ping>(payload).ClientTime);
    }

    [Fact]
    public void AppendsBufferedMessages()
    {
        var events = new MessageWriter();
        events.Clear();
        events.Write(MessageIds.Pong, new Pong { ClientTime = 1 });
        events.Write(MessageIds.Pong, new Pong { ClientTime = 2 });
        var frame = new MessageWriter();
        frame.BeginFrame(tick: 5);

        frame.Append(events);

        Assert.Equal(2, frame.MessageCount);
        var reader = FrameReader.Create(frame.WrittenMemory);
        Assert.True(reader.TryReadNext(out _, out var first));
        Assert.True(reader.TryReadNext(out _, out var second));
        Assert.Equal(1, FrameReader.Decode<Pong>(first).ClientTime);
        Assert.Equal(2, FrameReader.Decode<Pong>(second).ClientTime);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2 })]                     // shorter than the tick
    [InlineData(new byte[] { 0, 0, 0, 0, 0x80 })]         // truncated message ID
    [InlineData(new byte[] { 0, 0, 0, 0, 3, 10, 1, 2 })]  // length runs past the end
    public void RejectsMalformedFrames(byte[] frame)
    {
        Assert.Throws<ProtocolException>(() =>
        {
            var reader = FrameReader.Create(frame);
            while (reader.TryReadNext(out _, out _))
            {
            }
        });
    }
}
