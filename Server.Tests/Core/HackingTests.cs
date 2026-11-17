using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class HackingTests
{
    // Survivors receive the exact percentage; the killer only 0 or 100 (U2c N9, see KillerSeesOnlyDoneOrNotDone).
    private static byte Progress(RulesMatch m, int terminal = 0) => m.Survivors[0].Last<S2C_WorldState>().Terminals[terminal].Progress;

    [Fact]
    public void OneIdleSurvivorCompletesATerminalInNinetySecondsAndIsSignalWhileHacking()
    {
        using var m = RulesMatch.Start(2);
        var a = m[0];
        m.WalkTo(a, 10, 26, GameConstants.SurvivorSpeed); // 1 m from terminal 0 at (10,25)
        m.Tick(25); // past the Signal hold and a possible Packet Afterimage second
        Assert.Null(m.KillerSees(a)); // Silent before hacking.
        a.Send(new C2S_Interact { Active = 1 });
        m.Tick(1799); // 11/17 (R2): the passive part takes 90 s solo, twice the old 45 s.
        Assert.Equal(99, Progress(m));
        var seen = m.KillerSees(a);
        Assert.NotNull(seen);
        Assert.Equal((byte)PlayerFlags.Signal, seen.Value.Flags);
        Assert.Equal((byte)(PlayerFlags.Signal | PlayerFlags.Hacking), m.Truth(a).Flags);
        m.Tick();
        Assert.Equal(100, Progress(m));
        Assert.False(m.Killer.Last<S2C_WorldState>().Powered());
        // A completed terminal cannot be hacked further: the survivor goes Silent again.
        m.Tick(25);
        Assert.Null(m.KillerSees(a));
        Assert.Equal(0, m.Truth(a).Flags);
    }

    // GDD v2 §2 (11/17 R2: the 90 s passive part): 1 hacker 1.0x (1800 ticks), 2 hackers 1.8x (1000), 3 or more 2.4x (750).
    [Theory]
    [InlineData(1, 1800)]
    [InlineData(2, 1000)]
    [InlineData(3, 750)]
    [InlineData(4, 750)]
    public void CoHackingUsesTheDiminishingRates(int hackers, int ticksToComplete)
    {
        using var m = RulesMatch.Start(4);
        var spots = new[] { (9.5f, 25.5f), (10f, 26f), (10.5f, 25.5f), (10f, 24.2f) };
        for (int i = 0; i < 4; i++) m.WalkTo(m[i], spots[i].Item1, spots[i].Item2, GameConstants.SurvivorSpeed);
        for (int i = 0; i < hackers; i++) m[i].Send(new C2S_Interact { Active = 1 });
        m.Tick(ticksToComplete - 1);
        Assert.InRange(Progress(m), 95, 99);
        m.Tick();
        Assert.Equal(100, Progress(m));
    }

    [Fact]
    public void InterruptedProgressIsKeptAndOutOfRangeOrReleasedMakesNoProgress()
    {
        using var m = RulesMatch.Start(2);
        var a = m[0];
        m.WalkTo(a, 10, 26, GameConstants.SurvivorSpeed);
        a.Send(new C2S_Interact { Active = 1 });
        m.Tick(720); // 11/17 (R2): the passive part takes 90 s solo, twice the old 45 s.
        Assert.Equal(40, Progress(m));
        a.Send(new C2S_Interact { Active = 0 });
        m.Tick(40);
        Assert.Equal(40, Progress(m));
        // Held but out of range (2.24 m from the terminal): no progress, and progress is not lost.
        m.WalkTo(a, 8, 26, GameConstants.SurvivorSpeed);
        a.Send(new C2S_Interact { Active = 1 });
        m.Tick(40);
        Assert.Equal(40, Progress(m));
        // Held state persists: walking back into range resumes the hack.
        m.WalkTo(a, 10, 26, GameConstants.SurvivorSpeed);
        m.Tick(180);
        Assert.InRange(Progress(m), 50, 52);
    }

    [Fact]
    public void LaggingSurvivorCannotHackAndCaptureReleasesInteract()
    {
        using var m = RulesMatch.Start(2);
        var a = m[1]; // spawn (10,26), 1 m from terminal 0
        a.Send(new C2S_Interact { Active = 1 });
        m.Tick(180); // 11/17 (R2): the passive part takes 90 s solo, twice the old 45 s.
        Assert.Equal(10, Progress(m));
        m.WalkTo(m.Killer, 10, 25, GameConstants.KillerSpeed); // contact at 1.2 m on the way in
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(a).Status);
        byte captured = Progress(m);
        a.Send(new C2S_Interact { Active = 1 });
        m.Tick(40);
        Assert.Equal(captured, Progress(m));
        Assert.Equal(0, m.Truth(a).Flags & (byte)PlayerFlags.Hacking);
    }

    [Fact]
    public void RequiredTerminalCountPowersBothExitsForEveryone()
    {
        using var m = RulesMatch.Start(2);
        m.PowerExitsWithTwoSurvivors();
        foreach (var peer in new[] { m.Killer, m[0], m[1] })
        {
            var world = peer.Last<S2C_WorldState>();
            Assert.True(world.Powered());
            Assert.Equal(2, world.Exits.Count);
            Assert.False(world.AnyOpen());
            Assert.Equal(3, world.TerminalsRequired);
        }
    }
}
