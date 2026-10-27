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
        Assert.True(Is(seen, PlayerStatus.Lagging));
        Assert.Equal(1, seen.CaptureCount);
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
        for (int i = 0; i < 200 && Is(r.Truth(r.S[0]), PlayerStatus.Lagging); i++) r.Tick();
        double lived = r.Elapsed - captureAt;
        Assert.InRange(lived, 59.9, 60.11);
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
        Assert.Equal(0, r.KW.Terminals[0].Progress);
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
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Eliminated));
        Assert.Null(r.KillerSees(r.S[0]));
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
        r.Place(r.K, 12.0f, 10); // fresh touch after the grace
        r.Tick();
        Assert.True(Is(r.Truth(r.S[0]), PlayerStatus.Eliminated));
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
        while (!Is(r.Truth(r.S[0]), PlayerStatus.Eliminated) && k < 120)
        {
            r.Tick();
            k++;
            var t = r.Truth(r.S[0]);
            if (!Is(t, PlayerStatus.Eliminated)) Assert.True(Is(t, PlayerStatus.Normal), "must not become Lagging: a second capture eliminates");
        }
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
        for (int n = 1; n <= 230; n++)
        {
            r.Tick();
            int progress = r.KW.Terminals[0].Progress;
            Assert.Equal(progress, r.SW.Terminals[0].Progress); // same value for both roles
            Assert.True(progress >= prev, "progress must never fall");
            if (progress == 100 && complete < 0) complete = n;
            if (complete < 0)
            {
                Assert.InRange(progress, (int)MathF.Floor(n * 0.5f) - 1, Math.Min(99, (int)MathF.Floor(n * 0.5f) + 1));
                var seen = r.KillerSees(r.S[0]);
                Assert.NotNull(seen);
                Assert.Equal((byte)PlayerFlags.Signal, seen!.Value.Flags);
                Assert.NotEqual(0, r.Truth(r.S[0]).Flags & (byte)PlayerFlags.Hacking);
            }
            prev = progress;
        }
        Assert.InRange(complete, 199, 202);
        Assert.Null(r.KillerSees(r.S[0])); // nothing left to hack, not moving: Silent again
        Assert.Equal(0, r.Truth(r.S[0]).Flags & (byte)PlayerFlags.Hacking);
    }

    [Theory]
    [InlineData(1, 200)]
    [InlineData(2, 100)]
    [InlineData(3, 67)]
    [InlineData(4, 67)]
    [InlineData(6, 67)]
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
        while (r.KW.Terminals[0].Progress < 100 && n < 400) { r.Tick(); n++; }
        Assert.InRange(n, expectedTicks - 1, expectedTicks + 1);
    }

    [Fact]
    public void Hacking_ProgressKeptAfterInterruptionAndResumesWhereItStopped()
    {
        using var r = new QaRig(1);
        r.Place(r.S[0], 10, 25.6f);
        r.Interact(r.S[0], true);
        r.Tick(80); // 4 s
        Assert.InRange((int)r.KW.Terminals[0].Progress, 39, 41);
        r.Interact(r.S[0], false);
        r.Tick(30);
        Assert.InRange((int)r.KW.Terminals[0].Progress, 39, 41);
        r.Place(r.S[0], 30, 30); // wander off for 5 s
        r.Tick(100);
        Assert.InRange((int)r.SW.Terminals[0].Progress, 39, 41);
        r.Place(r.S[0], 10, 25.6f);
        r.Interact(r.S[0], true);
        int n = 0;
        while (r.KW.Terminals[0].Progress < 100 && n < 300) { r.Tick(); n++; }
        Assert.InRange(n, 118, 122); // the remaining 6 s
    }

    [Fact]
    public void Hacking_HackerCapturedMidHack_RateDropsAndInteractIsNotAutoResumedAfterRescue()
    {
        using var r = new QaRig(2);
        r.Place(r.S[0], 10, 25.6f);
        r.Place(r.S[1], 10.6f, 25.6f);
        r.Interact(r.S[0], true);
        r.Interact(r.S[1], true);
        r.Tick(20); // 2 hackers: 2 s of progress = 20 %
        Assert.InRange((int)r.KW.Terminals[0].Progress, 19, 21);
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
        int atRescue = r.KW.Terminals[0].Progress;
        Assert.InRange(atRescue, 49, 53); // 20 % + ~3 s at 1x
        r.Tick(20);
        Assert.InRange(r.KW.Terminals[0].Progress - atRescue, 9, 11); // S1 does NOT hack again until it presses Interact
        r.Interact(r.S[1], true);
        r.Tick(1);
        int resumed = r.KW.Terminals[0].Progress;
        r.Tick(20);
        Assert.InRange(r.KW.Terminals[0].Progress - resumed, 19, 21);
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
        Assert.Equal(0, r.KW.Terminals[0].Progress);
        r.Place(r.S[0], 10, 26.5f); // exactly 1.5 m
        r.Tick(40);
        Assert.True(r.KW.Terminals[0].Progress > 0);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 4)]
    [InlineData(7, 4)]
    public void Exit_OpensOnTheSameTickTheRequiredTerminalCountCompletes_ForBothRoles(int survivors, int required)
    {
        using var r = new QaRig(survivors);
        var map = new FixedMap();
        int placed = survivors <= 2 ? 4 : survivors <= 5 ? 5 : 6;
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
            for (int n = 0; n < 400 && r.KW.Terminals[t].Progress < 100; n++)
            {
                r.Tick();
                if (r.KW.Terminals[t].Progress == 100) break;
                Assert.Equal(0, r.KW.ExitOpen);
            }
            ++doneTerminals;
            var kw = r.KW;
            var sw = r.SW;
            Assert.Equal(100, kw.Terminals[t].Progress);
            byte open = (byte)(doneTerminals >= required ? 1 : 0);
            Assert.Equal(open, kw.ExitOpen);
            Assert.Equal(open, sw.ExitOpen);
            Assert.Equal((byte)(1 - open), kw.ExitLocked);
            Assert.Equal((byte)(1 - open), sw.ExitLocked);
        }
    }

    // ------------------------------------------------------------------ exit / FirewallBlock / win order

    [Fact]
    public void Exit_FirewallBlockLocksOpenExitForFiveSeconds_PreconditionDoesNotConsumeCooldown_EffectCarriesExitNotKiller()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.K, SkillIds.FirewallBlock);
        r.Use(r.K, SkillIds.FirewallBlock); // exit still closed
        r.Tick(3);
        Assert.Empty(r.K.Since<S2C_SkillEffect>(0));
        r.OpenExitWithTwo();
        Assert.Equal(1, r.KW.ExitOpen);
        Assert.Equal(0, r.KW.ExitLocked);
        var map = new FixedMap();
        r.Place(r.S[0], 56.5f, 30); // 1.5 m from the exit, no Interact held
        r.Place(r.S[1], 40, 55);
        r.Use(r.K, SkillIds.FirewallBlock); // immediately usable: the failed attempt consumed nothing
        r.Tick();
        Assert.Equal(1, r.KW.ExitLocked);
        Assert.Equal(1, r.SW.ExitLocked);
        Assert.Equal(1, r.SW.ExitOpen);
        var effect = r.S[1].Since<S2C_SkillEffect>(0).Single(e => e.SkillId == SkillIds.FirewallBlock);
        Assert.Equal(r.K.Id, effect.CasterId);
        Assert.Equal(map.Exit.X, effect.X);
        Assert.Equal(map.Exit.Z, effect.Z);
        Assert.Equal(5f, effect.Duration);
        Assert.Single(r.K.Since<S2C_SkillEffect>(0));
        int n = 1;
        while (!r.S[0].Has<S2C_GameResult>() && n < 250) { r.Tick(); n++; if (n < 98) Assert.Equal(1, r.KW.ExitLocked); }
        Assert.InRange(n, 99, 102); // 5 s lock, escape on the first unlocked tick
        var result = r.S[0].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.SurvivorEscaped, result.Reason);
        Assert.Equal(new[] { r.S[0].Id }, result.EscapedIds);
    }

    [Fact]
    public void Exit_LockExpiresAndOpenExitFlagsReturnToUnlocked()
    {
        using var r = new QaRig(2);
        r.SetSkill(r.K, SkillIds.FirewallBlock);
        r.OpenExitFlag();
        r.Place(r.S[0], 40, 55);
        r.Place(r.S[1], 42, 55);
        r.Use(r.K, SkillIds.FirewallBlock);
        r.Tick();
        Assert.Equal(1, r.KW.ExitLocked);
        int locked = 1;
        while (r.KW.ExitLocked == 1 && locked < 200) { r.Tick(); locked++; }
        Assert.InRange(locked, 99, 101);
        Assert.Equal(1, r.KW.ExitOpen);
        Assert.Equal(0, r.SW.ExitLocked);
    }

    [Fact]
    public void Win_EscapeAndAllDownInTheSameTick_SurvivorsWin()
    {
        using var r = new QaRig(2);
        r.OpenExitWithTwo();
        r.Place(r.S[1], 30, 5);
        r.MakeLagging(r.S[1]); // nobody but S0 is Normal
        r.Place(r.S[0], 56.5f, 30);
        r.Tick();
        var result = r.K.Last<S2C_GameResult>();
        Assert.Equal((byte)Role.Survivor, result.WinnerRole);
        Assert.Equal((byte)GameEndReason.SurvivorEscaped, result.Reason);
        Assert.Equal(new[] { r.S[0].Id }, result.EscapedIds);
    }

    [Fact]
    public void Win_CaptureOfTheOtherSurvivorInTheSameTickAsEscape_StillSurvivorsWin()
    {
        using var r = new QaRig(2);
        r.OpenExitWithTwo();
        r.Place(r.S[0], 56.5f, 30);
        r.Place(r.S[1], 30, 5);
        r.Place(r.K, 31.0f, 5); // captures S1 (1.0 m) in the very tick S0 reaches the exit
        r.Tick();
        Assert.True(Is(r.Truth(r.S[1]), PlayerStatus.Lagging));
        var result = r.S[1].Last<S2C_GameResult>();
        Assert.Equal((byte)Role.Survivor, result.WinnerRole);
        Assert.Equal((byte)GameEndReason.SurvivorEscaped, result.Reason);
    }

    [Fact]
    public void Win_TwoSurvivorsEnterTheExitInOneTick_BothListedInEscapedIds()
    {
        using var r = new QaRig(3);
        r.OpenExitFlag();
        r.Place(r.S[0], 56.5f, 30);
        r.Place(r.S[1], 58, 31.5f);
        r.Place(r.S[2], 40, 55);
        r.Tick();
        var result = r.S[2].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.SurvivorEscaped, result.Reason);
        Assert.Equal(new[] { r.S[0].Id, r.S[1].Id }.OrderBy(i => i), result.EscapedIds.OrderBy(i => i));
    }

    [Fact]
    public void Win_EscapeAtTheExactTimeUpTick_SurvivorsWinNotTimeUp_AndNoEscapeMeansTimeUpAtSixHundredSeconds()
    {
        using var r = new QaRig(1);
        r.OpenExitFlag();
        r.SetMatchTime(599.96);
        r.Place(r.S[0], 56.5f, 30);
        r.Tick(); // matchTime reaches 600.01 in this same tick as the escape
        var result = r.S[0].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.SurvivorEscaped, result.Reason);
        Assert.Equal((byte)Role.Survivor, result.WinnerRole);

        using var c = new QaRig(1);
        c.SetMatchTime(599.9);
        c.Tick();
        Assert.False(c.S[0].Has<S2C_GameResult>());
        Assert.InRange(c.KW.TimeLeft, 0.04f, 0.06f);
        c.Tick();
        var timeUp = c.S[0].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.TimeUp, timeUp.Reason);
        Assert.Equal((byte)Role.Killer, timeUp.WinnerRole);
        Assert.Empty(timeUp.EscapedIds);
        Assert.Equal(0f, c.KW.TimeLeft);
    }

    [Fact]
    public void Win_LastNormalSurvivorCapturedAtTheTimeUpTick_ReportsAllSurvivorsDownNotTimeUp()
    {
        using var r = new QaRig(1);
        r.SetMatchTime(599.96);
        r.Place(r.S[0], 13, 10);
        r.Place(r.K, 11.9f, 10);
        r.Tick();
        var result = r.S[0].Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.AllSurvivorsDown, result.Reason);
        Assert.Equal((byte)Role.Killer, result.WinnerRole);
    }

    [Fact]
    public void Win_AllSurvivorsLaggingOrEliminated_KillerWinsImmediately_LaggingSurvivorOnOpenExitDoesNotEscape()
    {
        using var r = new QaRig(2);
        r.OpenExitFlag();
        r.Place(r.S[0], 58, 30);
        r.MakeLagging(r.S[0]);
        r.Place(r.S[1], 30, 5);
        r.Place(r.K, 31f, 5);
        r.Tick();
        var result = r.K.Last<S2C_GameResult>();
        Assert.Equal((byte)GameEndReason.AllSurvivorsDown, result.Reason);
        Assert.Equal((byte)Role.Killer, result.WinnerRole);
        Assert.Empty(result.EscapedIds);
    }

    // ------------------------------------------------------------------ GameResult once, lobby, second match

    [Fact]
    public void Result_SentExactlyOncePerHuman_ThenLobby_ThenSecondMatchStartsClean_AndTokenStillValid()
    {
        using var r = new QaRig(1);
        int survivorId = r.S[0].Id;
        ulong token = r.S[0].Last<S2C_JoinResult>().ReconnectToken;
        r.OpenExitFlag();
        r.Place(r.S[0], 56.5f, 30);
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
        Assert.InRange(w.TimeLeft, 599.5f, 600f);
        Assert.Equal(0, w.ExitOpen);
        Assert.Equal(1, w.ExitLocked);
        Assert.All(w.Terminals, t => Assert.Equal(0, t.Progress));
        var s = back.World().Players.Single(p => p.Id == survivorId);
        Assert.True(Is(s, PlayerStatus.Normal));
        Assert.Equal(0, s.CaptureCount);
        // And the second match can end again, with exactly one more result for each human still connected.
        r.SetMatchTime(599.9);
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
        r.SetMatchTime(599.9);
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
        r.SetMatchTime(599.9);
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
