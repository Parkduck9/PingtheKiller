using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 2, test item 3 (F-06). docs/briefs/U4c-a2.md fix 4: a blip must never draw nothing for a frame. When a
    // held In view or Occluded class has left the frustum, the blip is drawn as an edge chevron at once, while the 100 ms
    // hysteresis of its class keeps running (the class itself does not switch early).
    [Category("U4cQaA2")]
    public sealed class KillerFrustumExitQaTests
    {
        private const float KillerX = 10, KillerZ = 23;
        private static readonly List<(float x, float z)> Terminals = new List<(float x, float z)> { (10, 25), (28, 12) };

        // Killer at (10, 23). The wall x 18..20, z 14..32 of FixedMap stands east of it.
        private static ViewPose Look(float yaw) => new ViewPose(new Vector3(KillerX, KillerViewRig.EyeHeight, KillerZ), yaw, 0, KillerViewRig.VerticalFov, 16f / 9);

        private static S2C_WorldState State(uint tick, params (float x, float z)[] survivors)
        {
            var state = new S2C_WorldState { Tick = tick, TimeLeft = 500, TerminalsRequired = 3 };
            state.Terminals.Add(new TerminalState { Id = 0, X = 10, Z = 25, Progress = 0 });
            state.Terminals.Add(new TerminalState { Id = 1, X = 28, Z = 12, Progress = 0 });
            foreach (var s in survivors)
                state.Players.Add(new PlayerState { Role = (byte)Role.Survivor, Status = (byte)PlayerStatus.Normal, X = s.x, Z = s.z, Flags = (byte)PlayerFlags.Signal });
            state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, Status = (byte)PlayerStatus.Normal, X = KillerX, Z = KillerZ, Yaw = 90 });
            return state;
        }

        private static KillerScene Scene(params (float x, float z)[] survivors)
        {
            var scene = new KillerScene(new System.Random(5), Terminals);
            scene.Accept(State(1, survivors), 0);
            return scene;
        }

        [Test]
        public void AHeldInViewBlipThatLeavesTheFrustumGetsAChevronOnThatSameFrameAndKeepsItsClassForTheHold()
        {
            KillerScene scene = Scene((15, 23)); // 5 m ahead, in the open
            BlipView first = scene.Frame(Look(90), .0, null)[0];
            Assert.That(first.Class, Is.EqualTo(BlipClass.InView));
            Assert.That(first.OutOfFrustum, Is.False);
            Assert.That(first.Edge, Is.False, "in view: a body, no chevron");

            foreach (double t in new[] { .02, .06 })
            {
                BlipView turned = scene.Frame(Look(270), t, null)[0]; // the view swings round, the blip is now behind the camera
                Assert.That(turned.OutOfFrustum, Is.True, "t=" + t);
                Assert.That(turned.Class, Is.EqualTo(BlipClass.InView), "the 100 ms hold keeps the class, t=" + t);
                Assert.That(turned.Edge, Is.True, "but it already has a chevron, t=" + t);
            }
            BlipView settled = scene.Frame(Look(270), .2, null)[0];
            Assert.That(settled.Class, Is.EqualTo(BlipClass.OffScreen), "after the hold the class itself switches");
            Assert.That(settled.Edge, Is.True);
        }

        [Test]
        public void AHeldOccludedBlipThatLeavesTheFrustumGetsAChevronOnThatSameFrame()
        {
            KillerScene scene = Scene((26, 23)); // behind the wall
            BlipView first = scene.Frame(Look(90), .0, null)[0];
            Assert.That(first.Class, Is.EqualTo(BlipClass.Occluded));
            Assert.That(first.Edge, Is.False, "occluded and on screen: a diamond, no chevron");

            BlipView turned = scene.Frame(Look(270), .03, null)[0];
            Assert.That(turned.OutOfFrustum, Is.True);
            Assert.That(turned.Class, Is.EqualTo(BlipClass.Occluded), "held");
            Assert.That(turned.Edge, Is.True, "chevron at once instead of nothing");
        }

        [Test]
        public void ABlipThatComesBackIntoViewLosesItsChevronAtOnceWhileItsClassIsStillHeld()
        {
            KillerScene scene = Scene((15, 23));
            scene.Frame(Look(270), .0, null); // starts behind the camera: OffScreen
            BlipView back = scene.Frame(Look(90), .03, null)[0];
            Assert.That(back.OutOfFrustum, Is.False);
            Assert.That(back.Class, Is.EqualTo(BlipClass.OffScreen), "still held");
            Assert.That(back.Edge, Is.True, "the held OffScreen class keeps its chevron: nothing is lost, nothing is blank");
        }

        [Test]
        public void EveryInstanceIsDrawnAsABodyADiamondOrAChevronOnEveryFrameOfAFullTurn()
        {
            // Three Survivors: in the open east, behind the wall, and off to the south. A full turn at 20 ms frames (7 deg each).
            KillerScene scene = Scene((15, 23), (26, 23), (10, 12));
            double now = 0;
            for (float yaw = 0; yaw <= 720; yaw += 7)
            {
                now += .02;
                IReadOnlyList<BlipView> views = scene.Frame(Look(yaw), now, null);
                Assert.That(views.Count, Is.EqualTo(3));
                foreach (BlipView view in views)
                {
                    bool body = view.Class == BlipClass.InView;
                    bool diamond = view.Class == BlipClass.Occluded && !view.OutOfFrustum;
                    Assert.That(body || diamond || view.Edge, Is.True,
                        "yaw " + yaw + " handle " + view.Handle + ": class " + view.Class + " outOfFrustum " + view.OutOfFrustum + " edge " + view.Edge + " would draw nothing");
                    if (view.OutOfFrustum) Assert.That(view.Edge, Is.True, "yaw " + yaw + ": a marker that left the frustum always has its chevron");
                    if (view.Class == BlipClass.OffScreen) Assert.That(view.Edge, Is.True, "yaw " + yaw);
                }
            }
        }

        [Test]
        public void AtMostSixChevronsAreDrawnEvenWhenMoreInstancesLeaveTheFrustum()
        {
            var survivors = new List<(float x, float z)>();
            for (int i = 0; i < 7; i++) survivors.Add((12 + i * 2, 23));
            KillerScene scene = Scene(survivors.ToArray());
            scene.Frame(Look(90), 0, null);
            IReadOnlyList<BlipView> views = scene.Frame(Look(270), .03, null);
            int edges = 0;
            foreach (BlipView view in views) if (view.Edge) edges++;
            Assert.That(edges, Is.EqualTo(6), "HUD_SPEC 6.2: only the 6 nearest get an edge indicator");
        }
    }
}
