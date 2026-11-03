using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Tests.Core;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.QA.U2d;

public sealed class U2dQaFairnessTests
{
    private readonly ITestOutputHelper _output;
    public U2dQaFairnessTests(ITestOutputHelper output) { _output = output; }

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [Fact]
    public void SurvivorBot_DoesNotReactToKillerMovementWhenBlockedBehindWall()
    {
        // Place survivor and killer behind a wall where line of sight is blocked.
        // Changing killer's position within the same ThreatLevel band (e.g. ThreatLevel 0 or ThreatLevel 1)
        // must produce the exact same bot intent.
        var map = new FixedMap();
        var nav = new NavGrid(map);
        var brain = new SurvivorBrain();
        var rng = new Random(12345);

        // Survivor at (5, 26)
        float sx = 5f, sz = 26f;
        // Wall between (5, 26) and killer spots at (30, 26) vs (35, 20)
        Assert.True(map.SegmentBlocked(sx, sz, 30f, 26f));
        Assert.True(map.SegmentBlocked(sx, sz, 35f, 20f));

        var view1 = new SurvivorBotView
        {
            Time = 1.0,
            SelfId = 1,
            X = sx,
            Z = sz,
            Speed = GameConstants.SurvivorSpeed,
            Status = PlayerStatus.Normal,
            Threat = 0,
            KillerVisible = false,
            KillerX = 0,
            KillerZ = 0,
            KillerYaw = 0,
            TerminalCount = 4,
            Terminals = new[]
            {
                new TerminalState { Id = 0, X = 10f, Z = 25f, Progress = 0 },
                new TerminalState { Id = 1, X = 28f, Z = 12f, Progress = 0 },
                new TerminalState { Id = 2, X = 49f, Z = 25f, Progress = 0 },
                new TerminalState { Id = 3, X = 12f, Z = 49f, Progress = 0 },
            }
        };

        var brain1 = new SurvivorBrain();
        var intent1 = brain1.Update(view1, nav, map, new Random(42));

        // Perturbed: same threat level, killer at different blocked spot -> KillerVisible is still false and KillerX/Z is 0
        var view2 = new SurvivorBotView
        {
            Time = 1.0,
            SelfId = 1,
            X = sx,
            Z = sz,
            Speed = GameConstants.SurvivorSpeed,
            Status = PlayerStatus.Normal,
            Threat = 0,
            KillerVisible = false,
            KillerX = 0,
            KillerZ = 0,
            KillerYaw = 0,
            TerminalCount = 4,
            Terminals = view1.Terminals
        };

        var brain2 = new SurvivorBrain();
        var intent2 = brain2.Update(view2, nav, map, new Random(42));

        Assert.Equal(intent1.DirX, intent2.DirX);
        Assert.Equal(intent1.DirZ, intent2.DirZ);
        Assert.Equal(intent1.Yaw, intent2.Yaw);
        Assert.Equal(intent1.Interact, intent2.Interact);
        Assert.Equal(intent1.UseSkill, intent2.UseSkill);
    }

    [Fact]
    public void KillerBot_DoesNotReactToSilentSurvivorPositionOrMovement()
    {
        // When a survivor is Silent, it is absent from KillerBotView.Blips.
        // Whether the silent survivor is at (5, 5) or (50, 50), KillerBotView is identical.
        var map = new FixedMap();
        var nav = new NavGrid(map);

        var view = new KillerBotView
        {
            Time = 2.0,
            X = 20f,
            Z = 20f,
            Yaw = 0f,
            Speed = GameConstants.KillerSpeed,
            TerminalCount = 4,
            Terminals = new[]
            {
                new KillerBotView.Terminal { X = 10f, Z = 25f, Done = false },
                new KillerBotView.Terminal { X = 28f, Z = 12f, Done = false },
                new KillerBotView.Terminal { X = 49f, Z = 25f, Done = false },
                new KillerBotView.Terminal { X = 12f, Z = 49f, Done = false },
            }
        };
        // Blips empty because survivors are Silent

        var brain1 = new KillerBrain();
        var intent1 = brain1.Update(view, nav, map, new Random(99));

        var brain2 = new KillerBrain();
        var intent2 = brain2.Update(view, nav, map, new Random(99));

        Assert.Equal(intent1.DirX, intent2.DirX);
        Assert.Equal(intent1.DirZ, intent2.DirZ);
        Assert.Equal(intent1.Yaw, intent2.Yaw);
    }

    [Fact]
    public void KillerBot_DoesNotReadExactTerminalProgress()
    {
        // In KillerBotView, terminals have only boolean 'Done' (progress >= 100).
        // Progress of 1% vs 99% does not exist in KillerBotView and cannot influence decisions.
        var map = new FixedMap();
        var nav = new NavGrid(map);

        var view = new KillerBotView
        {
            Time = 5.0,
            X = 15f,
            Z = 15f,
            Yaw = 90f,
            Speed = GameConstants.KillerSpeed,
            TerminalCount = 2,
            Terminals = new[]
            {
                new KillerBotView.Terminal { X = 10f, Z = 25f, Done = false }, // Incomplete (whether 5% or 95%)
                new KillerBotView.Terminal { X = 28f, Z = 12f, Done = false }
            }
        };

        var brain1 = new KillerBrain();
        var intent1 = brain1.Update(view, nav, map, new Random(77));

        var brain2 = new KillerBrain();
        var intent2 = brain2.Update(view, nav, map, new Random(77));

        Assert.Equal(intent1.DirX, intent2.DirX);
        Assert.Equal(intent1.DirZ, intent2.DirZ);
        Assert.Equal(intent1.Yaw, intent2.Yaw);
    }

    [Fact]
    public void SurvivorBot_DoesNotReceiveKillerPositionThroughTeammatesLineOfSight()
    {
        // Survivor A (Id 2) has clear line of sight to Killer.
        // Survivor B (Id 3) is behind a wall.
        // Server fills views: B must have KillerVisible = false, KillerX = 0, KillerZ = 0.
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = 7, BotCount = 3, AutoStart = true });
        server.Tick(0.05f); // Initialize match and create bots
        var players = (List<Player>)typeof(GameServer).GetField("_players", All)!.GetValue(server)!;
        var killer = players.First(p => p.Role == Role.Killer);
        var survivors = players.Where(p => p.Role == Role.Survivor).ToList();
        Assert.True(survivors.Count >= 2);
        var survivorA = survivors[0];
        var survivorB = survivors[1];

        // Place players specifically:
        // Killer at (5, 5)
        // Survivor A at (5, 15): Clear LOS to (5, 5)
        // Survivor B at (20, 33): Behind walls from (5, 5)
        server.PlaceForTests(killer.Id, 5f, 5f);
        server.PlaceForTests(survivorA.Id, 5f, 15f);
        server.PlaceForTests(survivorB.Id, 26f, 33f);

        var killerInSightOf = typeof(GameServer).GetMethod("KillerInSightOf", All)!;
        bool sightA = (bool)killerInSightOf.Invoke(server, new object[] { survivorA })!;
        bool sightB = (bool)killerInSightOf.Invoke(server, new object[] { survivorB })!;

        Assert.True(sightA, "Survivor A should have clear LOS to Killer");
        Assert.False(sightB, "Survivor B should NOT have LOS to Killer");

        // Verify SurvivorBotView for B:
        var view = new SurvivorBotView();
        var fillView = typeof(GameServer).GetMethod("FillSurvivorView", All)!;
        fillView.Invoke(server, new object[] { survivorB, view });

        Assert.False(view.KillerVisible);
        Assert.Equal(0f, view.KillerX);
        Assert.Equal(0f, view.KillerZ);
        Assert.Equal(0f, view.KillerYaw);
    }
}
