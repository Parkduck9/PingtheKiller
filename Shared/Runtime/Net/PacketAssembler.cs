using System;
using System.Buffers.Binary;

namespace PingTheKiller.Shared.Net
{
    public enum AssembleResult { Packet, NeedMoreData, Malformed }

    public sealed class PacketAssembler
    {
        private readonly int _maxBodyLength;
        private byte[] _buffer;
        private int _readOffset;
        private int _writeOffset;

        public PacketAssembler(int maxBodyLength = Protocol.MaxBodyLength, int initialCapacity = 4096)
        {
            if (maxBodyLength < Protocol.IdSize || maxBodyLength > Protocol.MaxBodyLength)
                throw new ArgumentOutOfRangeException(nameof(maxBodyLength));
            if (initialCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            _maxBodyLength = maxBodyLength;
            _buffer = new byte[Math.Min(initialCapacity, Protocol.HeaderSize + maxBodyLength)];
        }

        public bool IsFaulted { get; private set; }

        // Drain TryRead between chunks. Growth beyond the frame-plus-chunk cap is rejected.
        public void Append(ReadOnlySpan<byte> data)
        {
            if (IsFaulted || data.IsEmpty)
                return;

            int remaining = _writeOffset - _readOffset;
            long requiredLength = (long)remaining + data.Length;
            long limit = (long)Protocol.HeaderSize + _maxBodyLength + data.Length;
            if (requiredLength > int.MaxValue || requiredLength > limit)
            {
                IsFaulted = true;
                return;
            }
            // Compact only on Append: previously returned payloads must survive TryRead.
            if (_readOffset > _buffer.Length / 2 || data.Length > _buffer.Length - _writeOffset)
            {
                Buffer.BlockCopy(_buffer, _readOffset, _buffer, 0, remaining);
                _readOffset = 0;
                _writeOffset = remaining;
            }

            // If compaction was unnecessary, the sum already fits in the buffer.
            // Otherwise it equals requiredLength, which was range-checked above.
            int required = _writeOffset + data.Length;
            if (required > _buffer.Length)
            {
                long capacity = _buffer.Length;
                while (capacity < required)
                    capacity = Math.Min(capacity * 2, limit);
                Array.Resize(ref _buffer, (int)Math.Min(capacity, int.MaxValue));
            }
            data.CopyTo(_buffer.AsSpan(_writeOffset));
            _writeOffset = required;
        }

        public AssembleResult TryRead(out ushort packetId, out ReadOnlyMemory<byte> payload)
        {
            packetId = 0;
            payload = default;
            if (IsFaulted)
                return AssembleResult.Malformed;
            int available = _writeOffset - _readOffset;
            if (available < Protocol.HeaderSize)
                return AssembleResult.NeedMoreData;

            int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(_readOffset, Protocol.HeaderSize));
            if (bodyLength < Protocol.IdSize || bodyLength > _maxBodyLength)
            {
                IsFaulted = true;
                return AssembleResult.Malformed;
            }
            int frameLength = Protocol.HeaderSize + bodyLength;
            if (available < frameLength)
                return AssembleResult.NeedMoreData;

            int bodyOffset = _readOffset + Protocol.HeaderSize;
            packetId = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(bodyOffset, Protocol.IdSize));
            payload = _buffer.AsMemory(bodyOffset + Protocol.IdSize, bodyLength - Protocol.IdSize);
            _readOffset += frameLength;
            return AssembleResult.Packet;
        }
    }
}
