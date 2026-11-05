using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.QA.Tests;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests.Reverify
{
    // Re-verification Attack 2: Survivor client Killer rendering (Area C of Reverify-flash.md).
    // - Drawn only from Role=Killer entry of the current snapshot;
    // - No extrapolation;
    // - 0.3 s fade at the last position;
    // - Snap after a gap longer than 0.3 s;
    // - Never while spectating (Eliminated, Escaped);
    // - No marker or outline.
    [TestFixture]
    public sealed class SurvivorKillerRenderingReverifyTests
    {
        private const double Tick = 0.05;
        private const int OwnId = 1, KillerId = 999;

        private static S2C_WorldState CreateState(uint tick, float? killerX, float killerYaw = 0)
        {
            var state = new S2C_WorldState { Tick = tick };
            state.Players.Add(new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, X = 0, Z = 0 });
            if (killerX.HasValue)
            {
                state.Players.Add(new PlayerState { Id = KillerId, Role = (byte)Role.Killer, X = killerX.Value, Z = 10f, Yaw = killerYaw });
            }
            return state;
        }

        private static double At(double renderTime) => renderTime + SnapshotBuffer.Delay;

        [Test]
        public void SampleKiller_DrawnOnlyFromRoleKillerEntry()
        {
            var buffer = new SnapshotBuffer();
            // Snapshot with only Survivors
            var state1 = new S2C_WorldState { Tick = 1 };
            state1.Players.Add(new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, X = 1, Z = 1 });
            state1.Players.Add(new PlayerState { Id = 2, Role = (byte)Role.Survivor, X = 5, Z = 5 });
            buffer.Add(state1, 1 * Tick);

            Assert.That(buffer.SampleKiller(At(1 * Tick), out _, out _, out _), Is.False,
                "SampleKiller returns false when no Role=Killer entry exists");

            // Snapshot adding Role=Killer
            var state2 = new S2C_WorldState { Tick = 2 };
            state2.Players.Add(new PlayerState { Id = OwnId, Role = (byte)Role.Survivor, X = 1, Z = 1 });
            state2.Players.Add(new PlayerState { Id = 3, Role = (byte)Role.Killer, X = 12, Z = 15, Yaw = 45 });
            buffer.Add(state2, 2 * Tick);

            Assert.That(buffer.SampleKiller(At(2 * Tick), out Vector3 pos, out float yaw, out float alpha), Is.True);
            Assert.That(pos.x, Is.EqualTo(12f).Within(1e-3));
            Assert.That(pos.z, Is.EqualTo(15f).Within(1e-3));
            Assert.That(yaw, Is.EqualTo(45f).Within(1e-3));
            Assert.That(alpha, Is.EqualTo(1f));
        }

        [Test]
        public void SampleKiller_WhenAbsent_HoldsLastPositionWithoutExtrapolation()
        {
            var buffer = new SnapshotBuffer();
            // Killer present at ticks 1 and 2, moving by +2m each tick
            buffer.Add(CreateState(1, 10f), 1 * Tick);
            buffer.Add(CreateState(2, 12f), 2 * Tick);
            // Killer absent at tick 3, 4, 5
            buffer.Add(CreateState(3, null), 3 * Tick);
            buffer.Add(CreateState(4, null), 4 * Tick);
            buffer.Add(CreateState(5, null), 5 * Tick);

            double renderTimeAtAbsent = 3 * Tick;
            Assert.That(buffer.SampleKiller(At(renderTimeAtAbsent), out Vector3 pos, out _, out float alpha), Is.True);
            // Position must strictly stay at 12m (the last received position), NOT extrapolated to 14m
            Assert.That(pos.x, Is.EqualTo(12f).Within(1e-3), "Position is held at last received position, not extrapolated");
            Assert.That(alpha, Is.LessThanOrEqualTo(1f).And.GreaterThan(0f));

            // Advancing further within 0.3s fade window
            double renderTimeLater = 3 * Tick + 0.15;
            Assert.That(buffer.SampleKiller(At(renderTimeLater), out Vector3 posLater, out _, out float alphaLater), Is.True);
            Assert.That(posLater.x, Is.EqualTo(12f).Within(1e-3), "Position remains pinned at 12m during entire fade");
            Assert.That(alphaLater, Is.LessThan(alpha), "Alpha monotonically decreases");
        }

        [Test]
        public void SampleKiller_FadesOutOverExactlyPointThreeSeconds()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(CreateState(1, 20f), 1 * Tick);
            buffer.Add(CreateState(2, 20f), 2 * Tick);
            // Absent from tick 3 on
            for (uint t = 3; t <= 15; t++)
            {
                buffer.Add(CreateState(t, null), t * Tick);
            }

            double absentStart = 3 * Tick;

            // Halfway through 0.3s fade (+0.15s)
            Assert.That(buffer.SampleKiller(At(absentStart + 0.15), out _, out _, out float halfAlpha), Is.True);
            Assert.That(halfAlpha, Is.EqualTo(0.5f).Within(0.02f), "Alpha is 0.5 at 0.15s");

            // Just before fade expires (+0.29s)
            Assert.That(buffer.SampleKiller(At(absentStart + 0.29), out _, out _, out float endAlpha), Is.True);
            Assert.That(endAlpha, Is.GreaterThan(0f).And.LessThan(0.1f));

            // Beyond 0.3s (+0.31s)
            Assert.That(buffer.SampleKiller(At(absentStart + 0.31), out _, out _, out _), Is.False,
                "Model is completely hidden (returns false) after 0.3s gap");
        }

        [Test]
        public void SampleKiller_SnapsAfterGapLongerThanPointThreeSeconds()
        {
            var buffer = new SnapshotBuffer();
            // Killer at 10m at tick 1 (0.05s)
            buffer.Add(CreateState(1, 10f), 0.05);

            // Killer absent for 0.5s (> 0.3s fade threshold)
            buffer.Add(CreateState(2, null), 0.10);
            buffer.Add(CreateState(3, null), 0.20);
            buffer.Add(CreateState(4, null), 0.30);
            buffer.Add(CreateState(5, null), 0.40);
            buffer.Add(CreateState(6, null), 0.50);

            // Killer reappears at 50m at tick 7 (0.60s)
            buffer.Add(CreateState(7, 50f), 0.60);

            // While in the faded period (0.45s), model is hidden
            Assert.That(buffer.SampleKiller(At(0.45), out _, out _, out _), Is.False, "Model is hidden during prolonged gap");

            // Once tick 7 is reached, model must snap to 50m, never showing intermediate points (e.g. 20m, 30m)
            Assert.That(buffer.SampleKiller(At(0.60), out Vector3 snapPos, out _, out float snapAlpha), Is.True);
            Assert.That(snapPos.x, Is.EqualTo(50f).Within(1e-3), "Snaps immediately to 50m without interpolation across gap");
            Assert.That(snapAlpha, Is.EqualTo(1f));
        }

        [Test]
        public void KillerDrawable_ReturnsFalseWhileSpectating()
        {
            // Spectating statuses: Eliminated and Escaped
            Assert.That(WorldView.KillerDrawable((byte)PlayerStatus.Eliminated), Is.False, "Eliminated spectator never draws Killer");
            Assert.That(WorldView.KillerDrawable((byte)PlayerStatus.Escaped), Is.False, "Escaped spectator never draws Killer");

            // Active statuses: Normal and Lagging
            Assert.That(WorldView.KillerDrawable((byte)PlayerStatus.Normal), Is.True, "Normal survivor draws Killer in line of sight");
            Assert.That(WorldView.KillerDrawable((byte)PlayerStatus.Lagging), Is.True, "Lagging survivor draws Killer in line of sight");
        }

        [Test]
        public void KillerModel_HasNoMarkerOutlineOrThroughWallIndicators()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript(WorldSources.Characters));
            string makeModel = QaSource.MethodText(code, "private KillerModel MakeKillerModel(");
            Assert.That(makeModel, Is.Not.Null);

            // MakeKillerModel creates only body and visor primitives
            Assert.That(makeModel, Does.Contain("Killer body"));
            Assert.That(makeModel, Does.Contain("Killer visor"));

            // No outline shader, no diamond, square, chevron or marker
            Assert.That(makeModel, Does.Not.Contain("Outline"));
            Assert.That(makeModel, Does.Not.Contain("Marker"));
            Assert.That(makeModel, Does.Not.Contain("Tag"));

            // In RenderKiller, only position/rotation and alpha are set
            string renderKiller = QaSource.MethodText(code, "private void RenderKiller(");
            Assert.That(renderKiller, Does.Not.Contain("MarkerLayer"));
            Assert.That(renderKiller, Does.Not.Contain("Outline"));
        }

        private sealed class OfflineProbe : IDisposable
        {
            public readonly OfflineLink Link;
            public readonly List<IPacket> Seen = new List<IPacket>();
            private int cursor;

            public OfflineProbe(int bots = 3)
            {
                Link = new OfflineLink(bots);
                Link.PacketReceived += received => Seen.Add(received.Packet);
            }

            public void Pump() => Link.Pump();

            public T Await<T>(Func<T, bool> predicate = null, int timeoutMs = 5000) where T : class, IPacket
            {
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    Pump();
                    while (cursor < Seen.Count)
                    {
                        T packet = Seen[cursor++] as T;
                        if (packet != null && (predicate == null || predicate(packet))) return packet;
                    }
                    if (watch.ElapsedMilliseconds >= timeoutMs) break;
                    Thread.Sleep(5);
                }
                Assert.Fail("No " + typeof(T).Name + " within " + timeoutMs + " ms. Saw " + Seen.Count + " packets.");
                return null;
            }

            public void Dispose()
            {
                try { Link.Dispose(); } catch (Exception) { }
                Link.WaitStopped(2000);
            }
        }

        [Test]
        public void SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract()
        {
            using (var probe = new OfflineProbe())
            {
                S2C_JoinResult joined = probe.Await<S2C_JoinResult>();
                probe.Await<S2C_LobbyState>();
                probe.Link.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
                probe.Await<S2C_LobbyState>(state => state.HostRoleChoice == (byte)RoleChoice.Survivor);
                probe.Link.Send(new C2S_StartMatch());

                S2C_MatchStart match = probe.Await<S2C_MatchStart>();
                Assert.That(match.Role, Is.EqualTo((byte)Role.Survivor));

                int killerEntriesChecked = 0;
                int firstKillerSnapshot = -1;
                uint inputSeq = 0;
                // Map v2 (U6b): every survivor spawn is out of the Killer's sight by design, so the Killer entry only arrives once
                // the Killer bot has walked into line of sight. A Survivor that stands still is Silent and the Killer bot never
                // hunts it, so the probe keeps sending MoveInput (a square walk, a new side every ten snapshots): the Signal
                // makes the Killer bot chase it. Check at least ten snapshots, and go on (at most 2400 = 120 s of game time; five runs needed 103-599)
                // until a Killer entry has been checked.
                for (int i = 0; i < 2400 && (i < 10 || killerEntriesChecked == 0); i++)
                {
                    S2C_WorldState world = probe.Await<S2C_WorldState>();
                    int side = (i / 10) % 4;
                    probe.Link.Send(new C2S_MoveInput
                    {
                        DirX = side == 0 ? 1f : side == 2 ? -1f : 0f,
                        DirZ = side == 1 ? 1f : side == 3 ? -1f : 0f,
                        Yaw = 0f,
                        Seq = ++inputSeq
                    });
                    bool sawSelf = false;
                    foreach (PlayerState player in world.Players)
                    {
                        if (player.Id == joined.PlayerId)
                        {
                            sawSelf = true;
                            Assert.That(player.Role, Is.EqualTo((byte)Role.Survivor), "Local player is Survivor");
                        }
                        if (player.Role == (byte)Role.Killer)
                        {
                            if (killerEntriesChecked++ == 0) firstKillerSnapshot = i + 1;
                            // U2e contract: Killer entry has Role=Killer and Flags, StatusTimer, CaptureCount all zero
                            Assert.That(player.Id, Is.GreaterThan(0), "U2e contract: Killer Id must be non-zero");
                            Assert.That(player.Flags, Is.EqualTo((byte)PlayerFlags.None), "U2e contract: Killer entry Flags must be zero");
                            Assert.That(player.StatusTimer, Is.EqualTo(0f), "U2e contract: Killer entry StatusTimer must be zero");
                            Assert.That(player.CaptureCount, Is.EqualTo(0), "U2e contract: Killer entry CaptureCount must be zero");
                        }
                    }
                    Assert.That(sawSelf, Is.True, "Survivor sees itself in every world state snapshot");
                }
                Assert.That(killerEntriesChecked, Is.GreaterThan(0), "Survivor with clear line of sight must receive the Killer entry (U2e contract)");
                TestContext.WriteLine("SNAPSHOTS_TO_FIRST_KILLER=" + firstKillerSnapshot);
            }
        }
    }
}
