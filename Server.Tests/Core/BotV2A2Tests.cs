using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using Xunit;

namespace PingTheKiller.Tests.Core;

// U6d attempt 2: the patrol sweep, kick cooldown, switch stall, hide-in-place and big-team avoid rules, exercised with role
// views and the public static map.
public sealed class BotV2A2Tests
{
    private const BindingFlags Field = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly FixedMap _map = new();
    private readonly NavGrid _nav;
    public BotV2A2Tests() => _nav = new NavGrid(_map);

    private static T Read<T>(object brain, string name) => (T)brain.GetType().GetField(name, Field)!.GetValue(brain)!;
    private static void Write(object brain, string name, object value) => brain.GetType().GetField(name, Field)!.SetValue(brain, value);

    // Eight public terminals (the large-lobby knobs: no roaming, 45 s revisit); 4-7 are done.
    private static KillerBotView LargeKiller()
    {
        var view = new KillerBotView { X = 41, Z = 43, Speed = 4.6f, TerminalCount = 8, Terminals = new KillerBotView.Terminal[8] };
        view.Terminals[0] = new KillerBotView.Terminal { X = 41, Z = 44 };
        view.Terminals[1] = new KillerBotView.Terminal { X = 58, Z = 27 };
        view.Terminals[2] = new KillerBotView.Terminal { X = 20, Z = 60 };
        view.Terminals[3] = new KillerBotView.Terminal { X = 60, Z = 60 };
        for (int i = 4; i < 8; i++) view.Terminals[i] = new KillerBotView.Terminal { X = 10 + i, Z = 10, Done = true };
        return view;
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void KillerSweepRevisitsLeastRecentlyCheckedWorkWithSignalBonusAndSkipsTheTerminalUnderfoot(bool signal, int expected)
    {
        var view = LargeKiller(); var brain = new KillerBrain(); var rng = new Random(1);
        brain.Update(view, _nav, _map, rng);
        var visited = Read<double[]>(brain, "_visitedAt");
        visited[0] = -40; // the oldest check, but it is underfoot
        visited[1] = -10;
        visited[2] = -25;
        visited[3] = -10;
        if (signal) Read<double[]>(brain, "_signalAt")[3] = 0; // counts as SweepSignalBonus (20 s) older: -30
        Write(brain, "_patrol", -1);
        view.Time = 1;
        brain.Update(view, _nav, _map, rng);
        Assert.Equal(expected, Read<int>(brain, "_patrol"));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(46, true)]
    public void KillerDoesNotKickATerminalAgainWithinRevisit(double sinceKick, bool kicks)
    {
        var view = LargeKiller(); view.X = 41; view.Z = 44;
        var brain = new KillerBrain(); var rng = new Random(1);
        brain.Update(view, _nav, _map, rng);
        Assert.Equal(0, Read<int>(brain, "_patrol"));
        Read<double[]>(brain, "_kickedAt")[0] = view.Time - sinceKick;
        int held = 0;
        for (int i = 1; i <= 60; i++)
        {
            view.Time = i * .05;
            if (brain.Update(view, _nav, _map, rng).Interact) held++;
        }
        if (kicks) Assert.InRange(held, 40, 46);
        else Assert.Equal(0, held);
        Assert.True(Read<double[]>(brain, "_visitedAt")[0] >= 0, "the terminal still counts as checked");
    }

    private static PlayerState Mate(int id, float x, float z, bool signal = false) =>
        new() { Id = id, X = x, Z = z, Status = (byte)PlayerStatus.Normal, Flags = signal ? (byte)PlayerFlags.Signal : (byte)0 };

    private static SurvivorBotView Survivor() => new()
    {
        SelfId = 1, X = 41, Z = 42.7f, Speed = 4, Status = PlayerStatus.Normal,
        Terminals = new[] { new TerminalState { X = 41, Z = 44, Progress = 40 }, new TerminalState { X = 58, Z = 27, Progress = 20 } },
        TerminalCount = 2
    };

    private BotIntent StepMap(SurvivorBrain brain, SurvivorBotView view, Random rng)
    {
        view.Time += .05;
        return brain.Update(view, _nav, _map, rng);
    }

    private void HackFirst(SurvivorBrain brain, SurvivorBotView view, Random rng)
    {
        bool hacked = false;
        for (int i = 0; i < 300 && !hacked; i++) hacked = StepMap(brain, view, rng).Interact;
        Assert.True(hacked);
        Assert.Equal(0, Read<int>(brain, "_terminal"));
    }

    // A moving killer in sight at the given distance south of the bot (it shifts a little every tick, so it is "moving").
    private static void KillerAt(SurvivorBotView view, float distance, int tick)
    {
        view.KillerVisible = true;
        view.KillerX = 41 + (tick % 2 == 0 ? .1f : -.1f);
        view.KillerZ = 42.7f - distance;
        view.Threat = distance <= GameConstants.ThreatLevel3Distance ? (byte)3 : distance <= GameConstants.ThreatLevel2Distance ? (byte)2
            : distance <= GameConstants.ThreatLevel1Distance ? (byte)1 : (byte)0;
    }

    [Fact]
    public void KillerBeyondThreatLevelOneFreezesTheBotInPlaceAndCloserMakesItMoveToCover()
    {
        var view = Survivor(); view.Team.Add(Mate(1, view.X, view.Z));
        var brain = new SurvivorBrain(); var rng = new Random(4);
        HackFirst(brain, view, rng);
        for (int i = 0; i < 20; i++) { KillerAt(view, 25, i); StepMap(brain, view, rng); }
        Assert.True(Read<bool>(brain, "_hiding"));
        Assert.True(Read<bool>(brain, "_hideInPlace"));
        for (int i = 20; i < 60; i++)
        {
            KillerAt(view, 25, i);
            var intent = StepMap(brain, view, rng);
            Assert.False(intent.Interact);
            Assert.Equal(0f, intent.DirX);
            Assert.Equal(0f, intent.DirZ);
        }
        bool moved = false;
        for (int i = 60; i < 100 && !moved; i++)
        {
            KillerAt(view, 15, i);
            var intent = StepMap(brain, view, rng);
            moved = intent.DirX != 0 || intent.DirZ != 0;
        }
        Assert.True(moved, "a killer inside ThreatLevel 1 sends the bot behind cover");
        Assert.False(Read<bool>(brain, "_hideInPlace"));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void UnseenKillerAtThreatTwoFreezesTheBotAndAtThreatThreeItFlees(byte threat, bool flees)
    {
        var view = Survivor(); view.Team.Add(Mate(1, view.X, view.Z, signal: true));
        var brain = new SurvivorBrain(); var rng = new Random(5);
        HackFirst(brain, view, rng);
        view.Threat = threat;
        bool fled = false, movedLate = false;
        for (int i = 0; i < 40; i++)
        {
            var intent = StepMap(brain, view, rng);
            fled |= Read<bool>(brain, "_fleeing");
            if (i >= 20) movedLate |= intent.DirX != 0 || intent.DirZ != 0 || intent.Interact;
        }
        Assert.Equal(flees, fled);
        if (!flees) Assert.False(movedLate, "a Silent, still bot stays off the killer's view");
    }

    [Theory]
    [InlineData(4, -1)]
    [InlineData(5, 0)]
    public void OnlyABigTeamLeavesTheTerminalTheKillerCameTo(int normal, int avoided)
    {
        var view = Survivor(); view.Team.Add(Mate(1, view.X, view.Z));
        float[][] far = { new float[] { 10, 10 }, new float[] { 70, 70 }, new float[] { 10, 70 }, new float[] { 70, 10 } };
        for (int k = 0; k < normal - 1; k++) view.Team.Add(Mate(2 + k, far[k][0], far[k][1]));
        var brain = new SurvivorBrain(); var rng = new Random(6);
        HackFirst(brain, view, rng);
        for (int i = 0; i < 20; i++) { KillerAt(view, 25, i); StepMap(brain, view, rng); }
        Assert.True(Read<bool>(brain, "_hiding"));
        Assert.Equal(avoided, Read<int>(brain, "_avoidTerminal"));
    }

    [Fact]
    public void AStalledLeadingSwitchSendsTheNextNearestSurvivorToAnotherPoweredSwitch()
    {
        var view = Survivor(); view.SelfId = 2; view.X = 60; view.Z = 60;
        view.Team.Add(Mate(1, 10, 11)); view.Team.Add(Mate(2, 60, 60));
        view.Exits = new[] { new BotExit { Powered = true, Progress = 50, SwitchX = 10, SwitchZ = 10 },
            new BotExit { Powered = true, Progress = 0, SwitchX = 70, SwitchZ = 70 } };
        view.ExitCount = 2;
        var brain = new SurvivorBrain();
        var duty = typeof(SurvivorBrain).GetMethod("SwitchDuty", Field)!;
        int Duty(double time) { view.Time = time; return (int)duty.Invoke(brain, new object[] { view })!; }
        Assert.Equal(-1, Duty(0)); // survivor 1 is nearer and holds the leading switch
        Assert.Equal(-1, Duty(29));
        Assert.Equal(1, Duty(30)); // no progress for SwitchStallSeconds: the other powered switch gets a holder
        view.Exits[1].Progress = 10;
        Assert.Equal(1, Duty(31)); // progress on the second switch does not recall its holder
        view.Exits[0].Progress = 51;
        Assert.Equal(-1, Duty(32)); // the leading switch moves again: one holder
    }
}
