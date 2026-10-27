using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.ServerCore.Map;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a2.md section 4 (-ptk-wander): hold W while turning the yaw slowly (e.g. 25 degrees per
    // second) and reverse when the wall ahead is within 2 m (FixedMap), so the local survivor keeps moving (Signal).
    // Plus the isolation claim: the camera-dependent new code is a seam (a U4c camera redesign is coming).
    [Category("U4bQaA2")]
    public sealed class WanderQaTests
    {
        private static float Dx(float yaw) => Mathf.Sin(yaw * Mathf.Deg2Rad);
        private static float Dz(float yaw) => Mathf.Cos(yaw * Mathf.Deg2Rad);

        // ---- turning ---------------------------------------------------------------------------------------------------------

        [Test]
        public void InOpenSpaceTheYawTurnsSlowlyAtAboutTwentyFiveDegreesPerSecond()
        {
            var map = new FixedMap();
            Assert.That(Wander.TurnRate, Is.InRange(10f, 45f), "a slow turn, spec example 25 degrees per second");
            var wander = new Wander(0);
            float yaw = 0;
            for (int i = 0; i < 60; i++) yaw = wander.Next(map, 30, 30, 1f / 60f);
            Assert.That(yaw, Is.EqualTo(Wander.TurnRate).Within(0.05f), "one second of frames turns by the turn rate");
        }

        [Test]
        public void EachFrameTurnsNoMoreThanTheTurnRateAllows()
        {
            var map = new FixedMap();
            var wander = new Wander(100);
            float previous = 100;
            foreach (float dt in new[] { 1f / 120f, 1f / 60f, 1f / 30f, 0.1f })
            {
                float yaw = wander.Next(map, 30, 30, dt);
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(previous, yaw)), Is.LessThanOrEqualTo(Wander.TurnRate * dt + 0.01f), "dt " + dt);
                previous = yaw;
            }
        }

        [Test]
        public void TheYawStaysInZeroToThreeSixtyAndIsFiniteForAnyStartOrFrameTime()
        {
            var map = new FixedMap();
            foreach (float start in new[] { 0f, 359.9f, 720f, -90f, 1e6f })
            {
                var wander = new Wander(start);
                Assert.That(wander.Yaw, Is.InRange(0f, 360f), "start " + start);
                foreach (float dt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 1e-6f, 5f })
                {
                    float yaw = 0;
                    Assert.DoesNotThrow(() => yaw = wander.Next(map, 30, 30, dt), "dt " + dt);
                    Assert.That(float.IsNaN(yaw) || float.IsInfinity(yaw), Is.False, "start " + start + " dt " + dt);
                    Assert.That(yaw, Is.InRange(0f, 360f));
                }
            }
        }

        [Test]
        public void AZeroOrBadFrameTimeDoesNotTurn()
        {
            var map = new FixedMap();
            var wander = new Wander(45);
            Assert.That(wander.Next(map, 30, 30, 0f), Is.EqualTo(45f).Within(0.001f));
            Assert.That(wander.Next(map, 30, 30, -0.5f), Is.EqualTo(45f).Within(0.001f), "time never runs backwards");
            Assert.That(wander.Next(map, 30, 30, float.NaN), Is.EqualTo(45f).Within(0.001f));
            Assert.That(wander.Next(map, 30, 30, float.PositiveInfinity), Is.EqualTo(45f).Within(0.001f));
        }

        // ---- reversing before a wall -------------------------------------------------------------------------------------------

        [Test]
        public void ANorthFacingSurvivorOneMetreFromTheBoundaryWallTurnsAround()
        {
            var map = new FixedMap();
            // Yaw 0 faces +z; the north wall surface is at z = 60.
            Assert.That(Wander.WallAhead(map, 30, 59f, 0), Is.True, "1 m from the wall");
            var wander = new Wander(0);
            float yaw = wander.Next(map, 30, 59f, 1f / 60f);
            Assert.That(Dz(yaw), Is.LessThan(-0.9f), "now heading south, away from the wall (yaw " + yaw + ")");
            Assert.That(Wander.WallAhead(map, 30, 59f, yaw), Is.False);
        }

        [Test]
        public void AWallFourMetresAheadIsNotReactedTo()
        {
            var map = new FixedMap();
            Assert.That(Wander.WallAhead(map, 30, 56f, 0), Is.False, "4 m from the wall");
            var wander = new Wander(0);
            float yaw = wander.Next(map, 30, 56f, 1f);
            Assert.That(yaw, Is.EqualTo(Wander.TurnRate).Within(0.05f), "keeps turning slowly, no reversal");
        }

        [Test]
        public void EveryBoundaryWallAndEveryInnerWallTriggersAReversalFromTheFourCompassHeadings()
        {
            var map = new FixedMap();
            // x, z, yaw: boundary walls (z = 0 and 60, x = 0 and 60) and the inner wall (18..20, 14..32) seen from x = 16.5.
            var cases = new[]
            {
                new[] { 30f, 1.0f, 180f }, new[] { 30f, 59f, 0f }, new[] { 1.0f, 30f, 270f }, new[] { 59f, 30f, 90f },
                new[] { 16.5f, 20f, 90f },
            };
            foreach (float[] c in cases)
            {
                Assert.That(Wander.WallAhead(map, c[0], c[1], c[2]), Is.True, "wall ahead at " + c[0] + "," + c[1] + " yaw " + c[2]);
                var wander = new Wander(c[2]);
                float yaw = wander.Next(map, c[0], c[1], 0f);
                Assert.That(Wander.WallAhead(map, c[0], c[1], yaw), Is.False, "the new heading is clear at " + c[0] + "," + c[1]);
                Assert.That(Dx(yaw) * Dx(c[2]) + Dz(yaw) * Dz(c[2]), Is.LessThan(0f), "the new heading points away from the old one");
            }
        }

        [Test]
        public void AFacingCornerStillFindsAClearHeading()
        {
            var map = new FixedMap();
            // x, z, and the diagonal heading that points into that corner (yaw 0 faces +z, 90 faces +x).
            foreach (float[] c in new[] { new[] { 1.2f, 1.2f, 225f }, new[] { 58.8f, 1.2f, 135f }, new[] { 1.2f, 58.8f, 315f }, new[] { 58.8f, 58.8f, 45f } })
            {
                Assert.That(Wander.WallAhead(map, c[0], c[1], c[2]), Is.True, "the corner is ahead at " + c[0] + "," + c[1]);
                var wander = new Wander(c[2]);
                float yaw = wander.Next(map, c[0], c[1], 0f);
                Assert.That(Wander.WallAhead(map, c[0], c[1], yaw), Is.False, "corner " + c[0] + "," + c[1] + " gave " + yaw);
            }
        }

        [Test]
        public void ReversalAlternatesTheTurnDirectionSoTheWalkDoesNotRetraceTheSameLoop()
        {
            var map = new FixedMap();
            var wander = new Wander(0);
            float reversed = wander.Next(map, 30, 59f, 0f);
            float after = wander.Next(map, 30, 30, 1f);
            Assert.That(Mathf.DeltaAngle(reversed, after), Is.EqualTo(-Wander.TurnRate).Within(0.05f), "after a reversal the turn goes the other way");
        }

        // ---- the walk: moving all match long, never inside a wall ----------------------------------------------------------------

        private sealed class Walk
        {
            public float MinPathPer5Seconds = float.MaxValue;
            public int CellsVisited;
            public int Reversals;
            public float Distance;
            public bool EverBlocked;
        }

        private static Walk Simulate(float startX, float startZ, float startYaw, float seconds, float frame)
        {
            var map = new FixedMap();
            var wander = new Wander(startYaw);
            var walk = new Walk();
            var cells = new HashSet<int>();
            float x = startX, z = startZ, yaw = startYaw, windowPath = 0, windowStart = 0, last = yaw;
            const float speed = 4f; // GameConstants.SurvivorSpeed, W held
            for (int frameIndex = 0; frameIndex * frame < seconds; frameIndex++)
            {
                float time = frameIndex * frame;
                yaw = wander.Next(map, x, z, frame);
                if (Mathf.Abs(Mathf.DeltaAngle(last, yaw)) > Wander.TurnRate * frame + 1f) walk.Reversals++;
                last = yaw;
                MapPoint next = map.Move(x, z, Dx(yaw) * speed * frame, Dz(yaw) * speed * frame);
                float step = Mathf.Sqrt((next.X - x) * (next.X - x) + (next.Z - z) * (next.Z - z));
                walk.Distance += step; windowPath += step;
                if (Mathf.Abs(next.X - x - Dx(yaw) * speed * frame) > 0.001f || Mathf.Abs(next.Z - z - Dz(yaw) * speed * frame) > 0.001f) walk.EverBlocked = true;
                x = next.X; z = next.Z;
                if (!map.CanStand(x, z)) Assert.Fail("inside a wall at " + x + "," + z + " t=" + time);
                cells.Add((int)(x / 10) * 100 + (int)(z / 10));
                if (time - windowStart >= 5f)
                {
                    walk.MinPathPer5Seconds = Mathf.Min(walk.MinPathPer5Seconds, windowPath);
                    windowPath = 0; windowStart = time;
                }
            }
            walk.CellsVisited = cells.Count;
            return walk;
        }

        [Test]
        public void ASixHundredSecondWalkFromEverySpawnKeepsMovingAndNeverEntersAWall()
        {
            var map = new FixedMap();
            foreach (MapPoint spawn in map.SpawnPoints)
            {
                Walk walk = Simulate(spawn.X, spawn.Z, 0f, 600f, 1f / 60f);
                Assert.That(walk.MinPathPer5Seconds, Is.GreaterThan(10f), "the survivor must keep moving (Signal); every 5 s window walks more than half speed, spawn " + spawn.X + "," + spawn.Z);
            }
        }

        [Test]
        public void TheWalkActuallyMeetsWallsAndReversesAtThirtyFramesPerSecond()
        {
            Walk walk = Simulate(10f, 26f, 0f, 600f, 1f / 30f);
            Assert.That(walk.MinPathPer5Seconds, Is.GreaterThan(10f));
            Assert.That(walk.Reversals, Is.GreaterThan(0), "the walls are actually met");
        }

        [Test]
        public void TheWalkIsTheSameAtThirtyAndSixtyFramesPerSecondInSpirit()
        {
            Walk slow = Simulate(10f, 26f, 90f, 120f, 1f / 30f);
            Walk fast = Simulate(10f, 26f, 90f, 120f, 1f / 60f);
            Assert.That(slow.Distance, Is.EqualTo(fast.Distance).Within(fast.Distance * 0.25f), "frame rate does not change how far it walks");
        }

        // ---- isolation: what the camera redesign (U4c) would have to touch -------------------------------------------------------------

        [Test]
        public void WanderIsPureAndNamesNoCameraInputOrNetworkType()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Dev/Wander.cs"));
            foreach (string forbidden in new[] { "Camera", "RoleCamera", "MovementInput", "ClientApp", "NetClient", "UnityEngine.Input", "Keyboard", "MonoBehaviour", "Time." })
                Assert.That(code, Does.Not.Contain(forbidden), "Wander.cs must not depend on " + forbidden);
        }

        [Test]
        public void DevCaptureNeverMentionsTheCameraRigAndSendsNoMovementPackets()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Dev/DevCapture.cs"));
            Assert.That(code, Does.Not.Contain("RoleCamera"));
            Assert.That(code, Does.Not.Contain("Camera"), "DevCapture steers only by returning a yaw; the caller applies it");
            Assert.That(code, Does.Not.Contain("C2S_MoveInput"), "movement goes through MovementInput, never from the dev script");
            Assert.That(code, Does.Not.Contain("C2S_UseSkill"));
            Assert.That(code, Does.Not.Contain("C2S_Interact"));
        }

        [Test]
        public void TheOnlyDevHooksInTheCameraPathAreTheYawTurnAndTheLocalPositionRead()
        {
            string app = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(System.Text.RegularExpressions.Regex.Matches(app, @"rig\.Turn\(").Count, Is.EqualTo(1), "one yaw seam in ClientApp");
            Assert.That(app, Does.Not.Match(@"rig\.Yaw\s*="), "ClientApp never assigns the camera yaw directly");
            string rig = QaSource.StripComments(QaPaths.ReadScript("World/RoleCamera.cs"));
            Assert.That(rig, Does.Match(@"public void Turn\(float yaw\)\s*=>\s*Yaw\s*=\s*Mathf\.Repeat\(yaw,\s*360\)\s*;"), "RoleCamera.Turn is a one-line seam");
            Assert.That(rig, Does.Not.Contain("Wander"));
            Assert.That(rig, Does.Not.Contain("Dev"), "the camera knows nothing about the dev script");
        }

        [Test]
        public void TheDevHooksInClientAppAreGuardedSoAnOrdinaryPlayerRunsNone()
        {
            string app = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string squashed = System.Text.RegularExpressions.Regex.Replace(app, @"\s+", " ");
            foreach (System.Text.RegularExpressions.Match call in System.Text.RegularExpressions.Regex.Matches(squashed, @"\bdev\.(?!\?)(\w+)"))
            {
                string name = call.Groups[1].Value;
                // Set-up lines that run right after the component is created inside 'if (devArgs.Active)'.
                if (name == "Initialize" || name == "Join" || name == "Offline" || name == "HostSend" || name == "PreviewResult") continue;
                string before = squashed.Substring(Math.Max(0, call.Index - 200), Math.Min(200, call.Index));
                Assert.That(before, Does.Contain("dev != null"), "dev." + name + " must be behind a 'dev != null' guard");
            }
            Assert.That(squashed, Does.Contain("dev?.OnMatchStart("));
            Assert.That(squashed, Does.Contain("dev?.OnWorld("));
            Assert.That(squashed, Does.Contain("dev?.OnResult("));
            Assert.That(squashed, Does.Contain("if (devArgs.Active)"), "the component only exists for -ptk-* runs");
        }
    }
}
