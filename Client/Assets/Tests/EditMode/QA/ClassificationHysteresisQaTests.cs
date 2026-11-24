using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 1, test item 4. HUD_SPEC 6.2: "The classification must hold for 100 ms before it switches, which prevents
    // flicker at wall edges." Written from that sentence; time is the `now` passed to KillerScene.Frame.
    [Category("U4cQaA1")]
    public sealed class ClassificationHysteresisQaTests
    {
        // Scenes are written in the legacy 60 m numbers (Killer x 10, wall x 18..20) and laid on the 80 m map by KillerRoom.
        private static ViewPose Pose(float eyeX, float yaw) => new ViewPose(new Vector3(KillerRoom.MapX(eyeX), KillerViewRig.EyeHeight, KillerRoom.Z), yaw, 0, KillerViewRig.VerticalFov, 16f / 9);

        private static KillerScene SceneWith(float x, float z)
        {
            var scene = new KillerScene(new System.Random(5), null);
            var state = new S2C_WorldState { Tick = 1 };
            state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = KillerRoom.X, Z = KillerRoom.Z });
            state.Players.Add(new PlayerState { Role = (byte)Role.Survivor, X = KillerRoom.MapX(x), Z = KillerRoom.MapZ(z) });
            scene.Accept(state, 0);
            return scene;
        }

        // A Survivor at (15, 21): in view when looking +x (yaw 90), off-screen when looking -x (yaw 270).
        private static readonly ViewPose Sees = Pose(10, 90), Away = Pose(10, 270);

        private static BlipClass ClassAt(KillerScene scene, ViewPose pose, double now)
        {
            IReadOnlyList<BlipView> views = scene.Frame(pose, now, null);
            Assert.That(views.Count, Is.EqualTo(1));
            return views[0].Class;
        }

        [Test]
        public void ANewInstanceTakesItsClassAtOnce()
        {
            Assert.That(ClassAt(SceneWith(15, 21), Sees, .05), Is.EqualTo(BlipClass.InView));
            Assert.That(ClassAt(SceneWith(15, 21), Away, .05), Is.EqualTo(BlipClass.OffScreen));
        }

        [Test]
        public void AClassThatFlickersFasterThanOneHundredMillisecondsKeepsItsHeldClassForever()
        {
            KillerScene scene = SceneWith(15, 21);
            Assert.That(ClassAt(scene, Sees, .05), Is.EqualTo(BlipClass.InView));
            double t = .05;
            // 2 s of flicker: 30 ms in view, then 60 ms away (two frames), repeated. The away runs never reach 100 ms.
            for (int cycle = 0; cycle < 20; cycle++)
            {
                t += .03; Assert.That(ClassAt(scene, Away, t), Is.EqualTo(BlipClass.InView), "away 30 ms, cycle " + cycle);
                t += .03; Assert.That(ClassAt(scene, Away, t), Is.EqualTo(BlipClass.InView), "away 60 ms, cycle " + cycle);
                t += .03; Assert.That(ClassAt(scene, Sees, t), Is.EqualTo(BlipClass.InView), "back in view, cycle " + cycle);
            }
        }

        [Test]
        public void AChangeThatLastsOneHundredMillisecondsSwitchesAndNotBefore()
        {
            KillerScene scene = SceneWith(15, 21);
            Assert.That(ClassAt(scene, Sees, .05), Is.EqualTo(BlipClass.InView));
            // The first frame that disagrees is at 0.07 s. Frames then arrive every 20 ms.
            Assert.That(ClassAt(scene, Away, .07), Is.EqualTo(BlipClass.InView), "0 ms");
            Assert.That(ClassAt(scene, Away, .09), Is.EqualTo(BlipClass.InView), "20 ms");
            Assert.That(ClassAt(scene, Away, .11), Is.EqualTo(BlipClass.InView), "40 ms");
            Assert.That(ClassAt(scene, Away, .13), Is.EqualTo(BlipClass.InView), "60 ms");
            Assert.That(ClassAt(scene, Away, .15), Is.EqualTo(BlipClass.InView), "80 ms");
            Assert.That(ClassAt(scene, Away, .17), Is.EqualTo(BlipClass.OffScreen), "100 ms: the change has held, so it shows");
            Assert.That(ClassAt(scene, Away, .19), Is.EqualTo(BlipClass.OffScreen), "and stays");
        }

        [Test]
        public void AChangeObservedOnlyAfterALongGapSwitchesOnTheFrameThatConfirmsIt()
        {
            KillerScene scene = SceneWith(15, 21);
            Assert.That(ClassAt(scene, Sees, .05), Is.EqualTo(BlipClass.InView));
            Assert.That(ClassAt(scene, Away, .06), Is.EqualTo(BlipClass.InView), "first disagreeing frame");
            Assert.That(ClassAt(scene, Away, .50), Is.EqualTo(BlipClass.OffScreen), "a frame 440 ms later confirms a change that held for more than 100 ms");
        }

        [Test]
        public void TheSwitchBackIsHeldForOneHundredMillisecondsToo()
        {
            KillerScene scene = SceneWith(15, 21);
            ClassAt(scene, Away, .05);
            Assert.That(ClassAt(scene, Sees, .06), Is.EqualTo(BlipClass.OffScreen));
            Assert.That(ClassAt(scene, Sees, .10), Is.EqualTo(BlipClass.OffScreen), "40 ms");
            Assert.That(ClassAt(scene, Away, .12), Is.EqualTo(BlipClass.OffScreen), "a flicker back resets the clock");
            Assert.That(ClassAt(scene, Sees, .14), Is.EqualTo(BlipClass.OffScreen), "the clock restarted at 0.14");
            Assert.That(ClassAt(scene, Sees, .24), Is.EqualTo(BlipClass.InView), "100 ms later");
        }

        [Test]
        public void TheWallRayFlagUsedByTheSoundModelHasTheSameHundredMillisecondHold()
        {
            // The Survivor at (26, 22) is behind the wall x 18..20 when the Killer stands at x = 10, and clear when it stands at x = 22
            // (legacy numbers: on the 80 m map the wall is the building Walls[16] and these are 37 / 16 / 33).
            KillerScene scene = SceneWith(26, 22);
            ViewPose blocked = Pose(10, 90), clear = Pose(22, 90);
            IReadOnlyList<BlipView> first = scene.Frame(blocked, .05, null);
            Assert.That(first[0].Class, Is.EqualTo(BlipClass.Occluded));
            Assert.That(first[0].Occluded, Is.True);
            int handle = first[0].Handle;
            double t = .05;
            for (int i = 0; i < 20; i++)
            {
                t += .03;
                BlipView v = scene.Frame(i % 3 == 2 ? blocked : clear, t, null)[0];
                Assert.That(v.Occluded, Is.True, "a 30-60 ms gap in the wall ray does not flip the sound occlusion, frame " + i);
                Assert.That(v.Class, Is.EqualTo(BlipClass.Occluded), "nor the class, frame " + i);
                Assert.That(scene.IsOccluded(handle), Is.True);
            }
            // Now stay clear for 120 ms.
            BlipView settled = default;
            for (int i = 0; i < 5; i++) { t += .03; settled = scene.Frame(clear, t, null)[0]; }
            Assert.That(settled.Occluded, Is.False, "clear for more than 100 ms");
            Assert.That(settled.Class, Is.EqualTo(BlipClass.InView));
            Assert.That(scene.IsOccluded(handle), Is.False);
        }

        // ---- the pure holder ------------------------------------------------------------------------------------------------

        [Test]
        public void ThreeWayFlickerWithEveryRunUnderOneHundredMillisecondsNeverLeavesTheHeldValue()
        {
            var held = new HeldClass();
            held.BeginFrame(); Assert.That(held.Update(1, 0, 0), Is.EqualTo(0)); held.EndFrame();
            double t = 0;
            for (int i = 0; i < 40; i++)
            {
                t += .06;
                held.BeginFrame(); Assert.That(held.Update(1, (byte)(i % 2 == 0 ? 1 : 2), t), Is.EqualTo(0), "frame " + i); held.EndFrame();
            }
        }

        [Test]
        public void AKeyThatMissesAFrameIsForgottenAndComesBackWithItsRawValue()
        {
            var held = new HeldClass();
            held.BeginFrame(); held.Update(7, 0, 0); held.EndFrame();
            held.BeginFrame(); held.EndFrame(); // the instance despawned
            Assert.That(held.Count, Is.EqualTo(0));
            held.BeginFrame(); Assert.That(held.Update(7, 2, .5), Is.EqualTo(2), "a new instance has no previous class to hold"); held.EndFrame();
        }

        [Test]
        public void TwoKeysAreHeldIndependently()
        {
            var held = new HeldClass();
            held.BeginFrame(); held.Update(1, 0, 0); held.Update(2, 0, 0); held.EndFrame();
            held.BeginFrame(); held.Update(1, 1, .05); held.Update(2, 0, .05); held.EndFrame();
            held.BeginFrame(); held.Update(1, 1, .15); held.Update(2, 1, .15); held.EndFrame();
            held.BeginFrame();
            Assert.That(held.Update(1, 1, .16), Is.EqualTo(1), "key 1 switched at its 100 ms mark and stays");
            Assert.That(held.Update(2, 1, .16), Is.EqualTo(0), "key 2 only started disagreeing at 0.15");
            held.EndFrame();
        }
    }
}
