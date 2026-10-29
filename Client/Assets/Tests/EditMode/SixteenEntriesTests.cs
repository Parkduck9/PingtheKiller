using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // U4d scope 5: the Killer path handles Protocol.MaxWorldEntries (16) entries per snapshot, phantoms included: 15
    // anonymous Survivor entries (Id 0, shuffled each snapshot) plus the Killer for 200 ticks on crossing paths. No
    // exception, stable counts, and every entry is drawn by exactly one instance (no "avatar collapse").
    public sealed class SixteenEntriesTests
    {
        private const int Survivors = Protocol.MaxWorldEntries - 1;
        private static readonly Vector2 Center = new Vector2(30, 30);

        // Entry i walks a diameter of its own circle (radius 6..14 m) at 3 m/s and back, so paths cross at the centre at
        // different ticks, several at the same tick, and pairs swap sides. Two are Lagging (slower), like captured Survivors.
        private static Vector2 Position(int i, uint tick)
        {
            float radius = 6 + (i % 5) * 2;
            float angle = i * (360f / Survivors) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float speed = Lagging(i) ? 1f : 3f; // m/s, below the tracker gate of 8 m/s
            float travelled = speed * tick * .05f, length = 2 * radius;
            float phase = travelled % (2 * length);
            float along = phase <= length ? phase : 2 * length - phase; // there and back
            return Center + direction * (radius - along);
        }

        private static bool Lagging(int i) => i == 3 || i == 11;

        private static S2C_WorldState State(uint tick, System.Random shuffle, List<Vector2> truth)
        {
            var state = new S2C_WorldState { Tick = tick, TimeLeft = 300, TerminalsRequired = 3 };
            state.Terminals.Add(new TerminalState { Id = 0, X = 12, Z = 12 });
            var entries = new List<PlayerState>();
            truth.Clear();
            for (int i = 0; i < Survivors; i++)
            {
                Vector2 p = Position(i, tick);
                truth.Add(p);
                entries.Add(new PlayerState
                {
                    Id = 0, Role = (byte)Role.Survivor, X = p.x, Z = p.y, Flags = (byte)PlayerFlags.Signal,
                    Status = (byte)(Lagging(i) ? PlayerStatus.Lagging : PlayerStatus.Normal)
                });
            }
            for (int i = entries.Count - 1; i > 0; i--) { int j = shuffle.Next(i + 1); PlayerState t = entries[i]; entries[i] = entries[j]; entries[j] = t; }
            entries.Insert(shuffle.Next(entries.Count + 1), new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 30, Z = 8 });
            Assert.That(entries.Count, Is.EqualTo(Protocol.MaxWorldEntries));
            state.Players.AddRange(entries);
            return state;
        }

        private static int Compare(Vector2 a, Vector2 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);

        [Test]
        public void FifteenAnonymousEntriesPlusTheKillerTrackForTwoHundredTicksWithoutCollapse()
        {
            var scene = new KillerScene(new System.Random(5), KillerScene.TerminalPoints(new[] { new TerminalState { X = 12, Z = 12 } }));
            var sound = new BlipSoundModel(new System.Random(6));
            var shuffle = new System.Random(7);
            var pose = new ViewPose(new Vector3(30, KillerViewRig.EyeHeight, 8), 0, 0, KillerViewRig.VerticalFov, 16f / 9);
            var truth = new List<Vector2>();
            var drawn = new List<Vector2>();
            var handles = new HashSet<int>();
            int maxEdges = 0, laggingSeen = 0;
            for (uint tick = 1; tick <= 200; tick++)
            {
                double now = tick * .05;
                S2C_WorldState state = State(tick, shuffle, truth);
                Assert.DoesNotThrow(() => scene.Accept(state, now), "tick " + tick);
                Assert.That(scene.HasKiller, Is.True);
                Assert.That(scene.Blips.Count, Is.EqualTo(Survivors), "one instance per entry, tick " + tick);
                IReadOnlyList<BlipView> views = null;
                // Drawn at the end of the 50 ms tween, every instance sits exactly on its entry.
                Assert.DoesNotThrow(() => views = scene.Frame(pose, now + KillerScene.TweenSeconds, sound.IsVoiced), "frame " + tick);
                Assert.DoesNotThrow(() => sound.Update(.05f, scene.Blips, pose.Eye.x, pose.Eye.z, scene.IsOccluded));
                Assert.That(views.Count, Is.EqualTo(Survivors), "tick " + tick);
                Assert.That(sound.Voices.Count, Is.LessThanOrEqualTo(BlipSoundModel.MaxVoices));

                handles.Clear(); drawn.Clear();
                int edges = 0;
                foreach (BlipView view in views)
                {
                    Assert.That(handles.Add(view.Handle), Is.True, "a handle drawn twice at tick " + tick);
                    Assert.That(float.IsNaN(view.Position.x) || float.IsNaN(view.Position.z), Is.False);
                    drawn.Add(new Vector2(view.Position.x, view.Position.z));
                    if (view.Edge) edges++;
                    if (view.Status == BlipStatus.Lagging) laggingSeen++;
                }
                maxEdges = Math.Max(maxEdges, edges);
                // The multiset of drawn positions equals the multiset of entry positions: no entry is drawn twice and none
                // is missing, even when paths cross.
                drawn.Sort(Compare); truth.Sort(Compare);
                for (int i = 0; i < Survivors; i++)
                    Assert.That(Vector2.Distance(drawn[i], truth[i]), Is.LessThan(1e-3f), "tick " + tick + " entry " + i);
                Assert.That(scene.Count(BlipClass.InView) + scene.Count(BlipClass.Occluded) + CountOffScreen(views), Is.EqualTo(Survivors));
            }
            Assert.That(maxEdges, Is.LessThanOrEqualTo(KillerScene.EdgeCount), "edge indicators stay at the spec's 6");
            Assert.That(laggingSeen, Is.GreaterThan(0));
        }

        private static int CountOffScreen(IReadOnlyList<BlipView> views)
        {
            int count = 0;
            foreach (BlipView view in views) if (view.Class == BlipClass.OffScreen) count++;
            return count;
        }

        [Test]
        public void TheTrackerKeepsSixteenEntriesAcrossAResetAndAGap()
        {
            var tracker = new BlipTracker(new System.Random(1), null);
            var input = new List<BlipInput>();
            for (int i = 0; i < Protocol.MaxWorldEntries; i++) input.Add(new BlipInput(5 + i * 3, 10, BlipStatus.Normal));
            tracker.Accept(1, input, 0, 0);
            Assert.That(tracker.Blips.Count, Is.EqualTo(16));
            tracker.Accept(40, input, 0, 0); // a gap beyond MaxTickGap: everything respawns, still 16
            Assert.That(tracker.Blips.Count, Is.EqualTo(16));
            tracker.Reset();
            tracker.Accept(41, input, 0, 0);
            Assert.That(tracker.Blips.Count, Is.EqualTo(16));
        }
    }
}
