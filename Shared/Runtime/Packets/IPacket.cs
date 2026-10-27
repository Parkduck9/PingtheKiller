using PingTheKiller.Shared.Net;

namespace PingTheKiller.Shared.Packets
{
    public interface IPacket
    {
        PacketId Id { get; }
        void Write(PacketWriter w);
    }
}
