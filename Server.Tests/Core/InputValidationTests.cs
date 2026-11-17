using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// Hands packet objects straight to the server, bypassing the codec, so semantically invalid values reach the rules.
internal sealed class DirectConnection : IConnection
{
    private readonly ConcurrentQueue<IPacket> _incoming = new();
    private readonly List<IPacket> _sent = new();
    private int _closed;
    public string RemoteId { get; } = "direct:" + Guid.NewGuid().ToString("N");
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public event Action? PacketAvailable;
    public event Action? Closed;
    public bool TryReceive(out IPacket packet)
    {
        bool received = _incoming.TryDequeue(out var next);
        packet = next!;
        return received;
    }
    public void Push(IPacket packet)
    {
        _incoming.Enqueue(packet);
        PacketAvailable?.Invoke();
    }
    public void Send(byte[] frame)
    {
        var packet = PacketCodec.Decode(BitConverter.ToUInt16(frame, 4), frame.AsSpan(6));
        lock (_sent) _sent.Add(packet);
    }
    internal List<T> All<T>() where T : IPacket
    {
        lock (_sent) return _sent.OfType<T>().ToList();
    }
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0) Closed?.Invoke();
    }
    public void Dispose() => Close();
}

public sealed class InputValidationTests
{
    [Fact]
    public void InvalidEnumBytesAndNonFiniteFloatsAreIgnoredWithoutThrowingOrDisconnecting()
    {
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = 77 });
        var host = new DirectConnection();
        server.AddConnection(host);
        host.Push(new C2S_Join { Nickname = "Host" });
        server.Tick(0.05f);
        int hostId = host.All<S2C_JoinResult>().Single().PlayerId;

        host.Push(new C2S_SetBots { Count = 200 });
        host.Push(new C2S_SetRole { RoleChoice = 7 });
        server.Tick(0.05f);
        Assert.DoesNotContain(host.All<S2C_LobbyState>().Last().Slots, s => s.IsBot != 0);
        Assert.Equal((byte)RoleChoice.Random, host.All<S2C_LobbyState>().Last().HostRoleChoice);
        host.Push(new C2S_SetBots { Count = 1 });
        host.Push(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
        host.Push(new C2S_StartMatch());
        server.Tick(0.05f);
        Assert.Equal((byte)Role.Survivor, host.All<S2C_MatchStart>().Single().Role);
        byte skill = host.All<S2C_MatchStart>().Single().SkillId;

        // Spawn (5,26) is 5.1 m from terminal 0; walk to (10,26) with valid input.
        server.PlaceForTests(hostId,5,26);
        server.Tick(0.05f);
        uint seq = 1;
        for (int i = 0; i < 25; i++)
        {
            var self = host.All<S2C_WorldState>().Last().Players.Single(p => p.Id == hostId);
            float dx = 10 - self.X;
            host.Push(new C2S_MoveInput { DirX = Math.Clamp(dx / 0.2f, -1, 1), Seq = seq++ });
            server.Tick(0.05f);
        }
        host.Push(new C2S_MoveInput { Seq = seq++ });
        server.Tick(0.05f);
        var start = host.All<S2C_WorldState>().Last().Players.Single(p => p.Id == hostId);
        Assert.InRange(start.X, 9.99f, 10.01f);

        foreach (byte active in new byte[] { 2, 255 })
        {
            host.Push(new C2S_Interact { Active = active });
            server.Tick(0.05f);
        }
        server.Tick(0.05f);
        Assert.Equal(0, host.All<S2C_WorldState>().Last().Terminals[0].Progress);
        foreach (byte id in new byte[] { 5, 200, 255 })
        {
            host.Push(new C2S_UseSkill { SkillId = id });
            server.Tick(0.05f);
        }
        Assert.Empty(host.All<S2C_SkillEffect>());
        var bad = new[]
        {
            new C2S_MoveInput { DirX = float.NaN, DirZ = 1, Yaw = 10 },
            new C2S_MoveInput { DirX = 1, DirZ = float.NaN, Yaw = 10 },
            new C2S_MoveInput { DirX = 1, DirZ = 0, Yaw = float.NaN },
            new C2S_MoveInput { DirX = float.PositiveInfinity, DirZ = float.NegativeInfinity, Yaw = float.PositiveInfinity },
            new C2S_MoveInput { DirX = float.NaN, DirZ = float.NaN, Yaw = float.NaN }
        };
        foreach (var move in bad)
        {
            move.Seq = seq++;
            host.Push(move);
            server.Tick(0.05f);
            var self = host.All<S2C_WorldState>().Last().Players.Single(p => p.Id == hostId);
            Assert.True(float.IsFinite(self.X) && float.IsFinite(self.Z) && float.IsFinite(self.Yaw));
        }
        // A non-finite direction stops instead of moving; only the finite (1,0) with NaN yaw moved one tick, yaw 0.
        var after = host.All<S2C_WorldState>().Last().Players.Single(p => p.Id == hostId);
        Assert.InRange(after.X - start.X, 0.19f, 0.21f);
        Assert.Equal(0f, after.Yaw);
        Assert.False(host.IsClosed);

        // The valid value still works afterwards: the same connection hacks once Active = 1.
        host.Push(new C2S_MoveInput { DirX = -1, Seq = seq++ });
        server.Tick(0.05f);
        host.Push(new C2S_MoveInput { DirX = -1, Seq = seq++ });
        server.Tick(0.05f);
        host.Push(new C2S_MoveInput { Seq = seq++ });
        host.Push(new C2S_Interact { Active = 1 });
        // GDD v2 (11/17 R2): 90 s passive per terminal, so 1 % takes 0.9 s.
        for (int i = 0; i < 22; i++) server.Tick(0.05f);
        Assert.True(host.All<S2C_WorldState>().Last().Terminals[0].Progress > 0 ||
            host.All<S2C_WorldState>().Last().Players.Single(p => p.Id == hostId).Status != (byte)PlayerStatus.Normal);
        host.Push(new C2S_UseSkill { SkillId = skill });
        server.Tick(0.05f);
        Assert.False(host.IsClosed);
    }

    [Fact]
    public void BindAddressLoopbackListensOnlyOnLoopback()
    {
        using var transport = new TcpTransport(0, IPAddress.Loopback);
        transport.Start();
        Assert.Equal(IPAddress.Loopback, transport.Address);
        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, transport.Port);
            Assert.True(client.Connected);
        }
        IPAddress? external = null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                    external ??= unicast.Address;
        }
        if (external == null) return; // no non-loopback interface on this machine
        using var outside = new TcpClient();
        var error = Assert.ThrowsAny<SocketException>(() => outside.Connect(external, transport.Port));
        Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
    }
}
