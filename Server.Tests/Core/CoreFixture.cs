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

// The server's wall clock in tests. It stands still unless a test moves it, so inputs, ticks and the checks they send
// share one instant and sync presses are timed in simulated time only (SyncTimingTests moves it).
internal sealed class TestClock
{
    internal double Now;
    // When set, the clock jumps here right after the next read (time spent inside a tick before a packet leaves).
    internal double? Then;
    internal double Read()
    {
        double value = Now;
        if (Then is double then) { Now = then; Then = null; }
        return value;
    }
}

internal sealed class CoreFixture : IDisposable
{
    // seed is the private-RNG test hook; csprng = true leaves it unset like the console host and offline client.
    internal CoreFixture(int bots = 0, bool autostart = false, int seed = 1234, bool sandbox = false, int? matchSeed = null, bool csprng = false, bool offline = true)
    {
        Server = new GameServer(new ServerOptions
        {
            IsOffline = offline, Seed = csprng ? null : seed, BotCount = bots, AutoStart = autostart, Sandbox = sandbox,
            MatchSeedForTests = matchSeed, ClockForTests = Clock.Read
        });
        Transport = new InMemoryTransport();
        Transport.ConnectionAccepted += Server.AddConnection;
        Transport.Start();
    }
    internal GameServer Server { get; }
    internal InMemoryTransport Transport { get; }
    internal TestClock Clock { get; } = new();
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
        // Rule/input fixtures use an explicit open lane; production spawns are validated in MapV2Tests.
        Server.PlaceForTests(killer.Id, 5, 5);
        Server.PlaceForTests(survivor.Id, 5, 26);
        Tick();
        return (killer, survivor);
    }
    // StartPair on the first private seed whose killer skill is not the passive Packet Afterimage, for tests that
    // expect a stopped survivor to leave the killer frame at once.
    internal static (CoreFixture f, Peer killer, Peer survivor) PairWithoutAfterimage()
    {
        for (int seed = 1; ; seed++)
        {
            var f = new CoreFixture(seed: seed);
            var (killer, survivor) = f.StartPair();
            if (killer.Last<S2C_MatchStart>().SkillId != SkillIds.PacketAfterimage) return (f, killer, survivor);
            f.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var peer in _peers) peer.Dispose();
        Transport.Dispose();
        Server.Dispose();
    }
}
