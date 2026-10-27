using System;
using System.Buffers.Binary;
using System.Text;

namespace PingTheKiller.Shared.Net
{
    public ref struct PacketReader
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly ReadOnlySpan<byte> _buffer;
        private int _position;

        public PacketReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        public int Remaining => _buffer.Length - _position;

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > Remaining)
                throw new PacketFormatException("Truncated packet payload.");
            ReadOnlySpan<byte> value = _buffer.Slice(_position, count);
            _position += count;
            return value;
        }

        public byte ReadByte() => Take(1)[0];
        public byte ReadByte(byte maxValue)
        {
            byte value = ReadByte();
            if (value > maxValue)
                throw new PacketFormatException("Byte value is outside its allowed range.");
            return value;
        }
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        public string ReadString(int maxByteLength)
        {
            if (maxByteLength < 0 || maxByteLength > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxByteLength));
            int length = ReadUInt16();
            if (length > maxByteLength)
                throw new PacketFormatException("String exceeds its byte limit.");
            try
            {
                return Utf8.GetString(Take(length));
            }
            catch (DecoderFallbackException ex)
            {
                throw new PacketFormatException("String contains invalid UTF-8.", ex);
            }
        }

        public int ReadCount(int maxCount, bool exact = false)
        {
            int count = ReadByte();
            if (count > maxCount || (exact && count != maxCount))
                throw new PacketFormatException("Collection count is invalid.");
            return count;
        }

        public void EnsureEnd()
        {
            if (Remaining != 0)
                throw new PacketFormatException("Trailing bytes after packet payload.");
        }
    }
}
