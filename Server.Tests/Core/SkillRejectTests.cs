using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// Q3 (protocol v0.6): every refused C2S_UseSkill gets exactly one S2C_SkillRejected, to the caster only, with the true
// reason; at most one per player per tick.
public sealed class SkillRejectProtocolTests
{
    [Fact]
    public void GoldenBytesRoundTripEveryReasonAndRejectBadLengthsAndReasons()
    {
        Assert.Equal((ushort)0x0016, (ushort)PacketId.S2C_SkillRejected);
        var packet = new S2C_SkillRejected { SkillId = 3, Reason = (byte)SkillRejectReason.NoSwitchInProgress };
        byte[] golden = { 4, 0, 0, 0, 0x16, 0, 3, 6 };
        Assert.Equal(golden, PacketCodec.Encode(packet));
        Assert.Equivalent(packet, PacketCodec.Decode(0x0016, golden.AsSpan(6)), strict: true);
        // Numbered explicitly from 1; 0 and anything above Sandbox are format errors.
        var reasons = Enum.GetValues<SkillRejectReason>();
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (SkillRejectReason)i), reasons);
        for (int value = 0; value <= 255; value++)
        {
            byte[] payload = { 200, (byte)value };
            if (value >= 1 && value <= 8)
            {
                var decoded = Assert.IsType<S2C_SkillRejected>(PacketCodec.Decode(0x0016, payload));
                Assert.Equal(200, decoded.SkillId);
                Assert.Equal(value, decoded.Reason);
                Assert.Equal(payload, PacketCodec.Encode(decoded)[6..]);
            }
            else Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0016, payload));
        }
        foreach (int length in new[] { 0, 1, 3, 4 })
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(0x0016, new byte[length].Select(_ => (byte)1).ToArray()));
        // The default instance is valid on the wire (reflection-built packets in the protocol tests encode it).
        Assert.Equal((byte)SkillRejectReason.NotInMatch, new S2C_SkillRejected().Reason);
    }
}

public sealed class SkillRejectTests
{
    private static void Use(Peer peer, byte skill) => peer.Send(new C2S_UseSkill { SkillId = skill });

    private static void Clear(params Peer[] peers)
    {
        foreach (var peer in peers) { peer.Drain(); peer.Packets.RemoveAll(p => p is not S2C_JoinResult); }
    }

    private static IEnumerable<Peer> Everyone(RulesMatch m) => m.Survivors.Prepend(m.Killer);

    // Exactly one rejection, to the caster only, and no effect for it.
    private static void AssertRejected(RulesMatch m, Peer caster, byte skill, SkillRejectReason reason)
    {
        var rejection = Assert.Single(caster.All<S2C_SkillRejected>());
        Assert.Equal(skill, rejection.SkillId);
        Assert.Equal((byte)reason, rejection.Reason);
        foreach (var other in Everyone(m).Where(p => p != caster)) Assert.Empty(other.All<S2C_SkillRejected>());
        Assert.DoesNotContain(caster.All<S2C_SkillEffect>(), e => e.CasterId == caster.Id);
    }

    private static void AssertNoRejection(RulesMatch m)
    {
        foreach (var peer in Everyone(m)) Assert.Empty(peer.All<S2C_SkillRejected>());
    }

    [Fact]
    public void NotInMatchInTheLobby()
    {
        using var f = new CoreFixture();
        var host = f.Join("Host");
        var other = f.Join("Other");
        Clear(host, other);
        Use(host, SkillIds.PingScanner);
        f.Tick();
        var rejection = Assert.Single(host.All<S2C_SkillRejected>());
        Assert.Equal((byte)SkillRejectReason.NotInMatch, rejection.Reason);
        Assert.Equal(SkillIds.PingScanner, rejection.SkillId);
        Assert.Empty(other.All<S2C_SkillRejected>());
    }

    [Fact]
    public void NotYourSkillForAnotherIdAndForAnOutOfRangeId()
    {
        using var m = RulesMatch.Start(1, SkillIds.PingScanner, SkillIds.DummyPacket);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.Overclock);
        m.Tick();
        AssertRejected(m, m.Killer, SkillIds.Overclock, SkillRejectReason.NotYourSkill);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, 200);
        m.Tick();
        AssertRejected(m, m.Killer, 200, SkillRejectReason.NotYourSkill);
        Clear(Everyone(m).ToArray());
        Use(m[0], SkillIds.SurvivorSkillCount); // out of range for a Survivor
        m.Tick();
        AssertRejected(m, m[0], SkillIds.SurvivorSkillCount, SkillRejectReason.NotYourSkill);
        Clear(Everyone(m).ToArray());
        Use(m[0], SkillIds.WhiteNoise); // in range, not the own skill
        m.Tick();
        AssertRejected(m, m[0], SkillIds.WhiteNoise, SkillRejectReason.NotYourSkill);
    }

    [Theory]
    [InlineData(PlayerStatus.Lagging)]
    [InlineData(PlayerStatus.Eliminated)]
    [InlineData(PlayerStatus.Escaped)]
    public void NotNormalWhileLaggingEliminatedOrEscaped(PlayerStatus status)
    {
        using var m = RulesMatch.Start(2, -1, SkillIds.DummyPacket);
        m.F.Server.SetStatusForTests(m[0].Id, status);
        m.Tick();
        Clear(Everyone(m).ToArray());
        Use(m[0], SkillIds.DummyPacket);
        m.Tick();
        AssertRejected(m, m[0], SkillIds.DummyPacket, SkillRejectReason.NotNormal);
    }

    [Fact]
    public void CooldownUpToTheLastTickBeforeReadyAndNoRejectionOnAUse()
    {
        using var m = RulesMatch.Start(1, SkillIds.Overclock);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.Overclock);
        m.Tick();
        Assert.Single(m.Killer.All<S2C_SkillEffect>());
        AssertNoRejection(m);
        int ticks = (int)Math.Round(GameConstants.KillerSkillCooldownSeconds * GameConstants.TickRate);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.Overclock);
        m.Tick();
        AssertRejected(m, m.Killer, SkillIds.Overclock, SkillRejectReason.Cooldown);
        m.Tick(ticks - 3);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.Overclock); // the last tick before the cooldown ends (TimeEpsilon edge)
        m.Tick();
        AssertRejected(m, m.Killer, SkillIds.Overclock, SkillRejectReason.Cooldown);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.Overclock);
        m.Tick();
        Assert.Single(m.Killer.All<S2C_SkillEffect>());
        AssertNoRejection(m);
    }

    [Fact]
    public void NoTargetForPingScannerOnlyWithoutAnActiveSurvivor()
    {
        using var m = RulesMatch.Start(2, SkillIds.PingScanner);
        m.F.Server.SetStatusForTests(m[0].Id, PlayerStatus.Eliminated);
        m.F.Server.SetStatusForTests(m[1].Id, PlayerStatus.Escaped);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.PingScanner);
        m.Tick(); // skills run before the end check, so the press is answered in the final tick
        AssertRejected(m, m.Killer, SkillIds.PingScanner, SkillRejectReason.NoTarget);
        Assert.NotEmpty(m.Killer.All<S2C_GameResult>());
    }

    [Fact]
    public void NoTargetForEmergencyRestoreWithoutALaggingAllyInRadius()
    {
        using var m = RulesMatch.Start(2, -1, SkillIds.EmergencyRestore);
        Clear(Everyone(m).ToArray());
        Use(m[0], SkillIds.EmergencyRestore);
        m.Tick();
        AssertRejected(m, m[0], SkillIds.EmergencyRestore, SkillRejectReason.NoTarget);
        // A Lagging ally 5 m away is outside the 1.5 m radius.
        m.F.Server.SetStatusForTests(m[1].Id, PlayerStatus.Lagging);
        m.Tick();
        Clear(Everyone(m).ToArray());
        Use(m[0], SkillIds.EmergencyRestore);
        m.Tick();
        AssertRejected(m, m[0], SkillIds.EmergencyRestore, SkillRejectReason.NoTarget);
    }

    [Fact]
    public void NoSwitchInProgressForFirewallBlockMatchesTheKillersExitView()
    {
        using var m = RulesMatch.Start(1, SkillIds.FirewallBlock);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.FirewallBlock);
        m.Tick();
        AssertRejected(m, m.Killer, SkillIds.FirewallBlock, SkillRejectReason.NoSwitchInProgress);
        // The Killer's own stream carries every exit and its switch progress (HUD_SPEC 6.1d derives the same case).
        var exits = m.Killer.Last<S2C_WorldState>().Exits;
        Assert.Equal(m.F.Server.Map.Exits.Count, exits.Count);
        Assert.All(exits, e => Assert.Equal(0, e.SwitchProgress));
    }

    [Fact]
    public void PassiveForPacketAfterimage()
    {
        using var m = RulesMatch.Start(1, SkillIds.PacketAfterimage);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.PacketAfterimage);
        m.Tick();
        AssertRejected(m, m.Killer, SkillIds.PacketAfterimage, SkillRejectReason.Passive);
    }

    [Fact]
    public void SandboxRejectsEveryPressWithSandboxOncePerTick()
    {
        using var f = new CoreFixture(sandbox: true);
        var killer = f.Join("Killer");
        var a = f.Join("A");
        killer.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
        killer.Send(new C2S_StartMatch());
        f.Tick();
        Clear(killer, a);
        foreach (var peer in new[] { killer, a })
            for (byte id = 0; id < SkillIds.SurvivorSkillCount; id++) Use(peer, id);
        f.Tick();
        foreach (var peer in new[] { killer, a })
        {
            var rejection = Assert.Single(peer.All<S2C_SkillRejected>());
            Assert.Equal((byte)SkillRejectReason.Sandbox, rejection.Reason);
            Assert.Equal(0, rejection.SkillId); // the first press of the tick
            Assert.Empty(peer.All<S2C_SkillEffect>());
        }
    }

    [Fact]
    public void SuccessfulUsesSendNoRejectionToAnyone()
    {
        using var m = RulesMatch.Start(2, SkillIds.Overclock, SkillIds.DummyPacket, SkillIds.TrafficOverload);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.Overclock);
        Use(m[0], SkillIds.DummyPacket);
        Use(m[1], SkillIds.TrafficOverload);
        m.Tick();
        AssertNoRejection(m);
        Assert.Contains(m.Killer.All<S2C_SkillEffect>(), e => e.SkillId == SkillIds.Overclock && e.CasterId == m.Killer.Id);
        Assert.Contains(m[0].All<S2C_SkillEffect>(), e => e.SkillId == SkillIds.DummyPacket && e.CasterId == m[0].Id);
        Assert.Contains(m[1].All<S2C_SkillEffect>(), e => e.SkillId == SkillIds.TrafficOverload && e.CasterId == m[1].Id);
    }

    [Fact]
    public void FiftyPressesInOneTickGiveAtMostOneRejection()
    {
        using var m = RulesMatch.Start(1, SkillIds.Overclock);
        var log = new List<string>();
        m.F.Server.Log += log.Add;
        Clear(Everyone(m).ToArray());
        // Ready: the first press is used, the 49 refused by the new cooldown send nothing.
        for (int i = 0; i < 50; i++) Use(m.Killer, SkillIds.Overclock);
        m.Tick();
        Assert.Single(m.Killer.All<S2C_SkillEffect>());
        AssertNoRejection(m);
        // On cooldown: one rejection per tick, however many presses.
        Clear(Everyone(m).ToArray());
        for (int i = 0; i < 50; i++) Use(m.Killer, SkillIds.Overclock);
        m.Tick();
        AssertRejected(m, m.Killer, SkillIds.Overclock, SkillRejectReason.Cooldown);
        // Mixed wrong and out-of-range ids: the out-of-range one is refused on arrival, before the queued requests
        // execute, so it is the one answer of the tick.
        Clear(Everyone(m).ToArray());
        for (int i = 0; i < 50; i++) Use(m.Killer, (byte)(i % 2 == 0 ? SkillIds.FirewallBlock : 250));
        m.Tick();
        AssertRejected(m, m.Killer, 250, SkillRejectReason.NotYourSkill);
        // Spam for 40 ticks (2 s): one rejection per tick, one log line per second with the unlogged count.
        m.Tick(20); // a quiet second, so the first spam rejection is logged at once
        Clear(Everyone(m).ToArray());
        log.Clear();
        for (int t = 0; t < 40; t++)
        {
            for (int i = 0; i < 50; i++) Use(m.Killer, SkillIds.Overclock);
            m.Tick();
        }
        Assert.Equal(40, m.Killer.All<S2C_SkillRejected>().Count);
        var lines = log.Where(l => l.StartsWith("skill rejected", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lines.Count);
        // The first line also counts the mixed-id rejection the earlier second did not log.
        Assert.Equal($"skill rejected player={m.Killer.Id} id={SkillIds.Overclock} reason=Cooldown unlogged=1", lines[0]);
        Assert.EndsWith(" unlogged=19", lines[1]);
    }

    [Fact]
    public void ARejectionThenAUseInTheSameTickSendsBothInOrder()
    {
        using var m = RulesMatch.Start(1, SkillIds.Overclock);
        Clear(Everyone(m).ToArray());
        Use(m.Killer, SkillIds.PingScanner);
        Use(m.Killer, SkillIds.Overclock);
        m.Tick();
        m.Killer.Drain();
        var answers = m.Killer.Packets.Where(p => p is S2C_SkillRejected or S2C_SkillEffect).ToList();
        Assert.Equal(2, answers.Count);
        Assert.Equal((byte)SkillRejectReason.NotYourSkill, Assert.IsType<S2C_SkillRejected>(answers[0]).Reason);
        Assert.Equal(SkillIds.Overclock, Assert.IsType<S2C_SkillEffect>(answers[1]).SkillId);
    }

    // S2.2: what the Killer receives for a press never depends on where a Silent Survivor stands.
    [Theory]
    [InlineData(SkillIds.FirewallBlock)]
    [InlineData(SkillIds.PingScanner)]
    [InlineData(SkillIds.PacketAfterimage)]
    public void KillerAnswersDoNotDependOnASilentSurvivorsPosition(byte skill)
    {
        var answers = new List<string>();
        for (int place = 0; place < 3; place++)
        {
            using var m = RulesMatch.Start(1, skill);
            // Two open-lane points and exit 0's switch point (standing on it without holding Interact).
            var point = m.F.Server.Map.ExitSwitches[0];
            var (x, z) = place == 0 ? (5f, 26f) : place == 1 ? (10f, 36f) : (point.X, point.Z);
            m.F.Server.PlaceForTests(m[0].Id, x, z);
            m.Tick(3);
            Assert.Null(m.KillerSees(m[0])); // Silent: omitted from the Killer's WorldState
            Clear(Everyone(m).ToArray());
            Use(m.Killer, skill);
            m.Tick();
            m.Killer.Drain();
            var rejection = m.Killer.Packets.OfType<S2C_SkillRejected>().SingleOrDefault();
            bool effect = m.Killer.Packets.OfType<S2C_SkillEffect>().Any(e => e.CasterId == m.Killer.Id);
            answers.Add(rejection != null ? Convert.ToHexString(PacketCodec.Encode(rejection)) : effect ? "effect" : "none");
        }
        Assert.All(answers, a => Assert.Equal(answers[0], a));
        // Ping scanner finds a Silent Survivor anywhere; Firewall block and the passive skill are refused everywhere.
        Assert.Equal(skill == SkillIds.PingScanner, answers[0] == "effect");
        Assert.NotEqual("none", answers[0]);
    }

    // Bots run inside Server.Core with no connection: they never get a rejection and never log one.
    [Fact]
    public void BotsNeverReceiveOrLogRejectionsInABotMatch()
    {
        using var f = new CoreFixture(bots: 7);
        var log = new List<string>();
        f.Server.Log += log.Add;
        var human = f.Join("Human");
        human.Send(new C2S_StartMatch());
        f.Tick();
        Assert.NotEmpty(human.All<S2C_MatchStart>());
        for (int i = 0; i < 1200 && human.All<S2C_GameResult>().Count == 0; i++) f.Tick();
        Assert.Contains(log, l => l.StartsWith("skill player=", StringComparison.Ordinal)); // bots did use skills
        Assert.DoesNotContain(log, l => l.StartsWith("skill rejected", StringComparison.Ordinal));
        Assert.Empty(human.All<S2C_SkillRejected>()); // the human never pressed
    }
}
