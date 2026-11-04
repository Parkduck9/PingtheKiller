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
            if (self.Status == (byte)PlayerStatus.Escaped) return i;
            float dx = x - self.X, dz = z - self.Z, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < 0.001f) return i;
            float step = speed / GameConstants.TickRate, scale = d > step ? 1 / d : 1 / step;
            peer.Move(dx * scale, dz * scale);
            m.Tick();
        }
        return 2000;
    }

    // GDD v2 §1: each survivor escapes on its own; the match ends once nobody is Normal or Lagging.
    [Fact]
    public void EachSurvivorEscapesOnItsOwnAndTheLastEscapeEndsTheMatchAndReturnsToTheLobby()
    {
        using var m = RulesMatch.Start(2);
        m.OpenExitWithTwoSurvivors();
        var a = m[0];
        int lobbiesBefore = m.Killer.All<S2C_LobbyState>().Count;
        WalkUntilResult(m, a, 4f, 4, GameConstants.SurvivorSpeed);
        var mid = m[1].Last<S2C_WorldState>();
        Assert.Equal((byte)PlayerStatus.Escaped, RulesMatch.Entry(mid, a.Id)!.Value.Status);
        Assert.InRange(RulesMatch.Distance(RulesMatch.Entry(mid, a.Id)!.Value, 4, 4), 1.8f, 2.001f);
        Assert.Null(RulesMatch.Entry(m.Killer.Last<S2C_WorldState>(), a.Id));
        Assert.Empty(m.Killer.All<S2C_GameResult>());
        // The second survivor walks out through the same open exit: now nobody is in play.
        m.WalkTo(m[1], 8, 8, GameConstants.SurvivorSpeed);
        WalkUntilResult(m, m[1], 4f, 4, GameConstants.SurvivorSpeed);
        m.Tick();
        foreach (var peer in new[] { m.Killer, m[0], m[1] })
            AssertSingleResultThenLobby(peer, Role.Survivor, GameEndReason.AllResolved, a.Id, m[1].Id);
        Assert.Equal(lobbiesBefore + 1, m.Killer.All<S2C_LobbyState>().Count());
        // The simulation stops: no more WorldStates, inputs are ignored, nothing is sent twice.
        int worlds = m.Killer.All<S2C_WorldState>().Count();
        a.Move(1, 0);
        m.Tick(20);
        m.Killer.Drain();
        Assert.Equal(worlds, m.Killer.All<S2C_WorldState>().Count());
        Assert.Single(m.Killer.All<S2C_GameResult>());
    }

    // GDD v2 §3: Firewall Block locks the switch in progress (not an open exit) for 5 s; a locked switch does not move.
    [Fact]
    public void FirewallBlockLocksTheSwitchInProgressForFiveSeconds()
    {
        using var m = RulesMatch.Start(2, SkillIds.FirewallBlock);
        // Before any switch progress the precondition fails: ignored, no cooldown consumed.
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        m.Tick();
        Assert.Empty(m.Killer.All<S2C_SkillEffect>());
        m.PowerExitsWithTwoSurvivors();
        var a = m[0];
        var point = m.F.Server.Map.ExitSwitches[0];
        m.WalkTo(a, point.X, point.Z, GameConstants.SurvivorSpeed);
        a.Send(new C2S_Interact { Active = 1 });
        m.Tick(60); // 3 s of 15 s
        Assert.Equal(20, m.Killer.Last<S2C_WorldState>().SwitchProgress(0));
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        m.Tick();
        foreach (var peer in new[] { m.Killer, m[0], m[1] })
        {
            var effect = Assert.Single(peer.All<S2C_SkillEffect>());
            Assert.Equal(m.Killer.Id, effect.CasterId);
            Assert.Equal(SkillIds.FirewallBlock, effect.SkillId);
            Assert.Equal(4f, effect.X);
            Assert.Equal(4f, effect.Z);
            Assert.Equal(5f, effect.Duration);
            Assert.True(peer.Last<S2C_WorldState>().Locked(0));
            Assert.False(peer.Last<S2C_WorldState>().Locked(1));
        }
        byte frozen = m.Killer.Last<S2C_WorldState>().SwitchProgress(0);
        // A second cast while on cooldown is ignored.
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        int ticks = 1;
        while (m.Killer.Last<S2C_WorldState>().Locked(0) && ticks < 200)
        {
            Assert.Equal(frozen, m.Killer.Last<S2C_WorldState>().SwitchProgress(0));
            m.Tick();
            ++ticks;
        }
        Assert.InRange(ticks, 100, 101); // locked for the 100 frames [cast, cast + 5 s)
        Assert.Single(m.Killer.All<S2C_SkillEffect>());
        // Held on, the switch finishes the remaining 12 s and the exit opens.
        m.Tick(238);
        Assert.False(m.Killer.Last<S2C_WorldState>().Open(0));
        m.Tick(3);
        Assert.True(m.Killer.Last<S2C_WorldState>().Open(0));
    }

    [Fact]
    public void LaggingSurvivorStandingInTheOpenExitDoesNotEscape()
    {
        using var m = RulesMatch.Start(2);
        m.OpenExitWithTwoSurvivors();
        m.F.Server.PlaceForTests(m[0].Id,4,10); m.F.Server.PlaceForTests(m[1].Id,10,20);
        m.F.Server.PlaceForTests(m.Killer.Id,4,9); m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging,m.Truth(m[0]).Status);
        Assert.Equal((byte)PlayerStatus.Normal,m.Truth(m[1]).Status);
        m.WalkTo(m[0],4,4,GameConstants.SurvivorSpeed*GameConstants.LaggingSpeedMultiplier);
        m.Tick(20);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        Assert.True(m.Killer.Last<S2C_WorldState>().Open(0));
        Assert.Empty(m.Killer.All<S2C_GameResult>());
    }

    // GDD v2 §4: one capture leaves the survivor Lagging and in play; its timers run out 60 s + 30 s later.
    [Fact]
    public void KillerWinsWhenTheLastSurvivorIsEliminatedAndASecondMatchCanStart()
    {
        using var m = RulesMatch.Start(1);
        WalkUntilResult(m, m.Killer, 5, 25, GameConstants.KillerSpeed);
        Assert.Equal((byte)PlayerStatus.Lagging, m[0].Self().Status);
        Assert.Empty(m.Killer.All<S2C_GameResult>());
        m.WalkTo(m.Killer, 30, 5, GameConstants.KillerSpeed);
        for (int i = 0; i < 2000 && !m.Killer.All<S2C_GameResult>().Any(); i++) m.Tick();
        foreach (var peer in new[] { m.Killer, m[0] })
            AssertSingleResultThenLobby(peer, Role.Killer, GameEndReason.AllSurvivorsDown);
        Assert.Equal(new[] { m[0].Id }, m.Killer.Last<S2C_GameResult>().EliminatedIds);
        Assert.Equal((byte)PlayerStatus.Eliminated, m[0].Self().Status);
        Assert.Equal(2, m[0].Self().CaptureCount);
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
        Assert.False(m[0].Last<S2C_WorldState>().Powered());
        Assert.All(m[0].Last<S2C_WorldState>().Terminals, t => Assert.Equal(0, t.Progress));
        Assert.InRange(m[0].Last<S2C_WorldState>().TimeLeft, 899.9f, 900f);
    }

    [Fact]
    public void KillerWinsWhenTheFifteenMinutesRunOut()
    {
        using var m = RulesMatch.Start(1);
        for (int i = 0; i < 1799; i++) m.F.Tick(0.5f);
        Assert.Empty(m.Killer.All<S2C_GameResult>());
        Assert.InRange(m.Killer.Last<S2C_WorldState>().TimeLeft, 0.4f, 0.5f);
        m.F.Tick(0.5f);
        Assert.Equal(0f, m.Killer.Last<S2C_WorldState>().TimeLeft);
        foreach (var peer in new[] { m.Killer, m[0] })
            AssertSingleResultThenLobby(peer, Role.Killer, GameEndReason.TimeUp);
        Assert.Equal(new[] { m[0].Id }, m.Killer.Last<S2C_GameResult>().EliminatedIds);
    }
}
