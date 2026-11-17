using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class ProtocolV03Tests
{
    [Fact]
    public void SelfStateRoundTripsWithAFixedSixteenBytePayloadAfterTheV02Ids()
    {
        Assert.Equal((ushort)0x0010, (ushort)PacketId.S2C_SelfState);
        var packet = new S2C_SelfState { SkillCooldown = 7.25f, RescueProgress = 0.5f, InvulnerableLeft = 1.95f, ExecutionGraceLeft = float.Epsilon };
        byte[] frame = PacketCodec.Encode(packet);
        // v0.4 appends CollapseLeft, ActionProgress (floats) and ActionKind (byte): 25 bytes; 11/17 (R2) appends the own
        // route (RouteId, RouteLength, RouteDirs, RouteIndex): 29 bytes.
        Assert.Equal(4 + 2 + 29, frame.Length);
        Assert.Equal(new byte[] { 31, 0, 0, 0, 0x10, 0 }, frame.Take(6));
        Assert.Equal(BitConverter.GetBytes(7.25f), frame.Skip(6).Take(4));
        Assert.Equal(BitConverter.GetBytes(float.Epsilon), frame.Skip(18).Take(4));
        var decoded = Assert.IsType<S2C_SelfState>(PacketCodec.Decode(0x0010, frame.AsSpan(6)));
        Assert.Equal(7.25f, decoded.SkillCooldown);
        Assert.Equal(0.5f, decoded.RescueProgress);
        Assert.Equal(1.95f, decoded.InvulnerableLeft);
        Assert.Equal(float.Epsilon, decoded.ExecutionGraceLeft);
        Assert.Equal(-1f, decoded.CollapseLeft);
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0010, frame.AsSpan(6, 28)));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0010, new byte[30]));
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0014, Array.Empty<byte>()));
    }

    [Fact]
    public void PrivateRandomIsDeterministicPerSeedAndStreamAndIndependentAcrossThem()
    {
        var a = PrivateRandom.FromSeed(5, 0);
        var b = PrivateRandom.FromSeed(5, 0);
        var otherStream = PrivateRandom.FromSeed(5, 1);
        var otherSeed = PrivateRandom.FromSeed(6, 0);
        var drawsA = Enumerable.Range(0, 64).Select(_ => a.NextUInt64()).ToList();
        Assert.Equal(drawsA, Enumerable.Range(0, 64).Select(_ => b.NextUInt64()));
        Assert.NotEqual(drawsA, Enumerable.Range(0, 64).Select(_ => otherStream.NextUInt64()));
        Assert.NotEqual(drawsA, Enumerable.Range(0, 64).Select(_ => otherSeed.NextUInt64()));
        // Random(x) == Random(-x) collapsed the old seed space; the private generator keeps them apart.
        Assert.NotEqual(PrivateRandom.FromSeed(7, 0).NextUInt64(), PrivateRandom.FromSeed(-7, 0).NextUInt64());
        Assert.NotEqual(PrivateRandom.FromCsprng().NextUInt64(), PrivateRandom.FromCsprng().NextUInt64());
        var r = PrivateRandom.FromSeed(1, 3);
        var counts = new int[7];
        for (int i = 0; i < 70000; i++)
        {
            int v = r.Next(7);
            counts[v]++;
            double d = r.NextDouble();
            Assert.InRange(d, 0, 0.9999999999);
            Assert.InRange(r.Next(-3, 4), -3, 3);
        }
        Assert.All(counts, c => Assert.InRange(c, 9500, 10500));
        Assert.Equal(0, r.Next(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Next(-1));
    }

    private static List<S2C_SelfState> Selves(Peer peer) => peer.All<S2C_SelfState>();

    // What a client shows: the latest SelfState counted down (a running rescue counted up) by the ticks since it came.
    private static S2C_SelfState Now(Peer peer)
    {
        peer.Drain();
        uint lastWorld = 0, stateWorld = 0;
        S2C_SelfState? state = null;
        foreach (var packet in peer.Packets)
        {
            if (packet is S2C_WorldState w) lastWorld = w.Tick;
            else if (packet is S2C_SelfState s) { state = s; stateWorld = lastWorld; }
        }
        Assert.NotNull(state);
        float elapsed = (lastWorld - stateWorld) / (float)GameConstants.TickRate;
        Assert.InRange(elapsed, 0, 1.0001f); // the 1 Hz refresh
        return new S2C_SelfState
        {
            SkillCooldown = Math.Max(0, state.SkillCooldown - elapsed),
            InvulnerableLeft = Math.Max(0, state.InvulnerableLeft - elapsed),
            ExecutionGraceLeft = Math.Max(0, state.ExecutionGraceLeft - elapsed),
            RescueProgress = state.RescueProgress > 0 ? Math.Min(1, state.RescueProgress + elapsed / GameConstants.RescueTimeSeconds) : 0
        };
    }

    // Ticks (WorldState numbers) at which the peer received a SelfState.
    private static List<uint> SelfTicks(Peer peer)
    {
        peer.Drain();
        var ticks = new List<uint>();
        uint lastWorld = 0;
        foreach (var packet in peer.Packets)
        {
            if (packet is S2C_WorldState w) lastWorld = w.Tick;
            else if (packet is S2C_SelfState) ticks.Add(lastWorld);
        }
        return ticks;
    }

    [Fact]
    public void SelfStateIsSentOnEveryJumpAndOtherwiseOncePerSecondAndSkillCooldownFollowsTheServerTimer()
    {
        using var m = RulesMatch.Start(2, SkillIds.Overclock, SkillIds.StealthRelay);
        uint start = SelfTicks(m.Killer).First();
        Assert.Equal(new uint[] { start }, SelfTicks(m.Killer)); // the first frame of the match always carries one
        m.Tick(100);
        // Nothing changes for 5 s: exactly the 1 Hz refresh.
        foreach (var peer in m.Survivors.Append(m.Killer))
        {
            Assert.Equal(Enumerable.Range(0, 6).Select(k => start + (uint)(20 * k)), SelfTicks(peer).Where(t => t >= start));
            Assert.All(Selves(peer), s => Assert.Equal(0f, s.SkillCooldown));
        }
        m.Tick(5);
        m[0].Send(new C2S_UseSkill { SkillId = SkillIds.StealthRelay });
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.Overclock });
        m.Tick();
        // The cooldown start is sent in the cast tick itself, not at the next refresh.
        uint cast = m.Killer.Last<S2C_WorldState>().Tick;
        Assert.Equal(cast, SelfTicks(m[0]).Last());
        Assert.Equal(cast, SelfTicks(m.Killer).Last());
        Assert.Equal(15f, Selves(m[0]).Last().SkillCooldown, 3);
        Assert.Equal(12f, Selves(m.Killer).Last().SkillCooldown, 3);
        Assert.Equal(15f, Now(m[0]).SkillCooldown, 3);
        Assert.Equal(12f, Now(m.Killer).SkillCooldown, 3);
        Assert.Equal(0f, Now(m[1]).SkillCooldown); // another survivor's cooldown never reaches anyone else
        m.Tick(100);
        Assert.Equal(10f, Now(m[0]).SkillCooldown, 3);
        Assert.Equal(7f, Now(m.Killer).SkillCooldown, 3);
        m.Tick(200);
        Assert.Equal(0f, Now(m[0]).SkillCooldown);
        Assert.Equal(0f, Now(m.Killer).SkillCooldown);
        foreach (var peer in m.Survivors) Assert.All(Selves(peer), s => Assert.Equal(0f, s.ExecutionGraceLeft));
    }

    // A full 900 s match (sandbox rules end only on time) with a peer that drains once per second, as a slow client
    // would: the connection is never closed for a full queue, and SelfState stays near the 1 Hz refresh.
    [Fact]
    public void ATenMinuteMatchStaysFarBelowTheReceiveQueueCap()
    {
        using var f = new CoreFixture(bots: 7, sandbox: true);
        var human = f.Join("Human");
        human.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
        human.Send(new C2S_StartMatch());
        int worlds = 0, selves = 0, results = 0, maxBacklog = 0;
        for (int tick = 0; tick < 18100 && results == 0; tick++)
        {
            f.Tick();
            if (tick % 20 != 19) continue;
            human.Drain();
            maxBacklog = Math.Max(maxBacklog, human.Packets.Count);
            worlds += human.Packets.OfType<S2C_WorldState>().Count();
            selves += human.Packets.OfType<S2C_SelfState>().Count();
            results += human.Packets.OfType<S2C_GameResult>().Count();
            human.Packets.Clear();
        }
        human.Drain();
        results += human.Packets.OfType<S2C_GameResult>().Count();
        Assert.False(human.Connection.IsClosed);
        Assert.Equal(1, results);
        Assert.InRange(worlds, 17990, 18001);
        Assert.InRange(selves, 900, 930);
        Assert.InRange(maxBacklog, 20, 50);
    }

    [Fact]
    public void RescueProgressGoesToTheLaggingSurvivorAndItsRescuerAndInvulnerabilityFollows()
    {
        using var m = RulesMatch.Start(3);
        var (a, b, c) = (m[0], m[1], m[2]);
        m.WalkTo(m.Killer, 5, 25, GameConstants.KillerSpeed); // captures survivor 0 at (5,26)
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(a).Status);
        m.WalkTo(m.Killer, 5, 20, GameConstants.KillerSpeed);
        Assert.Equal(0f, Now(a).RescueProgress);
        m.WalkTo(b, 6.4f, 26, GameConstants.SurvivorSpeed);
        // b arrived this tick: the rescue has counted one tick (0.05 s of 3 s) for both.
        Assert.Equal(0.05f / 3f, Now(a).RescueProgress, 4);
        Assert.Equal(0.05f / 3f, Now(b).RescueProgress, 4);
        Assert.Equal(0f, Now(c).RescueProgress);
        Assert.All(Selves(m.Killer), s => Assert.Equal(0f, s.RescueProgress));
        m.Tick(30);
        Assert.Equal(31 * 0.05f / 3f, Now(a).RescueProgress, 3);
        Assert.Equal(Now(a).RescueProgress, Now(b).RescueProgress);
        m.Tick(29);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(a).Status);
        Assert.Equal(0f, Now(a).RescueProgress);
        Assert.Equal(2f, Now(a).InvulnerableLeft, 3);
        Assert.Equal(0f, Now(b).InvulnerableLeft);
        m.Tick(20);
        Assert.Equal(1f, Now(a).InvulnerableLeft, 3);
        m.Tick(20);
        Assert.Equal(0f, Now(a).InvulnerableLeft);
        Assert.All(Selves(m.Killer), s => Assert.Equal(0f, s.InvulnerableLeft));
    }

    [Fact]
    // GDD v2 §4 (2026-11-03): no execution, so the field is deprecated and always 0 for everyone, next to a fresh capture too.
    public void ExecutionGraceLeftIsDeprecatedAndAlwaysZero()
    {
        // Two survivors: the second stays Normal, so the capture does not end the match.
        using var m = RulesMatch.Start(2);
        var a = m[0];
        m.WalkTo(m.Killer, 5, 25, GameConstants.KillerSpeed); // capture tick
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(a).Status);
        m.Tick(10);
        m.WalkTo(m.Killer, 5, 23.1f, GameConstants.KillerSpeed);
        m.Tick(40);
        Assert.All(Selves(m.Killer), s => Assert.Equal(0f, s.ExecutionGraceLeft));
        Assert.All(Selves(a), s => Assert.Equal(0f, s.ExecutionGraceLeft));
    }

    [Fact]
    public void KillerSeesOnlyDoneOrNotDoneWhileSurvivorsSeeTheExactPercentage()
    {
        using var m = RulesMatch.Start(1);
        var a = m[0];
        m.WalkTo(a, 9.5f, 25.5f, GameConstants.SurvivorSpeed);
        a.Send(new C2S_Interact { Active = 1 });
        for (int tick = 0; tick < 1801; tick++) // 11/17 (R2): 90 s passive
        {
            m.Tick();
            byte exact = a.Last<S2C_WorldState>().Terminals[0].Progress;
            byte killer = m.Killer.Last<S2C_WorldState>().Terminals[0].Progress;
            Assert.Equal(exact >= 100 ? 100 : 0, killer);
            if (tick == 900) Assert.InRange(exact, 50, 51);
        }
        Assert.Equal(100, m.Killer.Last<S2C_WorldState>().Terminals[0].Progress);
        Assert.All(m.Killer.All<S2C_WorldState>().SelectMany(w => w.Terminals), t => Assert.True(t.Progress == 0 || t.Progress == 100));
    }
}
