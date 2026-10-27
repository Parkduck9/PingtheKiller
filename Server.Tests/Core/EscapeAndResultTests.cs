using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class EscapeAndResultTests
{
    private static int IndexOfLast<T>(Peer peer) where T : IPacket
    {
        peer.Drain();
        return peer.Packets.FindLastIndex(p => p is T);
    }

    private static void AssertSingleResultThenLobby(Peer peer, Role winner, GameEndReason reason, params int[] escaped)
    {
        peer.Drain();
        var results = peer.All<S2C_GameResult>().ToList();
        Assert.Single(results);
        Assert.Equal((byte)winner, results[0].WinnerRole);
        Assert.Equal((byte)reason, results[0].Reason);
        Assert.Equal(escaped, results[0].EscapedIds);
        int result = peer.Packets.IndexOf(results[0]);
        Assert.True(IndexOfLast<S2C_WorldState>(peer) < result, "the final WorldState precedes the GameResult");
        Assert.True(IndexOfLast<S2C_LobbyState>(peer) > result, "LobbyState follows the GameResult");
    }

    // Walks toward a point until the match ends or the point is reached; returns ticks walked.
    private static int WalkUntilResult(RulesMatch m, Peer peer, float x, float z, float speed)
    {
        for (int i = 0; i < 2000; i++)
        {
            peer.Drain();
            if (peer.All<S2C_GameResult>().Any()) return i;
            var self = peer.Self();
            float dx = x - self.X, dz = z - self.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < 0.001f) return i;
            float step = speed / GameConstants.TickRate, scale = d > step ? 1 / d : 1 / step;
            peer.Move(dx * scale, dz * scale);
            m.Tick();
        }
        return 2000;
    }

    [Fact]
    public void NormalSurvivorWithinTwoMetresOfTheOpenExitWinsAndTheServerReturnsToTheLobby()
    {
        using var m = RulesMatch.Start(2);
        m.OpenExitWithTwoSurvivors();
        var a = m[0];
        int lobbiesBefore = m.Killer.All<S2C_LobbyState>().Count;
        WalkUntilResult(m, a, 57.5f, 30, GameConstants.SurvivorSpeed);
        var final = m[1].Last<S2C_WorldState>();
        Assert.Equal((byte)PlayerStatus.Escaped, RulesMatch.Entry(final, a.Id)!.Value.Status);
        Assert.InRange(RulesMatch.Distance(RulesMatch.Entry(final, a.Id)!.Value, 58, 30), 1.8f, 2.001f);
        Assert.Null(RulesMatch.Entry(m.Killer.Last<S2C_WorldState>(), a.Id));
        foreach (var peer in new[] { m.Killer, m[0], m[1] })
            AssertSingleResultThenLobby(peer, Role.Survivor, GameEndReason.SurvivorEscaped, a.Id);
        Assert.Equal(lobbiesBefore + 1, m.Killer.All<S2C_LobbyState>().Count());
        // The simulation stops: no more WorldStates, inputs are ignored, nothing is sent twice.
        int worlds = m.Killer.All<S2C_WorldState>().Count();
        a.Move(1, 0);
        m.Tick(20);
        m.Killer.Drain();
        Assert.Equal(worlds, m.Killer.All<S2C_WorldState>().Count());
        Assert.Single(m.Killer.All<S2C_GameResult>());
    }

    [Fact]
    public void FirewallBlockLocksTheOpenExitForFiveSeconds()
    {
        using var m = RulesMatch.Start(2, SkillIds.FirewallBlock);
        // Before the exit opens the precondition fails: ignored, no cooldown consumed.
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        m.Tick();
        Assert.Empty(m.Killer.All<S2C_SkillEffect>());
        m.OpenExitWithTwoSurvivors();
        var a = m[0];
        m.WalkTo(a, 55, 30, GameConstants.SurvivorSpeed);
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        m.Tick();
        foreach (var peer in new[] { m.Killer, m[0], m[1] })
        {
            var effect = Assert.Single(peer.All<S2C_SkillEffect>());
            Assert.Equal(m.Killer.Id, effect.CasterId);
            Assert.Equal(SkillIds.FirewallBlock, effect.SkillId);
            Assert.Equal(58f, effect.X);
            Assert.Equal(30f, effect.Z);
            Assert.Equal(5f, effect.Duration);
            Assert.Equal(1, peer.Last<S2C_WorldState>().ExitLocked);
        }
        int ticks = 1 + WalkUntilResult(m, a, 57, 30, GameConstants.SurvivorSpeed);
        // A second cast while on cooldown is ignored.
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        while (!a.All<S2C_GameResult>().Any() && ticks < 200)
        {
            Assert.Equal(1, m.Killer.Last<S2C_WorldState>().ExitLocked);
            Assert.Equal((byte)PlayerStatus.Normal, m.Truth(a).Status);
            m.Tick();
            ++ticks;
            a.Drain();
        }
        Assert.Equal(101, ticks); // locked for the 100 frames [cast, cast + 5 s); escape on the next
        Assert.Single(m.Killer.All<S2C_SkillEffect>());
        AssertSingleResultThenLobby(m.Killer, Role.Survivor, GameEndReason.SurvivorEscaped, a.Id);
    }

    [Fact]
    public void LaggingSurvivorStandingInTheOpenExitDoesNotEscape()
    {
        using var m = RulesMatch.Start(2);
        m.OpenExitWithTwoSurvivors(); // survivors stand at (48,25) and (50,25.5)
        m.WalkPath(m.Killer, GameConstants.KillerSpeed, (30f, 8f), (46.9f, 25f));
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[1]).Status);
        // Around the Normal survivor at (50,25.5), so no rescue contact happens on the way.
        m.WalkPath(m[0], GameConstants.SurvivorSpeed * GameConstants.LaggingSpeedMultiplier, (48f, 28f), (57.5f, 30f));
        m.Tick(20);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        Assert.Equal(1, m.Killer.Last<S2C_WorldState>().ExitOpen);
        Assert.Empty(m.Killer.All<S2C_GameResult>());
    }

    [Fact]
    public void KillerWinsWhenNoSurvivorIsNormalAndASecondMatchCanStart()
    {
        using var m = RulesMatch.Start(1);
        WalkUntilResult(m, m.Killer, 5, 25, GameConstants.KillerSpeed);
        foreach (var peer in new[] { m.Killer, m[0] })
            AssertSingleResultThenLobby(peer, Role.Killer, GameEndReason.AllSurvivorsDown);
        Assert.Equal((byte)PlayerStatus.Lagging, m[0].Self().Status);
        var lobby = m.Killer.Last<S2C_LobbyState>();
        Assert.Equal(2, lobby.Slots.Count(s => s.PlayerId != 0));
        Assert.Equal(1, lobby.Slots.Single(s => s.PlayerId == m.Killer.Id).IsHost);
        m.Tick(10);
        Assert.Single(m.Killer.All<S2C_GameResult>());

        int starts = m[0].All<S2C_MatchStart>().Count();
        m.Killer.Send(new C2S_StartMatch());
        m.Tick();
        Assert.Equal(starts + 1, m[0].All<S2C_MatchStart>().Count());
        var self = m[0].Self();
        Assert.Equal((byte)PlayerStatus.Normal, self.Status);
        Assert.Equal(0, self.CaptureCount);
        Assert.Equal(0, m[0].Last<S2C_WorldState>().ExitOpen);
        Assert.All(m[0].Last<S2C_WorldState>().Terminals, t => Assert.Equal(0, t.Progress));
        Assert.InRange(m[0].Last<S2C_WorldState>().TimeLeft, 599.9f, 600f);
    }

    [Fact]
    public void KillerWinsWhenTheTenMinutesRunOut()
    {
        using var m = RulesMatch.Start(1);
        for (int i = 0; i < 1199; i++) m.F.Tick(0.5f);
        Assert.Empty(m.Killer.All<S2C_GameResult>());
        Assert.InRange(m.Killer.Last<S2C_WorldState>().TimeLeft, 0.4f, 0.5f);
        m.F.Tick(0.5f);
        Assert.Equal(0f, m.Killer.Last<S2C_WorldState>().TimeLeft);
        foreach (var peer in new[] { m.Killer, m[0] })
            AssertSingleResultThenLobby(peer, Role.Killer, GameEndReason.TimeUp);
    }
}
