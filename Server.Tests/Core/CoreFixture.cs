using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

internal sealed class Peer : IDisposable
{
    internal Peer(IConnection connection) { Connection = connection; }
    internal IConnection Connection { get; }
    internal List<IPacket> Packets { get; } = new();
    internal int Id => Last<S2C_JoinResult>().PlayerId;
    internal uint NextSeq = 1;
    // One MoveInput with the next sequence number (clients resend while a key is held).
    internal void Move(float x, float z) => Send(new C2S_MoveInput { DirX = x, DirZ = z, Seq = NextSeq++ });
    internal void Send(IPacket packet) => Connection.Send(PacketCodec.Encode(packet));
    internal void Drain()
    {
        while (Connection.TryReceive(out var packet)) Packets.Add(packet);
    }
    internal T Last<T>() where T : IPacket
    {
        Drain();
        return Packets.OfType<T>().Last();
    }
    internal PlayerState Self() => Last<S2C_WorldState>().Players.Single(p => p.Id == Id);
    public void Dispose() => Connection.Dispose();
}

internal sealed class CoreFixture : IDisposable
{
    internal CoreFixture(int bots = 0, bool autostart = false, int seed = 1234)
    {
        Server = new GameServer(new ServerOptions { IsOffline = true, Seed = seed, BotCount = bots, AutoStart = autostart });
        Transport = new InMemoryTransport();
        Transport.ConnectionAccepted += Server.AddConnection;
        Transport.Start();
    }
    internal GameServer Server { get; }
    internal InMemoryTransport Transport { get; }
    private readonly List<Peer> _peers = new();
    internal Peer Connect()
    {
        var peer = new Peer(Transport.Connect());
        _peers.Add(peer);
        return peer;
    }
    internal Peer Join(string name = "Human", ulong token = 0)
    {
        var peer = Connect();
        peer.Send(new C2S_Join { Nickname = name, ReconnectToken = token });
        Tick();
        Assert.Equal(0, peer.Last<S2C_JoinResult>().Result);
        return peer;
    }
    internal void Tick(float dt = 0.05f) => Server.Tick(dt);
    // Holds a direction for the given number of 20 Hz ticks, resending the input every tick like a real client.
    internal void Walk(Peer peer, float x, float z, int ticks)
    {
        for (int i = 0; i < ticks; i++) { peer.Move(x, z); Tick(); }
    }
    internal (Peer killer, Peer survivor) StartPair()
    {
        var killer = Join("Killer");
        var survivor = Join("Survivor");
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        Tick();
        return (killer, survivor);
    }
    public void Dispose()
    {
        foreach (var peer in _peers) peer.Dispose();
        Transport.Dispose();
        Server.Dispose();
    }
}
