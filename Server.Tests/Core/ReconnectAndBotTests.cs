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
        Assert.False(KillerTruth.Has(f.Server, killer.Last<S2C_WorldState>(), identity.PlayerId));
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
        // The bot opens with a Silent pause of up to 8 s (and may cast Stealth Relay), so wait for its first visible frame.
        // U6d: a bot that sees the (idle) killer from beyond ThreatLevel 1 hides in place, Silent, until the camp rule
        // releases it 20 s after the last news about the killer, so allow up to 60 s.
        PlayerState? bot = null;
        for (int i = 0; i < 1200 && bot == null; i++)
        {
            f.Tick();
            bot = KillerTruth.Entry(f.Server, killer.Last<S2C_WorldState>(), identity.PlayerId);
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
        var truthBefore = f.Server.PlayerSnapshot().Single(p => p.Id == botId);
        var goal = host.Last<S2C_WorldState>().Terminals
            .OrderBy(t => (t.X - truthBefore.X) * (t.X - truthBefore.X) + (t.Z - truthBefore.Z) * (t.Z - truthBefore.Z)).First();
        bool hacked = false;
        // The bot pauses Silently between bursts (opening pause up to 8 s), so allow it up to 30 s to start hacking.
        for (int i = 0; i < 600 && !hacked; ++i)
        {
            f.Tick();
            var world = host.Last<S2C_WorldState>();
            var truth = f.Server.PlayerSnapshot().Single(p => p.Id == botId);
            // Same movement rules as humans: at most 4 m/s per tick, and never moving while absent from the killer frame.
            Assert.InRange(MathF.Sqrt((truth.X - truthBefore.X) * (truth.X - truthBefore.X) + (truth.Z - truthBefore.Z) * (truth.Z - truthBefore.Z)), 0, 0.201f);
            var state = KillerTruth.Entry(f.Server, world, botId);
            truthBefore = truth;
            hacked = truth.Hacking;
            if (!state.HasValue) continue;
            if (previous.HasValue)
            {
                float dx = state.Value.X - previous.Value.X, dz = state.Value.Z - previous.Value.Z;
                Assert.InRange(MathF.Sqrt(dx * dx + dz * dz), 0, 0.201f * (world.Tick - previousTick));
            }
            previous = state;
            previousTick = (int)world.Tick;
        }
        // The bot walks to the placed terminal nearest its production spawn and hacks it.
        Assert.True(hacked, "the bot never started hacking");
        var final = f.Server.PlayerSnapshot().Single(p => p.Id == botId);
        Assert.True(MathF.Sqrt((final.X - goal.X) * (final.X - goal.X) + (final.Z - goal.Z) * (final.Z - goal.Z)) <= GameConstants.TerminalRadius);
        Assert.NotNull(previous);
        Assert.InRange(MathF.Sqrt(MathF.Pow(previous.Value.X - goal.X, 2) + MathF.Pow(previous.Value.Z - goal.Z, 2)), 0, GameConstants.TerminalRadius);
    }
}
