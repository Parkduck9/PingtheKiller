using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

// GDD v2 section 8 (unit Q5a): shutters and vault rails. Shutter 5 spans the west loop alley x 18-21 at z 33.35-33.65
// (crossed along Z); rail 5 stands at x 36.35-36.65, z 25-28 (crossed along X, vault point (36.5, 26.5)).
public sealed class ChasePropsTests
{
    private const int Shutter = 5;
    private const int Rail = 5;
    private static readonly int PullTicks = Ticks(GameConstants.ShutterPullSeconds);

    private static int Ticks(float seconds) => (int)Math.Round(seconds * GameConstants.TickRate);
    private static void Press(Peer peer) => peer.Send(new C2S_Interact { Active = 1 });
    private static void Release(Peer peer) => peer.Send(new C2S_Interact { Active = 0 });

    private static void Tap(RulesMatch m, Peer peer)
    {
        Press(peer);
        m.Tick();
        Release(peer);
    }

    private static RulesMatch Match(int survivors = 1)
    {
        var m = RulesMatch.Start(survivors);
        return m;
    }

    private static ShutterState State(RulesMatch m) => m.F.Server.ShutterStateForTests(Shutter);

    [Fact]
    public void PullTakesThreeTenthsFreezesTheSurvivorAndDropsTheShutterForEveryone()
    {
        using var m = Match();
        var s = m[0];
        m.F.Server.PlaceForTests(s.Id, 19.5f, 32.5f);
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 5);
        m.Tick();
        Assert.All(s.Last<S2C_WorldState>().Shutters, b => Assert.Equal((byte)ShutterState.Up, b));
        Assert.Equal(WorldChaseProps.ShutterCount, m.Killer.Last<S2C_WorldState>().Shutters.Count);
        Tap(m, s);
        Assert.True(WorldChaseProps.Has(s.Self(), ChaseFlags.Pulling));
        for (int i = 1; i < PullTicks; i++)
        {
            Assert.Equal(ShutterState.Up, State(m));
            s.Move(0, 1); // ignored while pulling
            m.Tick();
        }
        Assert.Equal(ShutterState.Up, State(m));
        m.Tick(); // 0.3 s after the pull started
        Assert.Equal(ShutterState.Down, State(m));
        Assert.Equal(32.5f, s.Self().Z, 3);
        Assert.Equal((byte)ShutterState.Down, m.Killer.Last<S2C_WorldState>().Shutters[Shutter]);
        Assert.Equal((byte)ShutterState.Down, s.Last<S2C_WorldState>().Shutters[Shutter]);
        // A Down shutter blocks movement.
        for (int i = 0; i < 20; i++) { s.Move(0, 1); m.Tick(); }
        Assert.True(s.Self().Z < 33.35f - FixedMap.PlayerRadius + 0.01f);
    }

    [Fact]
    public void KillerInTheZoneIsStunnedTwoSecondsAndCannotMoveCaptureOrUseSkills()
    {
        using var m = Match(2);
        var puller = m[0];
        var bait = m[1];
        m.F.Server.PlaceForTests(puller.Id, 19.5f, 32.5f);
        m.F.Server.PlaceForTests(m.Killer.Id, 19.5f, 34.3f);
        m.F.Server.PlaceForTests(bait.Id, 19.5f, 36.4f); // out of capture range (2.1 m)
        m.Tick();
        Tap(m, puller);
        m.Tick(PullTicks);
        Assert.Equal(ShutterState.Down, State(m));
        var killerSelf = m.Killer.Self();
        Assert.True(WorldChaseProps.Has(killerSelf, ChaseFlags.Stunned));
        Assert.True(killerSelf.Z > 33.65f + FixedMap.PlayerRadius - 0.01f); // on the far side from the puller
        // The bait walks into capture range; the stunned killer cannot capture, move or use its skill.
        m.F.Server.PlaceForTests(bait.Id, killerSelf.X, killerSelf.Z + 0.9f);
        m.Killer.Send(new C2S_UseSkill { SkillId = m.Killer.Last<S2C_MatchStart>().SkillId });
        for (int i = 0; i < Ticks(GameConstants.ShutterStunSeconds) - 2; i++)
        {
            m.Killer.Move(0, 1);
            m.Tick();
        }
        Assert.Equal((byte)PlayerStatus.Normal, bait.Self().Status);
        Assert.Equal(killerSelf.Z, m.Killer.Self().Z, 3);
        Assert.Contains(m.Killer.Packets.OfType<S2C_SkillRejected>(), r => r.Reason == (byte)SkillRejectReason.NotNormal);
        m.Killer.Move(0, 0);
        m.Tick(3);
        Assert.False(WorldChaseProps.Has(m.Killer.Self(), ChaseFlags.Stunned));
        Assert.Equal((byte)PlayerStatus.Lagging, bait.Self().Status); // captured once the stun ends
    }

    [Fact]
    public void KillerOutsideTheZoneIsNotStunned()
    {
        using var m = Match();
        m.F.Server.PlaceForTests(m[0].Id, 19.5f, 32.5f);
        m.F.Server.PlaceForTests(m.Killer.Id, 19.5f, 34.7f); // 1.05 m from the box: zone is 0.4 + 0.6
        m.Tick();
        Tap(m, m[0]);
        m.Tick(PullTicks);
        Assert.Equal(ShutterState.Down, State(m));
        Assert.False(WorldChaseProps.Has(m.Killer.Self(), ChaseFlags.Stunned));
        m.Killer.Move(1, 0);
        m.Tick();
        Assert.NotEqual(19.5f, m.Killer.Self().X);
    }

    [Fact]
    public void BodiesInTheGapArePushedOutAndThePullerKeepsItsSide()
    {
        using var m = Match(2);
        var puller = m[0];
        var other = m[1];
        m.F.Server.PlaceForTests(puller.Id, 18.6f, 33.2f);  // overlapping the box, south of its centre line
        m.F.Server.PlaceForTests(other.Id, 20.4f, 33.55f);  // overlapping, north of the centre line
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 5);
        m.Tick();
        Tap(m, puller);
        m.Tick(PullTicks);
        Assert.Equal(ShutterState.Down, State(m));
        var shutter = WorldChaseProps.Shutters[Shutter];
        Assert.False(shutter.IntersectsCircle(puller.Self().X, puller.Self().Z, FixedMap.PlayerRadius));
        Assert.False(shutter.IntersectsCircle(other.Self().X, other.Self().Z, FixedMap.PlayerRadius));
        Assert.True(puller.Self().Z < shutter.MinZ);
        Assert.True(other.Self().Z > shutter.MaxZ);
        Assert.Equal(18.6f, puller.Self().X, 3);
    }

    [Fact]
    public void SurvivorVaultsADownShutterInOneSecondAndTheKillerCannot()
    {
        using var m = Match();
        var s = m[0];
        m.F.Server.SetShutterForTests(Shutter, ShutterState.Down);
        m.F.Server.PlaceForTests(s.Id, 19.5f, 32.6f);
        m.F.Server.PlaceForTests(m.Killer.Id, 19.5f, 38.5f); // out of capture reach of the landing spot
        m.Tick();
        Tap(m, s);
        Assert.True(WorldChaseProps.Has(s.Self(), ChaseFlags.Vaulting));
        m.Tick(Ticks(GameConstants.ShutterVaultSeconds) - 2);
        Assert.True(WorldChaseProps.Has(s.Self(), ChaseFlags.Vaulting));
        m.Tick(2);
        Assert.False(WorldChaseProps.Has(s.Self(), ChaseFlags.Vaulting));
        Assert.True(s.Self().Z > 33.65f + FixedMap.PlayerRadius - 0.01f);
        // The killer's press next to a Down shutter starts no vault.
        m.F.Server.PlaceForTests(s.Id, 5, 26);
        m.F.Server.PlaceForTests(m.Killer.Id, 19.5f, 34.3f);
        m.Tick();
        Tap(m, m.Killer);
        m.Tick(Ticks(2));
        Assert.True(m.Killer.Self().Z > 33.65f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothRolesVaultARailToTheMirroredSide(bool killer)
    {
        using var m = Match();
        var peer = killer ? m.Killer : m[0];
        m.F.Server.PlaceForTests(m.Killer.Id, killer ? 35.7f : 5f, killer ? 26.7f : 5f);
        m.F.Server.PlaceForTests(m[0].Id, killer ? 5f : 35.7f, killer ? 26f : 26.7f);
        m.Tick();
        // Rails always block movement.
        for (int i = 0; i < 10; i++) { peer.Move(1, 0); m.Tick(); }
        peer.Move(0, 0);
        m.Tick(4);
        Assert.True(peer.Self().X < 36.35f);
        float x0 = peer.Self().X;
        Tap(m, peer);
        int ticks = Ticks(killer ? GameConstants.RailVaultKillerSeconds : GameConstants.RailVaultSurvivorSeconds);
        m.Tick(ticks - 2);
        Assert.True(WorldChaseProps.Has(peer.Self(), ChaseFlags.Vaulting));
        m.Tick(2);
        var end = peer.Self();
        Assert.False(WorldChaseProps.Has(end, ChaseFlags.Vaulting));
        // Mirrored across the vault point, at least clear of the rail (half thickness + radius + margin).
        Assert.Equal(36.5f + MathF.Max(36.5f - x0, 0.15f + FixedMap.PlayerRadius + 0.05f + 0.1f), end.X, 2);
        Assert.Equal(26.7f, end.Z, 2);
    }

    [Fact]
    public void KillerBreaksADownShutterByHoldingStillTwoSecondsAndMovingOrReleasingCancels()
    {
        using var m = Match();
        m.F.Server.SetShutterForTests(Shutter, ShutterState.Down);
        m.F.Server.PlaceForTests(m[0].Id, 5, 26);
        m.F.Server.PlaceForTests(m.Killer.Id, 19.5f, 34.5f);
        m.Tick();
        Press(m.Killer);
        m.Tick(Ticks(1.5f));
        Assert.True(WorldChaseProps.Has(m.Killer.Self(), ChaseFlags.Breaking));
        m.Step(m.Killer, 1, 0); // moving cancels
        m.Tick(Ticks(1.5f));
        Assert.Equal(ShutterState.Down, State(m));
        Release(m.Killer); // releasing cancels
        m.Tick();
        Press(m.Killer);
        m.Tick(Ticks(1.5f));
        Release(m.Killer);
        m.Tick(Ticks(1));
        Assert.Equal(ShutterState.Down, State(m));
        Press(m.Killer);
        m.Tick(Ticks(GameConstants.ShutterBreakSeconds) + 1);
        Assert.Equal(ShutterState.Broken, State(m));
        Assert.Equal((byte)ShutterState.Broken, m[0].Last<S2C_WorldState>().Shutters[Shutter]);
        // Broken is passable and permanent: a survivor press near it does nothing.
        Release(m.Killer);
        m.F.Server.PlaceForTests(m[0].Id, 19.5f, 32.5f);
        m.Tick();
        Tap(m, m[0]);
        m.Tick(PullTicks + 1);
        Assert.Equal(ShutterState.Broken, State(m));
        for (int i = 0; i < 20; i++) { m[0].Move(0, 1); m.Tick(); }
        Assert.True(m[0].Self().Z > 34f);
    }

    [Fact]
    public void PriorityRescueContactAndAHeldInteractNeverStartAPull()
    {
        using var m = Match(2);
        var s = m[0];
        var ally = m[1];
        // The ally is captured next to the shutter, then the killer leaves.
        m.F.Server.PlaceForTests(ally.Id, 20.2f, 31.5f);
        m.F.Server.PlaceForTests(m.Killer.Id, 20.2f, 30.5f);
        m.F.Server.PlaceForTests(s.Id, 5, 26);
        m.Tick();
        Assert.Equal((byte)PlayerStatus.Lagging, ally.Self().Status);
        m.F.Server.PlaceForTests(m.Killer.Id, 5, 5);
        m.F.Server.PlaceForTests(s.Id, 19.2f, 32.5f); // 0.85 m from the box, 1.4 m from the ally
        m.Tick(2);
        Tap(m, s);
        m.Tick(PullTicks + 1);
        Assert.Equal(ShutterState.Up, State(m)); // rescue first
        // A held Interact is not a press: walking into range while holding never pulls.
        m.F.Server.PlaceForTests(ally.Id, 5, 36);
        m.F.Server.PlaceForTests(s.Id, 19.5f, 30f);
        m.Tick();
        Press(s);
        m.Tick();
        for (int i = 0; i < 10; i++) { s.Move(0, 1); m.Tick(); }
        s.Move(0, 0);
        m.Tick(PullTicks + 1);
        Assert.Equal(ShutterState.Up, State(m));
        Release(s);
        m.Tick();
        Tap(m, s);
        m.Tick(PullTicks);
        Assert.Equal(ShutterState.Down, State(m));
    }

    [Fact]
    public void PullAndVaultForceSignalForTheirLengthAndOneSecondAfterAndTheKillerViewStaysAnonymous()
    {
        using var m = Match();
        var s = m[0];
        m.F.Server.PlaceForTests(s.Id, 19.5f, 32.5f);
        m.F.Server.PlaceForTests(m.Killer.Id, 19.5f, 40f); // sees the alley, far from the zone
        m.Tick(4);
        Assert.Null(m.KillerSees(s)); // standing still: Silent and omitted
        Tap(m, s);
        var entry = m.KillerSees(s);
        Assert.NotNull(entry);
        Assert.Equal(0, entry!.Value.Chase); // anonymous entry: no chase flags for the killer
        Assert.True(WorldChaseProps.Has(m.Truth(s), ChaseFlags.Pulling));
        m.Tick(PullTicks - 1);
        Assert.NotNull(m.KillerSees(s));
        m.Tick(Ticks(GameConstants.ChaseSignalTailSeconds) - 2);
        Assert.NotNull(m.KillerSees(s));
        m.Tick(3);
        if (m.Killer.Last<S2C_MatchStart>().SkillId != SkillIds.PacketAfterimage) Assert.Null(m.KillerSees(s));
        Assert.All(m.Killer.Last<S2C_WorldState>().Players, p => Assert.True(p.Role == (byte)Role.Killer || p.Chase == 0));
    }

    [Fact]
    public void ACapturedVaulterLandsOnAStandableSideAndTheVaultStops()
    {
        using var m = Match();
        var s = m[0];
        m.F.Server.PlaceForTests(s.Id, 35.7f, 26.5f);
        m.F.Server.PlaceForTests(m.Killer.Id, 37.6f, 26.5f);
        m.Tick();
        Tap(m, s);
        m.Tick(Ticks(0.5f));
        Assert.Equal((byte)PlayerStatus.Lagging, s.Self().Status);
        Assert.False(WorldChaseProps.Has(s.Self(), ChaseFlags.Vaulting));
        Assert.True(m.F.Server.Map.CanStand(s.Self().X, s.Self().Z));
    }

    [Fact]
    public void AReconnectingPlayerGetsTheShutterStatesInItsFirstSnapshot()
    {
        using var f = new CoreFixture();
        var (killer, survivor) = f.StartPair();
        var identity = survivor.Last<S2C_JoinResult>();
        f.Server.SetShutterForTests(2, ShutterState.Down);
        f.Server.SetShutterForTests(7, ShutterState.Broken);
        survivor.Dispose();
        f.Tick(); f.Tick();
        var restored = f.Join("Back", identity.ReconnectToken);
        var world = restored.Last<S2C_WorldState>();
        Assert.Equal(WorldChaseProps.ShutterCount, world.Shutters.Count);
        Assert.Equal(ShutterState.Down, WorldChaseProps.ShutterOf(world, 2));
        Assert.Equal(ShutterState.Broken, WorldChaseProps.ShutterOf(world, 7));
        Assert.Equal(world.Shutters, killer.Last<S2C_WorldState>().Shutters);
    }

    [Fact]
    public void ANewMatchResetsEveryShutterToUp()
    {
        using var m = Match();
        m.F.Server.SetShutterForTests(1, ShutterState.Broken);
        m.F.Server.SetShutterForTests(3, ShutterState.Down);
        Assert.False(m.F.Server.Nav.FindPath(40.5f, 17f, 40.5f, 20f, new List<MapPoint>()) &&
            m.F.Server.Nav.LineClear(40.5f, 17f, 40.5f, 20f));
        var server = m.F.Server;
        typeof(GameServer).GetMethod("ResetChaseProps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(server, null);
        for (int i = 0; i < WorldChaseProps.ShutterCount; i++) Assert.Equal(ShutterState.Up, server.ShutterStateForTests(i));
        Assert.True(server.Nav.LineClear(40.5f, 17f, 40.5f, 20f));
    }
}

// Static placement and navigation of the chase props against FixedMap v2.1.
public sealed class ChasePropsMapTests
{
    private static readonly FixedMap Map = new();

    [Fact]
    public void ShuttersSpanAlleysWallToWallAndNoPropTouchesAWallObstacleOrKeyPoint()
    {
        foreach (var s in WorldChaseProps.Shutters)
        {
            float cx = s.CenterX, cz = s.CenterZ;
            // Both ends sit on a wall face.
            float ex = s.AlongX ? s.MinX - 0.01f : cx, ez = s.AlongX ? cz : s.MinZ - 0.01f;
            float fx = s.AlongX ? s.MaxX + 0.01f : cx, fz = s.AlongX ? cz : s.MaxZ + 0.01f;
            Assert.False(Map.CanStandStatic(ex, ez, 0.001f), $"shutter {s.Id} start end is open");
            Assert.False(Map.CanStandStatic(fx, fz, 0.001f), $"shutter {s.Id} far end is open");
            Assert.True(Map.CanStandStatic(cx, cz, 0.14f), $"shutter {s.Id} overlaps a wall");
        }
        foreach (var r in WorldChaseProps.Rails)
            Assert.True(Map.CanStandStatic(r.CenterX, r.CenterZ, 0.9f + r.HalfLength) || RailClear(r), $"rail {r.Id} touches a wall");
        foreach (var p in WorldChaseProps.Shutters.Concat(WorldChaseProps.Rails))
        {
            foreach (var t in Map.TerminalPositions) Assert.True(p.Distance(t.X, t.Z) >= 5f, $"prop {p.Id} near terminal");
            foreach (var e in Map.Exits.Concat(Map.ExitSwitches)) Assert.True(p.Distance(e.X, e.Z) >= 6f, $"prop {p.Id} near exit");
            foreach (var sp in Map.SpawnPoints) Assert.True(p.Distance(sp.X, sp.Z) >= 5f, $"prop {p.Id} near spawn");
            foreach (var h in Map.HatchPoints) Assert.True(p.Distance(h.X, h.Z) >= 3f, $"prop {p.Id} near hatch");
        }
    }

    private static bool RailClear(ChaseProp r)
    {
        // Every point within 1 m of the rail box is free of walls and obstacles.
        for (float x = r.MinX - 1; x <= r.MaxX + 1; x += 0.25f)
            for (float z = r.MinZ - 1; z <= r.MaxZ + 1; z += 0.25f)
                if (r.Distance(x, z) <= 1f && !Map.CanStandStatic(x, z, 0.001f)) return false;
        return true;
    }

    [Fact]
    public void WithEveryShutterDownTheWalkableMapStaysOneRegion()
    {
        var map = new FixedMap();
        var nav = new NavGrid(map);
        for (int i = 0; i < WorldChaseProps.ShutterCount; i++) nav.SetShutterDown(i, true);
        var path = new List<MapPoint>();
        foreach (var t in map.TerminalPositions.Concat(map.Exits).Concat(map.SpawnPoints))
            Assert.True(nav.FindPath(34, 43.5f, t.X, t.Z, path), $"no route to {t.X},{t.Z} with every shutter down");
    }

    [Fact]
    public void ADownShutterBlocksRoutesAndVaultLinksCrossRailsOnlyWhenAllowed()
    {
        var nav = new NavGrid(new FixedMap());
        var path = new List<MapPoint>();
        Assert.True(nav.FindPath(19.5f, 31f, 19.5f, 36f, path));
        float open = Length(path);
        nav.SetShutterDown(5, true);
        Assert.True(nav.FindPath(19.5f, 31f, 19.5f, 36f, path));
        Assert.True(Length(path) > open + 8, "a down shutter must force a detour");
        nav.SetShutterDown(5, false);
        // Across rail 5: phantoms (no links) walk around; a cheap vault link crosses at the vault point.
        Assert.True(nav.FindPath(35.4f, 26.5f, 37.6f, 26.5f, path));
        float around = Length(path);
        Assert.True(around > 3.5f);
        Assert.True(nav.FindPath(35.4f, 26.5f, 37.6f, 26.5f, path, 0.5f));
        Assert.True(Length(path) < around);
        Assert.True(nav.FindPath(35.4f, 26.5f, 37.6f, 26.5f, path, 50f));
        Assert.Equal(around, Length(path), 2);
    }

    private static float Length(List<MapPoint> path)
    {
        float total = 0;
        for (int i = 1; i < path.Count; i++)
            total += MathF.Sqrt((path[i].X - path[i - 1].X) * (path[i].X - path[i - 1].X) + (path[i].Z - path[i - 1].Z) * (path[i].Z - path[i - 1].Z));
        return total;
    }
}

// GDD v2 section 8 bot layer (ChaseTactics) on hand-built role views.
public sealed class ChaseTacticsTests
{
    [Fact]
    public void AChasedSurvivorRunsPastAnUpShutterAndPullsItWhenTheKillerIsCloseBehind()
    {
        var tactics = new ChaseTactics();
        var view = new SurvivorBotView { Status = PlayerStatus.Normal, X = 19.5f, Z = 31.5f, KillerVisible = true, KillerX = 19.5f, KillerZ = 27f };
        var intent = new BotIntent();
        tactics.Survivor(view, null, ref intent);
        Assert.True(intent.DirZ > 0.9f); // through the gap, away from the killer
        Assert.False(intent.Interact);
        view.Z = 34.6f; // past shutter 5, killer 3 m behind the box: wait
        view.KillerZ = 30.3f;
        view.Time = 0.2;
        intent = new BotIntent();
        tactics.Survivor(view, null, ref intent);
        Assert.False(intent.Interact);
        Assert.Equal(0f, intent.DirZ);
        view.KillerZ = 32f; // close behind: pull
        view.Time = 0.4;
        intent = new BotIntent();
        tactics.Survivor(view, null, ref intent);
        Assert.True(intent.Interact);
    }

    [Fact]
    public void AnUnchasedSurvivorIgnoresShutters()
    {
        var tactics = new ChaseTactics();
        var view = new SurvivorBotView { Status = PlayerStatus.Normal, X = 19.5f, Z = 31.5f, KillerVisible = false };
        var intent = new BotIntent { DirX = 1 };
        tactics.Survivor(view, null, ref intent);
        Assert.Equal(1f, intent.DirX);
        Assert.False(intent.Interact);
    }

    [Fact]
    public void TheKillerBreaksADownShutterOnlyWhenGoingAroundIsMuchLonger()
    {
        var nav = new NavGrid(new FixedMap());
        nav.SetShutterDown(5, true);
        var tactics = new ChaseTactics();
        var view = new KillerBotView { X = 19.5f, Z = 32.5f, Time = 1 };
        view.Shutters[5] = (byte)ShutterState.Down;
        view.Blips.Add(new KillerBotView.Blip { X = 19.5f, Z = 36f });
        var intent = new BotIntent { DirX = 1 };
        tactics.Killer(view, nav, ref intent);
        Assert.True(intent.Interact); // the alley detour is far longer than 8 m
        Assert.Equal(0f, intent.DirX);

        // Shutter 3 (x 39-42 at z 18.5) with a survivor just across: going around the block is long too, but a target
        // reachable by a short detour is chased around instead.
        var near = new ChaseTactics();
        var open = new NavGrid(new FixedMap());
        var other = new KillerBotView { X = 40.5f, Z = 17.5f, Time = 1 };
        other.Shutters[3] = (byte)ShutterState.Down;
        open.SetShutterDown(3, true);
        other.Blips.Add(new KillerBotView.Blip { X = 40.5f, Z = 19.6f });
        var keep = new BotIntent { DirX = 1 };
        near.Killer(other, open, ref keep);
        var path = new List<MapPoint>();
        Assert.True(open.FindPath(40.5f, 17.5f, 40.5f, 19.6f, path));
        float around = 0;
        for (int i = 1; i < path.Count; i++) around += MathF.Sqrt(MathF.Pow(path[i].X - path[i - 1].X, 2) + MathF.Pow(path[i].Z - path[i - 1].Z, 2));
        Assert.Equal(around - 2.1f > ChaseTactics.BreakDetourMeters, keep.Interact);
    }
}

// The bots actually use the props in all-bot 1v7 matches (logs: one line per pull, vault, drop, stun and break).
public sealed class ChasePropsBotUsageTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public ChasePropsBotUsageTests(Xunit.Abstractions.ITestOutputHelper output) { _output = output; }

    [Fact]
    public void AllBotMatchesPullVaultAndStun()
    {
        int pulls = 0, downs = 0, stuns = 0, railVaults = 0, shutterVaults = 0, breaks = 0, matches = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = seed, BotCount = 8, AutoStart = true });
            bool ended = false;
            server.Log += line =>
            {
                if (line.Contains(" pull by ")) pulls++;
                if (line.StartsWith("shutter ") && line.Contains(" down by ")) downs++;
                if (line.Contains("stun=killer")) stuns++;
                if (line.StartsWith("rail ") && line.Contains(" vault by ")) railVaults++;
                if (line.StartsWith("shutter ") && line.Contains(" vault by ")) shutterVaults++;
                if (line.Contains(" broken by ")) breaks++;
                if (line.StartsWith("match end")) ended = true;
            };
            for (int t = 0; t < 900 * GameConstants.TickRate + 40 && !ended; t++) server.Tick(1f / GameConstants.TickRate);
            Assert.True(ended);
            matches++;
        }
        _output.WriteLine($"{matches} matches: pulls {pulls}, downs {downs}, stuns {stuns}, rail vaults {railVaults}, shutter vaults {shutterVaults}, breaks {breaks}");
        Assert.True(downs > 0 && railVaults > 0, $"pulls {pulls}, downs {downs}, rail vaults {railVaults}");
        Assert.True(downs <= pulls);
    }
}
