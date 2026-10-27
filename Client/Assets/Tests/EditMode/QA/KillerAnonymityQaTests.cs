using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.QA.Tests
{
    // U4c QA attempt 1, test item 1 and 2. Written from HUD_SPEC 6.2/6.3 and U2c-a1 section 1: on the Killer client the Survivor
    // entries are anonymous (Id = 0, shuffled), and their Id, Yaw, StatusTimer, CaptureCount and the Hacking, Rescuing,
    // Stealthed and Afterimage flags must not change anything that is drawn, counted or classified. Positions are the
    // only identity.
    [Category("U4cQaA1")]
    public sealed class KillerAnonymityQaTests
    {
        private const float KillerX = 10, KillerZ = 23;
        // Killer at (10, 23) looking +x (yaw 90). The wall x 18..20, z 14..32 of FixedMap stands in front of it.
        private static readonly ViewPose Pose = new ViewPose(new Vector3(KillerX, KillerViewRig.EyeHeight, KillerZ), 90, 0, KillerViewRig.VerticalFov, 16f / 9);
        private static readonly List<(float x, float z)> Terminals = new List<(float x, float z)> { (10, 25), (28, 12) };

        private struct Spec
        {
            public float X, Z;
            public byte Status;
        }

        // Six Survivors, always at least 2 m apart (the tracker gate is 0.4 m per tick, so no match can be ambiguous),
        // moving at most 0.05 m per tick: one in view, one Lagging in view, two behind the wall, two off-screen.
        private static List<Spec> Truth(uint tick)
        {
            float s = tick * .1f;
            return new List<Spec>
            {
                new Spec { X = 15, Z = 20 + s * .3f, Status = (byte)PlayerStatus.Normal },        // in view
                new Spec { X = 26, Z = 22 + s * .5f, Status = (byte)PlayerStatus.Normal },        // behind the wall
                new Spec { X = 4, Z = 24 + s * .5f, Status = (byte)PlayerStatus.Normal },         // behind the camera
                new Spec { X = 14, Z = 26 - s * .2f, Status = (byte)PlayerStatus.Lagging },       // Lagging, in view
                new Spec { X = 32, Z = 27 + s * .3f, Status = (byte)PlayerStatus.Normal },        // behind the wall, far
                new Spec { X = 10, Z = 10 + s * .4f, Status = (byte)PlayerStatus.Normal },        // to the right, off-screen
            };
        }

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        private static S2C_WorldState State(uint tick, List<Spec> truth, Func<int, PlayerState, PlayerState> entryFor, System.Random shuffle, int killerId = 1)
        {
            var state = new S2C_WorldState { Tick = tick, TimeLeft = 500, TerminalsRequired = 3 };
            state.Terminals.Add(new TerminalState { Id = 0, X = 10, Z = 25, Progress = 0 });
            state.Terminals.Add(new TerminalState { Id = 1, X = 28, Z = 12, Progress = 0 });
            var list = new List<PlayerState>();
            for (int i = 0; i < truth.Count; i++)
                list.Add(entryFor(i, new PlayerState { Role = (byte)Role.Survivor, Status = truth[i].Status, X = truth[i].X, Z = truth[i].Z }));
            list.Add(new PlayerState { Id = killerId, Role = (byte)Role.Killer, Status = (byte)PlayerStatus.Normal, X = KillerX, Z = KillerZ, Yaw = 90 });
            if (shuffle != null) Shuffle(list, shuffle);
            state.Players.AddRange(list);
            return state;
        }

        // What the U2c server sends to the Killer, with the unread fields cleared.
        private static PlayerState Zeroed(int index, PlayerState e)
        {
            e.Id = 0; e.Yaw = 0; e.StatusTimer = 0; e.CaptureCount = 0; e.Flags = (byte)PlayerFlags.Signal;
            return e;
        }

        private static Func<int, PlayerState, PlayerState> Garbage(System.Random rng)
            => (index, e) =>
            {
                e.Id = rng.Next(int.MinValue, int.MaxValue);
                e.Yaw = (float)(rng.NextDouble() * 720 - 360);
                e.StatusTimer = (float)(rng.NextDouble() * 120);
                e.CaptureCount = (byte)rng.Next(256);
                // Signal stays set (the server only sends Signal Survivors); Afterimage, Stealthed, Hacking, Rescuing are noise.
                e.Flags = (byte)((byte)PlayerFlags.Signal | (rng.Next(16) << 1));
                return e;
            };

        private static string Describe(IReadOnlyList<BlipView> views)
        {
            var items = new List<KeyValuePair<float[], string>>();
            foreach (BlipView v in views)
            {
                string text = string.Format(CultureInfo.InvariantCulture, "pos={0:0.000},{1:0.000} {2} {3} edge={4} occ={5} walk={6} dist={7:0.000} yaw={8:0.0}",
                    v.Position.x, v.Position.z, v.Status, v.Class, v.Edge, v.Occluded, v.Walking, v.Distance, v.FacingYaw);
                items.Add(new KeyValuePair<float[], string>(new[] { v.Position.x, v.Position.z }, text));
            }
            items.Sort((a, b) => a.Key[0] != b.Key[0] ? a.Key[0].CompareTo(b.Key[0]) : a.Key[1].CompareTo(b.Key[1]));
            var sb = new StringBuilder();
            foreach (var item in items) sb.AppendLine(item.Value);
            return sb.ToString();
        }

        private static string DescribeBlips(IReadOnlyList<Blip> blips)
        {
            var lines = new List<string>();
            foreach (Blip b in blips)
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000} prev={2:0.000},{3:0.000} {4} speed={5:0.0000} face={6:0.0000},{7:0.0000}", b.X, b.Z, b.PrevX, b.PrevZ, b.Status, b.Speed, b.FacingX, b.FacingZ));
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        private static string EventKinds(IReadOnlyList<BlipEvent> events)
        {
            var kinds = new List<string>();
            foreach (BlipEvent e in events) kinds.Add(e.Kind.ToString());
            kinds.Sort(StringComparer.Ordinal);
            return string.Join(",", kinds);
        }

        // ---- 1. anonymity ---------------------------------------------------------------------------------------------------

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void RandomGarbageInTheUnreadFieldsAndShuffledOrderDrawsExactlyLikeZeroedFieldsInFixedOrder(int seed)
        {
            var clean = new KillerScene(new System.Random(100 + seed), Terminals);
            var noisy = new KillerScene(new System.Random(200 + seed), Terminals);
            var garbage = Garbage(new System.Random(seed));
            var shuffle = new System.Random(300 + seed);
            for (uint tick = 1; tick <= 80; tick++)
            {
                double accepted = tick * .05;
                clean.Accept(State(tick, Truth(tick), Zeroed, null), accepted);
                noisy.Accept(State(tick, Truth(tick), garbage, shuffle, killerId: seed), accepted);
                // Two frames per tick, so the 100 ms class hold and the tween are exercised too.
                foreach (double offset in new[] { .02, .06 })
                {
                    IReadOnlyList<BlipView> a = clean.Frame(Pose, accepted + offset, null);
                    IReadOnlyList<BlipView> b = noisy.Frame(Pose, accepted + offset, null);
                    string context = "seed " + seed + " tick " + tick + " +" + offset;
                    Assert.That(b.Count, Is.EqualTo(6), "rendering count, " + context);
                    Assert.That(Describe(b), Is.EqualTo(Describe(a)), "classification, positions, tween and facing, " + context);
                    foreach (BlipClass c in new[] { BlipClass.InView, BlipClass.Occluded, BlipClass.OffScreen })
                        Assert.That(noisy.Count(c), Is.EqualTo(clean.Count(c)), c + " count, " + context);
                }
                Assert.That(DescribeBlips(noisy.Blips), Is.EqualTo(DescribeBlips(clean.Blips)), "tracker state, tick " + tick);
                Assert.That(EventKinds(noisy.Events), Is.EqualTo(EventKinds(clean.Events)), "tracker events, tick " + tick);
                Assert.That(noisy.KillerX, Is.EqualTo(KillerX));
                Assert.That(noisy.KillerZ, Is.EqualTo(KillerZ));
            }
        }

        [Test]
        public void TheSixSurvivorsLandInTheClassesTheScenarioWasBuiltFor()
        {
            // Guards the scenario above: if the map or the view changed, the comparison would stop covering all three classes.
            var scene = new KillerScene(new System.Random(1), Terminals);
            scene.Accept(State(1, Truth(1), Zeroed, null), 0);
            scene.Frame(Pose, .05, null);
            Assert.That(scene.Count(BlipClass.InView), Is.EqualTo(2), "one Normal and one Lagging in view");
            Assert.That(scene.Count(BlipClass.Occluded), Is.EqualTo(2), "two behind the wall");
            Assert.That(scene.Count(BlipClass.OffScreen), Is.EqualTo(2), "behind the camera and to the right");
        }

        [Test]
        public void TwoRunsWithDifferentSurvivorIdsForTheSamePositionsGiveIdenticalResults()
        {
            int[] idsA = { 2, 3, 4, 5, 6, 7 };
            int[] idsB = { 900, -4, int.MaxValue, 0, 17, int.MinValue };
            var a = new KillerScene(new System.Random(11), Terminals);
            var b = new KillerScene(new System.Random(12), Terminals);
            for (uint tick = 1; tick <= 60; tick++)
            {
                double now = tick * .05;
                a.Accept(State(tick, Truth(tick), (i, e) => { e.Id = idsA[i]; return e; }, null), now);
                // The second run also rotates which Id sits on which position every tick (a borrowed or shuffled Id).
                uint t = tick;
                b.Accept(State(tick, Truth(tick), (i, e) => { e.Id = idsB[(i + (int)t) % idsB.Length]; return e; }, null), now);
                string context = "tick " + tick;
                Assert.That(Describe(b.Frame(Pose, now + .03, null)), Is.EqualTo(Describe(a.Frame(Pose, now + .03, null))), context);
                Assert.That(EventKinds(b.Events), Is.EqualTo(EventKinds(a.Events)), context);
                Assert.That(DescribeBlips(b.Blips), Is.EqualTo(DescribeBlips(a.Blips)), context);
            }
        }

        [Test]
        public void SurvivorsThatSwapIdsEveryTickKeepTheirInstancesBecauseOnlyPositionMatches()
        {
            var scene = new KillerScene(new System.Random(21), Terminals);
            int handleA = -1, handleB = -1;
            for (uint tick = 1; tick <= 40; tick++)
            {
                float step = tick * .1f;
                var truth = new List<Spec>
                {
                    new Spec { X = 15, Z = 20 + step },       // A
                    new Spec { X = 15, Z = 30 - step },       // B: the two walk toward each other and cross at tick 50
                };
                uint t = tick;
                scene.Accept(State(tick, truth, (i, e) => { e.Id = ((int)t + i) % 2 == 0 ? 8 : 9; return e; }, null), tick * .05);
                Assert.That(scene.Blips.Count, Is.EqualTo(2));
                if (tick > 1)
                    foreach (BlipEvent e in scene.Events)
                        Assert.That(e.Kind, Is.Not.EqualTo(BlipEventKind.Spawned).And.Not.EqualTo(BlipEventKind.Despawned), "Ids swapping must not respawn anything, tick " + tick);
                int a = HandleNear(scene, 15, 20 + step), b = HandleNear(scene, 15, 30 - step);
                if (tick == 1) { handleA = a; handleB = b; }
                Assert.That(a, Is.EqualTo(handleA), "instance A, tick " + tick);
                Assert.That(b, Is.EqualTo(handleB), "instance B, tick " + tick);
            }
        }

        private static int HandleNear(KillerScene scene, float x, float z)
        {
            foreach (Blip b in scene.Blips)
                if (Mathf.Abs(b.X - x) < .01f && Mathf.Abs(b.Z - z) < .01f) return b.Handle;
            Assert.Fail("no instance near " + x + "," + z);
            return -1;
        }

        [Test]
        public void ABorrowedIdThatJumpsAcrossTheMapLooksLikeOneSurvivorVanishingAndAnotherAppearing()
        {
            var scene = new KillerScene(new System.Random(31), Terminals);
            scene.Accept(State(1, new List<Spec> { new Spec { X = 15, Z = 20 } }, (i, e) => { e.Id = 5; return e; }, null), 0);
            int first = scene.Blips[0].Handle;
            // The same Id now stands 40 m away: nothing within the 0.4 m gate, so the old instance despawns and a new one spawns.
            scene.Accept(State(2, new List<Spec> { new Spec { X = 45, Z = 50 } }, (i, e) => { e.Id = 5; return e; }, null), .05);
            Assert.That(scene.Blips.Count, Is.EqualTo(1));
            Assert.That(scene.Blips[0].Handle, Is.Not.EqualTo(first));
            Assert.That(EventKinds(scene.Events), Is.EqualTo("Despawned,Spawned"));
            Assert.That(scene.Blips[0].PrevX, Is.EqualTo(45f), "a new instance has no previous position to tween from");
        }

        [Test]
        public void TheKillersOwnEntryIsFoundByRoleWhateverItsIdOrItsPlaceInTheList()
        {
            for (int index = 0; index <= 3; index++)
                foreach (int killerId in new[] { 0, 1, 5 })
                {
                    var scene = new KillerScene(new System.Random(40 + index), Terminals);
                    var state = new S2C_WorldState { Tick = 1 };
                    var list = new List<PlayerState>();
                    for (int i = 0; i < 3; i++) list.Add(new PlayerState { Id = killerId, Role = (byte)Role.Survivor, X = 15 + i * 3, Z = 20 }); // Survivors share the Killer's Id on purpose
                    list.Insert(index, new PlayerState { Id = killerId, Role = (byte)Role.Killer, X = 7, Z = 9 });
                    state.Players.AddRange(list);
                    scene.Accept(state, 0);
                    Assert.That(scene.HasKiller, Is.True);
                    Assert.That(scene.KillerX, Is.EqualTo(7f), "index " + index + " id " + killerId);
                    Assert.That(scene.KillerZ, Is.EqualTo(9f));
                    Assert.That(scene.Blips.Count, Is.EqualTo(3), "the Killer is not an instance");
                }
        }

        [Test]
        public void ASnapshotBufferLookupForTheKillerKeyReturnsTheEntryWhoseRoleIsKillerNotTheOneWithTheSameId()
        {
            var buffer = new SnapshotBuffer();
            for (uint tick = 1; tick <= 3; tick++)
            {
                var state = new S2C_WorldState { Tick = tick };
                state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 50, Z = 50, Flags = (byte)PlayerFlags.Signal });
                state.Players.Add(new PlayerState { Id = 0, Role = (byte)Role.Survivor, X = 40, Z = 40 });
                state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 7 + tick, Z = 9 });
                buffer.Add(state, tick * .05);
            }
            Assert.That(buffer.Sample(SnapshotBuffer.KillerKey, 3 * .05 + .1, out Vector3 position, out _, out _), Is.True);
            Assert.That(position.x, Is.EqualTo(10f).Within(.01f), "the Killer's own entry, found by Role");
            Assert.That(position.z, Is.EqualTo(9f).Within(.01f));
        }

        [Test]
        public void EscapedAndEliminatedSurvivorsAreNotInstancesAndLaggingOnesKeepTheirStatus()
        {
            var scene = new KillerScene(new System.Random(51), Terminals);
            var truth = new List<Spec>
            {
                new Spec { X = 15, Z = 20, Status = (byte)PlayerStatus.Escaped },
                new Spec { X = 15, Z = 24, Status = (byte)PlayerStatus.Eliminated },
                new Spec { X = 14, Z = 27, Status = (byte)PlayerStatus.Lagging },
                new Spec { X = 4, Z = 25, Status = (byte)PlayerStatus.Normal },
            };
            scene.Accept(State(1, truth, Zeroed, null), 0);
            Assert.That(scene.Blips.Count, Is.EqualTo(2));
            int lagging = 0;
            foreach (Blip b in scene.Blips) if (b.Status == BlipStatus.Lagging) lagging++;
            Assert.That(lagging, Is.EqualTo(1));
        }

        // ---- 2. code audit ---------------------------------------------------------------------------------------------------

        private static IEnumerable<string> KillerPathFiles()
        {
            foreach (string path in Directory.GetFiles(Path.Combine(QaPaths.ScriptsDir(), "Killer"), "*.cs", SearchOption.TopDirectoryOnly)) yield return path;
            foreach (string relative in new[] { "UI/KillerHud.cs", "UI/MarkerLayer.cs", "UI/SkillRing.cs", "World/ViewPose.cs", "World/MapGeometry.cs", "World/KillerViewRig.cs", "World/SelfState.cs" })
                yield return Path.Combine(QaPaths.ScriptsDir(), relative);
        }

        [Test]
        public void NoKillerPathFileReadsASurvivorIdYawStatusTimerCaptureCountOrAnyBehaviourFlag()
        {
            // Allowed receivers: terminal.Id (a TerminalState, not a player), ViewPose/camera Yaw. Everything else is a violation.
            var allowedIdReceivers = new HashSet<string> { "terminal" };
            var allowedYawReceivers = new HashSet<string> { "pose", "view", "rig", "camera" };
            int files = 0;
            foreach (string path in KillerPathFiles())
            {
                files++;
                string code = QaSource.StripComments(File.ReadAllText(path));
                string name = Path.GetFileName(path);
                foreach (Match m in Regex.Matches(code, @"\b(\w+)\s*\.\s*(Id|StatusTimer|CaptureCount|Flags)\b"))
                    if (!(m.Groups[2].Value == "Id" && allowedIdReceivers.Contains(m.Groups[1].Value)))
                        Assert.Fail(name + ": reads " + m.Value + " (a Survivor entry field the Killer view must never use)");
                foreach (Match m in Regex.Matches(code, @"\b(\w+)\s*\.\s*Yaw\b"))
                    Assert.That(allowedYawReceivers, Does.Contain(m.Groups[1].Value), name + ": reads " + m.Value + " (a Survivor entry's facing)");
                Assert.That(code, Does.Not.Match(@"\bPlayerFlags\b"), name + " names the player flags");
                Assert.That(code, Does.Not.Match(@"\b(Afterimage|Stealthed|Hacking|Rescuing)\b"), name + " names a behaviour flag");
            }
            Assert.That(files, Is.GreaterThanOrEqualTo(14), "the audit walked the Killer folder and the killer UI files");
        }

        [Test]
        public void TheKillerStreamAdapterReadsOnlyRoleStatusAndPositionFromEachEntry()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("Killer/KillerScene.cs"));
            string accept = QaSource.MethodText(code, "public void Accept(");
            Assert.That(accept, Is.Not.Null);
            var fields = new SortedSet<string>();
            foreach (Match m in Regex.Matches(accept, @"\bentry\s*\.\s*(\w+)")) fields.Add(m.Groups[1].Value);
            Assert.That(fields, Is.EquivalentTo(new[] { "Role", "X", "Z", "Status" }), "fields read from a PlayerState in KillerScene.Accept");
        }

        [Test]
        public void TheWorldViewStopsBeforeReadingAnyEntryFieldOnAKillerClient()
        {
            string accept = QaSource.MethodText(QaSource.StripComments(QaPaths.ReadScript("World/WorldView.cs")), "public void Accept(");
            Assert.That(accept, Is.Not.Null);
            int stop = accept.IndexOf("role == Role.Killer) break;", StringComparison.Ordinal);
            Assert.That(stop, Is.GreaterThanOrEqualTo(0), "the Killer client leaves the entry loop before building anything");
            foreach (string read in new[] { "player.Id", "player.Yaw", "player.Status", "player.Flags", "player.X", "player.StatusTimer", "player.CaptureCount" })
            {
                int at = accept.IndexOf(read, StringComparison.Ordinal);
                Assert.That(at < 0 || at > stop, Is.True, read + " is read before the Killer break");
            }
        }

        [Test]
        public void TheClientAppFindsTheKillersOwnEntryByRoleAndLeavesTheSurvivorOnlyReadsBehindTheKillerBreak()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            int guard = code.IndexOf("role == Role.Killer ? player.Role != (byte)Role.Killer : player.Id != playerId", StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThanOrEqualTo(0), "own entry: by Role for the Killer, by Id for a Survivor");
            int stop = code.IndexOf("if (role == Role.Killer) break;", guard, StringComparison.Ordinal);
            Assert.That(stop, Is.GreaterThan(guard));
            foreach (string read in new[] { "player.Flags", "player.StatusTimer", "player.Yaw", "player.CaptureCount" })
            {
                int at = code.IndexOf(read, guard, StringComparison.Ordinal);
                Assert.That(at < 0 || at > stop, Is.True, read + " must sit behind the Killer break");
            }
        }
    }
}
