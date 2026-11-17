using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// R3 (11/17): a rescue needs the rescuer to HOLD Interact within RescueRadius of a Lagging ally for 3 s. Survivor 0 spawns at
// (5,26) and the killer captures it from (5,25); survivor 1 is the rescuer.
public sealed class ExplicitRescueTests
{
    private static RulesMatch Captured(int survivors = 2)
    {
        var m = RulesMatch.Start(survivors);
        m.WalkTo(m.Killer, 5, 25, GameConstants.KillerSpeed);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 5);
        m.Tick();
        return m;
    }

    private static void Hold(Peer p) => p.Send(new C2S_Interact { Active = 1 });
    private static void Release(Peer p) => p.Send(new C2S_Interact { Active = 0 });

    [Fact]
    public void StandingNextToALaggingAllyNeverRescuesWithoutTheHold()
    {
        using var m = Captured();
        m.F.Server.PlaceForTests(m[1].Id, 6, 26);
        m.Tick(200); // 10 s of standing in range
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        Assert.Equal(0, m.Truth(m[1]).Flags & (byte)PlayerFlags.Rescuing);
        Assert.Equal(0f, m[0].Last<S2C_SelfState>().RescueProgress);
        Assert.Equal((byte)ActionKind.None, m[1].Last<S2C_SelfState>().ActionKind);
    }

    [Fact]
    public void HoldingEForThreeSecondsRescuesAndTheRescuerReportsTheRescueAction()
    {
        using var m = Captured();
        m.F.Server.PlaceForTests(m[1].Id, 6, 26);
        Hold(m[1]);
        m.Tick(30);
        var self = m[1].Last<S2C_SelfState>();
        Assert.Equal((byte)ActionKind.Rescue, self.ActionKind);
        Assert.InRange(self.ActionProgress, 0.1f, 0.55f); // SelfState is 1 Hz plus on change: the last sample may be up to 1 s old
        Assert.NotEqual(0, m.Truth(m[1]).Flags & (byte)PlayerFlags.Rescuing);
        m.Tick(29);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        m.Tick(2);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[0]).Status);
    }

    [Fact]
    public void ReleasingEOrLeavingRangeResetsTheCount()
    {
        using var m = Captured();
        m.F.Server.PlaceForTests(m[1].Id, 6, 26);
        Hold(m[1]);
        m.Tick(55);
        Release(m[1]);
        m.Tick();
        Assert.Equal(0f, m[0].Last<S2C_SelfState>().RescueProgress);
        Hold(m[1]);
        m.Tick(55); // 2.75 s again: the earlier 2.75 s is gone
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        m.F.Server.PlaceForTests(m[1].Id, 8, 26); // out of range for one tick
        m.Tick();
        m.F.Server.PlaceForTests(m[1].Id, 6, 26);
        m.Tick(55);
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        m.Tick(10);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[0]).Status);
    }

    [Fact]
    public void RescueWinsTheEHoldOverATerminalAtTheSameSpot()
    {
        using var m = Captured();
        var terminal = m.F.Server.Map.TerminalPositions[0];
        m.F.Server.PlaceForTests(m[0].Id, terminal.X + 0.5f, terminal.Z);
        m.F.Server.PlaceForTests(m[1].Id, terminal.X - 0.5f, terminal.Z);
        m.F.Server.PlaceForTests(m.Killer.Id, 76, 76);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, m.Truth(m[0]).Status);
        Hold(m[1]);
        m.Tick(40);
        Assert.Equal(0, m.Killer.Last<S2C_WorldState>().Terminals[0].Progress);
        Assert.Equal((byte)ActionKind.Rescue, m[1].Last<S2C_SelfState>().ActionKind);
        Assert.Equal(0, m.Truth(m[1]).Flags & (byte)PlayerFlags.Hacking);
    }

    [Fact]
    public void RescueWinsAFreshPressOverAShutterInReach()
    {
        using var m = Captured();
        // Shutter 5 (west loop alley): pull range is within ShutterPullRadius of z 33.5; put the Lagging ally right beside the rescuer.
        m.F.Server.PlaceForTests(m[0].Id, 19.5f, 32.2f);
        m.F.Server.PlaceForTests(m[1].Id, 19.5f, 32.8f);
        m.Tick();
        Hold(m[1]);
        m.Tick(5);
        Assert.Equal((byte)ActionKind.Rescue, m[1].Last<S2C_SelfState>().ActionKind);
        Assert.False(WorldChaseProps.Has(m[1].Self(), ChaseFlags.Pulling));
    }

    [Fact]
    public void AVaultingRescuerDoesNotCountAndTheKillerSeesOnlySignal()
    {
        using var m = Captured();
        m.F.Server.PlaceForTests(m[1].Id, 6, 26);
        Hold(m[1]);
        m.Tick(10);
        var seen = m.KillerSees(m[1]);
        Assert.NotNull(seen);
        Assert.Equal((byte)PlayerFlags.Signal, seen!.Value.Flags); // Rescuing never reaches the killer
    }

    [Fact]
    public void ABotHoldsInteractNextToALaggingAlly()
    {
        var map = new FixedMap();
        var nav = new NavGrid(map);
        var view = new SurvivorBotView
        {
            SelfId = 1, X = 41, Z = 42.7f, Speed = 4, Status = PlayerStatus.Normal,
            Terminals = new[] { new TerminalState { X = 41, Z = 44, Progress = 40 } }, TerminalCount = 1
        };
        view.Team.Add(new PlayerState { Id = 1, X = view.X, Z = view.Z, Status = (byte)PlayerStatus.Normal });
        view.Team.Add(new PlayerState { Id = 2, X = 42.5f, Z = 43f, Status = (byte)PlayerStatus.Lagging, StatusTimer = 40, CaptureCount = 1 });
        var brain = new SurvivorBrain();
        var rng = new Random(4);
        bool held = false;
        for (int i = 0; i < 200 && !held; i++)
        {
            view.Time += .05;
            var intent = brain.Update(view, nav, map, rng);
            var next = map.Move(view.X, view.Z, intent.DirX * .2f, intent.DirZ * .2f);
            view.X = next.X; view.Z = next.Z;
            var self = view.Team[0]; self.X = view.X; self.Z = view.Z; view.Team[0] = self;
            float dx = view.X - 42.5f, dz = view.Z - 43f;
            if (intent.Interact && dx * dx + dz * dz <= GameConstants.RescueRadius * GameConstants.RescueRadius) held = true;
        }
        Assert.True(held, "the bot must hold Interact while within rescue range of the Lagging ally");
    }

    // ---- R3 S5: getting up after a rescue (1.5 s, inside the 2 s invulnerability) ----

    private static RulesMatch RescuedByHold()
    {
        var m = Captured();
        m.F.Server.PlaceForTests(m[1].Id, 6, 26);
        Hold(m[1]);
        m.Tick(61);
        Release(m[1]);
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[0]).Status);
        return m;
    }

    private static RulesMatch RescuedBySkill()
    {
        var m = RulesMatch.Start(2, SkillIds.PingScanner, SkillIds.EmergencyRestore);
        m.WalkTo(m.Killer, 10, 25, GameConstants.KillerSpeed);
        m.WalkTo(m[0], 8.6f, 26, GameConstants.SurvivorSpeed);
        m[0].Send(new C2S_UseSkill { SkillId = SkillIds.EmergencyRestore });
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 5);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Normal, m.Truth(m[1]).Status);
        return m;
    }

    private static void AssertGettingUpThenMoves(RulesMatch m, Peer getter)
    {
        Assert.NotEqual(0, m.Truth(getter).Flags & (byte)PlayerFlags.GettingUp);
        var start = getter.Self();
        // Hold a move key for 1.4 s: the position does not change, the look yaw is read, and the flag stays on.
        for (int i = 0; i < 28; i++)
        {
            getter.Send(new C2S_MoveInput { DirX = 1, DirZ = 0, Yaw = 90, Seq = (ushort)(1000 + i) });
            m.Tick();
            Assert.NotEqual(0, m.Truth(getter).Flags & (byte)PlayerFlags.GettingUp);
            Assert.Equal((byte)PlayerStatus.Normal, m.Truth(getter).Status);
        }
        var held = getter.Self();
        Assert.Equal(start.X, held.X, 3);
        Assert.Equal(start.Z, held.Z, 3);
        Assert.Equal(90f, m.Truth(getter).Yaw, 1);
        // 1.5 s after the rescue the flag is gone and the held key moves the Survivor again.
        for (int i = 0; i < 20; i++)
        {
            getter.Send(new C2S_MoveInput { DirX = 1, DirZ = 0, Yaw = 90, Seq = (ushort)(2000 + i) });
            m.Tick();
        }
        Assert.Equal(0, m.Truth(getter).Flags & (byte)PlayerFlags.GettingUp);
        Assert.True(getter.Self().X > start.X + 1f, "movement resumes after the get-up");
    }

    [Fact]
    public void ARescuedSurvivorCannotMoveForOnePointFiveSecondsAndThenMovesAgain()
    {
        using var m = RescuedByHold();
        AssertGettingUpThenMoves(m, m[0]);
    }

    [Fact]
    public void AnEmergencyRestoredSurvivorGetsUpTheSameWay()
    {
        using var m = RescuedBySkill();
        AssertGettingUpThenMoves(m, m[1]);
    }

    [Fact]
    public void AGettingUpSurvivorIsInvulnerableThroughoutAndCannotInteractOrUseASkill()
    {
        using var m = RescuedByHold();
        var target = m[0];
        var pos = target.Self();
        m.F.Server.PlaceForTests(m.Killer.Id, pos.X + 0.3f, pos.Z); // the Killer stands on top of it
        var terminal = m.F.Server.Map.TerminalPositions[0];
        m.F.Server.PlaceForTests(target.Id, terminal.X, terminal.Z);
        Hold(target);
        target.Send(new C2S_UseSkill { SkillId = target.Last<S2C_MatchStart>().SkillId });
        int before = target.All<S2C_SkillRejected>().Count;
        for (int i = 0; i < 28; i++)
        {
            m.Tick();
            Assert.Equal((byte)PlayerStatus.Normal, m.Truth(target).Status);
        }
        Assert.Equal((byte)ActionKind.None, target.Last<S2C_SelfState>().ActionKind);
        Assert.Equal(0, m.Killer.Last<S2C_WorldState>().Terminals[0].Progress);
        var rejected = target.All<S2C_SkillRejected>();
        Assert.True(rejected.Count > before || before > 0, "the skill press is refused while getting up");
        Assert.Equal((byte)SkillRejectReason.NotNormal, rejected[rejected.Count - 1].Reason);
    }

    [Fact]
    public void TheGettingUpFlagIsOnTheWireToSurvivorsAndNeverInTheKillerView()
    {
        using var m = RescuedByHold();
        var ally = m[1].Last<S2C_WorldState>();
        Assert.NotEqual(0, RulesMatch.Entry(ally, m[0].Id)!.Value.Flags & (byte)PlayerFlags.GettingUp);
        // The Killer's survivor entries are anonymous and carry Signal only (a flag that decoys never have would tell a real
        // Survivor from a phantom); masking is unchanged.
        m.F.Server.PlaceForTests(m.Killer.Id, 76, 76);
        m.Tick();
        foreach (var entry in m.Killer.Last<S2C_WorldState>().Players)
            Assert.Equal(0, entry.Flags & (byte)PlayerFlags.GettingUp);
    }
}
