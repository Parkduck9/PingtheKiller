using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // Q1-playcheck-polish: the 2026-11-05 play check findings 3-6 (elimination copy and capture tag, stacked bodies,
    // contiguous bot names, one terminal-count format).
    public sealed class PlaycheckPolishTests
    {
        // ---- finding 3: the elimination line names only the captures that happened -----------------------------------------

        private const string TwoAndStage = "Disconnected: caught 2 times, no Lagging stage left.";
        private const string Three = "Disconnected: caught 3 times.";
        private const string Unknown = "Disconnected: caught with no Lagging stage left.";

        private static CaptureWatch Feed(params (PlayerStatus status, float timer, byte count)[] steps)
        {
            var watch = new CaptureWatch();
            double now = 0;
            foreach (var step in steps) { watch.Observe(step.status, step.timer, step.count, now); now += 1; }
            return watch;
        }

        [Test]
        public void TwoRealCapturesAndAStageThatRanOut_SayTwoCaptures_NotAThird()
        {
            // The play check: captured at 48 s, the 60 s stage ran out (count 1 -> 2, still Lagging), rescued, captured again.
            CaptureWatch watch = Feed(
                (PlayerStatus.Normal, 0, 0),
                (PlayerStatus.Lagging, 60, 1),
                (PlayerStatus.Lagging, .1f, 1),
                (PlayerStatus.Lagging, 30, 2),
                (PlayerStatus.Lagging, 12, 2),
                (PlayerStatus.Normal, 0, 2),
                (PlayerStatus.Eliminated, 0, 3));
            Assert.That(watch.Reason, Is.EqualTo(EliminationReason.ThirdCapture));
            Assert.That(watch.Captures, Is.EqualTo(1), "one Normal -> Lagging capture before the eliminating one");
            Assert.That(watch.Escalated, Is.True);
            Assert.That(watch.CaughtTimes, Is.EqualTo(2));
            Assert.That(watch.ReasonText(), Is.EqualTo(TwoAndStage));
            Assert.That(watch.ReasonText(), Does.Not.Contain("third"));
        }

        [Test]
        public void ThreeRealCaptures_SayThreeCaptures()
        {
            CaptureWatch watch = Feed(
                (PlayerStatus.Normal, 0, 0),
                (PlayerStatus.Lagging, 60, 1),
                (PlayerStatus.Normal, 0, 1),
                (PlayerStatus.Lagging, 30, 2),
                (PlayerStatus.Normal, 0, 2),
                (PlayerStatus.Eliminated, 0, 3));
            Assert.That(watch.Reason, Is.EqualTo(EliminationReason.ThirdCapture));
            Assert.That(watch.Escalated, Is.False);
            Assert.That(watch.CaughtTimes, Is.EqualTo(3));
            Assert.That(watch.ReasonText(), Is.EqualTo(Three));
        }

        [Test]
        public void AnUnknownHistory_GetsTheCountFreeLine()
        {
            var histories = new Dictionary<string, (PlayerStatus, float, byte)[]>
            {
                ["reconnect into Lagging"] = new[] { (PlayerStatus.Lagging, 20f, (byte)1), (PlayerStatus.Normal, 0f, (byte)1), (PlayerStatus.Lagging, 30f, (byte)2), (PlayerStatus.Normal, 0f, (byte)2), (PlayerStatus.Eliminated, 0f, (byte)3) },
                ["reconnect, Normal with count 2"] = new[] { (PlayerStatus.Normal, 0f, (byte)2), (PlayerStatus.Eliminated, 0f, (byte)3) },
                ["reconnect, Normal with count 1"] = new[] { (PlayerStatus.Normal, 0f, (byte)1), (PlayerStatus.Lagging, 30f, (byte)2), (PlayerStatus.Normal, 0f, (byte)2), (PlayerStatus.Eliminated, 0f, (byte)3) },
                // Whole match, but the count moved without a capture this watch saw (a missed snapshot): no number is claimed.
                ["count disagrees"] = new[] { (PlayerStatus.Normal, 0f, (byte)0), (PlayerStatus.Normal, 0f, (byte)2), (PlayerStatus.Eliminated, 0f, (byte)3) },
            };
            foreach (var history in histories)
            {
                CaptureWatch watch = Feed(history.Value);
                Assert.That(watch.Reason, Is.EqualTo(EliminationReason.ThirdCapture), history.Key);
                Assert.That(watch.CaughtTimes, Is.Null, history.Key);
                Assert.That(watch.ReasonText(), Is.EqualTo(Unknown), history.Key);
            }
            Assert.That(CaptureWatch.ThirdCapture, Is.EqualTo(Unknown));
            Assert.That(CaptureWatch.ReasonText(EliminationReason.ThirdCapture), Is.EqualTo(Unknown), "the static line is the count-free one");
        }

        [Test]
        public void CollapseAndTimeout_AreUnchanged_AndResetForgetsTheHistory()
        {
            CaptureWatch timedOut = Feed((PlayerStatus.Normal, 0, 0), (PlayerStatus.Lagging, 60, 1), (PlayerStatus.Lagging, 30, 2), (PlayerStatus.Lagging, .1f, 2), (PlayerStatus.Eliminated, 0, 2));
            Assert.That(timedOut.Reason, Is.EqualTo(EliminationReason.TimedOut));
            Assert.That(timedOut.ReasonText(), Is.EqualTo("Disconnected: no rescue in the second Lagging stage (30 s)."));
            var collapse = new CaptureWatch();
            collapse.Observe(PlayerStatus.Normal, 0, 0, 0);
            collapse.Observe(PlayerStatus.Lagging, 60, 1, 1);
            collapse.Observe(PlayerStatus.Lagging, 40, 1, 2);
            collapse.Observe(PlayerStatus.Eliminated, 0, 1, 3, collapseRunning: true);
            Assert.That(collapse.Reason, Is.EqualTo(EliminationReason.Collapse));
            Assert.That(collapse.ReasonText(), Is.EqualTo("Disconnected: the shutdown reached you."));
            Assert.That(collapse.CaughtTimes, Is.Null, "only the capture elimination carries a count");

            CaptureWatch reused = Feed((PlayerStatus.Normal, 0, 0), (PlayerStatus.Lagging, 60, 1), (PlayerStatus.Normal, 0, 1), (PlayerStatus.Lagging, 30, 2), (PlayerStatus.Normal, 0, 2), (PlayerStatus.Eliminated, 0, 3));
            reused.Reset();
            Assert.That(reused.Captures, Is.EqualTo(0));
            Assert.That(reused.Escalated, Is.False);
            Assert.That(reused.WholeMatch, Is.False);
            Assert.That(reused.CaughtTimes, Is.Null);
            Assert.That(reused.ReasonText(), Is.Null);
        }

        [Test]
        public void TheEliminationLogLineStillPrints()
        {
            string app = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "ClientApp.cs"));
            Assert.That(app, Does.Contain("Debug.Log(\"[client] eliminated, reason=\" + capture.Reason + \" (\\\"\" + capture.ReasonText() + \"\\\")\");"));
        }

        [Test]
        public void TheEliminationLines_FitTwoReasonLines_Measured()
        {
            Font font = Inter();
            foreach (string line in new[] { TwoAndStage, Three, Unknown, CaptureWatch.Caught(1, false) })
                foreach (float size in new[] { 14f, ClientScreens.SmallSize(.8f) })
                    Assert.That(WrappedLines(font, line, size, 288), Is.LessThanOrEqualTo(2), line + " at " + size + " u in 288 u");
            Assert.That(CaptureWatch.Caught(1, false), Is.EqualTo("Disconnected: caught 1 time."));
            Assert.That(CaptureWatch.Caught(null, true), Is.EqualTo(Unknown));
            foreach (string line in new[] { TwoAndStage, Three, Unknown })
            {
                Assert.That(line, Does.Not.Contain("!"));
                Assert.That(line, Does.Not.Contain("Executed"));
            }
        }

        // ---- finding 3: the meter's capture tag ----------------------------------------------------------------------------

        [Test]
        public void TheCaptureTag_IsTrueForAnyHistory()
        {
            MeterState once = SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Normal, CaptureCount = 1 }, null, 0, 0, null);
            Assert.That(once.Tag, Is.EqualTo("Captured once"));
            Assert.That(once.TagMuted, Is.True);
            // Count 2 after one capture and a stage that ran out, or after two captures: either way no stage is left.
            MeterState afterStage = SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Normal, CaptureCount = 2 }, null, 0, 0, null);
            Assert.That(afterStage.Tag, Is.EqualTo("Next capture eliminates"));
            Assert.That(afterStage.TagMuted, Is.True);
            Assert.That(afterStage.Tag, Does.Not.Contain("twice"));
            Assert.That(SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Normal, Signal = true, CaptureCount = 2 }, null, 0, 0, null).Tag, Is.EqualTo("Next capture eliminates"));
            Assert.That(SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Normal }, null, 0, 0, null).Tag, Is.Null);
            Assert.That(SurvivorReadout.Meter(new OwnEntry { Status = PlayerStatus.Lagging, StatusTimer = 20, CaptureCount = 2 }, null, 0, 0, null).Tag, Is.Null, "only while Normal");
            Assert.That(SurvivorReadout.CaptureTag(0), Is.Null);
            Assert.That(SurvivorReadout.CaptureTag(1), Is.EqualTo(SurvivorReadout.CapturedOnce));
            Assert.That(SurvivorReadout.CaptureTag(2), Is.EqualTo(SurvivorReadout.NextCaptureEliminates));
            Assert.That(SurvivorReadout.CaptureTag(255), Is.EqualTo(SurvivorReadout.NextCaptureEliminates));
        }

        [Test]
        public void TheCaptureTag_FitsRowANextToTheStateWord_Measured()
        {
            // HUD_SPEC 4.1 row A: 288 u inner width, the state word at M 600 left, the tag at S right. Only Signal or Silent
            // share the row with a capture tag (the tag shows only while Normal). At least 8 u stay between them.
            Font font = Inter();
            float word = Mathf.Max(Measure(font, "Signal", 18, true), Measure(font, "Silent", 18, true));
            foreach (float size in new[] { 14f, ClientScreens.SmallSize(.8f) })
                foreach (string tag in new[] { SurvivorReadout.CapturedOnce, SurvivorReadout.NextCaptureEliminates })
                {
                    float width = Measure(font, tag, size, false);
                    Assert.That(word + 8 + width, Is.LessThanOrEqualTo(288f), tag + " at " + size + " u: " + width + " u");
                }
        }

        // ---- finding 5: contiguous bot names ------------------------------------------------------------------------------

        private static Slot Human(int id, string name, bool host = false) => new Slot { PlayerId = id, Nickname = name, IsHost = (byte)(host ? 1 : 0) };
        private static Slot Bot(int id, string name = null) => new Slot { PlayerId = id, Nickname = name ?? "Bot " + id, IsBot = 1 };

        private static S2C_LobbyState Lobby(params Slot[] occupied)
        {
            var state = new S2C_LobbyState { IsOffline = 1, HostRoleChoice = 2 };
            for (int i = 0; i < occupied.Length; i++) state.Slots[i] = occupied[i];
            return state;
        }

        private static string NameOf(S2C_LobbyState state, int id) => V2Wire.Nickname(state, id);

        [Test]
        public void BotNamesAreContiguous_WhereverThePlayerSits()
        {
            // Player in slot 1 (id 1): the server already says Bot 2..8; they become Bot 1..7.
            S2C_LobbyState first = LobbyCopy.ContiguousBotNames(Lobby(Human(1, "Player", true), Bot(2), Bot(3), Bot(4), Bot(5), Bot(6), Bot(7), Bot(8)));
            for (int id = 2; id <= 8; id++) Assert.That(NameOf(first, id), Is.EqualTo("Bot " + (id - 1)), "slot 1, id " + id);
            // The play check: the player has id 4, so the server's list read Bot 1, 2, 3, 5, 6, 7, 8.
            S2C_LobbyState fourth = LobbyCopy.ContiguousBotNames(Lobby(Bot(1), Bot(2), Bot(3), Human(4, "Player", true), Bot(5), Bot(6), Bot(7), Bot(8)));
            var expected = new Dictionary<int, string> { [1] = "Bot 1", [2] = "Bot 2", [3] = "Bot 3", [4] = "Player", [5] = "Bot 4", [6] = "Bot 5", [7] = "Bot 6", [8] = "Bot 7" };
            foreach (var pair in expected) Assert.That(NameOf(fourth, pair.Key), Is.EqualTo(pair.Value), "slot 4, id " + pair.Key);
            Assert.That(LobbyCopy.SlotText(fourth.Slots[4], 4), Is.EqualTo("Bot 4 · bot"));
            // Player last (id 8): Bot 1..7 unchanged.
            S2C_LobbyState last = LobbyCopy.ContiguousBotNames(Lobby(Bot(1), Bot(2), Bot(3), Bot(4), Bot(5), Bot(6), Bot(7), Human(8, "Player", true)));
            for (int id = 1; id <= 7; id++) Assert.That(NameOf(last, id), Is.EqualTo("Bot " + id));
            Assert.That(NameOf(last, 8), Is.EqualTo("Player"));
        }

        [Test]
        public void TwoHumans_AndAHumanTurnedBotKeepingItsName_AndNoCollisions()
        {
            S2C_LobbyState two = LobbyCopy.ContiguousBotNames(Lobby(Human(1, "Host", true), Bot(2), Human(3, "Guest"), Bot(4), Bot(5)));
            Assert.That(new[] { NameOf(two, 2), NameOf(two, 4), NameOf(two, 5) }, Is.EqualTo(new[] { "Bot 1", "Bot 2", "Bot 3" }));
            Assert.That(NameOf(two, 1), Is.EqualTo("Host"));
            Assert.That(NameOf(two, 3), Is.EqualTo("Guest"));

            // A human who left and became a bot keeps their name.
            S2C_LobbyState left = LobbyCopy.ContiguousBotNames(Lobby(Human(1, "Host", true), Bot(2, "Alice"), Bot(3), Bot(4)));
            Assert.That(NameOf(left, 2), Is.EqualTo("Alice"));
            Assert.That(NameOf(left, 3), Is.EqualTo("Bot 1"));
            Assert.That(NameOf(left, 4), Is.EqualTo("Bot 2"));

            // Kept names that look like bot names are never reused: a human-turned-bot "Bot 1" (id 5) and a human "Bot 2".
            S2C_LobbyState clash = LobbyCopy.ContiguousBotNames(Lobby(Human(1, "Bot 2", true), Bot(2), Bot(3), Bot(5, "Bot 1"), Bot(6)));
            Assert.That(NameOf(clash, 5), Is.EqualTo("Bot 1"), "kept");
            Assert.That(NameOf(clash, 1), Is.EqualTo("Bot 2"), "kept");
            Assert.That(new[] { NameOf(clash, 2), NameOf(clash, 3), NameOf(clash, 6) }, Is.EqualTo(new[] { "Bot 3", "Bot 4", "Bot 5" }));
            var names = new HashSet<string>();
            foreach (Slot slot in clash.Slots) if (slot.PlayerId != 0) Assert.That(names.Add(slot.Nickname), Is.True, "duplicate " + slot.Nickname);
        }

        [Test]
        public void EmptySlotsNullAndTheReceivedStateAreLeftAlone_AndTheResultIsDeterministic()
        {
            Assert.That(LobbyCopy.ContiguousBotNames(null), Is.Null);
            var noSlots = new S2C_LobbyState { Slots = null };
            Assert.That(LobbyCopy.ContiguousBotNames(noSlots).Slots, Is.Null);

            S2C_LobbyState received = Lobby(Human(2, "Player", true), Bot(3), Bot(5));
            received.Slots[5] = null;
            S2C_LobbyState shown = LobbyCopy.ContiguousBotNames(received);
            Assert.That(shown, Is.Not.SameAs(received));
            Assert.That(shown.IsOffline, Is.EqualTo(1));
            Assert.That(shown.HostRoleChoice, Is.EqualTo(2));
            Assert.That(shown.Slots.Length, Is.EqualTo(received.Slots.Length));
            Assert.That(NameOf(shown, 3), Is.EqualTo("Bot 1"));
            Assert.That(NameOf(shown, 5), Is.EqualTo("Bot 2"));
            Assert.That(received.Slots[1].Nickname, Is.EqualTo("Bot 3"), "the received state is not changed");
            Assert.That(shown.Slots[1], Is.Not.SameAs(received.Slots[1]));
            Assert.That(shown.Slots[3].PlayerId, Is.EqualTo(0), "an empty slot stays empty");
            Assert.That(LobbyCopy.SlotText(shown.Slots[3], 2), Is.EqualTo("Empty"));
            Assert.That(shown.Slots[5], Is.Null);

            // Same state in another slot order: the same name for every id.
            S2C_LobbyState shuffled = LobbyCopy.ContiguousBotNames(Lobby(Bot(5), Human(2, "Player", true), Bot(3)));
            Assert.That(NameOf(shuffled, 3), Is.EqualTo("Bot 1"));
            Assert.That(NameOf(shuffled, 5), Is.EqualTo("Bot 2"));
        }

        [Test]
        public void TheAppRenamesOnceWhereTheLobbyStateArrives()
        {
            string app = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "ClientApp.cs"));
            int arrive = app.IndexOf("case S2C_LobbyState state:", System.StringComparison.Ordinal);
            int rename = app.IndexOf("state = LobbyCopy.ContiguousBotNames(state);", System.StringComparison.Ordinal);
            int store = app.IndexOf("lobby = state;", System.StringComparison.Ordinal);
            Assert.That(arrive, Is.GreaterThan(0));
            Assert.That(rename, Is.GreaterThan(arrive));
            Assert.That(store, Is.GreaterThan(rename), "renamed before it is stored, shown or forwarded");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(app, @"ContiguousBotNames\(").Count, Is.EqualTo(1));
        }

        // ---- finding 4: bodies do not stand inside each other -------------------------------------------------------------

        private static Vector2[] Resolve(params SeparationBody[] bodies)
        {
            var offsets = new Vector2[bodies.Length];
            VisualSeparation.Resolve(bodies, VisualSeparation.MinDistance, offsets);
            return offsets;
        }

        private static float Gap(SeparationBody a, Vector2 oa, SeparationBody b, Vector2 ob)
            => (new Vector2(a.X, a.Z) + oa - (new Vector2(b.X, b.Z) + ob)).magnitude;

        [Test]
        public void CoincidentBodiesSeparateDeterministically()
        {
            Assert.That(VisualSeparation.MinDistance, Is.EqualTo(CharacterLook.Width), "about one body width");
            var a = new SeparationBody(3, 5, 5, false);
            var b = new SeparationBody(5, 5, 5, false);
            Vector2[] ab = Resolve(a, b), ba = Resolve(b, a);
            Assert.That(Gap(a, ab[0], b, ab[1]), Is.EqualTo(VisualSeparation.MinDistance).Within(1e-4f));
            Assert.That(ab[0].x, Is.LessThan(0), "the lower id goes to -x");
            Assert.That(ab[0], Is.EqualTo(ba[1]), "input order does not matter");
            Assert.That(ab[1], Is.EqualTo(ba[0]));
            Assert.That(ab[0].magnitude, Is.EqualTo(ab[1].magnitude).Within(1e-5f), "symmetric");

            // Three at one point (a rescue at a terminal) end up apart as well.
            var c = new SeparationBody(9, 5, 5, false);
            Vector2[] abc = Resolve(a, b, c);
            Assert.That(Gap(a, abc[0], b, abc[1]), Is.GreaterThanOrEqualTo(VisualSeparation.MinDistance - 1e-3f));
            Assert.That(Gap(b, abc[1], c, abc[2]), Is.GreaterThanOrEqualTo(VisualSeparation.MinDistance - 1e-3f));
            Assert.That(Gap(a, abc[0], c, abc[2]), Is.GreaterThanOrEqualTo(VisualSeparation.MinDistance - 1e-3f));
        }

        [Test]
        public void APairAtOrBeyondTheMinimumIsUntouched()
        {
            foreach (float d in new[] { VisualSeparation.MinDistance, VisualSeparation.MinDistance + .01f, 3f })
            {
                Vector2[] offsets = Resolve(new SeparationBody(1, 0, 0, false), new SeparationBody(2, d, 0, false));
                Assert.That(offsets[0], Is.EqualTo(Vector2.zero), "d " + d);
                Assert.That(offsets[1], Is.EqualTo(Vector2.zero), "d " + d);
            }
            Vector2[] pinned = Resolve(new SeparationBody(1, 0, 0, true), new SeparationBody(2, .1f, 0, true));
            Assert.That(pinned[0], Is.EqualTo(Vector2.zero), "two pinned bodies stay");
            Assert.That(pinned[1], Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void TheOwnAvatarMovesLessThanTheOther()
        {
            var own = new SeparationBody(4, 10, 10, true);
            var mate = new SeparationBody(2, 10.3f, 10, false);
            Vector2[] offsets = Resolve(own, mate);
            Assert.That(offsets[0].magnitude, Is.LessThan(offsets[1].magnitude));
            Assert.That(offsets[0], Is.EqualTo(Vector2.zero), "pinned: the camera's character does not move");
            Assert.That(Gap(own, offsets[0], mate, offsets[1]), Is.EqualTo(VisualSeparation.MinDistance).Within(1e-4f));
            Assert.That(offsets[1].x, Is.GreaterThan(0), "pushed away from the own avatar");
            Vector2[] coincident = Resolve(new SeparationBody(9, 0, 0, true), new SeparationBody(1, 0, 0, false));
            Assert.That(coincident[0], Is.EqualTo(Vector2.zero));
            Assert.That(coincident[1].magnitude, Is.EqualTo(VisualSeparation.MinDistance).Within(1e-4f));
        }

        [Test]
        public void OffsetsAreBounded()
        {
            var crowd = new SeparationBody[9];
            for (int i = 0; i < crowd.Length; i++) crowd[i] = new SeparationBody(i + 1, 3, 3, i == 4);
            Vector2[] offsets = Resolve(crowd);
            foreach (Vector2 offset in offsets)
            {
                Assert.That(offset.magnitude, Is.LessThanOrEqualTo(VisualSeparation.MinDistance + 1e-5f));
                Assert.That(float.IsNaN(offset.x) || float.IsNaN(offset.y), Is.False);
            }
            Assert.That(offsets[4], Is.EqualTo(Vector2.zero), "the pinned one stays");
            Vector2[] nan = Resolve(new SeparationBody(1, float.NaN, 0, false), new SeparationBody(2, 0, 0, false));
            Assert.That(nan[0], Is.EqualTo(Vector2.zero));
            Assert.That(nan[1], Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void OffsetsChangeContinuously_AndTheEaseNeverPops()
        {
            var a = new SeparationBody(1, 0, 0, false);
            var b = new SeparationBody(2, .45f, .1f, false);
            var c = new SeparationBody(3, .2f, .6f, true);
            Vector2[] before = Resolve(a, b, c);
            const float step = 1e-3f;
            Vector2[] after = Resolve(a, new SeparationBody(2, .45f + step, .1f, false), c);
            for (int i = 0; i < 3; i++)
                Assert.That((after[i] - before[i]).magnitude, Is.LessThanOrEqualTo(10 * step), "body " + i);

            Vector2 target = new Vector2(.4f, 0);
            Assert.That(VisualSeparation.Ease(Vector2.zero, target, 0), Is.EqualTo(Vector2.zero), "no time, no move");
            Assert.That(VisualSeparation.Ease(Vector2.zero, target, float.NaN), Is.EqualTo(Vector2.zero));
            Vector2 frame = VisualSeparation.Ease(Vector2.zero, target, 1f / 60);
            Assert.That(frame.x, Is.GreaterThan(0).And.LessThan(.1f), "one 60 fps frame moves a fraction of the way");
            Assert.That((VisualSeparation.Ease(Vector2.zero, target, 5) - target).magnitude, Is.LessThan(1e-4f), "a long gap reaches the target");
            Vector2 current = new Vector2(-.3f, .2f);
            Vector2 eased = VisualSeparation.Ease(current, target, .05f);
            Assert.That(eased.magnitude, Is.LessThanOrEqualTo(Mathf.Max(current.magnitude, target.magnitude) + 1e-6f), "bounded");
        }

        [Test]
        public void TheWorldDrawsStackedSurvivorsApart_WithoutTouchingTheSnapshots()
        {
            var root = new GameObject("separation views");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, null);
            try
            {
                characters.Initialize();
                var state = new S2C_WorldState { Tick = 1 };
                state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 2, Z = 2 });
                state.Players.Add(new PlayerState { Id = 2, Role = (byte)Role.Survivor, X = 2.1f, Z = 2 });
                characters.Accept(state);
                var snapshots = new SnapshotBuffer(); snapshots.Add(state, 0);
                characters.Render(.15, snapshots);
                Transform own = root.transform.Find("Player 1"), mate = root.transform.Find("Player 2");
                Assert.That(mate.position.x, Is.EqualTo(2.1f).Within(1e-4f), "the first frame draws where the entry is: no pop");
                float previous = mate.position.x;
                for (int frame = 1; frame <= 60; frame++)
                {
                    characters.Render(.15 + frame / 60.0, snapshots);
                    Assert.That(mate.position.x - previous, Is.LessThan(.2f), "frame " + frame + ": eased, no jump");
                    previous = mate.position.x;
                    Assert.That(own.position, Is.EqualTo(new Vector3(2, 0, 2)), "the own avatar never moves");
                }
                Assert.That(mate.position.x - own.position.x, Is.EqualTo(VisualSeparation.MinDistance).Within(.01f));
                Assert.That(mate.position.z, Is.EqualTo(2).Within(1e-4f));
                Assert.That(snapshots.Sample(2, 1.2, out Vector3 sampled, out _, out _), Is.True);
                Assert.That(sampled.x, Is.EqualTo(2.1f).Within(1e-5f), "the snapshot keeps the authoritative position");
                Assert.That(state.Players[1].X, Is.EqualTo(2.1f));
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        // ---- finding 6: one terminal-count format -------------------------------------------------------------------------

        [Test]
        public void BothRolesReadTerminalsOneOfThreeTheSameWay()
        {
            Assert.That(KillerHudCopy.Terminals(1, 3), Is.EqualTo("Terminals 1/3"));
            Assert.That(HudCopy.Terminals(1, 3), Is.EqualTo("Terminals 1/3"));
            Assert.That(SurvivorReadout.TerminalsLine(1, 3), Is.EqualTo("Terminals 1/3"));
            Assert.That(HudCopy.TerminalsLine(new[] { new TerminalState { Progress = 100 }, new TerminalState { Progress = 40 } }, 3), Is.EqualTo("Terminals 1/3"));
            for (int needed = 1; needed <= 5; needed++)
                for (int done = 0; done <= needed; done++)
                    Assert.That(KillerHudCopy.Terminals(done, needed), Is.EqualTo(SurvivorReadout.TerminalsLine(done, needed)));
            string spec = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "ui", "HUD_SPEC.md")));
            Assert.That(spec, Does.Contain("line 2 `Terminals 1/3`"), "HUD_SPEC 6.1a");
            Assert.That(spec, Does.Not.Contain("Terminals 1 of 3"));
        }

        // ---- helpers ------------------------------------------------------------------------------------------------------

        // TextCore's synthesized bold (Inter ships only Regular): 7/100 em extra advance per glyph (as in SurvivorFeedbackTests).
        private const float BoldSpacing = .07f;

        private static Font Inter()
        {
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            Assert.That(font, Is.Not.Null);
            Assert.That(Measure(font, "Emergency", 14, false), Is.InRange(72f, 78f), "measurement sanity");
            return font;
        }

        private static float Measure(Font font, string text, float size, bool bold)
        {
            const int Probe = 200;
            font.RequestCharactersInTexture(text, Probe, FontStyle.Normal);
            float units = 0;
            foreach (char c in text)
            {
                Assert.That(font.GetCharacterInfo(c, out CharacterInfo info, Probe, FontStyle.Normal), Is.True, "glyph '" + c + "'");
                units += info.advance;
            }
            return units * size / Probe + (bold ? BoldSpacing * size * text.Length : 0);
        }

        private static int WrappedLines(Font font, string text, float size, float width)
        {
            int lines = 1; float line = 0, space = Measure(font, " ", size, false);
            foreach (string word in text.Split(' '))
            {
                float w = Measure(font, word, size, false);
                Assert.That(w, Is.LessThanOrEqualTo(width), word);
                if (line > 0 && line + space + w > width) { lines++; line = w; } else line += (line > 0 ? space : 0) + w;
            }
            return lines;
        }
    }
}
