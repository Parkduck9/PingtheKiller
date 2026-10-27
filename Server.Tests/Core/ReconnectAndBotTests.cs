using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class ReconnectAndBotTests
{
    [Fact]
    public void ReconnectRestoresIdTokenRoleSkillAndPositionAndResetsSequence()
    {
        using var f = new CoreFixture();
        var (killer, survivor) = f.StartPair();
        var identity = survivor.Last<S2C_JoinResult>();
        var match = survivor.Last<S2C_MatchStart>();
        survivor.Send(new C2S_MoveInput { DirX = 1, Seq = 100 });
        f.Tick();
        var position = survivor.Self();
        survivor.Dispose();
        f.Tick();
        for (int i = 0; i < 99; i++) f.Tick();
        Assert.DoesNotContain(killer.Last<S2C_WorldState>().Players, p => p.Id == identity.PlayerId);
        var restored = f.Join("Changed name", identity.ReconnectToken);
        Assert.Equal(identity.PlayerId, restored.Id);
        Assert.Equal(identity.ReconnectToken, restored.Last<S2C_JoinResult>().ReconnectToken);
        Assert.Equal(match.Role, restored.Last<S2C_MatchStart>().Role);
        Assert.Equal(match.SkillId, restored.Last<S2C_MatchStart>().SkillId);
        Assert.Equal(match.Seed, restored.Last<S2C_MatchStart>().Seed);
        Assert.Equal(position.X, restored.Self().X);
        Assert.Equal(position.Z, restored.Self().Z);
        restored.Send(new C2S_MoveInput { DirX = 1, Seq = 1 });
        f.Tick();
        Assert.True(restored.Self().X > position.X);
    }

    [Fact]
    public void TenSecondDeadlineReplacesHumanWithBotAndRejectsExpiredToken()
    {
        using var f = new CoreFixture();
        var (killer, survivor) = f.StartPair();
        var identity = survivor.Last<S2C_JoinResult>();
        var before = survivor.Self();
        survivor.Dispose();
        f.Tick();
        for (int i = 0; i < 198; i++) f.Tick();
        Assert.Equal(0, killer.Last<S2C_LobbyState>().Slots.Single(p => p.PlayerId == identity.PlayerId).IsBot);
        f.Tick();
        Assert.Equal(1, killer.Last<S2C_LobbyState>().Slots.Single(p => p.PlayerId == identity.PlayerId).IsBot);
        // The bot may open with Stealth Relay (hidden for 2 s), so wait for its first visible frame.
        PlayerState? bot = null;
        for (int i = 0; i < 60 && bot == null; i++)
        {
            f.Tick();
            bot = killer.Last<S2C_WorldState>().Players.Where(p => p.Id == identity.PlayerId).Select(p => (PlayerState?)p).SingleOrDefault();
        }
        Assert.NotNull(bot);
        Assert.True(bot.Value.X != before.X || bot.Value.Z != before.Z);
        var expired = f.Connect();
        expired.Send(new C2S_Join { ReconnectToken = identity.ReconnectToken });
        f.Tick();
        Assert.NotEqual(0, expired.Last<S2C_JoinResult>().Result);
    }

    [Fact]
    public void UnknownTokenIsRejectedAndHostTransfersAfterTimeout()
    {
        using var f = new CoreFixture();
        var host = f.Join();
        var guest = f.Join();
        var attacker = f.Connect();
        ulong hostToken = host.Last<S2C_JoinResult>().ReconnectToken;
        ulong guestToken = guest.Last<S2C_JoinResult>().ReconnectToken;
        ulong forged = hostToken ^ 1;
        if (forged == guestToken) forged ^= 2;
        attacker.Send(new C2S_Join { ReconnectToken = forged });
        f.Tick();
        Assert.NotEqual(0, attacker.Last<S2C_JoinResult>().Result);
        Assert.False(host.Connection.IsClosed);
        Assert.False(guest.Connection.IsClosed);
        int hostId = host.Id;
        host.Dispose();
        f.Tick(10);
        Assert.Equal(1, guest.Last<S2C_LobbyState>().Slots.Single(s => s.PlayerId == hostId).IsBot);
        Assert.Equal(1, guest.Last<S2C_LobbyState>().Slots.Single(s => s.PlayerId == guest.Id).IsHost);
        guest.Send(new C2S_SetBots { Count = 3 });
        f.Tick();
        Assert.Equal(3, guest.Last<S2C_LobbyState>().Slots.Count(s => s.IsBot != 0));
    }

    [Fact]
    public void SurvivorBotWalksToNearestTerminalAndHacksItUsingSameMovementRules()
    {
        using var f = new CoreFixture();
        var host = f.Join();
        host.Send(new C2S_SetBots { Count = 1 });
        host.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        host.Send(new C2S_StartMatch());
        f.Tick();
        int botId = host.Last<S2C_LobbyState>().Slots.Single(s => s.IsBot != 0).PlayerId;
        PlayerState? previous = null;
        int previousTick = 0;
        for (int i = 0; i < 200; ++i)
        {
            f.Tick();
            var world = host.Last<S2C_WorldState>();
            var state = world.Players.Where(s => s.Id == botId).Select(s => (PlayerState?)s).SingleOrDefault();
            if (!state.HasValue) continue;
            if (previous.HasValue)
            {
                // Same movement rules as humans: at most 4 m/s between visible frames.
                float dx = state.Value.X - previous.Value.X, dz = state.Value.Z - previous.Value.Z;
                Assert.InRange(MathF.Sqrt(dx * dx + dz * dz), 0, 0.201f * (world.Tick - previousTick));
            }
            previous = state;
            previousTick = (int)world.Tick;
        }
        // Spawn (5,26): the nearest terminal is #0 at (10,25); after 10 s the bot stands there holding Interact.
        var final = host.Last<S2C_WorldState>();
        Assert.InRange(final.Terminals[0].Progress, 50, 99);
        Assert.NotNull(previous);
        Assert.InRange(MathF.Sqrt(MathF.Pow(previous.Value.X - 10, 2) + MathF.Pow(previous.Value.Z - 25, 2)), 0, GameConstants.TerminalRadius);
    }
}
