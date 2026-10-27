using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class CaptureRescueTests
{
    // Survivor 0 spawns at (5,26); the killer walks up x = 5 and touches it at 1.2 m. Survivor 1 stays Normal.
    private static RulesMatch CaptureFirst()
    {
        var m = RulesMatch.Start(2);
        m.WalkTo(m.Killer, 5, 25, GameConstants.KillerSpeed);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        return m;
    }

    [Fact]
    public void FirstContactLagsTheSurvivorWithASixtySecondCountdownAndQuarterSpeed()
    {
        using var m = CaptureFirst();
        var a = m.Truth(m[0]);
        Assert.Equal(1, a.CaptureCount);
        Assert.InRange(a.StatusTimer, 59.8f, 60f);
        m.Tick();
        Assert.InRange(a.StatusTimer - m.Truth(m[0]).StatusTimer, 0.049f, 0.051f);
        // Lagging is always Signal for the killer, even standing still.
        m.Tick(30);
        var seen = m.KillerSees(m[0]);
        Assert.NotNull(seen);
        Assert.Equal((byte)PlayerStatus.Lagging, seen.Value.Status);
        Assert.Equal((byte)PlayerFlags.Signal, seen.Value.Flags);
        Assert.Equal(1, seen.Value.CaptureCount);
        // 1.0 m/s: twenty ticks move one metre.
        float z = m.Truth(m[0]).Z;
        for (int i = 0; i < 20; i++) { m[0].Move(0, 1); m.Tick(); }
        Assert.InRange(m.Truth(m[0]).Z - z, 0.999f, 1.001f);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[1]).Status);
    }

    [Fact]
    public void TheCapturingContactDoesNotExecuteButAFreshTouchDoes()
    {
        using var m = CaptureFirst();
        m.Tick(40); // killer keeps standing 1 m away
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        for (int i = 0; i < 20; i++) { m[0].Move(0, 1); m.Tick(); } // to (5,27): out of reach
        m[0].Move(0, 0);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        m.WalkTo(m.Killer, 5, 26.2f, GameConstants.KillerSpeed);
        var a = m.Truth(m[0]);
        Assert.Equal((byte)PlayerStatus.Eliminated, a.Status);
        Assert.Null(m.KillerSees(m[0]));
        Assert.Empty(m.Killer.All<S2C_GameResult>());
    }

    private static float SinceCapture(RulesMatch m) => GameConstants.LaggingTimeoutSeconds - m.Truth(m[0]).StatusTimer;

    [Fact]
    public void AFreshTouchAtOnePointNineFiveSecondsIsInsideTheExecutionGrace()
    {
        using var m = CaptureFirst();
        m.Step(m.Killer, 0, -1); // 1.23 m: out of reach, so the next touch is a re-entry
        while (SinceCapture(m) < 1.9f - 0.001f) m.Tick();
        m.Step(m.Killer, 0, 1);
        Assert.InRange(SinceCapture(m), 1.949f, 1.951f);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        // The touch made during the grace is spent: staying in contact past 2 s does not execute either.
        m.Tick(5);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        Assert.Equal(2f, GameServer.ExecutionGraceSeconds);
    }

    [Fact]
    public void AFreshTouchAtTwoPointZeroFiveSecondsExecutes()
    {
        using var m = CaptureFirst();
        m.Step(m.Killer, 0, -1);
        while (SinceCapture(m) < 2.0f - 0.001f) m.Tick();
        Assert.InRange(SinceCapture(m), 1.999f, 2.001f);
        m.Step(m.Killer, 0, 1); // re-entry at 2.05 s
        Assert.Equal((byte)PlayerStatus.Eliminated, m.Truth(m[0]).Status);
    }

    [Fact]
    public void ThreeSecondsOfContactRescueThenTwoSecondsOfInvulnerabilityThenSecondCaptureEliminates()
    {
        using var m = CaptureFirst();
        var b = m[1];
        m.WalkTo(b, 6.6f, 26, GameConstants.SurvivorSpeed); // 1.6 m from the Lagging survivor: not yet in range
        m.Tick(3);
        Assert.Equal(0, m.Truth(b).Flags & (byte)PlayerFlags.Rescuing);
        m.Step(b, -1, 0); // 1.4 m: contact tick 1
        m.Tick(58);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        Assert.Equal((byte)(PlayerFlags.Signal | PlayerFlags.Rescuing), m.Truth(b).Flags);
        Assert.Equal((byte)PlayerFlags.Signal, m.KillerSees(b)!.Value.Flags); // rescuers are Signal while still
        m.Tick();
        var rescued = m.Truth(m[0]);
        Assert.Equal((byte)PlayerStatus.Normal, rescued.Status);
        Assert.Equal(1, rescued.CaptureCount);
        Assert.Equal(0f, rescued.StatusTimer);
        // The killer still stands 1 m away: protected for two seconds, then the second capture eliminates.
        m.Tick(39);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[0]).Status);
        m.Tick(2);
        Assert.Equal((byte)PlayerStatus.Eliminated, m.Truth(m[0]).Status);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(b).Status);
    }

    [Fact]
    public void BrokenContactAtTwoPointNineSecondsRestartsTheRescue()
    {
        using var m = CaptureFirst();
        var b = m[1];
        m.WalkTo(b, 6.6f, 26, GameConstants.SurvivorSpeed);
        m.Tick(3);
        m.Step(b, -1, 0);
        m.Tick(57); // 2.9 s of contact
        m.Step(b, 1, 0); // one tick out of range
        Assert.Equal(0, m.Truth(b).Flags & (byte)PlayerFlags.Rescuing);
        m.Step(b, -1, 0);
        m.Tick(58);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[0]).Status);
    }

    [Fact]
    public void UnrescuedLaggingSurvivorIsEliminatedWhenTheCountdownEnds()
    {
        using var m = CaptureFirst();
        float left = m.Truth(m[0]).StatusTimer;
        int ticks = 0;
        while (m.Truth(m[0]).Status == (byte)PlayerStatus.Lagging && ticks < 2000) { m.Tick(); ++ticks; }
        Assert.Equal((byte)PlayerStatus.Eliminated, m.Truth(m[0]).Status);
        Assert.InRange(ticks * 0.05f, left - 0.001f, left + 0.051f);
        Assert.Equal(0f, m.Truth(m[0]).StatusTimer);
        Assert.Empty(m.Killer.All<S2C_GameResult>());
    }
}
