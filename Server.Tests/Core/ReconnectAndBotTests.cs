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
        f.Tick();
        var bot = killer.Last<S2C_WorldState>().Players.Single(p => p.Id == identity.PlayerId);
        Assert.True(bot.X != before.X || bot.Z != before.Z);
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
    public void SurvivorBotWalksAndPausesForOneToThreeSecondsUsingSameMovementRules()
    {
        using var f = new CoreFixture();
        var host = f.Join();
        host.Send(new C2S_SetBots { Count = 1 });
        host.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        host.Send(new C2S_StartMatch());
        f.Tick();
        int botId = host.Last<S2C_LobbyState>().Slots.Single(s => s.IsBot != 0).PlayerId;
        var first = host.Last<S2C_WorldState>().Players.Single(s => s.Id == botId);
        PlayerState? previous = first;
        int silentTicks = 0;
        bool sawPause = false, sawResume = false;
        for (int i = 0; i < 500; ++i)
        {
            f.Tick();
            var world = host.Last<S2C_WorldState>();
            var state = world.Players.Where(s => s.Id == botId).Select(s => (PlayerState?)s).SingleOrDefault();
            if (!state.HasValue) { ++silentTicks; sawPause = true; }
            else
            {
                if (silentTicks > 0)
                {
                    Assert.InRange(silentTicks, 17, 62); // Signal tail removes up to 0.15 s of the pause.
                    sawResume = true;
                    break;
                }
                if (previous.HasValue)
                {
                    float dx = state.Value.X - previous.Value.X, dz = state.Value.Z - previous.Value.Z;
                    Assert.InRange(MathF.Sqrt(dx * dx + dz * dz), 0, 0.201f);
                }
                previous = state;
            }
        }
        Assert.True(sawPause);
        Assert.True(sawResume);
    }
}
