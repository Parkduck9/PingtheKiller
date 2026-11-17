using System;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // INTEG-R (11/17): the R3 QA minors fixed at integration (3: a hint is used up only when shown, 4: the intro card ignores
    // movement keys, 8: no ally tag on top of the own head, 9: the ally markers do not allocate per frame) and the aligned
    // rescue copy (one term, the real controls, the get-up).
    public sealed class IntegRHelpTests
    {
        private static readonly Vector2 Screen1600 = new Vector2(1600, 900);
        private static readonly Rect Cluster = new Rect(464, 804, 672, 72);
        private static ViewPose Pose => new ViewPose(new Vector3(10, 1.7f, 10), 0, 0, 50, 16f / 9);
        private static string Names(int id) => id == 3 ? "Mina" : id == 4 ? "Rio" : null;

        [Test]
        public void TheKillerHintIsNotUsedUpWhileThePromptSlotIsBusy()
        {
            var hints = new HintTracker();
            Assert.That(hints.Killer(6, false, true, false), Is.Null, "the slot shows another prompt");
            Assert.That(hints.FiredBlip, Is.False, "so the hint is still owed");
            Assert.That(hints.Killer(7, false, true, true), Is.EqualTo(HelpCopy.HintKillerBlip));
            Assert.That(hints.FiredBlip, Is.True);
        }

        [Test]
        public void ASurvivorHintIsNotUsedUpWhileAHoldBarOwnsTheSlot()
        {
            var hints = new HintTracker();
            Assert.That(hints.Survivor(1, false, true, true, null, false, false), Is.Null);
            Assert.That(hints.FiredTerminal, Is.False);
            Assert.That(hints.Survivor(2, false, true, true, null, false, true), Is.EqualTo(HelpCopy.HintTerminal));
        }

        [Test]
        public void OnlyEscEnterSpaceOrAClickEndTheIntroCard()
        {
            Assert.That(IntroCardState.DismissInput(false, false, false, false), Is.False, "WASD, arrows, E and Q are not inputs here");
            Assert.That(IntroCardState.DismissInput(true, false, false, false), Is.True);
            Assert.That(IntroCardState.DismissInput(false, true, false, false), Is.True);
            Assert.That(IntroCardState.DismissInput(false, false, true, false), Is.True);
            Assert.That(IntroCardState.DismissInput(false, false, false, true), Is.True);
        }

        [Test]
        public void AnAllyWithinTwoMetresGetsNoTagOverTheOwnHead()
        {
            var markers = new AllyMarkers();
            markers.Build(10, 10, new[] { new Mate { Id = 3, X = 10, Z = 11, Status = PlayerStatus.Lagging } }, Names, Pose, Screen1600, Cluster);
            Assert.That(markers.OnScreenCount, Is.EqualTo(0), "1 m ahead: the rescue prompt names the ally instead");
            markers.Build(10, 10, new[] { new Mate { Id = 3, X = 10, Z = 12.5f, Status = PlayerStatus.Lagging } }, Names, Pose, Screen1600, Cluster);
            Assert.That(markers.OnScreenCount, Is.EqualTo(1), "2.5 m ahead keeps its tag");
            markers.Build(10, 10, new[] { new Mate { Id = 3, X = 10, Z = 8.8f, Status = PlayerStatus.Lagging } }, Names, Pose, Screen1600, Cluster);
            Assert.That(markers.EdgeCount, Is.EqualTo(1), "an ally right behind still gets its edge arrow");
        }

        [Test]
        public void BuildingTheAllyMarkersDoesNotAllocatePerFrame()
        {
            var markers = new AllyMarkers();
            var mates = new[]
            {
                new Mate { Id = 3, X = 10, Z = 30, Status = PlayerStatus.Lagging },
                new Mate { Id = 4, X = 60, Z = 10, Status = PlayerStatus.Lagging },
                new Mate { Id = 5, X = 12, Z = 14, Status = PlayerStatus.Normal },
            };
            Func<int, string> names = Names;
            for (int i = 0; i < 20; i++) markers.Build(10, 10, mates, names, Pose, Screen1600, Cluster); // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) markers.Build(10, 10 + (i & 1) * .01f, mates, names, Pose, Screen1600, Cluster);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(markers.OnScreenCount + markers.EdgeCount, Is.EqualTo(2));
            Assert.That(allocated, Is.LessThan(1024), "500 frames: no closure, no boxed enumerator, no new tag string");
        }

        [Test]
        public void TheRescueCopyUsesOneTermAndTheRealControls()
        {
            Assert.That(HelpCopy.RescuePrompt("Mina"), Is.EqualTo("Hold [E] to rescue Mina — you will be Signal"));
            Assert.That(HelpCopy.RescuingPrompt("Mina"), Is.EqualTo("Rescuing Mina — you are Signal"));
            Assert.That(HelpCopy.SurvivorControls, Does.Contain("Hold E Hack / Rescue").And.Contain("While hacking: WASD routes packets"));
            Assert.That(HelpCopy.KillerControls, Does.Contain("Left click Attack"));
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
            {
                HelpPage page = HelpCopy.Page(role);
                foreach (string line in HelpCopy.AllText(page))
                {
                    Assert.That(line.ToLowerInvariant(), Does.Not.Contain("restor").Or.Contain("emergency restore"), line);
                    Assert.That(line, Does.Not.Contain("touch"), line);
                }
                Assert.That(page.Ladder[2], Does.StartWith("Rescue:").And.Contain("1.5 s").And.Contain("cannot move"));
            }
        }

        // R1 QA M4: 16 ticks is the hit length; 17 keeps one tick of slack; 18 (a miss read with lost snapshots) is not a hit.
        [Test]
        public void TheSwingHitLengthStopsAtSeventeenTicks()
        {
            Assert.That(KillerSwingWatch.HitTicks, Is.EqualTo(16));
            Assert.That(KillerSwingWatch.MissTicks, Is.EqualTo(20));
            Assert.That(KillerSwingWatch.HitLength(16) && KillerSwingWatch.HitLength(17), Is.True);
            Assert.That(KillerSwingWatch.HitLength(18), Is.False);
        }

        // R1 QA M1: a press while the own entry is stunned, pulling, vaulting or breaking plays no sweep or whoosh.
        [Test]
        public void APressTheServerWillRefusePlaysNoSwing()
        {
            foreach (ChaseFlags flag in new[] { ChaseFlags.Stunned, ChaseFlags.Pulling, ChaseFlags.Vaulting, ChaseFlags.Breaking })
            {
                var watch = new KillerSwingWatch();
                watch.OwnChase = (byte)flag;
                Assert.That(watch.Predict(1), Is.False, flag.ToString());
                watch.OwnChase = 0;
                Assert.That(watch.Predict(1.01), Is.True, flag + " cleared: the next press plays");
            }
        }
    }
}
