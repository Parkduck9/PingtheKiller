using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // The Killer pipeline (KillerScene = the stream adapter around BlipTracker + classification + edge selection) with
    // anonymous WorldStates: Survivor entries with Id = 0 in shuffled order must track and render exactly like the same
    // states with real Ids in a fixed order (HUD_SPEC 6.3, U2c section 1).
    public sealed class KillerSceneTests
    {
        // Killer at (10, 23) looking +x. Wall x 18..20, z 14..32 sits between it and x = 25.
        private static readonly ViewPose Pose = new ViewPose(new Vector3(10, KillerViewRig.EyeHeight, 23), 90, 0, KillerViewRig.VerticalFov, 16f / 9);

        private static S2C_WorldState State(uint tick, IList<PlayerState> survivors, bool anonymous, System.Random shuffle)
        {
            var state = new S2C_WorldState { Tick = tick, TimeLeft = 500, TerminalsRequired = 3 };
            state.Terminals.Add(new TerminalState { Id = 0, X = 10, Z = 25 });
            var entries = new List<PlayerState>();
            foreach (PlayerState s in survivors)
            {
                PlayerState e = s;
                if (anonymous) { e.Id = 0; e.Yaw = 0; e.StatusTimer = 0; e.CaptureCount = 0; e.Flags = (byte)PlayerFlags.Signal; }
                entries.Add(e);
            }
            if (anonymous)
                for (int i = entries.Count - 1; i > 0; i--) { int j = shuffle.Next(i + 1); PlayerState t = entries[i]; entries[i] = entries[j]; entries[j] = t; }
            state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 10, Z = 23 });
            // The Killer's own entry is found by Role, not position in the list.
            if (anonymous) state.Players.Insert(0, entries[0]); else state.Players.Add(entries[0]);
            for (int i = 1; i < entries.Count; i++) state.Players.Add(entries[i]);
            return state;
        }

        private static List<PlayerState> Survivors(uint tick)
        {
            float step = .1f * tick; // 2 m/s
            return new List<PlayerState>
            {
                new PlayerState { Id = 2, Role = (byte)Role.Survivor, X = 15, Z = 20 + step, Flags = (byte)(PlayerFlags.Signal | PlayerFlags.Hacking), Yaw = 33 },
                new PlayerState { Id = 3, Role = (byte)Role.Survivor, X = 25, Z = 21 + step, Flags = (byte)PlayerFlags.Signal, CaptureCount = 1 },
                new PlayerState { Id = 4, Role = (byte)Role.Survivor, X = 4, Z = 22 + step, Status = (byte)PlayerStatus.Lagging, StatusTimer = 40, Flags = (byte)PlayerFlags.Signal },
            };
        }

        [Test]
        public void AnonymousShuffledStatesTrackAndRenderExactlyLikeIdentifiedOnes()
        {
            var named = new KillerScene(new System.Random(1), null);
            var anonymous = new KillerScene(new System.Random(2), null);
            var shuffle = new System.Random(3);
            HashSet<int> handles = null;
            for (uint tick = 1; tick <= 40; tick++)
            {
                double now = tick * .05;
                named.Accept(State(tick, Survivors(tick), false, null), now);
                anonymous.Accept(State(tick, Survivors(tick), true, shuffle), now);
                IReadOnlyList<BlipView> a = named.Frame(Pose, now + .05, null), b = anonymous.Frame(Pose, now + .05, null);
                Assert.That(b.Count, Is.EqualTo(3), "tick " + tick);
                foreach (BlipClass c in new[] { BlipClass.InView, BlipClass.Occluded, BlipClass.OffScreen })
                {
                    Assert.That(anonymous.Count(c), Is.EqualTo(named.Count(c)), c + " at tick " + tick);
                    Assert.That(anonymous.Count(c), Is.EqualTo(1), c + " at tick " + tick);
                }
                var current = new HashSet<int>();
                foreach (BlipView view in b) current.Add(view.Handle);
                if (handles == null) handles = current;
                else Assert.That(current.SetEquals(handles), Is.True, "handles are stable at tick " + tick);
                if (tick > 1)
                    foreach (BlipEvent e in anonymous.Events)
                        Assert.That(e.Kind, Is.Not.EqualTo(BlipEventKind.Spawned).And.Not.EqualTo(BlipEventKind.Despawned), "tick " + tick);
            }
            Assert.That(anonymous.KillerX, Is.EqualTo(10f));
            Assert.That(anonymous.KillerZ, Is.EqualTo(23f));
        }

        [Test]
        public void TheLaggingInstanceKeepsItsStatusAndOnlyNormalOrLaggingEntriesAreTracked()
        {
            var scene = new KillerScene(new System.Random(5), null);
            List<PlayerState> survivors = Survivors(1);
            survivors.Add(new PlayerState { Id = 5, Role = (byte)Role.Survivor, X = 30, Z = 30, Status = (byte)PlayerStatus.Escaped });
            survivors.Add(new PlayerState { Id = 6, Role = (byte)Role.Survivor, X = 31, Z = 30, Status = (byte)PlayerStatus.Eliminated });
            scene.Accept(State(1, survivors, true, new System.Random(1)), 0);
            IReadOnlyList<BlipView> views = scene.Frame(Pose, .05, null);
            Assert.That(views.Count, Is.EqualTo(3));
            int lagging = 0;
            foreach (BlipView view in views) if (view.Status == BlipStatus.Lagging) { lagging++; Assert.That(view.Class, Is.EqualTo(BlipClass.OffScreen)); }
            Assert.That(lagging, Is.EqualTo(1));
            Assert.That(scene.NearestLagging(out float distance), Is.GreaterThan(0));
            Assert.That(distance, Is.EqualTo(new Vector2(6, .9f).magnitude).Within(1e-3f));
        }

        [Test]
        public void InstancesTweenFromThePreviousToTheCurrentSnapshotOverOneTick()
        {
            var scene = new KillerScene(new System.Random(7), null);
            var one = new List<PlayerState> { new PlayerState { Role = (byte)Role.Survivor, X = 15, Z = 20 } };
            var two = new List<PlayerState> { new PlayerState { Role = (byte)Role.Survivor, X = 15, Z = 20.2f } };
            scene.Accept(State(1, one, true, new System.Random(1)), 0);
            scene.Accept(State(2, two, true, new System.Random(1)), 1);
            Assert.That(scene.Frame(Pose, 1, null)[0].Position.z, Is.EqualTo(20f).Within(1e-4f), "at receipt: the previous position");
            Assert.That(scene.Frame(Pose, 1.025, null)[0].Position.z, Is.EqualTo(20.1f).Within(1e-3f), "half a tick");
            Assert.That(scene.Frame(Pose, 1.2, null)[0].Position.z, Is.EqualTo(20.2f).Within(1e-4f), "then it holds");
        }

        [Test]
        public void AClassChangeMustHoldOneHundredMillisecondsBeforeItShows()
        {
            var scene = new KillerScene(new System.Random(9), null);
            var visible = new List<PlayerState> { new PlayerState { Role = (byte)Role.Survivor, X = 15, Z = 20 } };
            scene.Accept(State(1, visible, true, new System.Random(1)), 0);
            Assert.That(scene.Frame(Pose, .05, null)[0].Class, Is.EqualTo(BlipClass.InView));
            var turned = new ViewPose(Pose.Eye, 270, 0, KillerViewRig.VerticalFov, 16f / 9);
            Assert.That(scene.Frame(turned, .06, null)[0].Class, Is.EqualTo(BlipClass.InView), "not yet");
            Assert.That(scene.Frame(turned, .12, null)[0].Class, Is.EqualTo(BlipClass.InView), "70 ms");
            Assert.That(scene.Frame(turned, .161, null)[0].Class, Is.EqualTo(BlipClass.OffScreen), "100 ms");
        }

        // F-06: while the class is still held, an instance whose marker point has left the frustum is an edge indicator at once.
        [Test]
        public void AHeldClassThatLeftTheFrustumIsDrawnAsAnEdgeIndicatorAtOnce()
        {
            var scene = new KillerScene(new System.Random(9), null);
            var hidden = new List<PlayerState> { new PlayerState { Role = (byte)Role.Survivor, X = 25, Z = 21 } }; // behind the wall
            scene.Accept(State(1, hidden, true, new System.Random(1)), 0);
            BlipView first = scene.Frame(Pose, .05, null)[0];
            Assert.That(first.Class, Is.EqualTo(BlipClass.Occluded));
            Assert.That(first.OutOfFrustum, Is.False);
            Assert.That(first.Edge, Is.False);
            var turned = new ViewPose(Pose.Eye, 270, 0, KillerViewRig.VerticalFov, 16f / 9);
            BlipView held = scene.Frame(turned, .06, null)[0];
            Assert.That(held.Class, Is.EqualTo(BlipClass.Occluded), "the 100 ms hold keeps running");
            Assert.That(held.OutOfFrustum, Is.True);
            Assert.That(held.Edge, Is.True, "no frame without a marker");
            Assert.That(scene.Count(BlipClass.OffScreen), Is.EqualTo(1));
            BlipView settled = scene.Frame(turned, .161, null)[0];
            Assert.That(settled.Class, Is.EqualTo(BlipClass.OffScreen));
            Assert.That(settled.Edge, Is.True);
        }

        [Test]
        public void OnlyTheSixNearestOffScreenInstancesGetEdgeIndicatorsWithVoicedOnesFirst()
        {
            var scene = new KillerScene(new System.Random(11), null);
            var behind = new List<PlayerState>();
            for (int i = 0; i < 8; i++) behind.Add(new PlayerState { Role = (byte)Role.Survivor, X = 8 - i, Z = 23 + (i % 2 == 0 ? 3 : -3) });
            scene.Accept(State(1, behind, true, new System.Random(1)), 0);
            IReadOnlyList<BlipView> views = scene.Frame(Pose, .05, null);
            Assert.That(scene.Count(BlipClass.OffScreen), Is.EqualTo(6));
            float farthest = 0; int farthestHandle = -1; float maxShown = 0;
            foreach (BlipView view in views)
            {
                if (view.Distance > farthest) { farthest = view.Distance; farthestHandle = view.Handle; }
                if (view.Edge) maxShown = Mathf.Max(maxShown, view.Distance);
            }
            Assert.That(maxShown, Is.LessThan(farthest), "the farthest is left out");
            views = scene.Frame(Pose, .06, handle => handle == farthestHandle);
            bool shown = false;
            foreach (BlipView view in views) if (view.Handle == farthestHandle) shown = view.Edge;
            Assert.That(shown, Is.True, "a voiced instance is always drawn");
            Assert.That(scene.Count(BlipClass.OffScreen), Is.EqualTo(6));
        }

        [Test]
        public void TheSoundOcclusionUsesTheWallRayEvenOffScreen()
        {
            var scene = new KillerScene(new System.Random(13), null);
            var behindWall = new List<PlayerState> { new PlayerState { Role = (byte)Role.Survivor, X = 25, Z = 23 } };
            scene.Accept(State(1, behindWall, true, new System.Random(1)), 0);
            var away = new ViewPose(Pose.Eye, 270, 0, KillerViewRig.VerticalFov, 16f / 9);
            BlipView view = scene.Frame(away, .05, null)[0];
            Assert.That(view.Class, Is.EqualTo(BlipClass.OffScreen));
            Assert.That(view.Occluded, Is.True);
            Assert.That(scene.IsOccluded(view.Handle), Is.True);
        }
    }
}
