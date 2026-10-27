using System;
using System.Buffers.Binary;
using System.Text;

namespace PingTheKiller.Shared.Net
{
    public sealed class PacketWriter
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private const int MaxPayloadLength = Protocol.MaxBodyLength - Protocol.IdSize;
        private byte[] _buffer;
        private int _position;

        public PacketWriter(int initialCapacity = 256)
        {
            if (initialCapacity < 1 || initialCapacity > MaxPayloadLength)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            _buffer = new byte[initialCapacity];
        }

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

        private Span<byte> Reserve(int count)
        {
            if (count < 0 || count > MaxPayloadLength - _position)
                throw new PacketFormatException("Payload exceeds the protocol limit.");
            int required = _position + count;
            if (required > _buffer.Length)
                Array.Resize(ref _buffer, Math.Min(MaxPayloadLength, Math.Max(required, _buffer.Length * 2)));
            Span<byte> destination = _buffer.AsSpan(_position, count);
            _position = required;
            return destination;
        }

        public void WriteByte(byte value) => Reserve(1)[0] = value;
        public void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
        public void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
        public void WriteUInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
        public void WriteInt64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);
        public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);
        public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

        public void WriteString(string value, int maxByteLength)
        {
            if (maxByteLength < 0 || maxByteLength > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxByteLength));
            if (value == null)
                throw new PacketFormatException("Strings cannot be null.");
            try
            {
                int length = Utf8.GetByteCount(value);
                if (length > maxByteLength)
                    throw new PacketFormatException("String exceeds its byte limit.");
                WriteUInt16((ushort)length);
                Utf8.GetBytes(value.AsSpan(), Reserve(length));
            }
            catch (EncoderFallbackException ex)
            {
                throw new PacketFormatException("String contains invalid Unicode.", ex);
            }
        }

        public void WriteCount(int count, int maxCount, bool exact = false)
        {
            if (count < 0 || count > byte.MaxValue || count > maxCount || (exact && count != maxCount))
                throw new PacketFormatException("Collection count is invalid.");
            WriteByte((byte)count);
        }
    }
}
