using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Tests
{
    public sealed class MovementInputTests
    {
        private sealed class Sim
        {
            public readonly MovementInput Input = new MovementInput();
            public readonly List<double> Times = new List<double>();
            public readonly List<C2S_MoveInput> Sent = new List<C2S_MoveInput>();
            public double Now;

            public bool Frame(bool held, double dt)
            {
                Now += dt;
                if (!Input.TryCreate(held, 0, held ? 1 : 0, 0, dt, out C2S_MoveInput packet)) return false;
                Times.Add(Now); Sent.Add(packet);
                return true;
            }

            public int Run(bool held, double fps, double seconds)
            {
                int sent = 0, frames = (int)Math.Round(seconds * fps);
                for (int i = 0; i < frames; i++) if (Frame(held, 1 / fps)) sent++;
                return sent;
            }

            public int CountBetween(double from, double to)
            {
                int count = 0;
                foreach (double time in Times) if (time > from && time <= to) count++;
                return count;
            }

            public void AssertSlidingWindowCap()
            {
                for (int i = 0; i + MovementInput.MaxPerSecond < Times.Count; i++)
                    Assert.That(Times[i + MovementInput.MaxPerSecond] - Times[i], Is.GreaterThan(1.0), "more than 31 sends within 1 s at send " + i);
            }
        }

        [TestCase(144)]
        [TestCase(30)]
        [TestCase(45)]
        [TestCase(60)]
        [TestCase(240)]
        public void HoldSendsThirtyPerSecondIndependentOfFrameRate(int fps)
        {
            var sim = new Sim();
            Assert.That(sim.Run(true, fps, 1), Is.InRange(29, 31), "first second");
            sim.Run(true, fps, 2);
            Assert.That(sim.CountBetween(1, 2), Is.InRange(29, 31), "steady second");
            Assert.That(sim.Sent.TrueForAll(p => p.DirZ == 1), Is.True);
            sim.AssertSlidingWindowCap();
        }

        [Test]
        public void FramesFasterThanTheSlotRateNeverSendTwicePerSlot()
        {
            var sim = new Sim();
            sim.Run(true, 144, 1);
            for (int i = 1; i < sim.Times.Count; i++)
                Assert.That(sim.Times[i] - sim.Times[i - 1], Is.GreaterThan(3.5 / 144), "gap before send " + i);
        }

        [Test]
        public void FrameHitchDropsMissedSlotsInsteadOfBursting()
        {
            var sim = new Sim();
            sim.Run(true, 144, .5);
            double before = sim.Now;
            Assert.That(sim.Frame(true, .25), Is.True, "the hitch frame sends once");
            for (int i = 0; i < 4; i++) Assert.That(sim.Frame(true, 1 / 144.0), Is.False, "frame " + i + " after the hitch");
            sim.Run(true, 144, .25);
            Assert.That(sim.CountBetween(before, sim.Now), Is.InRange(8, 9), "hitch frame plus 0.28 s at 30 Hz, not 7 extra catch-up sends");
            sim.AssertSlidingWindowCap();
        }

        [Test]
        public void ReleaseSendsZeroInSameFrameAndOneBackupAtNextSlotThenSilence()
        {
            var sim = new Sim();
            sim.Run(true, 144, .5);
            int held = sim.Sent.Count;
            Assert.That(sim.Frame(false, 1 / 144.0), Is.True, "release frame");
            Assert.That(sim.Sent[held].DirX, Is.Zero); Assert.That(sim.Sent[held].DirZ, Is.Zero);
            for (int i = 0; i < 4; i++) Assert.That(sim.Frame(false, 1 / 144.0), Is.False, "before next slot, frame " + i);
            Assert.That(sim.Frame(false, 1 / 144.0), Is.True, "backup at the next slot");
            Assert.That(sim.Sent[held + 1].DirX, Is.Zero); Assert.That(sim.Sent[held + 1].DirZ, Is.Zero);
            Assert.That(sim.Run(false, 144, 2), Is.Zero, "idle stays silent");
        }

        [Test]
        public void NothingIsSentWhileIdleFromTheStart()
        {
            var sim = new Sim();
            Assert.That(sim.Run(false, 144, 1), Is.Zero);
        }

        [Test]
        public void SeqIsStrictlyIncreasingAcrossHoldReleaseHold()
        {
            var sim = new Sim();
            sim.Run(true, 144, .3);
            sim.Run(false, 144, .2);
            sim.Run(true, 30, .3);
            sim.Run(false, 60, .5);
            Assert.That(sim.Sent[0].Seq, Is.EqualTo(1));
            for (int i = 1; i < sim.Sent.Count; i++) Assert.That(sim.Sent[i].Seq, Is.EqualTo(sim.Sent[i - 1].Seq + 1), "send " + i);
            int last = sim.Sent.Count - 1;
            Assert.That(sim.Sent[last].DirZ, Is.Zero); Assert.That(sim.Sent[last - 1].DirZ, Is.Zero);
            Assert.That(sim.Sent[last - 2].DirZ, Is.EqualTo(1));
        }

        [Test]
        public void ResetKeysForgetsHeldStateButKeepsSeq()
        {
            var sim = new Sim();
            sim.Run(true, 60, .2);
            uint seq = sim.Sent[sim.Sent.Count - 1].Seq;
            sim.Input.ResetKeys();
            Assert.That(sim.Run(false, 60, .5), Is.Zero, "no stale release after a new match");
            Assert.That(sim.Frame(true, 1 / 60.0), Is.True);
            Assert.That(sim.Sent[sim.Sent.Count - 1].Seq, Is.EqualTo(seq + 1));
        }

        [Test]
        public void TappingEveryFrameStaysUnderTheSlidingWindowCap()
        {
            var sim = new Sim();
            for (int i = 0; i < 288; i++) sim.Frame(i % 2 == 0, 1 / 144.0);
            Assert.That(sim.Sent.Count, Is.GreaterThanOrEqualTo(MovementInput.MaxPerSecond));
            sim.AssertSlidingWindowCap();
        }

        [Test]
        public void InvalidDeltaTimeDoesNotAdvanceTheSchedule()
        {
            var input = new MovementInput();
            Assert.That(input.TryCreate(true, 0, 1, 0, 0, out _), Is.True);
            Assert.That(input.TryCreate(true, 0, 1, 0, double.NaN, out _), Is.False);
            Assert.That(input.TryCreate(true, 0, 1, 0, -1, out _), Is.False);
            Assert.That(input.TryCreate(true, 0, 1, 0, double.PositiveInfinity, out _), Is.False);
        }
    }
}
