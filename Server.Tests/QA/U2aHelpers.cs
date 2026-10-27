using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Test-local helpers for the independent U2a QA suite. They use only the public API of Server.Core / Shared.

internal sealed class QaPeer : IDisposable
{
    internal QaPeer(IConnection connection) { Connection = connection; }
    internal IConnection Connection { get; }
    internal List<IPacket> Packets { get; } = new();
    internal uint Seq;

    internal int Id => Last<S2C_JoinResult>().PlayerId;
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
    internal bool Has<T>() where T : IPacket
    {
        Drain();
        return Packets.OfType<T>().Any();
    }
    internal int Mark()
    {
        Drain();
        return Packets.Count;
    }
    internal List<T> Since<T>(int mark) where T : IPacket
    {
        Drain();
        return Packets.Skip(mark).OfType<T>().ToList();
    }
    internal S2C_WorldState World() => Last<S2C_WorldState>();
    internal PlayerState Self() => World().Players.Single(p => p.Id == Id);
    internal S2C_LobbyState Lobby() => Last<S2C_LobbyState>();
    internal void Move(float x, float z, float yaw = 0) =>
        Send(new C2S_MoveInput { DirX = x, DirZ = z, Yaw = yaw, Seq = ++Seq });
    public void Dispose() => Connection.Dispose();
}

internal sealed class QaFixture : IDisposable
{
    internal QaFixture(int bots = 0, bool autostart = false, int seed = 1234)
    {
        Server = new GameServer(new ServerOptions { IsOffline = true, Seed = seed, BotCount = bots, AutoStart = autostart });
        Transport = new InMemoryTransport();
        Transport.ConnectionAccepted += Server.AddConnection;
        Transport.Start();
    }

    internal GameServer Server { get; }
    internal InMemoryTransport Transport { get; }
    private readonly List<QaPeer> _peers = new();

    internal QaPeer Connect()
    {
        var peer = new QaPeer(Transport.Connect());
        _peers.Add(peer);
        return peer;
    }

    internal QaPeer Join(string name = "Human", ulong token = 0, int expectedResult = 0)
    {
        var peer = Connect();
        peer.Send(new C2S_Join { Nickname = name, ReconnectToken = token });
        Tick();
        Assert.Equal(expectedResult, peer.Last<S2C_JoinResult>().Result);
        return peer;
    }

    internal void Tick(float dt = 0.05f) => Server.Tick(dt);
    internal void Ticks(int count, float dt = 0.05f)
    {
        for (int i = 0; i < count; i++) Server.Tick(dt);
    }

    // Killer joins first (host, spawn index 0 = (5,5)); survivor second (spawn index 1 = (5,10)).
    internal (QaPeer killer, QaPeer survivor) StartPair()
    {
        var killer = Join("Killer");
        var survivor = Join("Survivor");
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        Tick();
        Assert.Equal((byte)Role.Killer, killer.Last<S2C_MatchStart>().Role);
        Assert.Equal((byte)Role.Survivor, survivor.Last<S2C_MatchStart>().Role);
        return (killer, survivor);
    }

    public void Dispose()
    {
        foreach (var peer in _peers) peer.Dispose();
        Transport.Dispose();
        Server.Dispose();
    }
}

internal static class QaBytes
{
    internal static bool ContainsFloat(byte[] frame, float value)
    {
        Span<byte> needle = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(needle, value);
        return frame.AsSpan().IndexOf(needle) >= 0;
    }

    // Floats that legitimately appear in a WorldState frame (so a coincidental byte match is not a leak).
    internal static HashSet<int> LegitFloatBits(S2C_WorldState w)
    {
        var set = new HashSet<int> { BitConverter.SingleToInt32Bits(w.TimeLeft) };
        foreach (var t in w.Terminals)
        {
            set.Add(BitConverter.SingleToInt32Bits(t.X));
            set.Add(BitConverter.SingleToInt32Bits(t.Z));
        }
        foreach (var p in w.Players)
        {
            set.Add(BitConverter.SingleToInt32Bits(p.X));
            set.Add(BitConverter.SingleToInt32Bits(p.Z));
            set.Add(BitConverter.SingleToInt32Bits(p.Yaw));
            set.Add(BitConverter.SingleToInt32Bits(p.StatusTimer));
        }
        return set;
    }

    // Asserts the entity (hiddenId at hiddenX/hiddenZ) is wholly absent from this encoded frame.
    internal static void AssertAbsent(S2C_WorldState w, int hiddenId, float hiddenX, float hiddenZ, string context)
    {
        Assert.False(w.Players.Any(p => p.Id == hiddenId), context + ": entry for hidden id " + hiddenId + " present");
        byte[] frame = PacketCodec.Encode(w);
        var legit = LegitFloatBits(w);
        foreach (float v in new[] { hiddenX, hiddenZ })
        {
            if (legit.Contains(BitConverter.SingleToInt32Bits(v))) continue;
            Assert.False(ContainsFloat(frame, v), context + ": float bytes of hidden coordinate " + v + " found in frame");
        }
    }
}

// Minimal raw TCP client speaking the wire format via the shared assembler.
internal sealed class RawClient : IDisposable
{
    private readonly PacketAssembler _assembler = new();
    private RawClient(TcpClient client) { Client = client; Stream = client.GetStream(); }
    internal TcpClient Client { get; }
    internal NetworkStream Stream { get; }
    internal List<IPacket> Received { get; } = new();

    internal static async Task<RawClient> ConnectAsync(int port, int receiveBuffer = 0)
    {
        var client = new TcpClient { NoDelay = true };
        if (receiveBuffer > 0) client.ReceiveBufferSize = receiveBuffer;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        return new RawClient(client);
    }

    internal async Task SendAsync(byte[] bytes)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Stream.WriteAsync(bytes, cts.Token);
    }

    internal Task SendAsync(IPacket packet) => SendAsync(PacketCodec.Encode(packet));

    internal async Task SendByteByByteAsync(byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            await SendAsync(new[] { b });
            await Task.Delay(1);
        }
    }

    // Reads until a packet of type T is seen (all packets are kept in Received). Bounded by `timeout`.
    internal async Task<T> ReceiveAsync<T>(TimeSpan? timeout = null) where T : IPacket
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                while (true)
                {
                    var result = _assembler.TryRead(out ushort id, out var payload);
                    if (result == AssembleResult.NeedMoreData) break;
                    Assert.Equal(AssembleResult.Packet, result);
                    var packet = PacketCodec.Decode(id, payload.Span);
                    Received.Add(packet);
                    if (packet is T typed) return typed;
                }
                int count = await Stream.ReadAsync(buffer, cts.Token);
                if (count == 0) throw new EndOfStreamException("server closed the connection");
                _assembler.Append(buffer.AsSpan(0, count));
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("no " + typeof(T).Name + " within the bound");
        }
    }

    // True if the server closes this connection (EOF, reset) within the bound.
    internal async Task<bool> WaitClosedAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                int count = await Stream.ReadAsync(buffer, cts.Token);
                if (count == 0) return true;
            }
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return true; }
        catch (SocketException) { return true; }
        catch (ObjectDisposedException) { return true; }
    }

    public void Dispose() => Client.Dispose();
}
