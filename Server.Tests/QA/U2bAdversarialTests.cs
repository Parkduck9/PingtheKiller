using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.QA;

// Independent QA tests for unit U2b: capture, Lagging, rescue, hacking, exit, win order, GameResult / lobby return.
// Expected values derive from docs/briefs/U2b-a1.md and docs/GDD.md sections 3-4 (not from the implementation).
// Deterministic Tick(dt) over InMemoryTransport. Reflection (Spy) only arranges a scenario (teleport / state set-up).
public sealed class U2bAdversarialTests
{
    private const float Speed = GameConstants.SurvivorSpeed;
    private static bool Is(PlayerState p, PlayerStatus s) => p.Status == (byte)s;

    // ------------------------------------------------------------------ capture / Lagging / execution / invulnerability

    [Fact]
    public void Capture_FirstContactLagsWithSixtySecondTimer_ThenTimeoutEliminatesAtSixtySeconds()
    {
        using var r = new QaRig(2);
        r.Place(r.S[0], 13, 10);
        r.Place(r.K, 11.9f, 10); // 1.1 m: inside the 1.2 m capture radius
        r.Tick();
        var t = r.Truth(r.S[0]);
        Assert.True(Is(t, PlayerStatus.Lagging));
        Assert.Equal(1, t.CaptureCount);
        Assert.InRange(t.StatusTimer, 59.9f, 60.0f);
        var seen = r.KillerSees(r.S[0])!.Value;
        Assert.Equal((byte)PlayerFlags.Signal, seen.Flags);
        Assert.True(Is(seen, PlayerStatus.Lagging)); // Lagging stays visible to the killer (GDD 3.3)
        // U2c migration: the killer view is anonymous, so the survivor-side truth keeps CaptureCount 1 and the 60 s timer
        // (asserted above) while the killer-bound entry shows neither.
        Assert.Equal(0, seen.CaptureCount);
        Assert.Equal(0f, seen.StatusTimer);
        Assert.Equal(0, seen.Id);
        Assert.Equal(0, r.Truth(r.S[1]).CaptureCount);
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Normal));

        double captureAt = r.Elapsed;
        r.Place(r.K, 5, 5);
        // An idle Lagging survivor never goes Silent: it stays in the killer frame every tick, with a falling timer.
        for (int i = 0; i < 40; i++)
        {
            r.Tick();
            Assert.NotNull(r.KillerSees(r.S[0]));
        }
        Assert.InRange(r.Truth(r.S[0]).StatusTimer, 57.9f, 58.1f);

        r.Dt = 1f;
        while (r.Truth(r.S[0]).StatusTimer > 2f) r.Tick();
        r.Dt = 0.05f;
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        for (int i = 0; i < 200 && r.Truth(r.S[0]).CaptureCount == 1; i++) r.Tick();
        double lived = r.Elapsed - captureAt;
        Assert.InRange(lived, 59.9, 60.11);
        // GDD v2 §4: the first timeout advances to the second stage (30 s); the second timeout eliminates.
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        r.Dt = 1f;
        while (r.Truth(r.S[0]).StatusTimer > 2f) r.Tick();
        r.Dt = 0.05f;
        for (int i = 0; i < 200 && Is(r.Truth(r.S[0]), PlayerStatus.Lagging); i++) r.Tick();
        Assert.InRange(r.Elapsed - captureAt, 89.9, 90.11);
        var dead = r.Truth(r.S[0]);
        Assert.True(Is(dead, PlayerStatus.Eliminated));
        Assert.Equal(0f, dead.StatusTimer);
        Assert.Null(r.KillerSees(r.S[0])); // Eliminated survivors are omitted from the killer view
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Normal));
        Assert.False(r.K.Has<S2C_GameResult>()); // one survivor is still Normal
    }

    [Fact]
    public void Capture_LaggingSurvivorMovesAtQuarterSpeedAndSelfCannotHackOrUseSkills()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.S[0], SkillIds.DummyPacket);
        r.Place(r.S[0], 10, 24.4f); // within 1.5 m of terminal 0 (10,25)
        r.MakeLagging(r.S[0]);
        r.Interact(r.S[0], true);
        r.Use(r.S[0], SkillIds.DummyPacket);
        r.Tick(40);
        Assert.Equal(0, r.SW.Terminals[0].Progress);
        Assert.Empty(r.S[1].Since<S2C_SkillEffect>(0));
        float x0 = r.Truth(r.S[0]).X;
        for (int i = 0; i < 20; i++) { r.S[0].Move(1, 0); r.Tick(); }
        Assert.InRange(r.Truth(r.S[0]).X - x0, 0.95f, 1.05f); // 1.0 m/s for 1 s
    }

    [Fact]
    public void Capture_ContinuingContactNeverExecutes_FreshTouchExecutes_Characterisation()
    {
        // Judgement call 1 of the implementer: the contact that captured a survivor never executes it. A literal reading of
        // brief rule 3 would execute on the very next tick (making every first capture an elimination), so the call is sound;
        // this test pins the behaviour and the exact "fresh touch" semantics (separate beyond 1.2 m for one tick, touch again).
        using var r = new QaRig(2);
        r.Place(r.S[0], 13, 10);
        r.Place(r.K, 11.9f, 10);
        r.Tick();
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        r.Tick(100); // 5 s of continuous contact
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        r.Place(r.K, 14.5f, 10); // 1.5 m: out of capture range
        r.Tick();
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        r.Place(r.K, 12.0f, 10); // 1.0 m: fresh touch
        r.Tick();
        // GDD v2 §4 (2026-11-03): no execution; a fresh touch changes nothing either.
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        Assert.Equal(1, r.Truth(r.S[0]).CaptureCount);
    }

    [Fact]
    public void Capture_ExecutionGraceTwoSecondsAfterCapture_GddRuleAdded1027_NotInTheBrief()
    {
        // docs/GDD.md section 3.3 (row added 10/27 after the U2b brief was written): "for 2 s after the capture nobody can
        // be executed, and an execution needs the killer to leave 1.2 m and touch again". The fresh-touch half is implemented
        // (see the characterisation test above); the 2 s grace is not: a Lagging survivor is executed by a re-touch 0.1 s
        // after the capture. This test asserts the GDD rule and therefore FAILS on the current implementation (finding F0).
        using var r = new QaRig(2);
        r.Place(r.S[0], 13, 10);
        r.Place(r.K, 11.9f, 10);
        r.Tick(); // capture tick c
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        for (int i = 1; i <= 34; i++) // 1.7 s: leave and touch again, over and over
        {
            r.Place(r.K, i % 2 == 1 ? 14.5f : 12.0f, 10);
            r.Tick();
            Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging), $"executed {i * 0.05:0.00} s after the capture, inside the 2 s grace");
        }
        r.Place(r.K, 14.5f, 10);
        r.Tick(8); // past 2 s
        r.Place(r.K, 12.0f, 10); // fresh touch after the old grace
        r.Tick();
        // GDD v2 §4 (2026-11-03): no execution and no grace; the fresh touch changes nothing.
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        Assert.Equal(1, r.Truth(r.S[0]).CaptureCount);
    }

    [Fact]
    public void Capture_ExactRadiusEdges_At1_2mInside_At1_25mOutside()
    {
        using var r = new QaRig(2);
        r.Place(r.S[0], 13.2f, 10);
        r.Place(r.S[1], 30, 30);
        r.Place(r.K, 12f, 10); // exactly 1.2 m
        r.Tick();
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        using var r2 = new QaRig(2);
        r2.Place(r2.S[0], 13.25f, 10);
        r2.Place(r2.K, 12f, 10); // 1.25 m
        r2.Tick(20);
        Assert.True(Is(r2.Truth(r2.S[0]), PlayerStatus.Normal));
    }

    [Fact]
    public void Invulnerability_AfterRescueBlocksCaptureForTwoSeconds_ThenSecondCaptureEliminates()
    {
        using var r = new QaRig(2);
        r.Place(r.S[0], 10, 10);
        r.MakeLagging(r.S[0]);
        r.Place(r.S[1], 11, 10);
        int guard = 0;
        do { r.Tick(); guard++; } while (!Is(r.Truth(r.S[0]), PlayerStatus.Normal) && guard < 200);
        Assert.True(guard is >= 58 and <= 62, "3 s rescue took " + guard + " ticks");
        Assert.Equal(1, r.Truth(r.S[0]).CaptureCount); // stays 1 after a rescue
        // Killer is 1.05 m from the rescued survivor and 2.05 m from the rescuer.
        r.Place(r.K, 8.95f, 10);
        int k = 0;
        while (Is(r.Truth(r.S[0]), PlayerStatus.Normal) && k < 120) { r.Tick(); k++; }
        // GDD v2 §4: the second capture lags again, with the 30 s timer.
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        Assert.Equal(2, r.Truth(r.S[0]).CaptureCount);
        Assert.InRange(k, 39, 41); // 2 s of invulnerability = 40 ticks
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Normal));
    }

    // ------------------------------------------------------------------ rescue

    [Fact]
    public void Rescue_TwoRescuersDoNotSpeedItUp_AndRescuersAreSignalWithRescuingFlag()
    {
        using var r = new QaRig(3);
        r.Place(r.S[0], 10, 10);
        r.MakeLagging(r.S[0]);
        r.Place(r.S[1], 11, 10);
        r.Place(r.S[2], 10, 11);
        int n = 0;
        while (n < 200)
        {
            r.Tick();
            n++;
            if (Is(r.Truth(r.S[0]), PlayerStatus.Normal)) break;
            if (Is(r.Truth(r.S[0]), PlayerStatus.Lagging))
            {
                foreach (var s in new[] { r.S[1], r.S[2] })
                {
                    Assert.NotEqual(0, r.Truth(s).Flags & (byte)PlayerFlags.Rescuing);
                    var seen = r.KillerSees(s);
                    Assert.NotNull(seen); // idle rescuers are Signal
                    Assert.Equal((byte)PlayerFlags.Signal, seen!.Value.Flags); // killer sees only Signal, never Rescuing
                }
            }
        }
        Assert.InRange(n, 58, 62);
    }

    [Fact]
    public void Rescue_ContactBrokenForOneTickRestartsFromZero()
    {
        using var r = new QaRig(2);
        r.Place(r.S[0], 10, 10);
        r.MakeLagging(r.S[0]);
        r.Place(r.S[1], 11, 10);
        r.Tick(55); // 2.75 s of contact
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        r.Place(r.S[1], 12.5f, 10); // 2.5 m: out of range for exactly one tick
        r.Tick();
        r.Place(r.S[1], 11, 10);
        int n = 0;
        do { r.Tick(); n++; } while (!Is(r.Truth(r.S[0]), PlayerStatus.Normal) && n < 200);
        Assert.InRange(n, 59, 62); // a full fresh 3 s, not the remaining 0.25 s
    }

    [Fact]
    public void Rescue_OneOfTwoRescuersCapturedMidRescue_ContinuousSecondRescuerStillFinishesOnTime()
    {
        using var r = new QaRig(3);
        r.Place(r.S[0], 10, 10);
        r.MakeLagging(r.S[0]);
        r.Place(r.S[1], 11.3f, 10);
        r.Place(r.S[2], 10, 11.4f);
        r.Tick(20);
        r.Place(r.K, 12.4f, 10); // 1.1 m from S1 only
        r.Tick();
        r.Place(r.K, 5, 5);
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        int n = 21;
        while (!Is(r.Truth(r.S[0]), PlayerStatus.Normal) && n < 300) { r.Tick(); n++; }
        Assert.InRange(n, 58, 64); // S2 was in contact the whole time
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
    }

    [Fact]
    public void Rescue_SoleRescuerBecomesLaggingMidRescue_ProgressResetsAndNewRescuerNeedsAFullThreeSeconds()
    {
        using var r = new QaRig(3);
        r.Place(r.S[0], 10, 10);
        r.MakeLagging(r.S[0]);
        r.Place(r.S[1], 11.3f, 10);
        r.Place(r.S[2], 40, 55);
        r.Tick(30); // 1.5 s of progress
        r.Place(r.K, 12.4f, 10);
        r.Tick();
        r.Place(r.K, 5, 5);
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        r.Tick(8);
        r.Place(r.S[2], 10, 11.4f); // arrives; contact starts now
        int n = 0;
        do { r.Tick(); n++; } while (!Is(r.Truth(r.S[0]), PlayerStatus.Normal) && n < 300);
        Assert.InRange(n, 59, 63); // fresh 3 s from arrival (the 1.5 s earned before the capture is gone)
    }

    // ------------------------------------------------------------------ hacking

    [Fact]
    public void Hacking_SingleHackerProgressIsMonotonicFlooredPercent_CompletesAtTenSeconds_SignalOnlyWhileHacking()
    {
        using var r = new QaRig(1);
        r.Place(r.S[0], 10, 25.6f);
        r.Interact(r.S[0], true);
        int prev = 0, complete = -1;
        for (int n = 1; n <= 1830; n++) // 11/17 (R2): the passive part takes 90 s solo, twice the old 45 s.
        {
            r.Tick();
            int progress = r.SW.Terminals[0].Progress;
            Assert.Equal(progress, r.SW.Terminals[0].Progress); // same value for both roles
            Assert.True(progress >= prev, "progress must never fall");
            if (progress == 100 && complete < 0) complete = n;
            if (complete < 0)
            {
                Assert.InRange(progress, (int)MathF.Floor(n / 18f) - 1, Math.Min(99, (int)MathF.Floor(n / 18f) + 1));
                var seen = r.KillerSees(r.S[0]);
                Assert.NotNull(seen);
                Assert.Equal((byte)PlayerFlags.Signal, seen!.Value.Flags);
                Assert.NotEqual(0, r.Truth(r.S[0]).Flags & (byte)PlayerFlags.Hacking);
                // U2c (N9): the killer only learns done / not done, never the percentage.
                Assert.Equal(0, r.KW.Terminals[0].Progress);
            }
            prev = progress;
        }
        Assert.InRange(complete, 1799, 1802); // GDD v2 (11/17 R2): 90 s passive
        Assert.Equal(100, r.KW.Terminals[0].Progress);
        Assert.Null(r.KillerSees(r.S[0])); // nothing left to hack, not moving: Silent again
        Assert.Equal(0, r.Truth(r.S[0]).Flags & (byte)PlayerFlags.Hacking);
    }

    [Theory]
    // GDD v2 §2: 1.0x / 1.8x / 2.4x of the 90 s solo passive time (11/17 R2).
    [InlineData(1, 1800)]
    [InlineData(2, 1000)]
    [InlineData(3, 750)]
    [InlineData(4, 750)]
    [InlineData(6, 750)]
    public void Hacking_CoHackRateIsCappedAtThreeHackers(int survivors, int expectedTicks)
    {
        using var r = new QaRig(survivors);
        for (int i = 0; i < survivors; i++)
        {
            double a = i * Math.PI * 2 / survivors;
            r.Place(r.S[i], 10 + 0.7f * (float)Math.Cos(a), 25 + 0.7f * (float)Math.Sin(a));
            r.Interact(r.S[i], true);
        }
        int n = 0;
        while (r.SW.Terminals[0].Progress < 100 && n < 2000) { r.Tick(); n++; }
        Assert.InRange(n, expectedTicks - 1, expectedTicks + 1);
    }

    [Fact]
    public void Hacking_ProgressKeptAfterInterruptionAndResumesWhereItStopped()
    {
        using var r = new QaRig(1);
        r.Place(r.S[0], 10, 25.6f);
        r.Interact(r.S[0], true);
        r.Tick(720); // 36 s of the 90 s passive time (11/17 R2)
        Assert.InRange((int)r.SW.Terminals[0].Progress, 39, 41);
        r.Interact(r.S[0], false);
        r.Tick(30);
        Assert.InRange((int)r.SW.Terminals[0].Progress, 39, 41);
        r.Place(r.S[0], 30, 30); // wander off for 5 s
        r.Tick(100);
        Assert.InRange((int)r.SW.Terminals[0].Progress, 39, 41);
        r.Place(r.S[0], 10, 25.6f);
        r.Interact(r.S[0], true);
        int n = 0;
        while (r.SW.Terminals[0].Progress < 100 && n < 1400) { r.Tick(); n++; }
        Assert.InRange(n, 1078, 1082); // the remaining 54 s
    }

    [Fact]
    public void Hacking_HackerCapturedMidHack_RateDropsAndInteractIsNotAutoResumedAfterRescue()
    {
        using var r = new QaRig(2);
        r.Place(r.S[0], 10, 25.6f);
        r.Place(r.S[1], 10.6f, 25.6f);
        r.Interact(r.S[0], true);
        r.Interact(r.S[1], true);
        r.Tick(200); // 2 hackers (1.8x): 10 s = 20 % (11/17 R2: 90 s passive)
        Assert.InRange((int)r.SW.Terminals[0].Progress, 19, 21);
        r.Place(r.K, 11.7f, 25.6f); // 1.1 m from S1, 1.7 m from S0
        r.Tick();
        r.Place(r.K, 5, 5);
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        // S0 keeps hacking alone (rate 1x) and, in range, automatically rescues S1 over 3 s.
        // The capture tick already counts as the first rescue-contact tick (rescue runs after capture), so 3 s = 59 more ticks.
        r.Tick(57);
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        r.Tick(4);
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Normal));
        int atRescue = r.SW.Terminals[0].Progress;
        Assert.InRange(atRescue, 22, 24); // 20 % + ~3 s at 1x (1.1 %/s)
        r.Tick(180);
        Assert.InRange(r.SW.Terminals[0].Progress - atRescue, 9, 11); // S1 does NOT hack again until it presses Interact
        r.Interact(r.S[1], true);
        r.Tick(1);
        int resumed = r.SW.Terminals[0].Progress;
        r.Tick(100);
        Assert.InRange(r.SW.Terminals[0].Progress - resumed, 9, 11); // 1.8x: 5 s = 10 %
    }

    [Fact]
    public void Hacking_OutOfRangeAt1_6m_InRangeAt1_5m()
    {
        using var r = new QaRig(2);
        r.Place(r.S[0], 10, 26.6f); // 1.6 m
        r.Place(r.S[1], 12, 25);    // 2.0 m
        r.Interact(r.S[0], true);
        r.Interact(r.S[1], true);
        r.Tick(40);
        Assert.Equal(0, r.SW.Terminals[0].Progress);
        r.Place(r.S[0], 10, 26.5f); // exactly 1.5 m
        r.Tick(40);
        Assert.True(r.SW.Terminals[0].Progress > 0);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 5)]
    [InlineData(7, 5)]
    public void Exit_OpensOnTheSameTickTheRequiredTerminalCountCompletes_ForBothRoles(int survivors, int required)
    {
        using var r = new QaRig(survivors);
        var map = new FixedMap();
        int placed = survivors <= 2 ? 5 : survivors <= 5 ? 7 : 8;
        Assert.Equal(required, r.KW.TerminalsRequired);
        Assert.Equal(placed, r.KW.Terminals.Count);
        int doneTerminals = 0;
        for (int t = 0; t < required; t++)
        {
            var pos = map.TerminalPositions[t];
            int hackers = Math.Min(3, survivors);
            for (int i = 0; i < survivors; i++)
            {
                if (i < hackers)
                {
                    double a = i * Math.PI * 2 / hackers;
                    r.Place(r.S[i], pos.X + 0.7f * (float)Math.Cos(a), pos.Z + 0.7f * (float)Math.Sin(a));
                    r.Interact(r.S[i], true);
                }
                else r.Place(r.S[i], 55 + (i % 3), 55 + (i / 3) * 1.5f);
            }
            for (int n = 0; n < 2000 && r.KW.Terminals[t].Progress < 100; n++)
            {
                r.Tick();
                if (r.KW.Terminals[t].Progress == 100) break;
                Assert.False(r.KW.Powered());
            }
            ++doneTerminals;
            var kw = r.KW;
            var sw = r.SW;
            Assert.Equal(100, kw.Terminals[t].Progress);
            bool powered = doneTerminals >= required;
            Assert.Equal(powered, kw.Powered());
            Assert.Equal(powered, sw.Powered());
            Assert.False(kw.AnyOpen());
            Assert.False(sw.AnyOpen());
        }
    }

    // ------------------------------------------------------------------ exit / FirewallBlock / win order (GDD v2)

    [Fact]
    public void Exit_FirewallBlockLocksTheSwitchInProgressForFiveSeconds_PreconditionDoesNotConsumeCooldown_EffectCarriesExitNotKiller()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.K, SkillIds.FirewallBlock);
        r.Use(r.K, SkillIds.FirewallBlock); // no switch in progress
        r.Tick(3);
        Assert.Empty(r.K.Since<S2C_SkillEffect>(0));
        r.OpenExitWithTwo();
        Assert.True(r.KW.Powered());
        Assert.False(r.KW.AnyOpen());
        r.Use(r.K, SkillIds.FirewallBlock); // powered but no switch progress yet: still unusable
        r.Tick();
        Assert.Empty(r.K.Since<S2C_SkillEffect>(0));
        var map = new FixedMap();
        r.Place(r.S[0], map.ExitSwitches[0].X, map.ExitSwitches[0].Z);
        r.Place(r.S[1], 40, 55);
        r.Interact(r.S[0], true);
        r.Tick(40);
        byte before = r.KW.SwitchProgress(0);
        Assert.InRange(before, 12, 14);
        r.Use(r.K, SkillIds.FirewallBlock); // immediately usable: the failed attempts consumed nothing
        r.Tick();
        Assert.True(r.KW.Locked(0));
        Assert.True(r.SW.Locked(0));
        Assert.False(r.SW.Locked(1));
        var effect = r.S[1].Since<S2C_SkillEffect>(0).Single(e => e.SkillId == SkillIds.FirewallBlock);
        Assert.Equal(r.K.Id, effect.CasterId);
        Assert.Equal(map.Exits[0].X, effect.X);
        Assert.Equal(map.Exits[0].Z, effect.Z);
        Assert.Equal(5f, effect.Duration);
        Assert.Single(r.K.Since<S2C_SkillEffect>(0));
        byte frozen = r.KW.SwitchProgress(0);
        int n = 1;
        while (r.KW.Locked(0) && n < 250) { Assert.Equal(frozen, r.KW.SwitchProgress(0)); r.Tick(); n++; }
        Assert.InRange(n, 99, 101); // 5 s lock
        r.Tick(20);
        Assert.True(r.KW.SwitchProgress(0) > frozen, "the switch progresses again after the lock");
    }

    [Fact]
    public void Exit_LockExpiresAndTheFlagsReturnToUnlocked()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.K, SkillIds.FirewallBlock);
        r.ArmFirewall();
        r.Place(r.S[0], 40, 55);
        r.Place(r.S[1], 42, 55);
        r.Use(r.K, SkillIds.FirewallBlock);
        r.Tick();
        Assert.True(r.KW.Locked(0));
        int locked = 1;
        while (r.KW.Locked(0) && locked < 200) { r.Tick(); locked++; }
        Assert.InRange(locked, 99, 101);
        Assert.True(r.KW.Powered());
        Assert.False(r.SW.AnyLocked());
    }

    [Fact]
    public void Win_EscapeWhileTheOtherSurvivorIsLagging_TheMatchGoesOn()
    {
        using var r = new QaRig(2);
        r.OpenExitFlag();
        r.Place(r.S[1], 30, 5);
        r.MakeLagging(r.S[1]);
        r.Place(r.S[0], 2.5f, 4);
        r.Tick();
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Escaped));
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        Assert.False(r.K.Has<S2C_GameResult>());
    }

    [Fact]
    public void Win_CaptureOfTheOtherSurvivorInTheSameTickAsEscape_TheMatchGoesOn()
    {
        using var r = new QaRig(2);
        r.OpenExitFlag();
        r.Place(r.S[0], 2.5f, 4);
        r.Place(r.S[1], 30, 5);
        r.Place(r.K, 31.0f, 5); // captures S1 (1.0 m) in the very tick S0 reaches the exit
        r.Tick();
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        Assert.False(r.S[1].Has<S2C_GameResult>());
    }

    [Fact]
    public void Win_TwoSurvivorsEnterTheExitInOneTick_TheThirdLater_AllListedInEscapedIds()
    {
        using var r = new QaRig(3);
        r.Place(r.K,40,8);
        r.OpenExitFlag();
        r.Place(r.S[0], 2.5f, 4);
        r.Place(r.S[1], 4, 5.5f);
        r.Place(r.S[2], 40, 55);
        r.Tick();
        Assert.False(r.S[2].Has<S2C_GameResult>());
        r.Place(r.S[2], 5.5f, 4);
        r.Tick();
        var result = r.S[2].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.AllResolved, result.Reason);
        Assert.Equal((byte)Role.Survivor, result.WinnerRole);
        Assert.Equal(new[] { r.S[0].Id, r.S[1].Id, r.S[2].Id }.OrderBy(i => i), result.EscapedIds.OrderBy(i => i));
        Assert.Empty(result.EliminatedIds);
    }

    [Fact]
    public void Win_EscapeAtTheExactTimeUpTick_SurvivorsWinNotTimeUp_AndNoEscapeMeansTimeUpAtNineHundredSeconds()
    {
        using var r = new QaRig(1);
        r.OpenExitFlag();
        r.SetMatchTime(899.96);
        r.Place(r.S[0], 2.5f, 4);
        r.Tick(); // matchTime reaches 900.01 in this same tick as the escape
        var result = r.S[0].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.AllResolved, result.Reason);
        Assert.Equal((byte)Role.Survivor, result.WinnerRole);

        using var c = new QaRig(1);
        c.SetMatchTime(899.9);
        c.Tick();
        Assert.False(c.S[0].Has<S2C_GameResult>());
        Assert.InRange(c.KW.TimeLeft, 0.04f, 0.06f);
        c.Tick();
        var timeUp = c.S[0].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.TimeUp, timeUp.Reason);
        Assert.Equal((byte)Role.Killer, timeUp.WinnerRole);
        Assert.Empty(timeUp.EscapedIds);
        Assert.Equal(new[] { c.S[0].Id }, timeUp.EliminatedIds);
        Assert.Equal(0f, c.KW.TimeLeft);
    }

    [Fact]
    public void Win_LastNormalSurvivorCapturedAtTheTimeUpTick_IsStillInPlay_SoTheTimeUpEliminatesIt()
    {
        using var r = new QaRig(1);
        r.SetMatchTime(899.96);
        r.Place(r.S[0], 13, 10);
        r.Place(r.K, 11.9f, 10);
        r.Tick();
        var result = r.S[0].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.TimeUp, result.Reason);
        Assert.Equal((byte)Role.Killer, result.WinnerRole);
        Assert.Equal(new[] { r.S[0].Id }, result.EliminatedIds);
    }

    [Fact]
    public void Win_LaggingSurvivorsAreStillInPlay_NoResult_LaggingSurvivorOnOpenExitDoesNotEscape()
    {
        using var r = new QaRig(2);
        r.OpenExitFlag();
        r.Place(r.S[0], 4, 4);
        r.MakeLagging(r.S[0]);
        r.Place(r.S[1], 30, 5);
        r.Place(r.K, 31f, 5);
        r.Tick();
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Lagging));
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        Assert.False(r.K.Has<S2C_GameResult>());
    }

    // ------------------------------------------------------------------ GameResult once, lobby, second match

    [Fact]
    public void Result_SentExactlyOncePerHuman_ThenLobby_ThenSecondMatchStartsClean_AndTokenStillValid()
    {
        using var r = new QaRig(1);
        int survivorId = r.S[0].Id;
        ulong token = r.S[0].Last<S2C_JoinResult>().ReconnectToken;
        r.Place(r.K,40,8);
        r.OpenExitFlag();
        r.Place(r.S[0], 2.5f, 4);
        r.Tick();
        r.Tick(200);
        foreach (var p in new[] { r.K, r.S[0] })
        {
            p.Drain();
            Assert.Single(p.Packets.OfType<S2C_GameResult>());
            int resultIndex = p.Packets.FindIndex(x => x is S2C_GameResult);
            Assert.Contains(p.Packets.Skip(resultIndex + 1), x => x is S2C_LobbyState); // lobby re-broadcast after the result
            int lastWorld = p.Packets.FindLastIndex(x => x is S2C_WorldState);
            Assert.True(lastWorld < resultIndex, "no WorldState may follow the GameResult");
        }
        // Reconnect token from the lobby stays valid.
        r.S[0].Connection.Close();
        r.Tick(5);
        var back = r.F.Connect();
        back.Send(new C2S_Join { Nickname = "S0", ReconnectToken = token });
        r.Tick();
        var join = back.Last<S2C_JoinResult>();
        Assert.Equal(0, join.Result);
        Assert.Equal(survivorId, join.PlayerId);

        r.K.Drain();
        int starts = r.K.Packets.OfType<S2C_MatchStart>().Count();
        r.K.Send(new C2S_StartMatch());
        r.Tick();
        r.K.Drain();
        Assert.Equal(starts + 1, r.K.Packets.OfType<S2C_MatchStart>().Count());
        r.Tick(3);
        var w = r.KW;
        Assert.InRange(w.TimeLeft, 899.5f, 900f);
        Assert.False(w.Powered());
        Assert.False(w.AnyOpen());
        Assert.All(w.Terminals, t => Assert.Equal(0, t.Progress));
        var s = back.World().Players.Single(p => p.Id == survivorId);
        Assert.True(Is(s, PlayerStatus.Normal));
        Assert.Equal(0, s.CaptureCount);
        // And the second match can end again, with exactly one more result for each human still connected.
        r.SetMatchTime(899.9);
        r.Tick(3);
        r.K.Drain();
        back.Drain();
        Assert.Equal(2, r.K.Packets.OfType<S2C_GameResult>().Count());
        Assert.Single(back.Packets.OfType<S2C_GameResult>()); // the reconnected survivor gets exactly one for match 2
    }

    [Fact]
    public void Result_SeqIsResetAtMatchStart_ClientMayRestartAtOne_U4QA07_FixedInAttempt3()
    {
        // U4 QA-07: the per-player MoveInput Seq filter must be reset for a new match (attempt 1 did not; fixed in attempt 3).
        // A client that restarts Seq at 1 in the new match must be obeyed from its first input.
        using var r = new QaRig(1);
        for (int i = 0; i < 40; i++) { r.S[0].Move(1, 0); r.Tick(); }
        r.SetMatchTime(899.9);
        r.Tick(3);
        Assert.True(r.K.Has<S2C_GameResult>());
        r.K.Send(new C2S_StartMatch());
        r.Tick(2);
        r.Place(r.S[0], 20, 5);
        r.Tick();
        float x = r.Truth(r.S[0]).X;
        r.S[0].Send(new C2S_MoveInput { DirX = 1, Seq = 1 });
        r.Tick();
        Assert.True(r.Truth(r.S[0]).X > x, "the very first input of the new match (Seq 1) must move the survivor");
        for (uint seq = 2; seq <= 10; seq++) { r.S[0].Send(new C2S_MoveInput { DirX = 1, Seq = seq }); r.Tick(); }
        Assert.InRange(r.Truth(r.S[0]).X - x, 1.7f, 2.01f); // 10 ticks at 4 m/s
        // The filter is still a filter inside the new match: an old Seq is dropped.
        float x10 = r.Truth(r.S[0]).X;
        r.S[0].Send(new C2S_MoveInput { DirX = -1, Seq = 5 });
        r.Tick();
        Assert.True(r.Truth(r.S[0]).X >= x10, "a stale Seq inside the new match must still be dropped");
    }

    [Fact]
    public void Result_NewMatchAfterResult_ResetsDecoysAfterimageSkillCooldownsAndTimers()
    {
        using var r = new QaRig(2, seed: 11);
        r.SetSkill(r.S[0], SkillIds.WhiteNoise);
        r.Use(r.S[0], SkillIds.WhiteNoise);
        r.Tick(2);
        r.SetMatchTime(899.9);
        r.Tick(3);
        Assert.True(r.K.Has<S2C_GameResult>());
        r.K.Send(new C2S_StartMatch());
        r.Tick(2);
        // Fresh match: no leftover decoy entry may show in the killer frame (all survivors idle = absent).
        r.Tick(3);
        var w = r.KW;
        Assert.Single(w.Players); // only the killer itself
    }
}
