using System;
using System.Buffers.Binary;
using MessagePack;

namespace Comet.Protocol.Framing
{
    /// <summary>
    /// Reads the messages in a frame written by <see cref="MessageWriter"/>.
    /// Unknown message IDs can be skipped, because every message carries its length.
    /// </summary>
    public struct FrameReader
    {
        private ReadOnlyMemory<byte> _remaining;

        private FrameReader(uint tick, ReadOnlyMemory<byte> messages)
        {
            Tick = tick;
            _remaining = messages;
        }

        public uint Tick { get; }

        public static FrameReader Create(ReadOnlyMemory<byte> frame)
        {
            if (frame.Length < MessageWriter.TickSize)
            {
                throw new ProtocolException("Frame is shorter than its header.");
            }
            var tick = BinaryPrimitives.ReadUInt32LittleEndian(frame.Span);
            return new FrameReader(tick, frame.Slice(MessageWriter.TickSize));
        }

        /// <summary>Reads the next message, or returns false at the end of the frame.</summary>
        public bool TryReadNext(out ushort messageId, out ReadOnlyMemory<byte> payload)
        {
            if (_remaining.IsEmpty)
            {
                messageId = 0;
                payload = default;
                return false;
            }

            var span = _remaining.Span;
            if (!Varint.TryRead(span, out var id, out var idSize) || id > ushort.MaxValue)
            {
                throw new ProtocolException("Malformed message ID.");
            }
            if (!Varint.TryRead(span.Slice(idSize), out var length, out var lengthSize))
            {
                throw new ProtocolException("Malformed message length.");
            }
            var headerSize = idSize + lengthSize;
            if (length > (uint)(_remaining.Length - headerSize))
            {
                throw new ProtocolException("Message length runs past the end of the frame.");
            }

            messageId = (ushort)id;
            payload = _remaining.Slice(headerSize, (int)length);
            _remaining = _remaining.Slice(headerSize + (int)length);
            return true;
        }

        /// <param name="payload">A message's payload, from <see cref="TryReadNext"/>.</param>
        /// <param name="options">Serializer options; <see cref="ProtocolSerializer.Options"/> (Comet's messages only) if null.</param>
        public static T Decode<T>(ReadOnlyMemory<byte> payload, MessagePackSerializerOptions? options = null)
        {
            try
            {
                return MessagePackSerializer.Deserialize<T>(payload, options ?? ProtocolSerializer.Options);
            }
            catch (MessagePackSerializationException e)
            {
                throw new ProtocolException($"Malformed {typeof(T).Name} payload.", e);
            }
        }
    }
}
