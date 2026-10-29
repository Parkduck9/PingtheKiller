using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // U4d QA attempt 1, test item 4. With protocol v0.3 the Killer's WorldState can carry Protocol.MaxWorldEntries (16) entries: the
    // Killer, up to 7 real Survivors and the phantom decoys, all anonymous (Id = 0, shuffled). Nothing on the Killer path may be
    // sized for 7 or 8, no two instances may be drawn from one entry, and no entry may be missing from the picture.
    [Category("U4dQaA1")]
    public sealed class SixteenEntriesQaTests
    {
        private static readonly Vector3 Eye = new Vector3(30, KillerViewRig.EyeHeight, 8);
        private static readonly ViewPose Pose = new ViewPose(Eye, 0, 0, KillerViewRig.VerticalFov, 16f / 9);

        // Paths cross by construction: pairs run head-on along one line and swap sides, a block waits on the same spot for
        // 20 ticks and then splits, and the rest random-walk. Every step is at most 0.2 m per tick (4 m/s), inside the tracker gate.
        private sealed class Crowd
        {
            private readonly Vector2[] position;
            private readonly Vector2[] velocity;
            private readonly System.Random rng;
            public int Count => position.Length;

            public Crowd(int count, int seed)
            {
                rng = new System.Random(seed);
                position = new Vector2[count]; velocity = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    int group = i % 3;
                    if (group == 0)
                    {   // head-on pairs along a row: consecutive entries of this group run in opposite directions on the same z
                        int pair = i / 3;
                        float z = 14 + (pair / 2) * 1.5f;
                        bool right = pair % 2 == 0;
                        position[i] = new Vector2(right ? 12 : 48, z);
                        velocity[i] = new Vector2(right ? .15f : -.15f, 0);
                    }
                    else if (group == 1)
                    {   // a block standing on one spot, then splitting
                        position[i] = new Vector2(30, 30);
                        velocity[i] = Vector2.zero;
                    }
                    else
                    {   // random walk
                        position[i] = new Vector2(10 + (float)rng.NextDouble() * 40, 14 + (float)rng.NextDouble() * 30);
                        velocity[i] = RandomVelocity();
                    }
                }
            }

            private Vector2 RandomVelocity()
            {
                double a = rng.NextDouble() * Math.PI * 2;
                return new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * (.05f + (float)rng.NextDouble() * .12f);
            }

            public void Step(int tick)
            {
                for (int i = 0; i < Count; i++)
                {
                    int group = i % 3;
                    if (group == 1 && tick == 20) velocity[i] = new Vector2((i % 2 == 0 ? 1 : -1) * .1f, (i % 4 < 2 ? 1 : -1) * .08f);
                    if (group == 2 && tick % 17 == 0) velocity[i] = RandomVelocity();
                    Vector2 next = position[i] + velocity[i];
                    if (group == 0 && (next.x < 8 || next.x > 52)) velocity[i] = -velocity[i]; // bounce at the row ends
                    position[i] += velocity[i];
                }
            }

            public Vector2 At(int i) => position[i];
        }

        private static S2C_WorldState State(uint tick, Crowd crowd, System.Random shuffle, bool withKiller, bool killerIdZero)
        {
            var state = new S2C_WorldState { Tick = tick, TimeLeft = 300, TerminalsRequired = 3 };
            state.Terminals.Add(new TerminalState { Id = 0, X = 12, Z = 12 });
            var list = new List<PlayerState>();
            for (int i = 0; i < crowd.Count; i++)
            {
                Vector2 p = crowd.At(i);
                list.Add(new PlayerState
                {
                    Id = 0, Role = (byte)Role.Survivor, X = p.x, Z = p.y, Flags = (byte)PlayerFlags.Signal,
                    Status = (byte)(i % 7 == 3 ? PlayerStatus.Lagging : PlayerStatus.Normal)
                });
            }
            for (int i = list.Count - 1; i > 0; i--) { int j = shuffle.Next(i + 1); PlayerState t = list[i]; list[i] = list[j]; list[j] = t; }
            if (withKiller) list.Insert(shuffle.Next(list.Count + 1), new PlayerState { Id = killerIdZero ? 0 : 1, Role = (byte)Role.Killer, X = Eye.x, Z = Eye.z });
            state.Players.AddRange(list);
            return state;
        }

        private static int Compare(Vector2 a, Vector2 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);

        private static void Run(int survivors, int ticks, bool withKiller, bool killerIdZero, int seed)
        {
            var scene = new KillerScene(new System.Random(seed), KillerScene.TerminalPoints(new[] { new TerminalState { X = 12, Z = 12 } }));
            var sound = new BlipSoundModel(new System.Random(seed + 1));
            var crowd = new Crowd(survivors, seed + 2);
            var shuffle = new System.Random(seed + 3);
            var handles = new HashSet<int>();
            var drawn = new List<Vector2>();
            var truth = new List<Vector2>();
            for (int tick = 1; tick <= ticks; tick++)
            {
                crowd.Step(tick);
                S2C_WorldState state = State((uint)tick, crowd, shuffle, withKiller, killerIdZero);
                double now = tick * .05;
                Assert.DoesNotThrow(() => scene.Accept(state, now), "Accept, tick " + tick);
                Assert.That(scene.HasKiller, Is.EqualTo(withKiller), "tick " + tick);
                Assert.That(scene.Blips.Count, Is.EqualTo(survivors), "the blip count equals the entry count, tick " + tick);
                IReadOnlyList<BlipView> views = null;
                Assert.DoesNotThrow(() => views = scene.Frame(Pose, now + KillerScene.TweenSeconds, sound.IsVoiced), "Frame, tick " + tick);
                Assert.DoesNotThrow(() => sound.Update(.05f, scene.Blips, Eye.x, Eye.z, scene.IsOccluded), "sound, tick " + tick);
                Assert.That(views.Count, Is.EqualTo(survivors), "tick " + tick);
                Assert.That(sound.Voices.Count, Is.LessThanOrEqualTo(BlipSoundModel.MaxVoices), "the 4 voices stay at 4 by spec");

                handles.Clear(); drawn.Clear(); truth.Clear();
                int edges = 0;
                foreach (BlipView view in views)
                {
                    Assert.That(handles.Add(view.Handle), Is.True, "two views share handle " + view.Handle + " at tick " + tick);
                    drawn.Add(new Vector2(view.Position.x, view.Position.z));
                    if (view.Edge) edges++;
                }
                Assert.That(edges, Is.LessThanOrEqualTo(KillerScene.EdgeCount), "tick " + tick);
                for (int i = 0; i < crowd.Count; i++) truth.Add(crowd.At(i));
                drawn.Sort(Compare); truth.Sort(Compare);
                for (int i = 0; i < survivors; i++)
                    Assert.That(Vector2.Distance(drawn[i], truth[i]), Is.LessThan(2e-3f), "an entry is drawn twice or not at all, tick " + tick + " rank " + i);
            }
        }

        [Test]
        public void FifteenAnonymousEntriesPlusTheKillerOnCrossingPathsForTwoHundredTicks()
        {
            Run(15, 200, true, false, 11);
        }

        [Test]
        public void TheSameWhenTheKillersOwnEntryAlsoHasIdZero()
        {
            Run(15, 120, true, true, 23);
        }

        [Test]
        public void SixteenSurvivorEntriesWithNoKillerEntryAreDrawnWithoutACrash()
        {
            Run(16, 120, false, false, 31);
        }

        [Test]
        public void MoreThanSixteenEntriesNeverCrashTheTrackerOrTheScene()
        {
            Run(24, 80, true, false, 41);
            Run(40, 60, true, false, 43);
        }

        [Test]
        public void TheTrackerIsIndifferentToHowManyEntriesAreFedAcrossRepeatedResets()
        {
            var tracker = new BlipTracker(new System.Random(9), null);
            for (int round = 0; round < 3; round++)
            {
                tracker.Reset();
                int count = 1 + round * 20;
                var input = new List<BlipInput>();
                for (int i = 0; i < count; i++) input.Add(new BlipInput(4 + i * 1.3f, 20 + (i % 5), i % 4 == 0 ? BlipStatus.Lagging : BlipStatus.Normal));
                for (uint tick = 1; tick <= 15; tick++)
                {
                    Assert.DoesNotThrow(() => tracker.Accept(tick, input, 30, 8));
                    Assert.That(tracker.Blips.Count, Is.EqualTo(count));
                }
                var seen = new HashSet<int>();
                foreach (Blip blip in tracker.Blips) Assert.That(seen.Add(blip.Handle), Is.True);
            }
        }

        [Test]
        public void ASixteenEntryWorldStateSurvivesTheWireAndASeventeenthEntryIsRefused()
        {
            var state = new S2C_WorldState { Tick = 5, TimeLeft = 100 };
            for (int i = 0; i < Protocol.MaxWorldEntries; i++)
                state.Players.Add(new PlayerState { Id = 0, Role = (byte)Role.Survivor, X = i * 3, Z = 5, Flags = (byte)PlayerFlags.Signal });
            byte[] frame = PacketCodec.Encode(state);
            Assert.That(frame.Length - 4, Is.LessThanOrEqualTo(Protocol.MaxBodyLength));
            var decoded = (S2C_WorldState)PacketCodec.Decode(BitConverter.ToUInt16(frame, 4), new ReadOnlySpan<byte>(frame, 6, frame.Length - 6));
            Assert.That(decoded.Players.Count, Is.EqualTo(Protocol.MaxWorldEntries));
            for (int i = 0; i < Protocol.MaxWorldEntries; i++) Assert.That(decoded.Players[i].X, Is.EqualTo(i * 3f), "entry order and values survive");
            state.Players.Add(new PlayerState { Role = (byte)Role.Survivor });
            Assert.Catch(() => PacketCodec.Encode(state), "the wire format caps a snapshot at 16 entries");
        }
    }
}
