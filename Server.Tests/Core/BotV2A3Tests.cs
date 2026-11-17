using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using Xunit;

namespace PingTheKiller.Tests.Core;

// U6d attempt 3: survivor commitment (commit mode, commit pick, switch duty away from the killer, idle allies) and the
// killer search / open-exit patrol, exercised with role views and the public static map.
public sealed class BotV2A3Tests
{
    private const BindingFlags Field = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly FixedMap _map = new();
    private readonly NavGrid _nav;
    public BotV2A3Tests() => _nav = new NavGrid(_map);

    private static T Read<T>(object brain, string name) => (T)brain.GetType().GetField(name, Field)!.GetValue(brain)!;
    private static void Write(object brain, string name, object value) => brain.GetType().GetField(name, Field)!.SetValue(brain, value);
    private static object? Call(object brain, string name, params object[] args) => brain.GetType().GetMethod(name, Field)!.Invoke(brain, args);

    private static PlayerState Mate(int id, float x, float z, PlayerStatus status = PlayerStatus.Normal, float timer = 0) =>
        new() { Id = id, X = x, Z = z, Status = (byte)status, StatusTimer = timer };

    private static SurvivorBotView Survivor() => new()
    {
        SelfId = 1, X = 41, Z = 42.7f, Speed = 4, Status = PlayerStatus.Normal,
        Terminals = new[] { new TerminalState { X = 41, Z = 44, Progress = 40 }, new TerminalState { X = 58, Z = 27, Progress = 20 } },
        TerminalCount = 2
    };

    private BotIntent Step(SurvivorBrain brain, SurvivorBotView view, Random rng)
    {
        view.Time += .05;
        return brain.Update(view, _nav, _map, rng);
    }

    private static byte ThreatAt(float distance) => distance <= GameConstants.ThreatLevel3Distance ? (byte)3
        : distance <= GameConstants.ThreatLevel2Distance ? (byte)2 : distance <= GameConstants.ThreatLevel1Distance ? (byte)1 : (byte)0;

    // One tick with the intent applied to the bot's position (and its own team entry).
    private BotIntent Walk(SurvivorBrain brain, SurvivorBotView view, Random rng)
    {
        var intent = Step(brain, view, rng);
        var next = _map.Move(view.X, view.Z, intent.DirX * view.Speed * .05f, intent.DirZ * view.Speed * .05f);
        view.X = next.X; view.Z = next.Z;
        var self = view.Team[0]; self.X = view.X; self.Z = view.Z; view.Team[0] = self;
        return intent;
    }

    [Fact]
    public void AfterCommitSecondsWithoutProgressAStillFarKillerNoLongerStopsTheWorkButAClosingOneStillDoes()
    {
        var view = Survivor(); view.Team.Add(Mate(1, view.X, view.Z));
        var brain = new SurvivorBrain(); var rng = new Random(7);
        bool hacked = false;
        for (int i = 0; i < 300 && !hacked; i++) hacked = Walk(brain, view, rng).Interact;
        Assert.True(hacked);
        // A still killer in plain sight 25 m off (outside every threat level): before commit mode the bot hides.
        view.KillerVisible = true; view.KillerX = 41; view.KillerZ = 17.7f;
        for (int i = 0; i < 100; i++) Walk(brain, view, rng);
        Assert.True(Read<bool>(brain, "_hiding"));
        Assert.False(Read<bool>(brain, "_committed"));
        // No progress in this view: CommitSeconds after the start the bot commits, walks to the commit pick (a terminal
        // out of the killer's line of sight) and hacks there although the killer stays in view.
        int hacks = 0;
        while (view.Time < 60)
        {
            var intent = Walk(brain, view, rng);
            if (view.Time >= 50 && intent.Interact) hacks++;
        }
        Assert.True(Read<bool>(brain, "_committed"));
        Assert.False(Read<bool>(brain, "_hiding"));
        int pick = Read<int>(brain, "_commitPick");
        Assert.InRange(pick, 0, 1);
        Assert.True(_map.SegmentBlocked(view.KillerX, view.KillerZ, view.Terminals[pick].X, view.Terminals[pick].Z) ||
            !_map.SegmentBlocked(view.KillerX, view.KillerZ, view.Terminals[1 - pick].X, view.Terminals[1 - pick].Z));
        // Hacking in bursts with HackPause breaks: about half of the ticks.
        Assert.True(hacks >= 60, $"committed bot hacked only {hacks} ticks of 200");
        Assert.True(MathF.Sqrt(MathF.Pow(view.X - view.Terminals[pick].X, 2) + MathF.Pow(view.Z - view.Terminals[pick].Z, 2)) <= 2f);
        // The killer walks in: inside CommitThreatDistance and closing, the committed bot breaks off (the normal hide and
        // flee rules take over) and stops hacking.
        bool brokeOff = false;
        int lateHacks = 0;
        view.KillerX = view.X;
        for (float d = 14; d > 7; d -= .05f)
        {
            view.KillerZ = view.Z - d; view.Threat = ThreatAt(d);
            var intent = Step(brain, view, rng);
            if (d < 9) lateHacks += intent.Interact ? 1 : 0;
            brokeOff |= d <= CommitThreatDistance && (Read<bool>(brain, "_hiding") || Read<bool>(brain, "_fleeing"));
        }
        Assert.True(brokeOff, "a closing killer inside 10 m interrupts commit mode");
        Assert.Equal(0, lateHacks);
    }

    private const float CommitThreatDistance = 10f;

    [Fact]
    public void CommittedSurvivorStillFleesAThreatThreeUnseenKiller()
    {
        var view = Survivor();
        view.Team.Add(new PlayerState { Id = 1, X = view.X, Z = view.Z, Status = (byte)PlayerStatus.Normal, Flags = (byte)PlayerFlags.Signal });
        var brain = new SurvivorBrain(); var rng = new Random(8);
        while (view.Time < 30) Walk(brain, view, rng);
        Assert.True(Read<bool>(brain, "_committed"));
        view.Threat = 3;
        bool fled = false;
        for (int i = 0; i < 40 && !fled; i++) { Walk(brain, view, rng); fled = Read<bool>(brain, "_fleeing"); }
        Assert.True(fled);
    }

    // QA-a3 M1 regression: late in the match, with the clock no longer fitting the work left (the old "desperate" mode,
    // TimeShort(view, 3, 30)), commit mode is still interrupted by a sighted killer closing in within CommitThreatDistance.
    private SurvivorBotView LateSurvivor()
    {
        var view = Survivor();
        view.TerminalsRequired = 2; // 60 % + 80 % of SurvivorBrain.ExpectedHackSeconds left, one Normal survivor
        view.Time = 700;
        view.Team.Add(new PlayerState { Id = 1, X = view.X, Z = view.Z, Status = (byte)PlayerStatus.Normal, Flags = (byte)PlayerFlags.Signal });
        return view;
    }

    private static bool TimeShort(SurvivorBotView view, double factor, double margin) =>
        (bool)typeof(SurvivorBrain).GetMethod("TimeShort", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { view, factor, margin })!;

    [Fact]
    public void LateInTheMatchWhenTheClockIsShortACommittedSurvivorStillBreaksOffForAClosingKiller()
    {
        var view = LateSurvivor();
        var brain = new SurvivorBrain(); var rng = new Random(11);
        bool hacked = false;
        for (int i = 0; i < 300 && !hacked; i++) hacked = Walk(brain, view, rng).Interact;
        Assert.True(hacked);
        // The old desperate condition holds, and the clock alone keeps the bot in commit mode.
        Assert.True(TimeShort(view, 3, 30));
        Assert.True((bool)Call(brain, "Urgent", view)!);
        bool brokeOff = false;
        int lateHacks = 0;
        view.KillerVisible = true; view.KillerX = view.X;
        for (float d = 14; d > 7; d -= .05f)
        {
            view.KillerZ = view.Z - d; view.Threat = ThreatAt(d);
            var intent = Step(brain, view, rng);
            if (d < 9) lateHacks += intent.Interact ? 1 : 0;
            brokeOff |= d <= CommitThreatDistance && (Read<bool>(brain, "_hiding") || Read<bool>(brain, "_fleeing"));
        }
        Assert.True(TimeShort(view, 3, 30));
        Assert.True(brokeOff, "a closing killer inside 10 m interrupts commit mode even when the clock is short");
        Assert.Equal(0, lateHacks);
    }

    [Fact]
    public void LateInTheMatchWhenTheClockIsShortACommittedSurvivorStillFleesThreatThree()
    {
        var view = LateSurvivor();
        var brain = new SurvivorBrain(); var rng = new Random(12);
        bool hacked = false;
        for (int i = 0; i < 300 && !hacked; i++) hacked = Walk(brain, view, rng).Interact;
        Assert.True(hacked);
        Assert.True(TimeShort(view, 3, 30));
        Assert.True((bool)Call(brain, "Urgent", view)!);
        view.Threat = 3;
        bool fled = false;
        float startX = view.X, startZ = view.Z;
        for (int i = 0; i < 40 && !fled; i++) { Walk(brain, view, rng); fled = Read<bool>(brain, "_fleeing"); }
        Assert.True(fled, "ThreatLevel 3 interrupts commit mode even when the clock is short");
        // It is on its way off the terminal, not hacking at it.
        for (int i = 0; i < 20; i++) Assert.False(Walk(brain, view, rng).Interact);
        Assert.True(MathF.Sqrt(MathF.Pow(view.X - startX, 2) + MathF.Pow(view.Z - startZ, 2)) > .5f);
    }

    // The map v2.1 terminals.
    private static readonly float[,] Terminals =
        { { 10, 25 }, { 58, 27 }, { 64.5f, 74 }, { 19, 49 }, { 41, 44 }, { 54.5f, 8 }, { 35.5f, 60 }, { 76, 40 } };

    [Fact]
    public void CommitPickAvoidsTheKillersTerminalAndPrefersOneOutOfItsSight()
    {
        for (int k = 0; k < Terminals.GetLength(0); k++)
        {
            var view = new SurvivorBotView { SelfId = 1, X = 40, Z = 40, Speed = 4, Status = PlayerStatus.Normal, Time = 100, TerminalCount = 8,
                Terminals = new TerminalState[8] };
            for (int i = 0; i < 8; i++) view.Terminals[i] = new TerminalState { X = Terminals[i, 0], Z = Terminals[i, 1], Progress = 30 };
            view.Team.Add(Mate(1, view.X, view.Z));
            var brain = new SurvivorBrain();
            Write(brain, "_killerSeenAt", view.Time);
            Write(brain, "_killerX", Terminals[k, 0]);
            Write(brain, "_killerZ", Terminals[k, 1] + 2);
            int pick = (int)Call(brain, "CommitPick", view, _map)!;
            Assert.InRange(pick, 0, 7);
            Assert.NotEqual(k, pick);
            bool anyHidden = false;
            for (int i = 0; i < 8; i++)
                if (i != k && _map.SegmentBlocked(Terminals[k, 0], Terminals[k, 1] + 2, Terminals[i, 0], Terminals[i, 1])) anyHidden = true;
            if (anyHidden) Assert.True(_map.SegmentBlocked(Terminals[k, 0], Terminals[k, 1] + 2, Terminals[pick, 0], Terminals[pick, 1]),
                $"killer at terminal {k}: pick {pick} is in its line of sight");
        }
    }

    [Fact]
    public void ALoneSurvivorMovesToTheOtherSwitchWhenTheLeadingOneStallsNearTheKiller()
    {
        var view = new SurvivorBotView { SelfId = 1, X = 40, Z = 40, Speed = 4, Status = PlayerStatus.Normal };
        view.Team.Add(Mate(1, 40, 40));
        view.Exits = new[] { new BotExit { Powered = true, Progress = 30, SwitchX = 8, SwitchZ = 8 },
            new BotExit { Powered = true, Progress = 0, SwitchX = 72, SwitchZ = 72 } };
        view.ExitCount = 2;
        var brain = new SurvivorBrain();
        Write(brain, "_killerSeenAt", 0.0);
        Write(brain, "_killerX", 10f);
        Write(brain, "_killerZ", 10f);
        int Duty(double time) { view.Time = time; return (int)Call(brain, "SwitchDuty", view)!; }
        Assert.Equal(0, Duty(0)); // the leading switch first
        Assert.Equal(0, Duty(29));
        Assert.Equal(1, Duty(30)); // stalled: the switch farthest from the killer
    }

    [Fact]
    public void AnIdleTeammateIsNotCountedOnToRescueOrHoldASwitch()
    {
        var view = new SurvivorBotView { SelfId = 2, X = 60, Z = 60, Speed = 4, Status = PlayerStatus.Normal };
        view.Team.Add(Mate(1, 30, 30)); // never moves, never Signals
        view.Team.Add(Mate(2, 60, 60));
        view.Team.Add(Mate(3, 33, 33, PlayerStatus.Lagging, 60));
        view.Exits = new[] { new BotExit { Powered = true, SwitchX = 28, SwitchZ = 28 } };
        view.ExitCount = 1;
        var brain = new SurvivorBrain();
        void At(double time) { view.Time = time; Call(brain, "TrackTeam", view); }
        At(0);
        Assert.Equal(-1, (int)Call(brain, "RescueTarget", view, view.Time)!); // the nearer teammate goes
        Assert.Equal(-1, (int)Call(brain, "SwitchDuty", view)!);
        At(13); // AllyIdleSeconds without a move or a Signal
        Assert.Equal(2, (int)Call(brain, "RescueTarget", view, view.Time)!);
        Assert.Equal(0, (int)Call(brain, "SwitchDuty", view)!);
    }

    [Fact]
    public void EscapeDetourIsReusedForOneSecondAndThenReplanned()
    {
        var view = new SurvivorBotView { SelfId = 1, X = 5, Z = 25, Speed = 4, Status = PlayerStatus.Normal, KillerVisible = true, KillerX = 5, KillerZ = 15 };
        view.Exits = new[] { new BotExit { X = 5, Z = 5, Open = true } };
        view.ExitCount = 1;
        var brain = new SurvivorBrain();
        Call(brain, "PlanEscape", view, 0, _nav);
        Assert.Equal(0.0, Read<double>(brain, "_escapePlanAt"));
        float x = Read<float>(brain, "_goalX"), z = Read<float>(brain, "_goalZ");
        Assert.False(x == 5 && z == 5, "the visible killer blocks the straight route");
        view.Time = 0.5;
        Call(brain, "PlanEscape", view, 0, _nav);
        Assert.Equal(0.0, Read<double>(brain, "_escapePlanAt"));
        Assert.Equal(x, Read<float>(brain, "_goalX"));
        Assert.Equal(z, Read<float>(brain, "_goalZ"));
        view.Time = 1.1;
        Call(brain, "PlanEscape", view, 0, _nav);
        Assert.Equal(1.1, Read<double>(brain, "_escapePlanAt"));
    }

    // Eight public terminals (the large-lobby knobs), none done.
    private static KillerBotView Killer()
    {
        var view = new KillerBotView { X = 41, Z = 43, Speed = 4.6f, TerminalCount = 8, Terminals = new KillerBotView.Terminal[8] };
        for (int i = 0; i < 8; i++) view.Terminals[i] = new KillerBotView.Terminal { X = Terminals[i, 0], Z = Terminals[i, 1] };
        return view;
    }

    [Fact]
    public void KillerWithoutAnyBlipForSearchSecondsSweepsTheMapCellByCell()
    {
        var view = Killer(); var brain = new KillerBrain(); var rng = new Random(3);
        var spots = new List<(float X, float Z)>();
        bool searchedEarly = false;
        for (int i = 0; i < 20 * 150; i++)
        {
            view.Time = i * .05;
            var intent = brain.Update(view, _nav, _map, rng);
            var next = _map.Move(view.X, view.Z, intent.DirX * view.Speed * .05f, intent.DirZ * view.Speed * .05f);
            view.X = next.X; view.Z = next.Z;
            if (Read<int>(brain, "_patrol") != view.TerminalCount + 3) continue;
            if (view.Time < KillerBrain.SearchSeconds) searchedEarly = true;
            var spot = (Read<float>(brain, "_patrolX"), Read<float>(brain, "_patrolZ"));
            if (spots.Count == 0 || spots[^1] != spot) spots.Add(spot);
        }
        Assert.False(searchedEarly);
        Assert.True(spots.Count >= 4, $"only {spots.Count} search spots in 105 s");
        Assert.Equal(spots.Count, spots.Distinct().Count());
    }

    [Theory]
    [InlineData(0, false)] // nobody works the unopened switch: patrol the open exit only
    [InlineData(10, true)] // the unopened switch has progress: guard it, in turn with the open exit
    public void KillerPatrolsAnOpenExitAndGuardsAnUnopenedSwitchOnlyWhileItHasProgress(byte progress, bool guards)
    {
        var view = Killer();
        for (int i = 0; i < 8; i++) view.Terminals[i].Done = true;
        view.Exits = new[] { new BotExit { X = 4, Z = 4, SwitchX = 8, SwitchZ = 8, Powered = true, Open = true },
            new BotExit { X = 76, Z = 76, SwitchX = 72, SwitchZ = 72, Powered = true, Progress = progress } };
        view.ExitCount = 2;
        var brain = new KillerBrain(); var rng = new Random(4);
        brain.Update(view, _nav, _map, rng);
        int openPatrols = 0, guardPatrols = 0;
        for (int k = 1; k <= 4; k++)
        {
            view.Time = k;
            Write(brain, "_patrol", -1);
            Call(brain, "Patrol", new BotIntent(), view, _nav, _map, rng);
            int patrol = Read<int>(brain, "_patrol");
            float x = Read<float>(brain, "_patrolX"), z = Read<float>(brain, "_patrolZ");
            if (patrol == view.TerminalCount + 2) { openPatrols++; Assert.True(MathF.Sqrt((x - 4) * (x - 4) + (z - 4) * (z - 4)) <= 3.01f); }
            else if (patrol == view.TerminalCount) { guardPatrols++; Assert.True(MathF.Sqrt((x - 72) * (x - 72) + (z - 72) * (z - 72)) <= 3.01f); }
            else Assert.Fail($"patrol {patrol}");
        }
        Assert.True(openPatrols >= 2);
        Assert.Equal(guards, guardPatrols >= 2);
        if (!guards) Assert.Equal(0, guardPatrols);
    }
}
