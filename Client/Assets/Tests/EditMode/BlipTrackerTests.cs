using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;

namespace PingTheKiller.Client.Tests
{
    public sealed class BlipTrackerTests
    {
        private static readonly List<(float x, float z)> NoTerminals = new List<(float x, float z)>();
        private const float FarX = 100f, FarZ = 100f; // killer position far from everything

        private static BlipInput N(float x, float z) => new BlipInput(x, z, BlipStatus.Normal);
        private static BlipInput L(float x, float z) => new BlipInput(x, z, BlipStatus.Lagging);

        private static BlipTracker Tracker(int seed, List<(float x, float z)> terminals = null) =>
            new BlipTracker(new Random(seed), terminals ?? NoTerminals);

        private static Blip At(BlipTracker t, float x, float z)
        {
            Blip found = null;
            for (int i = 0; i < t.Blips.Count; i++)
            {
                Blip b = t.Blips[i];
                if (b.X == x && b.Z == z)
                {
                    Assert.That(found, Is.Null, "two blips at the same position");
                    found = b;
                }
            }
            Assert.That(found, Is.Not.Null, $"no blip at ({x}, {z})");
            return found;
        }

        private static int Count(BlipTracker t, BlipEventKind kind)
        {
            int c = 0;
            for (int i = 0; i < t.Events.Count; i++) if (t.Events[i].Kind == kind) c++;
            return c;
        }

        private static bool Has(BlipTracker t, BlipEventKind kind, int handle)
        {
            for (int i = 0; i < t.Events.Count; i++)
                if (t.Events[i].Kind == kind && t.Events[i].Handle == handle) return true;
            return false;
        }

        private static List<BlipInput> Shuffled(Random r, params BlipInput[] items)
        {
            var list = new List<BlipInput>(items);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = r.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        // --- Anonymity and matching -------------------------------------------------------------

        [Test]
        public void CrossingBlipsInShuffledOrderKeepTheirHandles()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var feed = new Random(seed + 1000); // the fake server's own shuffle (Id = 0, no order)
                var t = Tracker(seed);
                float ax = -1f, bx = 1f;
                t.Accept(1, Shuffled(feed, N(ax, 0f), N(bx, 0.3f)), FarX, FarZ);
                int ha = At(t, ax, 0f).Handle, hb = At(t, bx, 0.3f).Handle;
                Assert.That(ha, Is.Not.EqualTo(hb));
                for (uint tick = 2; tick <= 14; tick++)
                {
                    ax += 0.15f;
                    bx -= 0.15f;
                    t.Accept(tick, Shuffled(feed, N(ax, 0f), N(bx, 0.3f)), FarX, FarZ);
                    Assert.That(t.Blips.Count, Is.EqualTo(2));
                    Assert.That(At(t, ax, 0f).Handle, Is.EqualTo(ha), $"seed {seed} tick {tick}");
                    Assert.That(At(t, bx, 0.3f).Handle, Is.EqualTo(hb), $"seed {seed} tick {tick}");
                    Assert.That(t.Events.Count, Is.Zero);
                }
                Assert.That(ax, Is.GreaterThan(bx), "the paths really crossed");
            }
        }

        [Test]
        public void HandlesDoNotFollowInputOrder()
        {
            int firstGetsHandle1 = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var t = Tracker(seed);
                t.Accept(1, new[] { N(0f, 0f), N(5f, 0f) }, FarX, FarZ); // fixed wire order
                if (At(t, 0f, 0f).Handle == 1) firstGetsHandle1++;
            }
            Assert.That(firstGetsHandle1, Is.InRange(40, 160), "handle assignment must not follow wire order");
        }

        [Test]
        public void MatchedPairUpdatesPrevAndCurrent()
        {
            var t = Tracker(1);
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            Blip b = t.Blips[0];
            Assert.That((b.PrevX, b.PrevZ), Is.EqualTo((0f, 0f)), "spawned: Prev == current");
            t.Accept(2, new[] { N(0.2f, 0.1f) }, FarX, FarZ);
            Assert.That(t.Blips[0], Is.SameAs(b));
            Assert.That((b.PrevX, b.PrevZ, b.X, b.Z), Is.EqualTo((0f, 0f, 0.2f, 0.1f)));
        }

        [Test]
        public void JumpBeyondGateDespawnsAndRespawns()
        {
            var t = Tracker(2);
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            int h = t.Blips[0].Handle;
            t.Accept(2, new[] { N(0.39f, 0f) }, FarX, FarZ);
            Assert.That(t.Blips[0].Handle, Is.EqualTo(h), "0.39 m is inside the 0.4 m gate");
            t.Accept(3, new[] { N(0.39f + 0.5f, 0f) }, FarX, FarZ);
            Assert.That(t.Blips.Count, Is.EqualTo(1));
            Assert.That(t.Blips[0].Handle, Is.Not.EqualTo(h));
            Assert.That(Has(t, BlipEventKind.Despawned, h), Is.True);
            Assert.That(Has(t, BlipEventKind.Spawned, t.Blips[0].Handle), Is.True);
            Assert.That(t.Blips[0].Speed, Is.Zero);
            Assert.That((t.Blips[0].PrevX, t.Blips[0].PrevZ), Is.EqualTo((t.Blips[0].X, t.Blips[0].Z)));
        }

        [TestCase(2u, 0.7f, true)]   // gate 0.8
        [TestCase(2u, 0.9f, false)]
        [TestCase(3u, 1.1f, true)]   // gate 1.2
        [TestCase(3u, 1.3f, false)]
        [TestCase(10u, 3.9f, true)]  // gate 4.0
        [TestCase(10u, 0f, true)]
        [TestCase(11u, 0f, false)]   // gap > 10: nothing matches
        [TestCase(0u, 0.3f, true)]   // same tick again: gate 0.4 x max(1, 0)
        public void TickGapScalesTheGate(uint gap, float move, bool matched)
        {
            var t = Tracker(3);
            t.Accept(100, new[] { N(0f, 0f) }, FarX, FarZ);
            int h = t.Blips[0].Handle;
            t.Accept(100 + gap, new[] { N(move, 0f) }, FarX, FarZ);
            Assert.That(t.Blips[0].Handle == h, Is.EqualTo(matched));
        }

        [Test]
        public void TickRegressionRespawnsEverything()
        {
            var t = Tracker(4);
            t.Accept(100, new[] { N(0f, 0f) }, FarX, FarZ);
            int h = t.Blips[0].Handle;
            t.Accept(99, new[] { N(0f, 0f) }, FarX, FarZ);
            Assert.That(t.Blips[0].Handle, Is.Not.EqualTo(h));
        }

        [Test]
        public void SymmetricTieIsBrokenRandomlyNotByOrder()
        {
            int leftWins = 0, rightWins = 0;
            for (int seed = 0; seed < 1000; seed++)
            {
                var t = Tracker(seed);
                t.Accept(1, new[] { N(-0.1f, 0f), N(0.1f, 0f) }, FarX, FarZ);
                int left = At(t, -0.1f, 0f).Handle, right = At(t, 0.1f, 0f).Handle;
                t.Accept(2, new[] { N(0f, 0f) }, FarX, FarZ); // exactly 0.1 m from both
                Assert.That(t.Blips.Count, Is.EqualTo(1));
                int survivor = t.Blips[0].Handle;
                if (survivor == left) leftWins++;
                else if (survivor == right) rightWins++;
                else Assert.Fail("the tie must be matched, not respawned");
            }
            Assert.That(leftWins, Is.GreaterThan(300));
            Assert.That(rightWins, Is.GreaterThan(300));
        }

        [Test]
        public void GreedyTakesTheNearestPairFirst()
        {
            var t = Tracker(5);
            t.Accept(1, new[] { N(0f, 0f), N(0.5f, 0f) }, FarX, FarZ);
            int a = At(t, 0f, 0f).Handle, b = At(t, 0.5f, 0f).Handle;
            // New (0.3,0): 0.3 from A, 0.2 from B -> goes to B. New (0f, 0.1f): 0.1 from A.
            t.Accept(2, new[] { N(0.3f, 0f), N(0f, 0.1f) }, FarX, FarZ);
            Assert.That(At(t, 0.3f, 0f).Handle, Is.EqualTo(b));
            Assert.That(At(t, 0f, 0.1f).Handle, Is.EqualTo(a));
        }

        // --- Facing -----------------------------------------------------------------------------

        [Test]
        public void NewBlipFacesNearestTerminal()
        {
            var terminals = new List<(float x, float z)> { (10f, 0f), (0f, -3f), (-20f, 20f) };
            var t = Tracker(6, terminals);
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            Assert.That(t.Blips[0].FacingX, Is.EqualTo(0f).Within(1e-6));
            Assert.That(t.Blips[0].FacingZ, Is.EqualTo(-1f).Within(1e-6));
        }

        [Test]
        public void NewBlipWithoutTerminalsFacesPlusZ()
        {
            var t = Tracker(7);
            t.Accept(1, new[] { N(3f, 4f) }, FarX, FarZ);
            Assert.That((t.Blips[0].FacingX, t.Blips[0].FacingZ), Is.EqualTo((0f, 1f)));
        }

        [Test]
        public void FacingFollowsDisplacementsOfAtLeastThreeCentimetres()
        {
            var terminals = new List<(float x, float z)> { (10f, 0f) };
            var below = Tracker(8, terminals);
            below.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            below.Accept(2, new[] { N(0f, 0.029f) }, FarX, FarZ); // below threshold, never moved: still faces terminal
            Assert.That(below.Blips[0].FacingX, Is.EqualTo(1f).Within(1e-3));

            var t = Tracker(8, terminals);
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            Blip b = t.Blips[0];
            t.Accept(2, new[] { N(0f, 0.03f) }, FarX, FarZ); // exactly 0.03 m along +z
            Assert.That(b.FacingX, Is.EqualTo(0f).Within(1e-6));
            Assert.That(b.FacingZ, Is.EqualTo(1f).Within(1e-6));
            t.Accept(3, new[] { N(0.02f, 0.03f) }, FarX, FarZ); // small step: keep last real facing, not terminal
            Assert.That(b.FacingX, Is.EqualTo(0f).Within(1e-6));
            Assert.That(b.FacingZ, Is.EqualTo(1f).Within(1e-6));
            t.Accept(4, new[] { N(0.02f, -0.12f) }, FarX, FarZ);
            Assert.That(b.FacingX, Is.EqualTo(0f).Within(1e-3));
            Assert.That(b.FacingZ, Is.EqualTo(-1f).Within(1e-3));
        }

        // --- Speed ------------------------------------------------------------------------------

        [Test]
        public void SpeedIsMeanOfLastFourDisplacements()
        {
            var t = Tracker(9);
            float x = 0f;
            t.Accept(1, new[] { N(x, 0f) }, FarX, FarZ);
            Blip b = t.Blips[0];
            Assert.That(b.Speed, Is.Zero);
            Assert.That(b.Walking, Is.False);
            float[] steps = { 0.1f, 0.2f, 0.3f, 0.35f, 0.05f };
            float[] expected = { 2f, 3f, 4f, 4.75f, 4.5f }; // means / 0.05
            for (int i = 0; i < steps.Length; i++)
            {
                float prev = x;
                x += steps[i];
                t.Accept((uint)(2 + i), new[] { N(x, 0f) }, FarX, FarZ);
                Assert.That(t.Blips[0], Is.SameAs(b));
                Assert.That(b.Speed, Is.EqualTo(expected[i]).Within(2e-3), $"step {i}");
                Assert.That(b.Walking, Is.True);
            }
        }

        [Test]
        public void WalkingIsSpeedAboveHalfMetrePerSecond()
        {
            var t = Tracker(10);
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            t.Accept(2, new[] { N(0.02f, 0f) }, FarX, FarZ); // 0.4 m/s
            Assert.That(t.Blips[0].Walking, Is.False);
            t.Accept(3, new[] { N(0.08f, 0f) }, FarX, FarZ); // mean (0.02, 0.06) = 0.04 -> 0.8 m/s
            Assert.That(t.Blips[0].Walking, Is.True);
        }

        [Test]
        public void SpeedIsPerTickAcrossTickGaps()
        {
            var t = Tracker(11);
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            t.Accept(3, new[] { N(0.4f, 0f) }, FarX, FarZ); // 0.4 m over 2 ticks = 4 m/s
            Assert.That(t.Blips[0].Speed, Is.EqualTo(4f).Within(1e-3));
        }

        // --- Events -----------------------------------------------------------------------------

        [Test]
        public void SpawnAndDespawnEvents()
        {
            var t = Tracker(12);
            t.Accept(1, new[] { N(0f, 0f), N(5f, 5f) }, FarX, FarZ);
            Assert.That(Count(t, BlipEventKind.Spawned), Is.EqualTo(2));
            int gone = At(t, 5f, 5f).Handle;
            t.Accept(2, new[] { N(0f, 0f) }, FarX, FarZ);
            Assert.That(t.Events.Count, Is.EqualTo(1));
            Assert.That(Has(t, BlipEventKind.Despawned, gone), Is.True);
            t.Accept(3, new[] { N(0f, 0f) }, FarX, FarZ);
            Assert.That(t.Events.Count, Is.Zero, "events belong to the last Accept only");
        }

        [Test]
        public void NormalToLaggingOnMatchedPairConfirmsCapture()
        {
            var t = Tracker(13);
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            int h = t.Blips[0].Handle;
            t.Accept(2, new[] { L(0f, 0f) }, FarX, FarZ); // far from the killer: still a capture
            Assert.That(Has(t, BlipEventKind.StatusNormalToLagging, h), Is.True);
            Assert.That(Has(t, BlipEventKind.CaptureConfirm, h), Is.True);
            Assert.That(t.Blips[0].Status, Is.EqualTo(BlipStatus.Lagging));

            t.Accept(3, new[] { L(0f, 0f) }, FarX, FarZ);
            Assert.That(t.Events.Count, Is.Zero, "Lagging -> Lagging is silent");
            t.Accept(4, new[] { N(0f, 0f) }, FarX, FarZ);
            Assert.That(t.Events.Count, Is.Zero, "Lagging -> Normal (rescue) is silent");
        }

        [Test]
        public void NewLaggingEntryNearKillerConfirmsCapture()
        {
            var t = Tracker(14);
            t.Accept(1, new BlipInput[0], 0f, 0f);
            t.Accept(2, new[] { L(1.2f, 0f) }, 0f, 0f);
            Assert.That(Count(t, BlipEventKind.Spawned), Is.EqualTo(1));
            Assert.That(Has(t, BlipEventKind.CaptureConfirm, t.Blips[0].Handle), Is.True);
            Assert.That(Count(t, BlipEventKind.StatusNormalToLagging), Is.Zero);
        }

        [TestCase(3.0f, BlipStatus.Lagging)] // R1 (11/17): beyond SwingCaptureRadius (swing range 2.6 m + 0.2 m)
        [TestCase(0.5f, BlipStatus.Normal)]
        public void OtherNewEntriesDoNotConfirmCapture(float distance, BlipStatus status)
        {
            var t = Tracker(15);
            t.Accept(1, new BlipInput[0], 0f, 0f);
            t.Accept(2, new[] { new BlipInput(distance, 0f, status) }, 0f, 0f);
            Assert.That(Count(t, BlipEventKind.CaptureConfirm), Is.Zero);
        }

        [Test]
        public void CaptureConfirmAtMostOncePerHalfSecond()
        {
            var t = Tracker(16);
            var normal = new[] { N(0f, 0f), N(5f, 0f), N(10f, 0f) };
            t.Accept(99, normal, FarX, FarZ);
            int a = At(t, 0f, 0f).Handle, b = At(t, 5f, 0f).Handle, c = At(t, 10f, 0f).Handle;

            t.Accept(100, new[] { L(0f, 0f), N(5f, 0f), N(10f, 0f) }, FarX, FarZ);
            Assert.That(Has(t, BlipEventKind.CaptureConfirm, a), Is.True);
            for (uint tick = 101; tick < 109; tick++) t.Accept(tick, new[] { L(0f, 0f), N(5f, 0f), N(10f, 0f) }, FarX, FarZ);

            t.Accept(109, new[] { L(0f, 0f), L(5f, 0f), N(10f, 0f) }, FarX, FarZ); // 0.45 s later
            Assert.That(Has(t, BlipEventKind.StatusNormalToLagging, b), Is.True);
            Assert.That(Count(t, BlipEventKind.CaptureConfirm), Is.Zero);

            t.Accept(110, new[] { L(0f, 0f), L(5f, 0f), L(10f, 0f) }, FarX, FarZ); // 0.5 s after the first
            Assert.That(Has(t, BlipEventKind.CaptureConfirm, c), Is.True);
        }

        [Test]
        public void TwoCapturesInOneSnapshotConfirmOnce()
        {
            var t = Tracker(17);
            t.Accept(1, new[] { N(0f, 0f), N(5f, 0f) }, FarX, FarZ);
            t.Accept(2, new[] { L(0f, 0f), L(5f, 0f) }, FarX, FarZ);
            Assert.That(Count(t, BlipEventKind.StatusNormalToLagging), Is.EqualTo(2));
            Assert.That(Count(t, BlipEventKind.CaptureConfirm), Is.EqualTo(1));
        }

        [Test]
        public void LaggingBlipNearKillerVanishingConfirmsExecute()
        {
            var t = Tracker(18);
            t.Accept(1, new[] { L(1f, 0f), N(30f, 0f) }, 0f, 0f);
            int h = At(t, 1f, 0f).Handle;
            t.Accept(2, new[] { N(30f, 0f) }, 50f, 50f); // killer moved away now; the previous snapshot counts
            Assert.That(Has(t, BlipEventKind.Despawned, h), Is.True);
            Assert.That(Has(t, BlipEventKind.ExecuteConfirm, h), Is.True);
        }

        [TestCase(1.3f, BlipStatus.Lagging)]
        [TestCase(1.0f, BlipStatus.Normal)]
        public void OtherVanishingBlipsDoNotConfirmExecute(float distance, BlipStatus status)
        {
            var t = Tracker(19);
            t.Accept(1, new[] { new BlipInput(distance, 0f, status) }, 0f, 0f);
            t.Accept(2, new BlipInput[0], 0f, 0f);
            Assert.That(Count(t, BlipEventKind.Despawned), Is.EqualTo(1));
            Assert.That(Count(t, BlipEventKind.ExecuteConfirm), Is.Zero);
        }

        [Test]
        public void LaggingBlipMovingAwayStaysMatchedAndIsNotExecuted()
        {
            var t = Tracker(20);
            t.Accept(1, new[] { L(1f, 0f) }, 0f, 0f);
            t.Accept(2, new[] { L(1.05f, 0f) }, 0f, 0f);
            Assert.That(Count(t, BlipEventKind.ExecuteConfirm), Is.Zero);
        }

        [Test]
        public void NoCueEventsAcrossADiscontinuity()
        {
            // First snapshot after construction/reset, and after a > 10 tick gap: nothing is observable as a change.
            var t = Tracker(21);
            t.Accept(1, new[] { L(0.5f, 0f) }, 0f, 0f);
            Assert.That(Count(t, BlipEventKind.Spawned), Is.EqualTo(1));
            Assert.That(Count(t, BlipEventKind.CaptureConfirm), Is.Zero);
            t.Accept(20, new[] { L(0.5f, 0f) }, 0f, 0f);
            Assert.That(Count(t, BlipEventKind.Despawned), Is.EqualTo(1));
            Assert.That(Count(t, BlipEventKind.Spawned), Is.EqualTo(1));
            Assert.That(Count(t, BlipEventKind.ExecuteConfirm), Is.Zero);
            Assert.That(Count(t, BlipEventKind.CaptureConfirm), Is.Zero);
        }

        // --- Reset, sanitizing, allocation ------------------------------------------------------

        [Test]
        public void ResetClearsStateAndRespawnsWithFreshHandles()
        {
            var t = Tracker(22);
            t.Accept(1, new[] { N(0f, 0f), N(3f, 0f) }, FarX, FarZ);
            int maxOld = Math.Max(t.Blips[0].Handle, t.Blips[1].Handle);
            t.Reset();
            Assert.That(t.Blips.Count, Is.Zero);
            Assert.That(t.Events.Count, Is.Zero);
            t.Accept(2, new[] { N(0f, 0f), N(3f, 0f) }, FarX, FarZ);
            Assert.That(Count(t, BlipEventKind.Spawned), Is.EqualTo(2), "nothing matches across a reset");
            Assert.That(Count(t, BlipEventKind.Despawned), Is.Zero);
            Assert.That(Math.Min(t.Blips[0].Handle, t.Blips[1].Handle), Is.GreaterThan(maxOld));
        }

        [Test]
        public void ResetAlsoClearsCaptureRateLimit()
        {
            var t = Tracker(23);
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            t.Accept(2, new[] { L(0f, 0f) }, FarX, FarZ);
            Assert.That(Count(t, BlipEventKind.CaptureConfirm), Is.EqualTo(1));
            t.Reset();
            t.Accept(3, new[] { N(0f, 0f) }, FarX, FarZ);
            t.Accept(4, new[] { L(0f, 0f) }, FarX, FarZ);
            Assert.That(Count(t, BlipEventKind.CaptureConfirm), Is.EqualTo(1));
        }

        [Test]
        public void NonFiniteEntriesAreIgnored()
        {
            var t = Tracker(24);
            t.Accept(1, new[] { N(float.NaN, 0f), N(0f, float.PositiveInfinity), N(1f, 1f) }, FarX, FarZ);
            Assert.That(t.Blips.Count, Is.EqualTo(1));
        }

        [Test]
        public void ManyEntriesGrowBuffers()
        {
            var t = Tracker(25);
            var list = new List<BlipInput>();
            for (int i = 0; i < 40; i++) list.Add(N(i * 2f, 0f));
            t.Accept(1, list, FarX, FarZ);
            t.Accept(2, list, FarX, FarZ);
            Assert.That(t.Blips.Count, Is.EqualTo(40));
            Assert.That(t.Events.Count, Is.Zero);
        }

        [Test]
        public void BlipsListReferenceIsStable()
        {
            var t = Tracker(26);
            IReadOnlyList<Blip> view = t.Blips;
            t.Accept(1, new[] { N(0f, 0f) }, FarX, FarZ);
            t.Accept(2, new[] { N(9f, 0f), N(0f, 0f) }, FarX, FarZ);
            Assert.That(t.Blips, Is.SameAs(view));
            Assert.That(view.Count, Is.EqualTo(2));
        }

        [Test]
        public void SteadyStateAcceptDoesNotAllocate()
        {
            var t = Tracker(27, new List<(float x, float z)> { (10f, 0f) });
            var list = new List<BlipInput>();
            for (int i = 0; i < 8; i++) list.Add(N(i * 3f, 0f));
            t.Accept(1, list, FarX, FarZ);
            for (uint tick = 2; tick < 50; tick++) t.Accept(tick, list, FarX, FarZ); // warm up

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint tick = 50; tick < 550; tick++)
            {
                for (int i = 0; i < list.Count; i++) list[i] = N(list[i].X + 0.01f, (tick & 1) * 0.05f);
                t.Accept(tick, list, FarX, FarZ);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(t.Blips.Count, Is.EqualTo(8));
            Assert.That(allocated, Is.LessThan(4096), "per-snapshot path must not allocate (no LINQ, no closures)");
        }
    }
}
