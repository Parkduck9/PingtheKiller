using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PingTheKiller.Client.Tests
{
    // U4j: the motion trigger tracker, the Killer-view filtering, the Killer model's attack, the first-person kick and
    // the Mixamo import / controller.
    public sealed class CharacterMotionTests
    {
        private const byte N = (byte)PlayerStatus.Normal, L = (byte)PlayerStatus.Lagging, E = (byte)PlayerStatus.Eliminated, X = (byte)PlayerStatus.Escaped;
        private const byte GettingUpFlag = (byte)PlayerFlags.GettingUp;
        private const byte Hacking = (byte)PlayerFlags.Hacking, Rescuing = (byte)PlayerFlags.Rescuing, Afterimage = (byte)PlayerFlags.Afterimage;
        private const byte Signal = (byte)PlayerFlags.Signal, Stealthed = (byte)PlayerFlags.Stealthed;
        private const double Hit = MotionTiming.HitSeconds, Down = MotionTiming.HitSeconds + MotionTiming.KnockedDownSeconds;

        private static MotionTracker Seen(MotionView view, byte status, byte flags = 0, byte count = 0)
        {
            var tracker = new MotionTracker(view);
            tracker.Update(0, status, flags, count, 0);
            return tracker;
        }

        // ---- trigger state machine (Survivor view) -------------------------------------------------------------------

        [Test]
        public void ACaptureHitsThenKnocksDownThenCrawlsOrHoldsWhileLagging()
        {
            MotionTracker t = Seen(MotionView.Survivor, N);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Locomotion));
            Assert.That(t.CaptureEdge, Is.False);
            t.Update(10, L, Signal, 1, 0);
            Assert.That(t.CaptureEdge, Is.True, "Normal -> Lagging is a capture");
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Hit));
            Assert.That(t.PoseSince, Is.EqualTo(10));
            t.Update(10 + Hit - .01, L, Signal, 1, 0);
            Assert.That(t.CaptureEdge, Is.False, "the capture edge lasts one update");
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Hit));
            t.Update(10 + Hit + .01, L, Signal, 1, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.KnockedDown));
            Assert.That(t.PoseSince, Is.EqualTo(10 + Hit).Within(1e-9), "KnockedDown starts where HitReaction blends out");
            t.Update(10 + Down + .01, L, Signal, 1, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Crawl), "held down: the crawl state (frozen at speed 0)");
            t.Update(20, L, Signal, 1, 1f);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Crawl), "moving while Lagging: crawl");
            t.Update(30, L, Hacking | Rescuing, 1, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Crawl), "no hacking or rescue clip while Lagging");
        }

        [Test]
        public void ARescueGetsUpThenReturnsToLocomotionWithoutASwipe()
        {
            MotionTracker t = Seen(MotionView.Survivor, L, 0, 1);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Crawl), "first seen Lagging: already down, no knock-down replay");
            Assert.That(double.IsNegativeInfinity(t.PoseSince), Is.True);
            t.Update(5, N, GettingUpFlag, 1, 0);
            Assert.That(t.CaptureEdge, Is.False);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.GettingUp));
            Assert.That(t.PoseSince, Is.EqualTo(5));
            t.Update(5 + MotionTiming.GetUpSeconds - .01, N, GettingUpFlag, 1, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.GettingUp), "1.5 s, from the server flag");
            t.Update(5 + MotionTiming.GetUpSeconds + .01, N, 0, 1, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Injured), "CaptureCount >= 1 in the Survivor view: injured locomotion");
            t.Update(9, N, 0, 0, 4);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Locomotion));
        }

        [Test]
        public void EliminationPlaysTheDeathOnceAndHoldsItUntilTheBodyDespawns()
        {
            MotionTracker t = Seen(MotionView.Survivor, L, 0, 1);
            t.Update(40, E, 0, 1, 0);
            Assert.That(t.CaptureEdge, Is.False, "Lagging -> Eliminated (execution or timeout) is not a capture");
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Death));
            Assert.That(t.PoseSince, Is.EqualTo(40));
            Assert.That(t.DeathShown(40 + 1), Is.True);
            Assert.That(t.DeathShown(40 + MotionTiming.DeathShownSeconds - .01), Is.True, "the last pose holds");
            t.Update(40 + MotionTiming.DeathSeconds + 1, E, 0, 1, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Death));
            Assert.That(t.PoseSince, Is.EqualTo(40), "the fall is not replayed");
            Assert.That(t.DeathShown(40 + MotionTiming.DeathShownSeconds + .01), Is.False, "then the body despawns");
            Assert.That(MotionTiming.DeathShownSeconds, Is.GreaterThan(MotionTiming.DeathSeconds + 1));
        }

        [Test]
        public void ASecondCaptureFromNormalIsACaptureAndPlaysTheDeath()
        {
            MotionTracker t = Seen(MotionView.Survivor, N, 0, 1);
            t.Update(3, E, 0, 1, 0);
            Assert.That(t.CaptureEdge, Is.True, "GDD v2 §4: a capture of an already-captured Survivor eliminates");
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Death));
        }

        [TestCase(L, N)]
        [TestCase(L, E)]
        [TestCase(N, X)]
        [TestCase(E, N)]
        public void NoSwipeOnAStatusChangeThatIsNotACapture(byte from, byte to)
        {
            foreach (MotionView view in new[] { MotionView.Survivor, MotionView.Killer })
            {
                MotionTracker t = Seen(view, from);
                t.Update(1, to, 0, 0, 0);
                Assert.That(t.CaptureEdge, Is.False, view + " " + from + " -> " + to);
                t.Update(2, to, 0, 0, 0);
                Assert.That(t.CaptureEdge, Is.False);
            }
        }

        [Test]
        public void NoSwipeWithoutAStatusChange()
        {
            MotionTracker t = Seen(MotionView.Survivor, N);
            for (int i = 1; i < 20; i++) { t.Update(i * .05, N, (byte)(i % 2 == 0 ? Hacking : 0), (byte)(i / 10), i % 3); Assert.That(t.CaptureEdge, Is.False); }
            MotionTracker first = new MotionTracker(MotionView.Survivor);
            first.Update(0, L, 0, 0, 0);
            Assert.That(first.CaptureEdge, Is.False, "first sight of a Lagging Survivor is not a capture");
        }

        [Test]
        public void HackingLoopsButtonPushingOnlyWhileStillInTheSurvivorView()
        {
            MotionTracker t = Seen(MotionView.Survivor, N);
            t.Update(1, N, Hacking | Signal, 0, .1f);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Working));
            t.Update(2, N, Hacking | Signal, 0, 3f);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Locomotion), "moving: locomotion even with the flag");
            t.Update(3, N, Hacking | Signal, 2, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Working), "hacking wins over the injured idle");
            t.Update(4, N, Signal, 0, 0);
            Assert.That(t.Pose, Is.EqualTo(MotionPose.Locomotion), "flag off: locomotion");
        }

        // ---- Killer view (HUD_SPEC 6.3) ------------------------------------------------------------------------------

        private static readonly MotionPose[] KillerAllowed = { MotionPose.Locomotion, MotionPose.Hit, MotionPose.KnockedDown, MotionPose.Crawl };

        [Test]
        public void HackingRescuingAndCapturedSurvivorsGetOnlyLocomotionCrawlOrKnockDownInTheKillerView()
        {
            byte[] flagSets = { Hacking, Rescuing, (byte)(Hacking | Signal), (byte)(Rescuing | Signal), Afterimage, Stealthed, 0xFF };
            foreach (byte flags in flagSets)
                for (byte count = 0; count <= 3; count++)
                    foreach (float speed in new[] { 0f, .1f, 1f, 4f })
                    {
                        MotionTracker t = Seen(MotionView.Killer, N, flags, count);
                        t.Update(1, N, flags, count, speed);
                        Assert.That(t.Pose, Is.EqualTo(MotionPose.Locomotion), "flags=" + flags + " count=" + count + " speed=" + speed);
                        t.Update(2, L, flags, count, speed);
                        Assert.That(KillerAllowed, Does.Contain(t.Pose));
                        t.Update(9, L, flags, count, speed);
                        Assert.That(t.Pose, Is.EqualTo(MotionPose.Crawl));
                        t.Update(10, N, flags, count, speed);
                        Assert.That(t.Pose, Is.EqualTo(MotionPose.Locomotion), "no getting-up clip in the Killer view (rescue)");
                    }
        }

        [Test]
        public void TheKillerViewKnocksDownATrackedBlipButNotASpawnedOne()
        {
            MotionTracker tracked = Seen(MotionView.Killer, N);
            tracked.Update(1, L, 0, 0, 0);
            Assert.That(tracked.Pose, Is.EqualTo(MotionPose.Hit));
            tracked.Update(1 + Hit + .01, L, 0, 0, 0);
            Assert.That(tracked.Pose, Is.EqualTo(MotionPose.KnockedDown));
            tracked.Update(1 + Down + .01, L, 0, 0, 1);
            Assert.That(tracked.Pose, Is.EqualTo(MotionPose.Crawl));
            MotionTracker spawned = Seen(MotionView.Killer, L);
            Assert.That(spawned.Pose, Is.EqualTo(MotionPose.Crawl), "an unmatched new entry spawns in place, no animation");
            // Eliminated / Escaped never reach the Killer; if they did, they would not play a death.
            MotionTracker odd = Seen(MotionView.Killer, N);
            odd.Update(1, E, 0, 0, 0);
            Assert.That(odd.Pose, Is.EqualTo(MotionPose.Locomotion));
        }

        [Test]
        public void DecoysAndAfterimagesAnimateExactlyLikeRealSurvivorsInTheKillerView()
        {
            var random = new System.Random(4);
            var real = new MotionTracker(MotionView.Killer);
            var decoy = new MotionTracker(MotionView.Killer);
            var afterimage = new MotionTracker(MotionView.Killer);
            byte status = N;
            for (int i = 0; i < 600; i++)
            {
                double now = i * .05;
                if (random.Next(40) == 0) status = status == N ? L : N;
                float speed = random.Next(3) == 0 ? 0 : (float)random.NextDouble() * 4;
                real.Update(now, status, (byte)random.Next(256), (byte)random.Next(3), speed);
                decoy.Update(now, status, Signal, 0, speed);
                afterimage.Update(now, status, Afterimage | Signal, 0, speed);
                Assert.That(real.Pose, Is.EqualTo(decoy.Pose), "step " + i);
                Assert.That(afterimage.Pose, Is.EqualTo(decoy.Pose), "step " + i);
                Assert.That(real.PoseSince, Is.EqualTo(decoy.PoseSince));
                Assert.That(real.CaptureEdge, Is.EqualTo(decoy.CaptureEdge));
                Assert.That(KillerAllowed, Does.Contain(real.Pose));
            }
        }

        // ---- the Killer model's attack and the first-person kick -----------------------------------------------------

        [Test]
        public void TheKillerModelSwipesOnlyWhileDrawnAndTheOverrideEndsWithin1_3Seconds()
        {
            var attack = new AttackTracker();
            Assert.That(attack.Trigger(1, false), Is.False, "not drawn: the Survivor sees nothing");
            Assert.That(attack.Active(1.1), Is.False);
            Assert.That(attack.Trigger(2, true), Is.True);
            Assert.That(attack.Active(2.5), Is.True);
            Assert.That(attack.Weight(2.5), Is.EqualTo(1));
            Assert.That(MotionTiming.SwipeSeconds, Is.InRange(.9f, 1.3f), "U8: the visible swing lasts 0.9-1.3 s");
            Assert.That(attack.Active(2 + MotionTiming.SwipeSeconds + .001), Is.False);
            Assert.That(attack.Weight(2 + MotionTiming.SwipeSeconds + .001), Is.Zero);
            attack.Trigger(10, true);
            Assert.That(attack.Count, Is.EqualTo(2));
            Assert.That(Array.Exists(MotionLibrary.Clips, x => x.File == "KillerPunch"), Is.False, "the swipe only (no punch)");
        }

        [Test]
        public void ReducedMotionDisablesTheKick()
        {
            bool saved = KillerAttackKick.ReducedMotion;
            var host = new GameObject("kick");
            try
            {
                var kick = host.AddComponent<KillerAttackKick>();
                KillerAttackKick.ReducedMotion = true;
                Assert.That(kick.Play(1), Is.False);
                Assert.That(kick.Played, Is.Zero);
                Assert.That(kick.Active(1.05), Is.False);
                for (double t = 0; t < .3; t += .01)
                {
                    KillerAttackKick.Offset(t, true, out float fov, out float push);
                    Assert.That(fov, Is.Zero); Assert.That(push, Is.Zero);
                }
                KillerAttackKick.ReducedMotion = false;
                Assert.That(kick.Play(1), Is.True);
                Assert.That(kick.Active(1.05), Is.True);
            }
            finally { KillerAttackKick.ReducedMotion = saved; Object.DestroyImmediate(host); }
        }

        [Test]
        public void TheKickIsShortAndReturnsToZero()
        {
            // U8: a wind-up (pull back, wider FOV), then the strike lunge and a small trauma shake, all within 0.4 s.
            double peak = KillerAttackKick.WindUpSeconds + KillerAttackKick.StrikePeak;
            Assert.That(KillerAttackKick.Seconds, Is.LessThanOrEqualTo(.4f));
            Assert.That(KillerAttackKick.WindUpSeconds, Is.InRange(.04f, .1f), "a short wind-up feel");
            Assert.That(KillerAttackKick.Envelope(-.01), Is.Zero);
            Assert.That(KillerAttackKick.Envelope(KillerAttackKick.WindUpSeconds * .5), Is.Zero, "no lunge during the wind-up");
            KillerAttackKick.Offset(KillerAttackKick.WindUpSeconds * .99, false, out float windFov, out float windPush);
            Assert.That(windFov, Is.GreaterThan(0), "wind-up widens the view");
            Assert.That(windPush, Is.LessThan(0), "wind-up pulls the eye back");
            Assert.That(KillerAttackKick.Envelope(peak), Is.EqualTo(1).Within(1e-4));
            Assert.That(KillerAttackKick.Envelope(KillerAttackKick.Seconds), Is.Zero);
            Assert.That(KillerAttackKick.TraumaAt(KillerAttackKick.Seconds), Is.Zero, "the shake ends with the kick");
            Assert.That(KillerAttackKick.TraumaAt(KillerAttackKick.WindUpSeconds), Is.EqualTo(KillerAttackKick.Trauma).Within(1e-4));
            for (double t = 0; t < .5; t += .01)
            {
                KillerAttackKick.Shake(t, false, out float right, out float up, out float roll);
                Assert.That(Mathf.Abs(right), Is.LessThanOrEqualTo(KillerAttackKick.ShakeMetres * .25f + 1e-6f), "small: trauma .5 squared");
                Assert.That(Mathf.Abs(roll), Is.LessThanOrEqualTo(KillerAttackKick.ShakeRoll * .25f + 1e-6f));
                KillerAttackKick.Shake(t, true, out right, out up, out roll);
                Assert.That(right == 0 && up == 0 && roll == 0, Is.True, "reduced motion: no shake");
            }
            float previous = 1;
            for (double t = peak; t < KillerAttackKick.Seconds; t += .005)
            {
                float e = KillerAttackKick.Envelope(t);
                Assert.That(e, Is.LessThanOrEqualTo(previous + 1e-6f));
                previous = e;
            }
            KillerAttackKick.Offset(peak, false, out float fov, out float push);
            Assert.That(fov, Is.EqualTo(-KillerAttackKick.FovDegrees).Within(1e-4));
            Assert.That(push, Is.EqualTo(KillerAttackKick.PushMetres).Within(1e-4));
        }

        // ---- import and controller -----------------------------------------------------------------------------------

        private const string Folder = "Assets/Art/Characters/Mixamo/";

        private static AnimationClip ClipOf(string file)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(Folder + "Mixamo@" + file + ".fbx"))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            return null;
        }

        [Test]
        public void EveryMixamoClipImportsAsHumanoidWithTheExpectedLoopFlags()
        {
            Avatar source = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(Folder + "Mixamo@" + MotionLibrary.AvatarSource + ".fbx"))
                if (asset is Avatar avatar) source = avatar;
            Assert.That(source, Is.Not.Null);
            Assert.That(source.isHuman && source.isValid, Is.True);
            Assert.That(MotionLibrary.Clips.Length, Is.EqualTo(9));
            foreach (MotionLibrary.Entry entry in MotionLibrary.Clips)
            {
                string path = Folder + "Mixamo@" + entry.File + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human), path);
                if (entry.File == MotionLibrary.AvatarSource) Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
                else
                {
                    Assert.That(importer.avatarSetup, Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther), path);
                    Assert.That(importer.sourceAvatar, Is.SameAs(source), path);
                }
                Assert.That(importer.clipAnimations.Length, Is.EqualTo(1), path);
                ModelImporterClipAnimation settings = importer.clipAnimations[0];
                Assert.That(settings.loopTime, Is.EqualTo(entry.Loop), path);
                Assert.That(settings.lockRootRotation && settings.lockRootHeightY, Is.True, path + ": rotation and height baked into the pose");
                bool falls = entry.File == "KnockedDown" || entry.File == "FallingBackDeath";
                Assert.That(settings.lockRootPositionXZ, Is.EqualTo(!falls), path + ": falls keep their XZ travel out of the pose (in place)");
                Assert.That(settings.keepOriginalPositionXZ, Is.EqualTo(!falls), path + ": falls are centred on the mass");
                if (entry.File == "KillerSwipe")
                {
                    Assert.That(settings.firstFrame, Is.EqualTo(Mathf.Round(MotionTiming.SwipeFromSeconds * 30)), path + ": trimmed to the wind-up and strike");
                    Assert.That(settings.lastFrame - settings.firstFrame, Is.EqualTo(Mathf.Round(MotionTiming.SwipeClipSeconds * 30)).Within(.01f), path);
                }
                AnimationClip clip = ClipOf(entry.File);
                Assert.That(clip, Is.Not.Null, path);
                Assert.That(clip.isHumanMotion, Is.True, path);
                Assert.That(clip.isLooping, Is.EqualTo(entry.Loop), path);
                Assert.That(clip.frameRate, Is.EqualTo(30).Within(.01f), path);
            }
        }

        [Test]
        public void TimingConstantsMatchTheImportedClips()
        {
            Assert.That(ClipOf("KnockedDown").length, Is.EqualTo(MotionTiming.KnockedDownSeconds).Within(.02f));
            Assert.That(ClipOf("GettingUp").length, Is.EqualTo(MotionTiming.GettingUpSeconds).Within(.02f));
            Assert.That(ClipOf("FallingBackDeath").length, Is.EqualTo(MotionTiming.DeathSeconds).Within(.02f));
            Assert.That(ClipOf("KillerSwipe").length, Is.EqualTo(MotionTiming.SwipeClipSeconds).Within(.02f));
            Assert.That(ClipOf("HitReaction").length, Is.GreaterThan(MotionTiming.HitSeconds));
        }

        private static AnimatorState Find(AnimatorController controller, int layer, string name)
        {
            foreach (ChildAnimatorState child in controller.layers[layer].stateMachine.states)
                if (child.state.name == name) return child.state;
            return null;
        }

        [Test]
        public void TheMotionControllerHasEveryStateAndAnUpperBodyAttackLayer()
        {
            var controller = Resources.Load<RuntimeAnimatorController>(MotionLibrary.ControllerResource) as AnimatorController;
            Assert.That(controller, Is.Not.Null, "Run MotionSetup.Run (Ping the Killer/Setup Character Motion)");
            Assert.That(controller, Is.Not.SameAs(CharacterArt.Load().Controller), "a new controller; Locomotion.controller stays");
            Assert.That(controller.layers.Length, Is.EqualTo(2));
            foreach (string state in new[] { MotionStates.Locomotion, MotionStates.Injured, MotionStates.Hit, MotionStates.KnockedDown, MotionStates.Crawl,
                MotionStates.GettingUp, MotionStates.Death, MotionStates.Working })
            {
                AnimatorState found = Find(controller, 0, state);
                Assert.That(found, Is.Not.Null, state);
                Assert.That(found.motion, Is.Not.Null, state);
            }
            Assert.That(Find(controller, 0, MotionStates.Hit).speed, Is.EqualTo(1 / MotionTiming.LaggingRate).Within(1e-4), "undoes the Lagging rate");
            AnimatorState crawl = Find(controller, 0, MotionStates.Crawl);
            Assert.That(crawl.speedParameterActive && crawl.speedParameter == MotionStates.SpeedParameter, Is.True, "crawl rate follows the speed");
            Assert.That(controller.layers[1].name, Is.EqualTo(MotionStates.ActionLayer));
            Assert.That(controller.layers[1].avatarMask, Is.Not.Null);
            Assert.That(controller.layers[1].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg), Is.False, "legs keep their locomotion");
            Assert.That(controller.layers[1].avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm), Is.True);
            Assert.That(Find(controller, 1, MotionStates.Swipe), Is.Not.Null);
            Assert.That(Find(controller, 1, MotionStates.Swipe).speed, Is.Zero, "U8: the driver sets the swipe time every frame");
            Assert.That(Find(controller, 1, "Punch"), Is.Null, "the swipe only");
            Assert.That(controller.layers[1].defaultWeight, Is.Zero);
        }

        // ---- the driver on the real characters -------------------------------------------------------------------------

        [Test]
        public void ACaptureDrivesRiosAnimatorThroughTheKnockDownAndKeepsTheBodyOnTheFloor()
        {
            GameObject model = Object.Instantiate(CharacterArt.Load().SurvivorPrefab);
            CharacterMotion motion = CharacterMotion.Attach(model, MotionView.Survivor);
            var instance = new CharacterInstance(model, true);
            try
            {
                Assert.That(motion.Drives, Is.True);
                Assert.That(instance.Animator.runtimeAnimatorController, Is.SameAs(CharacterMotion.Controller));
                double now = 0;
                void Step(byte status, double until, byte flags = 0)
                {
                    for (; now < until; now += 1 / 30.0)
                    {
                        motion.Observe(now, status, flags, 0, instance.Speed);
                        motion.Drive(now);
                        instance.Animate(Vector3.zero, now, status == L, false);
                        motion.Ground();
                    }
                }
                Step(N, 1);
                Assert.That(StateIs(instance.Animator, MotionStates.Locomotion), Is.True);
                float sole = Lowest(model);
                Assert.That(sole, Is.EqualTo(0).Within(.03f), "standing: the soles on the floor");
                Step(L, 1 + Hit + .5);
                Assert.That(StateIs(instance.Animator, MotionStates.KnockedDown), Is.True);
                Step(L, 1 + Down + 1);
                Assert.That(StateIs(instance.Animator, MotionStates.Crawl), Is.True);
                Assert.That(Lowest(model), Is.EqualTo(0).Within(.005f), "down: the lowest point touches the floor");
                Step(N, now + 1, GettingUpFlag);
                Assert.That(StateIs(instance.Animator, MotionStates.GettingUp), Is.True);
                Step(E, now + MotionTiming.DeathSeconds + .5);
                Assert.That(StateIs(instance.Animator, MotionStates.Death), Is.True);
                Assert.That(Lowest(model), Is.EqualTo(0).Within(.005f));
            }
            finally { motion.Dispose(); instance.Dispose(); Object.DestroyImmediate(model); }
        }

        // Drives a fresh Rio through `script` at 30 fps (Observe, Drive, Animate, Ground: the game's order) and reports
        // each frame to `sample`.
        private static void DriveRio(MotionView view, Func<double, byte> status, double seconds, Action<double, GameObject, CharacterMotion, Animator> sample,
            byte flags = 0, Func<double, bool> stepped = null)
        {
            GameObject model = Object.Instantiate(CharacterArt.Load().SurvivorPrefab);
            CharacterMotion motion = CharacterMotion.Attach(model, view);
            var instance = new CharacterInstance(model, true);
            try
            {
                Assert.That(motion.Drives, Is.True);
                for (double now = 0; now < seconds; now += 1 / 30.0)
                {
                    byte s = status(now);
                    motion.Observe(now, s, flags, 0, instance.Speed);
                    motion.Drive(now);
                    instance.Animate(Vector3.zero, now, s == L, stepped != null && stepped(now));
                    motion.Ground();
                    sample(now, model, motion, instance.Animator);
                }
            }
            finally { motion.Dispose(); instance.Dispose(); Object.DestroyImmediate(model); }
        }

        private static Vector2 HipsXZ(GameObject model, Animator animator)
        {
            Vector3 hips = model.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
            return new Vector2(hips.x, hips.z);
        }

        [TestCase(MotionView.Survivor)]
        [TestCase(MotionView.Killer)]
        public void TheKnockDownFallsInPlaceAndFlowsIntoTheCrawlWithoutAJump(MotionView view)
        {
            // Hit -> KnockedDown -> Crawl (QA a1 finding: the fall travelled ~0.9 m back and popped forward into the crawl).
            const double capture = 1;
            Vector2 atCapture = default;
            float drift = 0, jump = 0;
            Vector2 previous = default;
            bool first = true;
            DriveRio(view, t => t < capture ? N : L, capture + Down + 2, (now, model, motion, animator) =>
            {
                Vector2 hips = HipsXZ(model, animator);
                if (now < capture) { atCapture = hips; previous = hips; first = false; return; }
                drift = Mathf.Max(drift, (hips - atCapture).magnitude);
                if (!first) jump = Mathf.Max(jump, (hips - previous).magnitude);
                previous = hips;
            });
            TestContext.WriteLine(view + " hips XZ drift " + drift.ToString("0.000") + " m, largest frame step " + jump.ToString("0.000") + " m");
            Assert.That(drift, Is.LessThanOrEqualTo(.15f), "the body stays over the authoritative position");
            Assert.That(jump, Is.LessThanOrEqualTo(.05f), "no pop between frames (30 fps)");
        }

        [Test]
        public void TheDeathFallsInPlace()
        {
            float drift = 0;
            DriveRio(MotionView.Survivor, t => t < 1 ? N : E, 1 + MotionTiming.DeathSeconds + 1, (now, model, motion, animator) =>
            {
                if (now >= 1) drift = Mathf.Max(drift, HipsXZ(model, animator).magnitude);
            });
            Assert.That(drift, Is.LessThanOrEqualTo(.15f));
        }

        [Test]
        public void ButtonPushingPressesAtTheTerminalTop()
        {
            // The terminal cabinet top is 0.72 m (WorldArt.Terminal) with its devices on it.
            float highest = float.MinValue, lowest = float.MaxValue, reach = 0;
            DriveRio(MotionView.Survivor, t => N, 1 + MotionTiming.ButtonReachIn + 3.3, (now, model, motion, animator) =>
            {
                if (now < 1 + MotionTiming.ButtonReachIn) return;
                Transform tip = animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal) ?? animator.GetBoneTransform(HumanBodyBones.RightHand);
                float y = model.transform.InverseTransformPoint(tip.position).y;
                highest = Mathf.Max(highest, y); lowest = Mathf.Min(lowest, y); reach = motion.Reach;
            }, flags: Hacking);
            TestContext.WriteLine("right fingers " + lowest.ToString("0.000") + ".." + highest.ToString("0.000") + " m");
            Assert.That(reach, Is.EqualTo(1));
            Assert.That(highest, Is.LessThanOrEqualTo(.85f), "the press reaches the devices on the 0.72 m top, not a chest-high panel");
            Assert.That(lowest, Is.GreaterThanOrEqualTo(.68f), "the hand does not go through the top");
        }

        [Test]
        public void TheLaggingTagSitsJustAboveADownBodyAndStaysPutWhileUpright()
        {
            float crawlTop = 0;
            bool upright = true;
            DriveRio(MotionView.Killer, t => t < 1 ? N : L, 1 + Down + 1.5, (now, model, motion, animator) =>
            {
                bool down = motion.TagHeight(1.95f, out float top);
                if (now < 1) upright &= !down;
                if (now > 1 + Down + .5) { Assert.That(down, Is.True); crawlTop = top; }
            }, stepped: t => true);
            Assert.That(upright, Is.True, "standing: the caller keeps its head point");
            Assert.That(crawlTop, Is.InRange(.7f, .9f), "crawling: just above the body");
        }

        [Test]
        public void TheKickWritesBasePlusOffsetAndNeverAccumulates()
        {
            bool saved = KillerAttackKick.ReducedMotion;
            var host = new GameObject("kick");
            var eye = new GameObject("eye", typeof(Camera));
            try
            {
                KillerAttackKick.ReducedMotion = false;
                var camera = eye.GetComponent<Camera>();
                camera.fieldOfView = 58; eye.transform.position = new Vector3(1, 1.7f, 2);
                var kick = host.AddComponent<KillerAttackKick>();
                kick.Play(10);
                // The rig does not place the camera on these frames: the base must not drift.
                kick.Apply(camera, 10 + KillerAttackKick.WindUpSeconds + KillerAttackKick.StrikePeak);
                Assert.That(camera.fieldOfView, Is.EqualTo(58 - KillerAttackKick.FovDegrees).Within(1e-3));
                kick.Apply(camera, 10 + KillerAttackKick.WindUpSeconds + KillerAttackKick.StrikePeak);
                Assert.That(camera.fieldOfView, Is.EqualTo(58 - KillerAttackKick.FovDegrees).Within(1e-3), "no accumulation");
                kick.Apply(camera, 10 + KillerAttackKick.Seconds + .01);
                Assert.That(camera.fieldOfView, Is.EqualTo(58).Within(1e-4), "restored after the kick");
                Assert.That(eye.transform.position, Is.EqualTo(new Vector3(1, 1.7f, 2)));
                // The rig places the camera between frames: the new placement is the base.
                kick.Play(20);
                kick.Apply(camera, 20.05);
                camera.fieldOfView = 60; eye.transform.position = new Vector3(5, 1.7f, 5);
                kick.Apply(camera, 20.06);
                KillerAttackKick.Offset(.06, false, out float fov, out _);
                Assert.That(camera.fieldOfView, Is.EqualTo(60 + fov).Within(1e-3));
                kick.Play(30);
                kick.Apply(camera, 30.05);
                kick.Restore(); // what KillerWorldLayer.OnDestroy calls before Destroy(kick)
                Assert.That(camera.fieldOfView, Is.EqualTo(60).Within(1e-4), "teardown restores the camera");
                Assert.That(typeof(KillerAttackKick).GetNestedTypes(System.Reflection.BindingFlags.NonPublic), Is.Empty, "no coroutine state machine");
            }
            finally { KillerAttackKick.ReducedMotion = saved; Object.DestroyImmediate(host); Object.DestroyImmediate(eye); }
        }

        private static bool StateIs(Animator animator, string state)
            => animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash(state)
               || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == Animator.StringToHash(state));

        private static float Lowest(GameObject model)
        {
            float lowest = float.PositiveInfinity;
            var mesh = new Mesh();
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.BakeMesh(mesh, true);
                Matrix4x4 m = model.transform.worldToLocalMatrix * Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                foreach (Vector3 v in mesh.vertices) lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v).y);
            }
            Object.DestroyImmediate(mesh);
            return lowest;
        }

        [Test]
        public void TheKillerModelSwipesWhenASurvivorIsCapturedInSight()
        {
            CharacterArt art = CharacterArt.Load();
            var root = new GameObject("motion views");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, art);
            try
            {
                characters.Initialize();
                var snapshots = new SnapshotBuffer();
                for (int tick = 1; tick <= 40; tick++)
                {
                    var state = new S2C_WorldState { Tick = (uint)tick };
                    byte mate = tick < 20 ? N : L;
                    state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 2, Z = 2 });
                    state.Players.Add(new PlayerState { Id = 2, Role = (byte)Role.Survivor, Status = mate, X = 3, Z = 3, CaptureCount = (byte)(mate == L ? 1 : 0) });
                    state.Players.Add(new PlayerState { Id = 99, Role = (byte)Role.Killer, X = 3.5f, Z = 3.5f });
                    double at = tick * .05;
                    snapshots.Add(state, at);
                    characters.Accept(state);
                    characters.Render(at + .15, snapshots);
                }
                Assert.That(characters.KillerAttacks, Is.EqualTo(1), "one capture in sight: one swipe");
                Assert.That(characters.KillerAttacking(40 * .05 + .15), Is.True);
                Transform mate2 = root.transform.Find("Player 2");
                Assert.That(mate2.gameObject.activeSelf, Is.True);
                Assert.That(Vector3.Angle(mate2.up, Vector3.up), Is.LessThan(.01f), "the knock-down replaces the old Lagging lean");
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void AnEliminatedSurvivorStaysDownForTheDeathThenDespawns()
        {
            var root = new GameObject("death views");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, CharacterArt.Load());
            try
            {
                var snapshots = new SnapshotBuffer();
                double at = 0;
                for (int tick = 1; tick <= 200; tick++)
                {
                    var state = new S2C_WorldState { Tick = (uint)tick };
                    state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 2, Z = 2 });
                    state.Players.Add(new PlayerState { Id = 2, Role = (byte)Role.Survivor, Status = tick < 10 ? L : E, X = 3, Z = 3 });
                    at = tick * .05;
                    snapshots.Add(state, at);
                    characters.Accept(state);
                    characters.Render(at + .15, snapshots);
                    if (tick == 20) Assert.That(root.transform.Find("Player 2").gameObject.activeSelf, Is.True, "falling");
                    if (tick == 100) Assert.That(root.transform.Find("Player 2").gameObject.activeSelf, Is.True, "holding the last pose");
                }
                Assert.That(root.transform.Find("Player 2").gameObject.activeSelf, Is.False, "despawned after DeathShownSeconds");
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        // ---- dev flag ----------------------------------------------------------------------------------------------------

        [Test]
        public void ThePreviewMotionFlagParsesKnownClipsOnly()
        {
            DevArgs args = DevArgs.Parse(new[] { "x.exe", "-ptk-preview-motion", "knockdown" });
            Assert.That(args.PreviewMotion, Is.EqualTo(MotionPreviewClip.KnockDown));
            Assert.That(args.Active, Is.True);
            Assert.That(args.Errors, Is.Empty);
            args = DevArgs.Parse(new[] { "x.exe", "-ptk-preview-motion", "killer-down" });
            Assert.That(args.PreviewMotion, Is.EqualTo(MotionPreviewClip.KillerDown));
            args = DevArgs.Parse(new[] { "x.exe", "-ptk-preview-motion", "dance" });
            Assert.That(args.PreviewMotion, Is.EqualTo(MotionPreviewClip.None));
            Assert.That(args.Errors, Is.Not.Empty);
            Assert.That(DevArgs.Parse(new string[0]).PreviewMotion, Is.EqualTo(MotionPreviewClip.None));
            foreach (string name in MotionPreview.Names)
                if (name != "none") Assert.That(MotionPreview.TryParse(name, out _), Is.True, name);
        }

        [Test]
        public void ThePreviewScriptsOnlyTheInputsItNames()
        {
            byte status = N, flags = Signal, count = 0;
            MotionPreview.Inputs(MotionPreviewClip.KnockDown, MotionPreview.StartSeconds - .1, ref status, ref flags, ref count);
            Assert.That(status, Is.EqualTo(N));
            MotionPreview.Inputs(MotionPreviewClip.KnockDown, MotionPreview.StartSeconds, ref status, ref flags, ref count);
            Assert.That(status, Is.EqualTo(L));
            status = N;
            MotionPreview.Inputs(MotionPreviewClip.Button, 10, ref status, ref flags, ref count);
            Assert.That(flags & Hacking, Is.EqualTo(Hacking));
            Assert.That(flags & Signal, Is.EqualTo(Signal));
            Assert.That(MotionPreview.ShotTimes(MotionPreviewClip.Death).Length, Is.EqualTo(2));
            Assert.That(MotionPreview.ShotTimes(MotionPreviewClip.KillerDown), Is.Empty);
        }
    }
}
