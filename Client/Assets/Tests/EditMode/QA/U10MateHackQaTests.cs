using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PingTheKiller.Client.QA.Tests
{
    // U10-motion QA (attempt 1): do bot mates show the hack pose on a Survivor client in a real offline match?
    // The real GameServer (7 bots) runs through OfflineLink; a CharacterViews renders at about 60 fps in real time while
    // the own Survivor stands still. For every mate frame with a received Hacking flag (Status Normal) it records whether
    // the interaction is Hack, and if not, why (measured speed or tracked pose), plus the burst lengths and the facing.
    public sealed class U10MateHackQaTests
    {
        private sealed class Stat
        {
            public int Flagged, Shown, LateMissSpeed, LateMissPose, Bursts, FacingSamples, FacingOff;
            public double Since = -1, BurstSum, BurstMin = double.MaxValue, MaxSpeed, FacingSum;
            public byte Flags, Status;
            public float X, Z, Yaw;
        }

        [Test, Explicit("real-time diagnostic, 50 s")]
        public void BotMatesShowTheHackPoseWhileTheirHackingFlagIsReceived() => Measure(PlayerFlags.Hacking, InteractionKind.Hack, 50, 60);

        // Same measurement for Rescuing -> Restore (rescues need a capture first, so the run is longer; inconclusive
        // when the bots happened not to rescue anyone).
        [Test, Explicit("real-time diagnostic, 150 s; a rescuer following a crawling ally shows no pose")]
        public void BotMatesShowTheRestorePoseWhileTheirRescuingFlagIsReceived() => Measure(PlayerFlags.Rescuing, InteractionKind.Restore, 150, 0);

        private static void Measure(PlayerFlags flag, InteractionKind expected, double Seconds, int needFrames)
        {
            const double Grace = .5;
            var poses = new Dictionary<string, int>();
            var root = new GameObject("u10 qa mate hack");
            var link = new OfflineLink(7);
            CharacterViews characters = null;
            var snapshots = new SnapshotBuffer();
            var stats = new Dictionary<int, Stat>();
            var terminals = new List<Vector2>();
            int localId = 0;
            bool started = false;
            link.PacketReceived += received =>
            {
                switch (received.Packet)
                {
                    case S2C_JoinResult joined: localId = joined.PlayerId; break;
                    case S2C_MatchStart _: started = true; break;
                    case S2C_WorldState state:
                        if (characters == null) return;
                        snapshots.Add(state, received.Time);
                        characters.Accept(state);
                        terminals.Clear();
                        foreach (TerminalState t in state.Terminals) if (t.Progress < 100) terminals.Add(new Vector2(t.X, t.Z));
                        foreach (PlayerState p in state.Players)
                        {
                            if (p.Role != (byte)Role.Survivor || p.Id == localId) continue;
                            if (!stats.TryGetValue(p.Id, out Stat s)) stats[p.Id] = s = new Stat();
                            s.Flags = p.Flags; s.Status = p.Status; s.X = p.X; s.Z = p.Z; s.Yaw = p.Yaw;
                        }
                        break;
                }
            };
            try
            {
                var wait = Stopwatch.StartNew();
                while (localId == 0 && wait.ElapsedMilliseconds < 5000) { link.Pump(); Thread.Sleep(5); }
                Assert.That(localId, Is.Not.EqualTo(0), "joined");
                link.Send(new C2S_SetRole { RoleChoice = (byte)RoleChoice.Survivor });
                Thread.Sleep(200); link.Pump();
                characters = new CharacterViews(new WorldMaterials(root.transform), localId, Role.Survivor, CharacterArt.Load());
                characters.Initialize();
                link.Send(new C2S_StartMatch());
                wait.Restart();
                while (!started && wait.ElapsedMilliseconds < 5000) { link.Pump(); Thread.Sleep(5); }
                Assert.That(started, Is.True, "match started");

                var clock = Stopwatch.StartNew();
                while (clock.Elapsed.TotalSeconds < Seconds)
                {
                    link.Pump();
                    double now = LinkClock.Now;
                    characters.SetLocalLook(0, false);
                    characters.Render(now, snapshots);
                    foreach (var entry in stats)
                    {
                        Stat s = entry.Value;
                        bool flagged = s.Status == (byte)PlayerStatus.Normal && (s.Flags & (byte)flag) != 0;
                        if (!flagged)
                        {
                            if (s.Since >= 0)
                            {
                                double length = now - s.Since;
                                s.Bursts++; s.BurstSum += length; if (length < s.BurstMin) s.BurstMin = length;
                                s.Since = -1;
                            }
                            continue;
                        }
                        if (s.Since < 0) s.Since = now;
                        s.Flagged++;
                        bool shown = characters.InteractionOf(entry.Key) == expected;
                        if (shown) s.Shown++;
                        float speed = characters.SpeedOf(entry.Key);
                        if (now - s.Since >= Grace)
                        {
                            if (speed > s.MaxSpeed) s.MaxSpeed = speed;
                            if (!shown)
                            {
                                if (speed >= MotionTiming.StillSpeed) s.LateMissSpeed++;
                                else s.LateMissPose++;
                                string key = characters.PoseOf(entry.Key) + "/" + characters.InteractionOf(entry.Key) + "/flags=" + s.Flags + (speed >= MotionTiming.StillSpeed ? "/moving" : "/still");
                                poses.TryGetValue(key, out int n); poses[key] = n + 1;
                            }
                            // Facing against the direction to the nearest unfinished terminal.
                            float best = float.MaxValue; Vector2 to = Vector2.zero;
                            foreach (Vector2 t in terminals)
                            {
                                Vector2 d = t - new Vector2(s.X, s.Z);
                                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; to = d; }
                            }
                            if (best < 4f && best > .01f)
                            {
                                float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                                float off = Mathf.Abs(Mathf.DeltaAngle(s.Yaw, want));
                                s.FacingSamples++; s.FacingSum += off; if (off > 60) s.FacingOff++;
                            }
                        }
                    }
                    Thread.Sleep(12);
                }

                var report = new StringBuilder();
                int flaggedAll = 0, lateMiss = 0;
                foreach (var entry in stats)
                {
                    Stat s = entry.Value;
                    flaggedAll += s.Flagged; lateMiss += s.LateMissSpeed + s.LateMissPose;
                    report.Append("mate ").Append(entry.Key)
                        .Append(" flaggedFrames=").Append(s.Flagged).Append(" shown=").Append(s.Shown)
                        .Append(" missAfterGrace(speed)=").Append(s.LateMissSpeed).Append(" missAfterGrace(pose)=").Append(s.LateMissPose)
                        .Append(" maxSpeedAfterGrace=").Append(s.MaxSpeed.ToString("0.00", CultureInfo.InvariantCulture))
                        .Append(" bursts=").Append(s.Bursts)
                        .Append(" burstAvg=").Append((s.Bursts > 0 ? s.BurstSum / s.Bursts : 0).ToString("0.00", CultureInfo.InvariantCulture))
                        .Append(" burstMin=").Append((s.Bursts > 0 ? s.BurstMin : 0).ToString("0.00", CultureInfo.InvariantCulture))
                        .Append(" facingSamples=").Append(s.FacingSamples)
                        .Append(" facingOffAvgDeg=").Append((s.FacingSamples > 0 ? s.FacingSum / s.FacingSamples : 0).ToString("0", CultureInfo.InvariantCulture))
                        .Append(" facingOff>60=").Append(s.FacingOff).AppendLine();
                }
                foreach (var entry in poses) report.Append("miss ").Append(entry.Key).Append(" x").Append(entry.Value).AppendLine();
                TestContext.WriteLine(report.ToString());
                UnityEngine.Debug.Log("[U10-QA] mate " + expected + "\n" + report);
                if (needFrames == 0 && flaggedAll < 30) Assert.Inconclusive("no bot showed " + flag + " in " + Seconds + " s\n" + report);
                Assert.That(flaggedAll, Is.GreaterThan(needFrames), "no bot showed " + flag + " in " + Seconds + " s: nothing measured");
                Assert.That(lateMiss, Is.LessThanOrEqualTo(flaggedAll / 20), "mates flagged " + flag + " for over " + Grace + " s without " + expected + "\n" + report);
            }
            finally
            {
                characters?.Dispose();
                link.Dispose();
                link.WaitStopped(2000);
                Object.DestroyImmediate(root);
            }
        }
    }
}
