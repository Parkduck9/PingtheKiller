using System;
using System.Collections.Concurrent;
using System.Threading;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore.Transport
{
    public abstract class FramedConnection : IConnection
    {
        private const int MaxPendingPackets = 4096;
        private readonly ConcurrentQueue<IPacket> _received = new ConcurrentQueue<IPacket>();
        private readonly PacketAssembler _assembler = new PacketAssembler();
        private readonly object _receiveGate = new object();
        private int _pendingPackets;
        private int _closed;

        protected FramedConnection(string remoteId) { RemoteId = remoteId; }
        public string RemoteId { get; }
        public bool IsClosed => Volatile.Read(ref _closed) != 0;
        public event Action PacketAvailable;
        public event Action Closed;

        public bool TryReceive(out IPacket packet)
        {
            if (!_received.TryDequeue(out packet)) return false;
            Interlocked.Decrement(ref _pendingPackets);
            return true;
        }

        protected void ReceiveBytes(ReadOnlySpan<byte> bytes)
        {
            lock (_receiveGate)
            {
                if (IsClosed) return;
                _assembler.Append(bytes);
                while (!IsClosed)
                {
                    var result = _assembler.TryRead(out ushort id, out var payload);
                    if (result == AssembleResult.NeedMoreData) break;
                    if (result == AssembleResult.Malformed) { Close(); break; }
                    IPacket packet;
                    try { packet = PacketCodec.Decode(id, payload.Span); }
                    catch (PacketFormatException) { Close(); break; }
                    if (Interlocked.Increment(ref _pendingPackets) > MaxPendingPackets)
                    {
                        Interlocked.Decrement(ref _pendingPackets);
                        Close();
                        break;
                    }
                    _received.Enqueue(packet);
                    PacketAvailable?.Invoke();
                }
            }
        }

        public abstract void Send(byte[] frame);
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            OnClose();
            Closed?.Invoke();
        }
        protected abstract void OnClose();
        public void Dispose() => Close();
    }
}
