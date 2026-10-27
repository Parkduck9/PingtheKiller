using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4-a2.md item 1 (30 Hz MoveInput rule), independent of the implementer's MovementInputTests.
    // Supersedes InputQaTests.OneSendPerHeldFrameThenExactlyOneZeroSendThenSilence and
    // InputQaTests.SeqIsStrictlyIncreasingAcrossHeldFramesZeroSendAndALaterPress (per-frame rule, replaced by orchestrator ruling).
    public sealed class MovementRateQaTests
    {
        private const double Slot = 1.0 / 30;

        private struct Send
        {
            public double Time;
            public int Frame;
            public C2S_MoveInput Packet;
            public bool IsZero => Packet.DirX == 0f && Packet.DirZ == 0f;
        }

        // Frame simulator: Time accumulates the same dt values that MovementInput receives.
        private sealed class Sim
        {
            public readonly MovementInput Input;
            public readonly List<Send> Sends = new List<Send>();
            public double Time;
            public int Frame;

            public Sim(MovementInput input = null) { Input = input ?? new MovementInput(); }

            public bool Step(bool held, double dt, float yaw = 0f)
            {
                Time += dt; Frame++;
                bool sent = Input.TryCreate(held, 0f, held ? 1f : 0f, yaw, dt, out C2S_MoveInput packet);
                if (sent) Sends.Add(new Send { Time = Time, Frame = Frame, Packet = packet });
                return sent;
            }

            public int Run(bool held, double seconds, double dt, float yaw = 0f)
            {
                int before = Sends.Count;
                int frames = (int)Math.Round(seconds / dt);
                for (int i = 0; i < frames; i++) Step(held, dt, yaw);
                return Sends.Count - before;
            }

            public int CountIn(double from, double toExclusive)
            {
                int n = 0;
                foreach (Send s in Sends) if (s.Time >= from && s.Time < toExclusive) n++;
                return n;
            }
        }

        private static void AssertSlidingSecondCap(List<Send> sends, int cap, string context)
        {
            for (int i = 0; i < sends.Count; i++)
            {
                int n = 0;
                for (int j = i; j < sends.Count && sends[j].Time - sends[i].Time < 1.0; j++) n++;
                Assert.That(n, Is.LessThanOrEqualTo(cap), context + ": window starting at send " + i + " (t=" + sends[i].Time + ")");
            }
        }

        private static void AssertStrictlyIncreasingSeq(List<Send> sends, string context)
        {
            for (int i = 1; i < sends.Count; i++)
                Assert.That(sends[i].Packet.Seq, Is.GreaterThan(sends[i - 1].Packet.Seq), context + ": send " + i);
        }

        // ---- steady hold -------------------------------------------------------------------------------------------

        [TestCase(240)]
        [TestCase(144)]
        [TestCase(90)]
        [TestCase(60)]
        [TestCase(45)]
        [TestCase(30)]
        public void OneSecondOfHoldSendsThirtyPlusOrMinusOne(int fps)
        {
            var sim = new Sim();
            int sends = sim.Run(true, 1.0, 1.0 / fps);
            Assert.That(sends, Is.InRange(29, 31), "sends in 1 s at " + fps + " fps");
            foreach (Send s in sim.Sends) Assert.That(s.Packet.DirZ, Is.EqualTo(1f).Within(1e-4f));
        }

        [TestCase(144)]
        [TestCase(30)]
        public void HoldNeverSendsMoreThanOncePerFrameAndSpacingFollowsTheSlot(int fps)
        {
            double dt = 1.0 / fps;
            var sim = new Sim();
            sim.Run(true, 2.0, dt);
            for (int i = 1; i < sim.Sends.Count; i++)
            {
                Assert.That(sim.Sends[i].Frame, Is.GreaterThan(sim.Sends[i - 1].Frame), "two sends in one frame at index " + i);
                double gap = sim.Sends[i].Time - sim.Sends[i - 1].Time;
                Assert.That(gap, Is.GreaterThanOrEqualTo(Slot - dt - 1e-6), "gap too short at index " + i);
                Assert.That(gap, Is.LessThanOrEqualTo(Slot + dt + 1e-6), "gap too long at index " + i + " (server needs inputs fresher than 0.15 s)");
            }
        }

        [Test]
        public void LongHoldKeepsEveryInteriorSecondBetween29And31Sends()
        {
            var sim = new Sim();
            sim.Run(true, 5.0, 1.0 / 144);
            double last = sim.Sends[sim.Sends.Count - 1].Time;
            int checkedWindows = 0;
            foreach (Send start in sim.Sends)
            {
                if (start.Time + 1.0 > last) break;
                int n = sim.CountIn(start.Time, start.Time + 1.0);
                Assert.That(n, Is.InRange(29, 31), "window at t=" + start.Time);
                checkedWindows++;
            }
            Assert.That(checkedWindows, Is.GreaterThan(100));
            AssertSlidingSecondCap(sim.Sends, 31, "5 s hold");
            AssertStrictlyIncreasingSeq(sim.Sends, "5 s hold");
        }

        [Test]
        public void FirstPressAfterLongIdleIsSentInTheSameFrame()
        {
            var sim = new Sim();
            Assert.That(sim.Run(false, 5.0, 1.0 / 144), Is.EqualTo(0), "idle sends nothing");
            Assert.That(sim.Step(true, 1.0 / 144), Is.True, "a fresh press must not wait for a slot");
        }

        // ---- hitch ---------------------------------------------------------------------------------------------------

        [Test]
        public void ATwoHundredFiftyMillisecondHitchDoesNotProduceABurst()
        {
            const double frame = 1.0 / 60;
            var sim = new Sim();
            sim.Run(true, 0.5, frame);
            double hitchStart = sim.Time;
            Assert.That(sim.Step(true, 0.25) ? 1 : 0, Is.LessThanOrEqualTo(1), "at most one send in the hitch frame");
            sim.Run(true, 0.1, frame); // six frames after the hitch
            int inWindow = sim.CountIn(hitchStart, sim.Time + 1e-9);
            // 0.35 s of wall time: one send for the hitch frame plus at most ~3 slots; a catch-up burst would be 8+.
            Assert.That(inWindow, Is.LessThanOrEqualTo(5), "sends during the hitch frame and the 100 ms after it");
            sim.Run(true, 0.5, frame);
            AssertSlidingSecondCap(sim.Sends, 31, "hitch");
            AssertStrictlyIncreasingSeq(sim.Sends, "hitch");
        }

        [Test]
        public void HoldStaysFreshAfterAHitch()
        {
            const double frame = 1.0 / 60;
            var sim = new Sim();
            sim.Run(true, 0.3, frame);
            sim.Step(true, 0.25);
            sim.Run(true, 1.0, frame);
            double last = sim.Sends[sim.Sends.Count - 1].Time;
            Assert.That(sim.Time - last, Is.LessThan(0.15), "newest input must be fresher than the server's 0.15 s window");
            Assert.That(sim.CountIn(sim.Time - 1.0, sim.Time + 1e-9), Is.InRange(28, 31));
        }

        // ---- release -------------------------------------------------------------------------------------------------

        [TestCase(144)]
        [TestCase(60)]
        [TestCase(30)]
        public void ReleaseSendsZeroInTheSameFrameThenExactlyOneBackupAtTheNextSlotThenSilence(int fps)
        {
            double dt = 1.0 / fps;
            var sim = new Sim();
            sim.Run(true, 0.5, dt, 10f);
            int heldCount = sim.Sends.Count;
            uint lastHeldSeq = sim.Sends[heldCount - 1].Packet.Seq;

            Assert.That(sim.Step(false, dt, 45f), Is.True, "zero must go out in the release frame");
            Send release = sim.Sends[heldCount];
            Assert.That(release.IsZero, Is.True);
            Assert.That(release.Packet.Yaw, Is.EqualTo(45f).Within(1e-4f), "zero keeps the look yaw");
            Assert.That(release.Packet.Seq, Is.GreaterThan(lastHeldSeq));

            sim.Run(false, 1.0, dt, 45f);
            int after = sim.Sends.Count - (heldCount + 1);
            Assert.That(after, Is.EqualTo(1), "exactly one backup zero, then silence");
            Send backup = sim.Sends[heldCount + 1];
            Assert.That(backup.IsZero, Is.True);
            Assert.That(backup.Packet.Seq, Is.GreaterThan(release.Packet.Seq));
            double delay = backup.Time - release.Time;
            Assert.That(delay, Is.GreaterThanOrEqualTo(Slot - dt - 1e-6), "backup is the NEXT 30 Hz slot, not an immediate duplicate");
            Assert.That(delay, Is.LessThanOrEqualTo(Slot + dt + 1e-6), "backup must not be late");
        }

        [Test]
        public void ReleaseZeroAfterALongHoldStillGoesOutInTheReleaseFrame()
        {
            // 144 fps, 3 s hold, release at each frame phase of the 30 Hz cycle. Steady hold = 30 sends/s, so the zero
            // is at most the 31st send of the second and must not be postponed by the 31-per-second cap.
            const double dt = 1.0 / 144;
            for (int extraFrames = 0; extraFrames < 6; extraFrames++)
            {
                var sim = new Sim();
                sim.Run(true, 3.0, dt);
                for (int i = 0; i < extraFrames; i++) sim.Step(true, dt);
                Assert.That(sim.Step(false, dt), Is.True, "release " + extraFrames + " frames into the cycle: zero must be immediate");
            }
        }

        [Test]
        public void NothingIsSentWhileIdleAfterTheBackupZero()
        {
            var sim = new Sim();
            sim.Run(true, 0.4, 1.0 / 60);
            sim.Run(false, 0.5, 1.0 / 60);
            int sentSoFar = sim.Sends.Count;
            Assert.That(sim.Run(false, 3.0, 1.0 / 60), Is.EqualTo(0));
            Assert.That(sim.Sends.Count, Is.EqualTo(sentSoFar));
        }

        // ---- cap -----------------------------------------------------------------------------------------------------

        [TestCase(240, 1)]
        [TestCase(144, 1)]
        [TestCase(144, 2)]
        [TestCase(144, 3)]
        [TestCase(60, 1)]
        [TestCase(60, 2)]
        [TestCase(30, 1)]
        public void RapidTappingNeverExceeds31SendsInAnySlidingSecondAndEndsOnAZero(int fps, int periodFrames)
        {
            double dt = 1.0 / fps;
            var sim = new Sim();
            int frames = fps * 5;
            for (int i = 0; i < frames; i++) sim.Step((i / periodFrames) % 2 == 0, dt);
            sim.Run(false, 0.5, dt);
            Assert.That(sim.Sends.Count, Is.GreaterThan(20), "tapping must still send");
            AssertSlidingSecondCap(sim.Sends, 31, "tap period " + periodFrames + " @" + fps);
            AssertStrictlyIncreasingSeq(sim.Sends, "tapping");
            Assert.That(sim.Sends[sim.Sends.Count - 1].IsZero, Is.True, "after the last release the final packet must be a zero so the survivor stops");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RandomKeysAndJitteredFrameTimesNeverExceed31SendsInASecondAndEndOnAZero(int seed)
        {
            var random = new Random(seed);
            var sim = new Sim();
            bool held = false;
            while (sim.Time < 8.0)
            {
                if (random.NextDouble() < 0.15) held = !held;
                double dt = 1.0 / (20 + random.Next(0, 221)); // 20..240 fps
                if (random.NextDouble() < 0.01) dt = 0.25;    // occasional hitch
                sim.Step(held, dt);
            }
            sim.Run(false, 0.5, 1.0 / 60);
            AssertSlidingSecondCap(sim.Sends, 31, "seed " + seed);
            AssertStrictlyIncreasingSeq(sim.Sends, "seed " + seed);
            Assert.That(sim.Sends[sim.Sends.Count - 1].IsZero, Is.True);
        }

        // ---- Seq -----------------------------------------------------------------------------------------------------

        [Test]
        public void SeqIsStrictlyIncreasingAcrossHoldReleaseHold()
        {
            const double dt = 1.0 / 144;
            var sim = new Sim();
            sim.Run(true, 0.3, dt);
            sim.Run(false, 0.3, dt);
            sim.Run(true, 0.3, dt);
            sim.Run(false, 0.3, dt);
            AssertStrictlyIncreasingSeq(sim.Sends, "hold-release-hold");
            int zeros = 0;
            foreach (Send s in sim.Sends) if (s.IsZero) zeros++;
            Assert.That(zeros, Is.EqualTo(4), "two releases, each one zero now plus one backup");
        }

        [Test]
        public void PressDuringTheBackupWindowKeepsSeqIncreasing()
        {
            const double dt = 1.0 / 144;
            var sim = new Sim();
            sim.Run(true, 0.3, dt);
            sim.Run(false, 2 * dt, dt);   // release, then press again before the backup slot
            sim.Run(true, 0.3, dt);
            sim.Run(false, 0.5, dt);
            AssertStrictlyIncreasingSeq(sim.Sends, "quick re-press");
            Assert.That(sim.Sends[sim.Sends.Count - 1].IsZero, Is.True);
        }

        [Test]
        public void ANewMatchOnTheSameConnectionDoesNotRestartSeq()
        {
            const double dt = 1.0 / 60;
            var input = new MovementInput();
            var first = new Sim(input);
            first.Run(true, 0.5, dt);
            first.Run(false, 0.2, dt);
            uint lastOfMatchOne = first.Sends[first.Sends.Count - 1].Packet.Seq;
            Assert.That(lastOfMatchOne, Is.GreaterThan(5u));

            input.ResetKeys(); // what ClientApp does on S2C_MatchStart; the instance (connection) stays the same
            var second = new Sim(input);
            Assert.That(second.Run(false, 0.5, dt), Is.EqualTo(0), "no stray sends right after a reset");
            second.Run(true, 0.5, dt);
            Assert.That(second.Sends.Count, Is.GreaterThan(5));
            Assert.That(second.Sends[0].Packet.Seq, Is.GreaterThan(lastOfMatchOne), "first Seq of match two");
            AssertStrictlyIncreasingSeq(second.Sends, "match two");
        }

        [Test]
        public void ResetKeysWhileHeldDoesNotRestartSeqEither()
        {
            const double dt = 1.0 / 60;
            var input = new MovementInput();
            var sim = new Sim(input);
            sim.Run(true, 0.5, dt);
            uint before = sim.Sends[sim.Sends.Count - 1].Packet.Seq;
            input.ResetKeys();
            sim.Run(true, 0.2, dt);
            Assert.That(sim.Sends[sim.Sends.Count - 1].Packet.Seq, Is.GreaterThan(before));
            AssertStrictlyIncreasingSeq(sim.Sends, "reset while held");
        }

        [Test]
        public void TwoConnectionsHaveIndependentSeq()
        {
            // The contract is "one MovementInput per NetClient": a fresh instance starts its own counter.
            var a = new Sim(); var b = new Sim();
            a.Run(true, 0.5, 1.0 / 60);
            b.Run(true, 0.1, 1.0 / 60);
            Assert.That(b.Sends[0].Packet.Seq, Is.EqualTo(a.Sends[0].Packet.Seq), "a new connection's counter starts from the same base");
            Assert.That(b.Sends[b.Sends.Count - 1].Packet.Seq, Is.LessThan(a.Sends[a.Sends.Count - 1].Packet.Seq), "counters are not shared");
        }

        // ---- robustness ----------------------------------------------------------------------------------------------

        [TestCase(double.NaN)]
        [TestCase(-0.5)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidDeltaTimeNeverThrowsAndNeverBursts(double dt)
        {
            var input = new MovementInput();
            int sends = 0;
            for (int i = 0; i < 100; i++)
                if (input.TryCreate(true, 0f, 1f, 0f, dt, out C2S_MoveInput _)) sends++;
            Assert.That(sends, Is.LessThanOrEqualTo(1), "no time passed, so at most the fresh-press send");
        }
    }
}
