using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class LobbyTests
{
    [Fact]
    public void JoinSanitizesNamesAndOnlyHostCanChangeLobbyOrStart()
    {
        using var f = new CoreFixture();
        var host = f.Join(" \t\n ");
        var guest = f.Join(" \tGu\nest\0 ");
        Assert.NotEqual(host.Id, guest.Id);
        Assert.NotEqual(0UL, host.Last<S2C_JoinResult>().ReconnectToken);
        Assert.NotEqual(host.Last<S2C_JoinResult>().ReconnectToken, guest.Last<S2C_JoinResult>().ReconnectToken);
        var lobby = host.Last<S2C_LobbyState>();
        Assert.Equal(8, lobby.Slots.Length);
        Assert.Equal(1, lobby.IsOffline);
        Assert.Equal("Player " + host.Id, lobby.Slots[0].Nickname);
        Assert.Equal("Guest", lobby.Slots[1].Nickname);
        Assert.Equal(1, lobby.Slots[0].IsHost);
        Assert.Equal(0, lobby.Slots[1].IsHost);
        guest.Send(new C2S_SetBots { Count = 7 });
        guest.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        guest.Send(new C2S_StartMatch());
        f.Tick();
        Assert.Equal((byte)RoleChoice.Random, host.Last<S2C_LobbyState>().HostRoleChoice);
        Assert.DoesNotContain(host.Last<S2C_LobbyState>().Slots, p => p.IsBot != 0);
        host.Drain();
        Assert.Empty(host.Packets.OfType<S2C_MatchStart>());
        host.Send(new C2S_SetBots { Count = 7 });
        f.Tick();
        Assert.Equal(6, host.Last<S2C_LobbyState>().Slots.Count(p => p.IsBot == 1));
        host.Send(new C2S_SetBots { Count = 0 });
        f.Tick();
        Assert.Equal(2, host.Last<S2C_LobbyState>().Slots.Count(p => p.PlayerId != 0));
    }

    [Fact]
    public void BotCountClampsToMinimumAndMaximumAndSingleHumanCannotStartAlone()
    {
        using var f = new CoreFixture();
        var host = f.Join();
        host.Send(new C2S_StartMatch());
        f.Tick();
        host.Drain();
        Assert.Empty(host.Packets.OfType<S2C_MatchStart>());
        host.Send(new C2S_SetBots { Count = 0 });
        f.Tick();
        Assert.Single(host.Last<S2C_LobbyState>().Slots, p => p.IsBot == 1);
        host.Send(new C2S_SetBots { Count = 7 });
        f.Tick();
        Assert.Equal(7, host.Last<S2C_LobbyState>().Slots.Count(p => p.IsBot == 1));
        host.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        host.Send(new C2S_StartMatch());
        f.Tick();
        Assert.Equal((byte)Role.Killer, host.Last<S2C_MatchStart>().Role);
        var world = host.Last<S2C_WorldState>();
        // Bots open with a Silent pause, so the killer frame may omit them; the match itself has all 8 players.
        Assert.Equal(8, f.Server.PlayerSnapshot().Count);
        Assert.InRange(world.Players.Count, 1, 8);
        Assert.Single(world.Players, p => p.Role == (byte)Role.Killer);
        Assert.InRange(host.Last<S2C_MatchStart>().SkillId, 0, 3);
        Assert.Equal(8, world.Terminals.Count);
        Assert.Equal(5, world.TerminalsRequired);
        Assert.All(world.Terminals, t => Assert.Equal(0, t.Progress));
        Assert.False(world.Powered());
        Assert.False(world.AnyOpen());
    }

    [Theory]
    [InlineData(RoleChoice.Killer)]
    [InlineData(RoleChoice.Survivor)]
    [InlineData(RoleChoice.Random)]
    public void AllHumansReceiveMatchingSeedAndExactlyOneKiller(RoleChoice choice)
    {
        using var f = new CoreFixture();
        var host = f.Join();
        var guest = f.Join();
        host.Send(new C2S_SetRole { RoleChoice = (byte)choice });
        host.Send(new C2S_StartMatch());
        f.Tick();
        var a = host.Last<S2C_MatchStart>();
        var b = guest.Last<S2C_MatchStart>();
        // The wire seed is cosmetic and independent of the private gameplay seed (1234).
        Assert.NotEqual(1234, a.Seed);
        Assert.Equal(a.Seed, b.Seed);
        Assert.NotEqual(a.Role, b.Role);
        if (choice != RoleChoice.Random) Assert.Equal((byte)choice, a.Role);
        Assert.InRange(a.SkillId, 0, a.Role == (byte)Role.Killer ? 3 : 4);
        Assert.InRange(b.SkillId, 0, b.Role == (byte)Role.Killer ? 3 : 4);
        Assert.Equal(5, host.Last<S2C_WorldState>().Terminals.Count);
        Assert.Equal(3, host.Last<S2C_WorldState>().TerminalsRequired);
    }

    [Fact]
    public void AutostartAllowsHumanToTakeBotSlotWithoutChangingMatch()
    {
        using var f = new CoreFixture(bots: 8, autostart: true);
        f.Tick();
        var human = f.Join();
        Assert.NotNull(human.Last<S2C_MatchStart>());
        Assert.Equal(7, human.Last<S2C_LobbyState>().Slots.Count(p => p.IsBot != 0));
        Assert.Equal(1, human.Last<S2C_LobbyState>().Slots.Single(p => p.PlayerId == human.Id).IsHost);
        Assert.Contains(human.Last<S2C_WorldState>().Players, p => p.Id == human.Id);
    }

    [Fact]
    public void PingIsAnsweredWithoutWaitingForATick()
    {
        using var f = new CoreFixture();
        var peer = f.Connect();
        peer.Send(new C2S_Ping { Timestamp = long.MinValue });
        Assert.Equal(long.MinValue, peer.Last<S2C_Pong>().Timestamp);
    }

    [Fact]
    public void FullHumanLobbyRejectsNewJoinWithoutDisruptingExistingClients()
    {
        using var f = new CoreFixture();
        var host = f.Join();
        for (int i = 1; i < 8; ++i) f.Join();
        var ninth = f.Connect();
        ninth.Send(new C2S_Join { Nickname = "Ninth" });
        f.Tick();
        Assert.NotEqual(0, ninth.Last<S2C_JoinResult>().Result);
        Assert.Equal(8, host.Last<S2C_LobbyState>().Slots.Count(s => s.PlayerId != 0));
    }
}
