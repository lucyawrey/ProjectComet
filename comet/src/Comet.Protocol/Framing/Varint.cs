using System;

namespace Comet.Protocol.Framing
{
    /// <summary>
    /// Unsigned LEB128 variable-length integers: 7 bits per byte, the top bit set when more bytes follow.
    /// 0–127 take 1 byte, 128–16,383 take 2.
    /// </summary>
    public static class Varint
    {
        public const int MaxLength = 5;

        public static int SizeOf(uint value)
        {
            var size = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                size++;
            }
            return size;
        }

        /// <summary>Writes <paramref name="value"/> and returns the number of bytes written.</summary>
        public static int Write(Span<byte> destination, uint value)
        {
            var i = 0;
            while (value >= 0x80)
            {
                destination[i++] = (byte)(value | 0x80);
                value >>= 7;
            }
            destination[i++] = (byte)value;
            return i;
        }

        /// <summary>Reads a varint, or returns false if <paramref name="source"/> ends early or the value is too long.</summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out uint value, out int bytesRead)
        {
            value = 0;
            for (var i = 0; i < MaxLength && i < source.Length; i++)
            {
                var b = source[i];
                if (i == MaxLength - 1 && b > 0x0F)
                {
                    break; // more than 32 bits
                }
                value |= (uint)(b & 0x7F) << (7 * i);
                if (b < 0x80)
                {
                    bytesRead = i + 1;
                    return true;
                }
            }
            value = 0;
            bytesRead = 0;
            return false;
        }
    }
}
