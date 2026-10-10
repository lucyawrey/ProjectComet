using System;
using System.Buffers;
using System.Buffers.Binary;
using MessagePack;

namespace Comet.Protocol.Framing
{
    /// <summary>
    /// Builds a frame: the tick (4 bytes, little-endian), then messages, each a varint message ID,
    /// a varint payload length and a MessagePack payload. Also used without a header as a buffer of
    /// encoded messages that is later appended to a frame.
    /// Reuse one writer rather than allocating per frame. Not thread-safe.
    /// </summary>
    public sealed class MessageWriter : IBufferWriter<byte>
    {
        public const int TickSize = 4;

        private readonly MessagePackSerializerOptions _options;
        private byte[] _buffer;
        private int _length;

        /// <param name="initialCapacity">The starting buffer size; it grows as needed.</param>
        /// <param name="options">Serializer options; <see cref="ProtocolSerializer.Options"/> (Comet's messages only) if null.</param>
        public MessageWriter(int initialCapacity = 512, MessagePackSerializerOptions? options = null)
        {
            _buffer = new byte[initialCapacity];
            _options = options ?? ProtocolSerializer.Options;
        }

        public int Length => _length;

        public int MessageCount { get; private set; }

        public ReadOnlyMemory<byte> WrittenMemory => new ReadOnlyMemory<byte>(_buffer, 0, _length);

        public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(_buffer, 0, _length);

        /// <summary>Clears the writer and starts a frame for <paramref name="tick"/>.</summary>
        public void BeginFrame(uint tick)
        {
            Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(GetSpan(TickSize), tick);
            _length = TickSize;
        }

        /// <summary>Clears the writer, with no frame header (for buffering encoded messages).</summary>
        public void Clear()
        {
            _length = 0;
            MessageCount = 0;
        }

        public void Write<T>(ushort messageId, in T message)
        {
            _length += Varint.Write(GetSpan(Varint.MaxLength), messageId);

            // Reserve one byte for the length; most payloads are under 128 bytes.
            var lengthPosition = _length;
            GetSpan(1);
            _length++;
            var payloadStart = _length;

            MessagePackSerializer.Serialize(this, message, _options);

            var payloadLength = _length - payloadStart;
            var extra = Varint.SizeOf((uint)payloadLength) - 1;
            if (extra > 0)
            {
                GetSpan(extra);
                Buffer.BlockCopy(_buffer, payloadStart, _buffer, payloadStart + extra, payloadLength);
                _length += extra;
            }
            Varint.Write(_buffer.AsSpan(lengthPosition), (uint)payloadLength);
            MessageCount++;
        }

        /// <summary>Appends messages buffered in another writer (one started with <see cref="Clear"/>).</summary>
        public void Append(MessageWriter messages) => Append(messages, messages.Length, messages.MessageCount);

        /// <summary>Appends the first <paramref name="count"/> messages, <paramref name="length"/> bytes, buffered in another writer.</summary>
        public void Append(MessageWriter messages, int length, int count)
        {
            messages.WrittenSpan.Slice(0, length).CopyTo(GetSpan(length));
            _length += length;
            MessageCount += count;
        }

        /// <summary>Removes the first <paramref name="count"/> messages, <paramref name="length"/> bytes, keeping the rest in order (for a writer started with <see cref="Clear"/>).</summary>
        public void RemoveStart(int length, int count)
        {
            Buffer.BlockCopy(_buffer, length, _buffer, 0, _length - length);
            _length -= length;
            MessageCount -= count;
        }

        void IBufferWriter<byte>.Advance(int count) => _length += count;

        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_length);
        }

        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => GetSpan(sizeHint);

        private Span<byte> GetSpan(int sizeHint)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_length);
        }

        private void EnsureCapacity(int sizeHint)
        {
            var needed = _length + Math.Max(sizeHint, 1);
            if (needed > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Max(needed, _buffer.Length * 2));
            }
        }
    }
}
