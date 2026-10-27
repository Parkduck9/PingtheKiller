using System;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.ServerCore.Transport
{
    public interface IConnection : IDisposable
    {
        string RemoteId { get; }
        bool IsClosed { get; }
        event Action PacketAvailable;
        event Action Closed;
        bool TryReceive(out IPacket packet);
        // A frame is immutable after Send. Implementations never block on network I/O.
        void Send(byte[] frame);
        void Close();
    }

    public interface ITransportListener : IDisposable
    {
        event Action<IConnection> ConnectionAccepted;
        void Start();
        void Stop();
    }
}
