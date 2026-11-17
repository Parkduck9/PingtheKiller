using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// R1 (11/17): the server-authoritative Killer swing (GameServer.Combat), the lobby skill choice and the reworked Killer
// skills. Every swing test switches the legacy contact hook off, so only C2S_Attack (or a bot intent) can capture.
public sealed class KillerSwingTests
{
    private const int WindUpTicks = 4; // 0.2 s
    private static readonly float Step = GameConstants.KillerSpeed / GameConstants.TickRate;

    private static RulesMatch Match(int survivors = 2, int killerSkill = -1)
    {
        var m = RulesMatch.Start(survivors, killerSkill);
        m.F.Server.ContactSwingForTests = false;
        return m;
    }

    private static void Face(RulesMatch m, float yaw, float x = 0, float z = 0) =>
        m.Killer.Send(new C2S_MoveInput { DirX = x, DirZ = z, Yaw = yaw, Seq = m.Killer.NextSeq++ });

    private static uint _attackSeq = 100;
    private static void Attack(Peer killer, uint? seq = null) => killer.Send(new C2S_Attack { Seq = seq ?? ++_attackSeq });

    private static byte StatusOf(RulesMatch m, int survivor) => m.Truth(m[survivor]).Status;

    // Survivor 0 two metres in front of the Killer (yaw 0 faces +z); survivor 1 far away.
    private static RulesMatch Facing(float distance = 2.0f, float side = 0)
    {
        var m = Match();
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 24);
        m.F.Server.PlaceForTests(m[0].Id, 5 + side, 24 + distance);
        m.F.Server.PlaceForTests(m[1].Id, 5, 36);
        m.Tick(25); // past every Signal hold
        return m;
    }

    [Fact]
    public void NoCaptureWithoutASwingEvenStandingOnTheSurvivor()
    {
        using var m = Match();
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 25.6f);
        m.Tick(60);
        Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 0));
        Assert.Equal(0, m.Truth(m[0]).CaptureCount);
    }

    [Fact]
    public void ASwingHitsAtTheEndOfTheWindUpWithTheCaptureConsequences()
    {
        using var m = Facing();
        var log = new List<string>();
        m.F.Server.Log += log.Add;
        Face(m, 0);
        Attack(m.Killer);
        m.Tick(); // the press starts the swing in this tick's rules pass
        Assert.True(m.F.Server.SwingingForTests(m.Killer.Id));
        for (int i = 1; i < WindUpTicks; i++) { m.Tick(); Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 0)); }
        m.Tick();
        var a = m.Truth(m[0]);
        Assert.Equal((byte)PlayerStatus.Lagging, a.Status);
        Assert.Equal(1, a.CaptureCount);
        Assert.InRange(a.StatusTimer, 59.9f, 60f);
        Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 1));
        Assert.Single(log, l => l.StartsWith($"swing player={m.Killer.Id} result=hit target={m[0].Id}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2.9f, 0f, 0f, true)]     // range 2.95 m (B1 balance; INTEG-R had 2.75 m, R1 2.6 m)
    [InlineData(3.05f, 0f, 0f, false)]
    [InlineData(2.0f, 35f, 0f, true)]    // arc +-40 degrees
    [InlineData(2.0f, -39f, 0f, true)]
    [InlineData(2.0f, 45f, 0f, false)]
    [InlineData(2.0f, 180f, 0f, false)]  // behind
    public void TheHitCheckUsesRangeAndArcOfTheLookYaw(float distance, float yawOffset, float unused, bool hit)
    {
        _ = unused;
        using var m = Facing(distance);
        Face(m, yawOffset);
        Attack(m.Killer);
        m.Tick(WindUpTicks + 1);
        Assert.Equal(hit ? (byte)PlayerStatus.Lagging : (byte)PlayerStatus.Normal, StatusOf(m, 0));
    }

    [Fact]
    public void AWallOrADownShutterBetweenThemMakesItAMiss()
    {
        using (var m = Match())
        {
            // Around the corner of the building box (12..20, 8..14): 1.9 m apart, the segment cuts the corner.
            m.F.Server.PlaceForTests(m.Killer.Id, 11.4f, 9.0f);
            m.F.Server.PlaceForTests(m[0].Id, 12.6f, 7.5f);
            m.Tick(25);
            Face(m, MathF.Atan2(1.2f, -1.5f) * 180 / MathF.PI);
            Attack(m.Killer);
            m.Tick(WindUpTicks + 1);
            Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 0));
        }
        using (var m = Match())
        {
            var shutter = WorldChaseProps.Shutters[0];
            float d = shutter.HalfThickness + 0.75f;
            m.F.Server.SetShutterForTests(0, ShutterState.Down);
            m.F.Server.PlaceForTests(m.Killer.Id, shutter.CenterX - shutter.CrossX * d, shutter.CenterZ - shutter.CrossZ * d);
            m.F.Server.PlaceForTests(m[0].Id, shutter.CenterX + shutter.CrossX * d, shutter.CenterZ + shutter.CrossZ * d);
            m.Tick(25);
            Face(m, MathF.Atan2(shutter.CrossX, shutter.CrossZ) * 180 / MathF.PI);
            Attack(m.Killer);
            m.Tick(WindUpTicks + 1);
            Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 0));
            // The same swing with the shutter up hits.
            m.F.Server.SetShutterForTests(0, ShutterState.Up);
            m.Tick(25);
            Attack(m.Killer);
            m.Tick(WindUpTicks + 1);
            Assert.Equal((byte)PlayerStatus.Lagging, StatusOf(m, 0));
        }
    }

    [Fact]
    public void InvulnerableAndLaggingSurvivorsAreNotCandidates()
    {
        using var m = Facing();
        m.F.Server.SetStatusForTests(m[0].Id, PlayerStatus.Lagging);
        m.Tick();
        Face(m, 0);
        Attack(m.Killer);
        m.Tick(WindUpTicks + 1);
        Assert.Equal((byte)PlayerStatus.Lagging, StatusOf(m, 0));
        Assert.Equal(0, m.Truth(m[0]).CaptureCount); // set directly: a swing never advanced it
        // Back to Normal (the rescue path) grants 2 s of immunity: a swing inside it misses.
        m.F.Server.SetStatusForTests(m[0].Id, PlayerStatus.Normal);
        m.Tick(25); // past the 1.0 s wind-up + miss recovery
        Attack(m.Killer);
        m.Tick(WindUpTicks + 1);
        Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 0));
    }

    [Fact]
    public void OneCapturePerSwingTheNearestCandidate()
    {
        using var m = Match();
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 24);
        m.F.Server.PlaceForTests(m[0].Id, 5.6f, 26);   // 2.09 m
        m.F.Server.PlaceForTests(m[1].Id, 4.6f, 25.6f); // 1.65 m
        m.Tick(25);
        Face(m, 0);
        Attack(m.Killer);
        m.Tick(WindUpTicks + 1);
        Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 0));
        Assert.Equal((byte)PlayerStatus.Lagging, StatusOf(m, 1));
    }

    // Steps of the Killer moving +x in the open south street while it swings once (no Survivor near: a miss, or a hit).
    private static List<float> SwingSteps(bool hit)
    {
        using var m = Match();
        m.F.Server.PlaceForTests(m.Killer.Id, 3, 2);
        m.F.Server.PlaceForTests(m[0].Id, hit ? 4.5f : 5, hit ? 2 : 36);
        m.Tick(25);
        var steps = new List<float>();
        float x = m.Killer.Self().X;
        Face(m, 90, 1, 0);
        Attack(m.Killer);
        for (int i = 0; i < 40; i++)
        {
            Face(m, 90, 1, 0);
            m.Tick();
            float now = m.Killer.Self().X;
            steps.Add(now - x);
            x = now;
        }
        return steps;
    }

    [Fact]
    public void WindUpIsHalfSpeedAndAMissRecoversAt48PercentForPointEightSeconds()
    {
        var steps = SwingSteps(hit: false);
        Assert.InRange(steps[0], Step * 0.999f, Step * 1.001f); // the press tick moved at full speed
        for (int i = 1; i <= 4; i++) Assert.InRange(steps[i], Step * 0.499f, Step * 0.501f);
        for (int i = 5; i <= 20; i++) Assert.InRange(steps[i], Step * 0.479f, Step * 0.481f);
        for (int i = 21; i < 40; i++) Assert.InRange(steps[i], Step * 0.999f, Step * 1.001f);
    }

    [Fact]
    public void AHitRecoversAtZeroSpeedForPointSixSeconds()
    {
        var steps = SwingSteps(hit: true);
        for (int i = 1; i <= 4; i++) Assert.InRange(steps[i], Step * 0.499f, Step * 0.501f);
        for (int i = 5; i <= 16; i++) Assert.Equal(0f, steps[i]);
        for (int i = 17; i < 40; i++) Assert.InRange(steps[i], Step * 0.999f, Step * 1.001f);
    }

    [Fact]
    public void NoNewSwingUntilTheRecoveryEnds()
    {
        using var m = Facing(distance: 3);
        Face(m, 0);
        Attack(m.Killer);
        m.Tick();
        for (int i = 0; i < 19; i++)
        {
            Attack(m.Killer); // every press inside wind-up + miss recovery (1.0 s) is dropped
            m.Tick();
            Assert.True(m.F.Server.SwingingForTests(m.Killer.Id), "tick " + i);
        }
        m.Tick(2);
        Assert.False(m.F.Server.SwingingForTests(m.Killer.Id));
    }

    [Fact]
    public void AStunDuringTheWindUpInterruptsWithoutAHit()
    {
        using var m = Facing();
        var log = new List<string>();
        m.F.Server.Log += log.Add;
        Face(m, 0);
        Attack(m.Killer);
        m.Tick(2);
        m.F.Server.StunForTests(m.Killer.Id, GameConstants.ShutterStunSeconds);
        m.Tick(WindUpTicks + 2);
        Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 0));
        Assert.False(m.F.Server.SwingingForTests(m.Killer.Id));
        Assert.Contains(log, l => l.Contains("result=interrupted", StringComparison.Ordinal));
        // A stunned Killer cannot start a swing either.
        Attack(m.Killer);
        m.Tick();
        Assert.False(m.F.Server.SwingingForTests(m.Killer.Id));
    }

    [Fact]
    public void StaleSeqIsDroppedAndPressesAreRateLimited()
    {
        using var m = Facing(distance: 3);
        Face(m, 0);
        Attack(m.Killer, 1000);
        m.Tick(30); // swing and its 1.0 s wind-up + miss recovery are over
        Assert.False(m.F.Server.SwingingForTests(m.Killer.Id));
        Attack(m.Killer, 1000); // same Seq
        Attack(m.Killer, 999);  // older Seq
        m.Tick();
        Assert.False(m.F.Server.SwingingForTests(m.Killer.Id));
        // Five presses in one tick: the first swings, all five count against the one-second budget.
        for (uint s = 1001; s <= 1005; s++) Attack(m.Killer, s);
        m.Tick();
        Assert.True(m.F.Server.SwingingForTests(m.Killer.Id));
        m.F.Server.StunForTests(m.Killer.Id, 0.06); // cuts the wind-up; the stun ends next tick
        m.Tick(2);
        Assert.False(m.F.Server.SwingingForTests(m.Killer.Id));
        Attack(m.Killer, 1006); // within the same second: over the limit of five
        m.Tick();
        Assert.False(m.F.Server.SwingingForTests(m.Killer.Id));
        m.Tick(16);
        Attack(m.Killer, 1007);
        m.Tick();
        Assert.True(m.F.Server.SwingingForTests(m.Killer.Id));
    }

    // a2 (F3): the Killer client reads an eliminating hit (the entry is removed, no CaptureConfirm) from the length of its
    // own Swinging flag in WorldState ticks: KillerSwingWatch expects exactly 16 after a hit and 20 after a miss.
    [Theory]
    [InlineData(2.0f, 16)]
    [InlineData(3.5f, 20)]
    public void TheOwnSwingingFlagLastsSixteenTicksAfterAHitAndTwentyAfterAMiss(float distance, int ticks)
    {
        using var m = Facing(distance);
        Face(m, 0);
        Attack(m.Killer);
        m.Tick();
        uint rise = m.Killer.Last<S2C_WorldState>().Tick;
        Assert.NotEqual(0, m.Killer.Self().Chase & (byte)ChaseFlags.Swinging);
        int n = 0;
        while ((m.Killer.Self().Chase & (byte)ChaseFlags.Swinging) != 0 && n++ < 60) m.Tick();
        Assert.Equal(ticks, (int)(m.Killer.Last<S2C_WorldState>().Tick - rise));
        Assert.Equal(distance < GameConstants.SwingRange ? (byte)PlayerStatus.Lagging : (byte)PlayerStatus.Normal, StatusOf(m, 0));
    }

    [Fact]
    public void SwingingIsShownToSurvivorsWhoSeeTheKillerAndTheKillerLearnsNothingNew()
    {
        using var m = Facing(distance: 3);
        Face(m, 0);
        Attack(m.Killer);
        m.Tick();
        var killerSelf = m.Killer.Self();
        Assert.NotEqual(0, killerSelf.Chase & (byte)ChaseFlags.Swinging);
        // Survivor 0 is in the open 3 m in front: its world carries the Killer entry with the flag.
        var seen = m[0].Last<S2C_WorldState>().Players.Single(p => p.Role == (byte)Role.Killer);
        Assert.NotEqual(0, seen.Chase & (byte)ChaseFlags.Swinging);
        // The Killer's view has no Survivor entry (both Silent), and no survivor-shaped entry ever carries Chase.
        var killerWorld = m.Killer.Last<S2C_WorldState>();
        Assert.DoesNotContain(killerWorld.Players, p => p.Role == (byte)Role.Survivor);
        m.Tick(WindUpTicks + 20);
        Assert.Equal(0, m.Killer.Self().Chase & (byte)ChaseFlags.Swinging);
    }

    [Fact]
    public void AttackFromASurvivorOrInTheLobbyDoesNothing()
    {
        using var m = Facing(distance: 0.8f);
        Attack(m[0]);
        Attack(m[1]);
        m.Tick(WindUpTicks + 2);
        Assert.Equal((byte)PlayerStatus.Normal, StatusOf(m, 0));
        Assert.False(m.F.Server.SwingingForTests(m[0].Id));
    }

    // ------------------------------------------------------------------ skill choice

    [Fact]
    public void AHumansValidChoiceForItsRoleWinsOtherwiseTheDrawStands()
    {
        foreach (byte skill in new byte[] { 0, 1, 2, 3 })
        {
            using var f = new CoreFixture(seed: 7);
            var host = f.Join("Host");
            var other = f.Join("Other");
            host.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Killer });
            host.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(Role.Killer, skill) });
            other.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(Role.Survivor, SkillIds.WhiteNoise) });
            f.Tick();
            // a2: each recipient sees only its own choice.
            Assert.Equal(SkillIds.Choice(Role.Killer, skill), host.Last<S2C_LobbyState>().Slots[0].SkillChoice);
            Assert.Equal(SkillIds.Choice(Role.Survivor, SkillIds.WhiteNoise), other.Last<S2C_LobbyState>().Slots[1].SkillChoice);
            host.Send(new C2S_StartMatch());
            f.Tick();
            Assert.Equal(skill, host.Last<S2C_MatchStart>().SkillId);
            Assert.Equal(SkillIds.WhiteNoise, other.Last<S2C_MatchStart>().SkillId);
        }
    }

    [Fact]
    public void EachLobbyRecipientReceivesOnlyItsOwnSkillChoice()
    {
        using var f = new CoreFixture(seed: 5, bots: 1);
        var host = f.Join("Host");
        var a = f.Join("A");
        var b = f.Join("B");
        host.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(Role.Killer, SkillIds.PacketTrace) });
        a.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(Role.Survivor, SkillIds.WhiteNoise) });
        b.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(Role.Survivor, 0) });
        f.Tick();
        var clients = new[] { host, a, b };
        byte[] chosen = { SkillIds.Choice(Role.Killer, SkillIds.PacketTrace), SkillIds.Choice(Role.Survivor, SkillIds.WhiteNoise), SkillIds.Choice(Role.Survivor, 0) };
        for (int c = 0; c < clients.Length; c++)
            foreach (var lobby in clients[c].All<S2C_LobbyState>())
                foreach (var slot in lobby.Slots.Where(s => s.PlayerId != 0))
                    if (slot.PlayerId == clients[c].Id) Assert.Contains(slot.SkillChoice, new[] { SkillIds.NoChoice, chosen[c] });
                    else Assert.Equal(SkillIds.NoChoice, slot.SkillChoice);
        for (int c = 0; c < clients.Length; c++)
            Assert.Equal(chosen[c], clients[c].Last<S2C_LobbyState>().Slots.Single(s => s.PlayerId == clients[c].Id).SkillChoice);
    }

    [Fact]
    public void SkillChoicesAreThrottledToFourPerSecondPerPlayer()
    {
        using var f = new CoreFixture(seed: 5);
        var host = f.Join("Host");
        var other = f.Join("Other");
        f.Tick();
        int before = other.All<S2C_LobbyState>().Count();
        // Ten different choices in one tick: four are applied (four rebroadcasts), the rest dropped.
        for (int i = 0; i < 10; i++) host.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(i % 2 == 0 ? Role.Killer : Role.Survivor, (byte)(i / 2 % 4)) });
        f.Tick();
        Assert.Equal(before + GameServer.MaxSkillChoicesPerSecond, other.All<S2C_LobbyState>().Count());
        byte fourth = SkillIds.Choice(Role.Survivor, 1);
        Assert.Equal(fourth, host.Last<S2C_LobbyState>().Slots[0].SkillChoice);
        // A second later the budget is back.
        f.Tick(1.05f);
        host.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(Role.Killer, SkillIds.Overclock) });
        f.Tick();
        Assert.Equal(SkillIds.Choice(Role.Killer, SkillIds.Overclock), host.Last<S2C_LobbyState>().Slots[0].SkillChoice);
    }

    [Fact]
    public void AChoiceForTheOtherRoleOrNoneGetsTheRandomDrawAndBotsShowNoChoice()
    {
        var drawn = new HashSet<byte>();
        for (int seed = 1; seed <= 40; seed++)
        {
            using var f = new CoreFixture(seed: seed, bots: 2);
            var host = f.Join("Host");
            host.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
            host.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(Role.Killer, SkillIds.Overclock) }); // wrong role
            f.Tick();
            Assert.All(host.Last<S2C_LobbyState>().Slots.Where(s => s.IsBot == 1), s => Assert.Equal(SkillIds.NoChoice, s.SkillChoice));
            host.Send(new C2S_StartMatch());
            f.Tick();
            var start = host.Last<S2C_MatchStart>();
            Assert.Equal((byte)Role.Survivor, start.Role);
            Assert.InRange(start.SkillId, 0, SkillIds.SurvivorSkillCount - 1);
            drawn.Add(start.SkillId);
        }
        Assert.True(drawn.Count >= 3, "random survivor skills drawn: " + string.Join(",", drawn));
    }

    [Fact]
    public void ChoicesAreLobbyOnlyAndUndefinedValuesAreAWireError()
    {
        using var m = Match(1, SkillIds.PingScanner);
        m.Killer.Send(new C2S_ChooseSkill { SkillId = SkillIds.Choice(Role.Killer, SkillIds.Overclock) });
        m.Tick();
        Assert.Equal(SkillIds.NoChoice, m.Killer.All<S2C_LobbyState>().LastOrDefault()?.Slots[0].SkillChoice ?? SkillIds.NoChoice);
        foreach (byte bad in new byte[] { 5, 0x7F, 0x84, 0xFE })
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode((ushort)PacketId.C2S_ChooseSkill, new[] { bad }));
        var slot = new Slot { SkillChoice = 0x90 };
        Assert.Throws<PacketFormatException>(() => PacketCodec.Encode(new S2C_LobbyState { Slots = Enumerable.Repeat(slot, 8).ToArray() }));
    }

    // ------------------------------------------------------------------ reworked Killer skills

    [Fact]
    public void FirewallBlockLocksTheTerminalWithTheMostProgressForEightSecondsWithoutASwitchInProgress()
    {
        using var m = Match(1, SkillIds.FirewallBlock);
        var server = m.F.Server;
        var terminals = m[0].Last<S2C_WorldState>().Terminals;
        server.SetTerminalProgressForTests(0, 20);
        server.SetTerminalProgressForTests(1, 40);
        // The Survivor hacks terminal 1.
        server.PlaceForTests(m[0].Id, terminals[1].X + 0.8f, terminals[1].Z);
        m[0].Send(new C2S_Interact { Active = 1 });
        m.Tick(5);
        double before = server.TerminalProgressForTests(1);
        Assert.True(before > 40);
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
        m.Tick();
        var effect = m[0].All<S2C_SkillEffect>().Last();
        Assert.Equal((terminals[1].X, terminals[1].Z, SkillIds.FirewallTerminalSeconds), (effect.X, effect.Z, effect.Duration));
        Assert.Equal(SkillIds.FirewallBlock, m.Killer.All<S2C_SkillEffect>().Last().SkillId);
        double locked = server.TerminalProgressForTests(1);
        int lockedTicks = 0;
        for (int i = 0; i < 200; i++)
        {
            bool survivorSees = (m[0].Last<S2C_WorldState>().Terminals[1].Flags & (byte)TerminalFlags.Locked) != 0;
            bool killerSees = (m.Killer.Last<S2C_WorldState>().Terminals[1].Flags & (byte)TerminalFlags.Locked) != 0;
            Assert.Equal(survivorSees, killerSees);
            Assert.Equal(0, m[0].Last<S2C_WorldState>().Terminals[0].Flags & (byte)TerminalFlags.Locked);
            if (!survivorSees) break;
            lockedTicks++;
            Assert.Equal(locked, server.TerminalProgressForTests(1), 6); // nobody hacks a locked terminal
            m.Tick();
        }
        Assert.InRange(lockedTicks, 159, 161);
        m.Tick(5);
        Assert.True(server.TerminalProgressForTests(1) > locked); // the held Interact hacks again
    }

    [Fact]
    public void ATerminalCannotBeLockedAgainForTwentySecondsAfterItsLockEndsButAnotherCan()
    {
        using var m = Match(1, SkillIds.FirewallBlock);
        var server = m.F.Server;
        var terminals = m[0].Last<S2C_WorldState>().Terminals;
        server.SetTerminalProgressForTests(0, 20);
        server.SetTerminalProgressForTests(1, 40);
        (float, float) Locked()
        {
            var effect = m.Killer.All<S2C_SkillEffect>().Last();
            return (effect.X, effect.Z);
        }
        int Fire()
        {
            int effects = m.Killer.All<S2C_SkillEffect>().Count();
            m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.FirewallBlock });
            m.Tick();
            return m.Killer.All<S2C_SkillEffect>().Count() - effects;
        }
        // t = 0: terminal 1 (most progress) is locked for 8 s.
        Assert.Equal(1, Fire());
        Assert.Equal((terminals[1].X, terminals[1].Z), Locked());
        // t = 12.5 s (cooldown over, lock over 4.5 s ago): terminal 1 is still in its relock window, so terminal 0 is locked.
        m.Tick(249);
        Assert.Equal(1, Fire());
        Assert.Equal((terminals[0].X, terminals[0].Z), Locked());
        // t = 25 s: terminal 1 relocks at 28 s, terminal 0 at 40.5 s: nothing to lock, refused with the reason.
        m.Tick(249);
        Assert.Equal(0, Fire());
        Assert.Equal((byte)SkillRejectReason.NoSwitchInProgress, m.Killer.All<S2C_SkillRejected>().Last().Reason);
        // t = 27.5 s: still refused; t = 28.5 s: terminal 1 locks again.
        m.Tick(49);
        Assert.Equal(0, Fire());
        m.Tick(19);
        Assert.Equal(1, Fire());
        Assert.Equal((terminals[1].X, terminals[1].Z), Locked());
    }

    [Fact]
    public void PacketTraceForcesSurvivorsWithinTwelveMetresSignalForTwoAndAHalfSeconds()
    {
        using var m = Match(2, SkillIds.PacketTrace);
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 15);
        m.F.Server.PlaceForTests(m[0].Id, 5, 26);  // 11 m
        m.F.Server.PlaceForTests(m[1].Id, 5, 28);  // 13 m
        m.Tick(25);
        Assert.Null(m.KillerSees(m[0]));
        m.Killer.Send(new C2S_UseSkill { SkillId = SkillIds.PacketTrace });
        m.Tick();
        var own = Assert.Single(m.Killer.All<S2C_SkillEffect>());
        Assert.Equal((m.Killer.Id, SkillIds.PacketTrace, 5f, 15f, SkillIds.PacketTraceSeconds), (own.CasterId, own.SkillId, own.X, own.Z, own.Duration));
        var traced = Assert.Single(m[0].All<S2C_SkillEffect>());
        Assert.Equal((0f, 0f), (traced.X, traced.Z));
        Assert.Empty(m[1].All<S2C_SkillEffect>());
        int frames = 0;
        while (m.KillerSees(m[0]) != null && frames < 200) { Assert.Null(m.KillerSees(m[1])); m.Tick(); frames++; }
        Assert.InRange(frames, 49, 51);
    }

    // ------------------------------------------------------------------ the bot Killer

    [Fact]
    public void TheBotKillerSwingsOnlyAtANormalBlipCloseInFrontAndAimsAtIt()
    {
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = 3 });
        KillerBotView View(float bx, float bz, bool lagging)
        {
            var view = new KillerBotView { Time = 10, X = 40, Z = 43.5f, Yaw = 0, Speed = GameConstants.KillerSpeed };
            view.Blips.Add(new KillerBotView.Blip { X = bx, Z = bz, Lagging = lagging });
            return view;
        }
        var random = new Random(1);
        var close = new KillerBrain().Update(View(40.8f, 45.2f, false), server.Nav, server.Map, random);
        Assert.True(close.Attack);
        Assert.InRange(close.Yaw, 25f, 26f); // atan2(0.8, 1.7)
        Assert.False(new KillerBrain().Update(View(40, 47.1f, false), server.Nav, server.Map, random).Attack); // 3.6 m (INTEG-R: trigger 2.75 m)
        Assert.False(new KillerBrain().Update(View(40, 45, true), server.Nav, server.Map, random).Attack);    // Lagging
    }

    [Fact]
    public void BotKillersCaptureOnlyThroughSwingsInABotMatch()
    {
        using var f = new CoreFixture(bots: 7, autostart: true, seed: 11);
        f.Server.ContactSwingForTests = false;
        var log = new List<string>();
        f.Server.Log += log.Add;
        for (int i = 0; i < 20 * 300 && !log.Any(l => l.StartsWith("match end", StringComparison.Ordinal)); i++) f.Tick();
        int captures = log.Count(l => l.Contains("reason=capture", StringComparison.Ordinal) || l.Contains("reason=third capture", StringComparison.Ordinal));
        int hits = log.Count(l => l.Contains("result=hit", StringComparison.Ordinal));
        Assert.True(captures > 0, $"no capture in 300 s; swings: {string.Join(" | ", log.Where(l => l.StartsWith("swing")).Take(10))}; {log.Count(l => l.StartsWith("swing"))}; {string.Join(" | ", log.Take(5))}");
        Assert.Equal(captures, hits);
    }
}
