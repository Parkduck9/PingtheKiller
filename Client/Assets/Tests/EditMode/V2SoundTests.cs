using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Audio;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // Rules v2 event cues: the procedural clips, the edge rules that pick them, and the volume path.
    public sealed class V2SoundTests
    {
        private const int Rate = KillerAudio.Rate;
        private const int Own = 1;

        // ---- synth ------------------------------------------------------------------------------------------------------

        [Test]
        public void EveryCueRendersAudibleBoundedClickFreeDeterministicAndAtItsFixedLength()
        {
            foreach (V2Cue cue in Enum.GetValues(typeof(V2Cue)))
            {
                float[] s = V2CueSynth.Build(cue, Rate);
                float seconds = V2CueSynth.Seconds(cue);
                Assert.That(seconds, Is.InRange(.08f, 1.2f), cue + " length");
                Assert.That(s.Length, Is.EqualTo((int)(Rate * seconds)), cue + " fixed length");
                Assert.That(s[0] == 0f, Is.True, cue + " first sample");
                Assert.That(s[s.Length - 1] == 0f, Is.True, cue + " last sample");
                Assert.That(Peak(s), Is.LessThanOrEqualTo(.95f), cue + " peak");
                Assert.That(Rms(s), Is.GreaterThan(.01f), cue + " is audible");
                Assert.That(V2CueSynth.Build(cue, Rate), Is.EqualTo(s), cue + " is deterministic");
            }
        }

        [Test]
        public void TheHoldLoopWrapsWithoutAStepAndItsPitchRisesWithProgress()
        {
            float[] s = V2CueSynth.BuildHold(Rate);
            Assert.That(s.Length, Is.EqualTo((int)(Rate * V2CueSynth.HoldSeconds)));
            float maxStep = 0;
            for (int i = 1; i < s.Length; i++) maxStep = Math.Max(maxStep, Math.Abs(s[i] - s[i - 1]));
            float wrap = Math.Abs(s[0] - s[s.Length - 1]);
            Assert.That(wrap, Is.LessThanOrEqualTo(maxStep * 1.05f), "the wrap step is no bigger than any step inside the loop");
            Assert.That(wrap, Is.LessThan(.03f));
            Assert.That(Peak(s), Is.LessThanOrEqualTo(.95f));
            Assert.That(Rms(s), Is.GreaterThan(.01f));
            Assert.That(V2CueSynth.BuildHold(Rate), Is.EqualTo(s));
            foreach (ActionKind kind in new[] { ActionKind.Switch, ActionKind.Kick, ActionKind.HatchClose })
            {
                Assert.That(V2CueSynth.HoldPitch(kind, 1), Is.GreaterThan(V2CueSynth.HoldPitch(kind, .5f)), kind.ToString());
                Assert.That(V2CueSynth.HoldPitch(kind, .5f), Is.GreaterThan(V2CueSynth.HoldPitch(kind, 0)), kind.ToString());
                Assert.That(V2CueSynth.HoldPitch(kind, float.NaN), Is.EqualTo(V2CueSynth.HoldPitch(kind, 0)));
                Assert.That(V2CueSynth.HoldPitch(kind, 5), Is.EqualTo(V2CueSynth.HoldPitch(kind, 1)));
            }
            Assert.That(V2CueSynth.HoldPitch(ActionKind.Kick, 0), Is.LessThan(V2CueSynth.HoldPitch(ActionKind.Switch, 0)));
            Assert.That(V2CueSynth.HoldPitch(ActionKind.HatchClose, 0), Is.LessThan(V2CueSynth.HoldPitch(ActionKind.Kick, 0)));
        }

        // ---- edge rules -------------------------------------------------------------------------------------------------

        [Test]
        public void TheBaselineSnapshotTriggersNothingEvenWhenEveryConditionIsAlreadyTrue()
        {
            var rules = Survivor();
            var late = World(powered: true, open0: true, collapse: 20, hatch: true, regressing2: true, players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Lagging)));
            Assert.That(Step(rules, late), Is.Empty);
            for (int i = 0; i < 5; i++) Assert.That(Step(rules, late), Is.Empty);
            // The hatch was already seen at the baseline: a later re-sighting is silent.
            Assert.That(Step(rules, World(powered: true, open0: true, collapse: 19, regressing2: true, players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Lagging)))), Is.Empty);
            Assert.That(Step(rules, late), Is.Empty);
        }

        [Test]
        public void EachEdgeTriggersExactlyItsCueOnceAndRepeatedSnapshotsTriggerNothingMore()
        {
            var rules = Survivor();
            var normal = P((Own, PlayerStatus.Normal), (2, PlayerStatus.Normal), (3, PlayerStatus.Normal));
            Assert.That(Step(rules, World(players: normal)), Is.Empty);

            AssertOnce(rules, World(powered: true, players: normal), V2Cue.ExitsPowered);
            AssertOnce(rules, World(powered: true, hatch: true, players: normal), V2Cue.HatchOpen);
            AssertOnce(rules, World(powered: true, hatch: true, regressing2: true, players: normal), V2Cue.TerminalKick);
            AssertOnce(rules, World(powered: true, hatch: true, players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Lagging), (3, PlayerStatus.Normal))), V2Cue.Capture);
            AssertOnce(rules, World(powered: true, hatch: true, players: normal), V2Cue.Rescue);
            // The second kick on the same terminal is a new rising edge.
            AssertOnce(rules, World(powered: true, hatch: true, regressing2: true, players: normal), V2Cue.TerminalKick);

            // An exit opens: the collapse starts in the same snapshot, and that is not a hatch close.
            var opened = World(powered: true, open0: true, collapse: 30, hatch: true, players: normal);
            List<V2Cue> cues = Step(rules, opened);
            Assert.That(cues, Is.EquivalentTo(new[] { V2Cue.ExitOpen, V2Cue.CollapseStart }));
            for (int i = 0; i < 4; i++) Assert.That(Step(rules, opened), Is.Empty);
            // The second exit opening is its own edge; the collapse already runs.
            AssertOnce(rules, World(powered: true, open0: true, open1: true, collapse: 29, hatch: true, players: normal), V2Cue.ExitOpen);
        }

        [Test]
        public void TheOwnCaptureGivesNoCaptureCueButTheOwnRescueGivesTheRescueCue()
        {
            var rules = Survivor();
            Step(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Normal))));
            Assert.That(Step(rules, World(players: P((Own, PlayerStatus.Lagging), (2, PlayerStatus.Normal)))), Is.Empty);
            AssertOnce(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Normal))), V2Cue.Rescue);
        }

        [Test]
        public void TwoCapturesInOneSnapshotGiveOneCueAndAnEntryThatReturnsIsNoStatusEdge()
        {
            var rules = Survivor();
            Step(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Normal), (3, PlayerStatus.Normal))));
            Assert.That(Step(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Lagging), (3, PlayerStatus.Lagging)))),
                Is.EqualTo(new[] { V2Cue.Capture }));
            // 3 leaves the snapshot and comes back Normal: no rescue; 2 is rescued in that snapshot: one Rescue.
            Assert.That(Step(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Lagging)))), Is.Empty);
            Assert.That(Step(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Normal), (3, PlayerStatus.Normal)))),
                Is.EqualTo(new[] { V2Cue.Rescue }));
            Assert.That(Step(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Normal)))), Is.Empty);
            Assert.That(Step(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Normal), (3, PlayerStatus.Lagging)))), Is.Empty);
        }

        [Test]
        public void AHatchLeavingSightRangeNeverGivesHatchClose()
        {
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
            {
                var rules = new V2CueRules();
                rules.Begin(role, Own);
                Step(rules, World(), 0);
                Assert.That(Step(rules, World(hatch: true), 1), Is.EqualTo(new[] { V2Cue.HatchOpen }), role.ToString());
                for (int i = 0; i < 6; i++) Assert.That(Step(rules, World(hatch: i % 2 == 1), 2 + i), Is.Empty, role.ToString());
                // A Killer hold released halfway, then walking out of sight: still no close.
                rules.OnSelf((byte)ActionKind.HatchClose, .1f, 10);
                rules.OnSelf((byte)ActionKind.None, 0, 10.4);
                Assert.That(Step(rules, World(), 10.45), Is.Empty, role.ToString());
            }
        }

        [Test]
        public void TheCollapseStartingWithNoExitOpenIsTheHatchClosingOnBothRolesOncePerMatch()
        {
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
            {
                var rules = new V2CueRules();
                rules.Begin(role, Own);
                Step(rules, World(), 0);
                Assert.That(Step(rules, World(powered: true, collapse: 30), 1),
                    Is.EquivalentTo(new[] { V2Cue.ExitsPowered, V2Cue.CollapseStart, V2Cue.HatchClose }), role.ToString());
                Assert.That(Step(rules, World(powered: true, collapse: 29), 2), Is.Empty, role.ToString());
            }
        }

        [Test]
        public void TheKillersCompletedHatchHoldWithTheHatchGoneIsHatchCloseInEitherPacketOrder()
        {
            foreach (bool selfFirst in new[] { true, false })
            {
                var rules = Killer();
                // The collapse already runs (an exit opened earlier), so only the Killer's own hold can tell the close.
                Step(rules, World(powered: true, open0: true, collapse: 20), 0);
                Step(rules, World(powered: true, open0: true, collapse: 19, hatch: true), 1);
                rules.OnSelf((byte)ActionKind.HatchClose, .05f, 10);
                Assert.That(rules.HoldKind, Is.EqualTo(ActionKind.HatchClose));
                List<V2Cue> cues;
                if (selfFirst)
                {
                    rules.OnSelf((byte)ActionKind.None, 0, 10.95);
                    Assert.That(Take(rules), Is.Empty, "the hatch is still in the snapshot");
                    cues = Step(rules, World(powered: true, open0: true, collapse: 10), 11);
                }
                else
                {
                    Assert.That(Step(rules, World(powered: true, open0: true, collapse: 10), 10.95), Is.Empty);
                    rules.OnSelf((byte)ActionKind.None, 0, 11);
                    cues = Take(rules);
                }
                Assert.That(cues, Is.EqualTo(new[] { V2Cue.HatchClose }), "self first: " + selfFirst);
                Assert.That(rules.HoldKind, Is.EqualTo(ActionKind.None));
            }
        }

        [Test]
        public void TheKillersCompletedKickIsTerminalKickAndAnEarlyReleaseIsSilent()
        {
            var rules = Killer();
            Step(rules, World(), 0);
            rules.OnSelf((byte)ActionKind.Kick, .025f, 1);
            Assert.That(rules.HoldProgress(2), Is.EqualTo(.025f + 1 / GameConstants.KickHoldSeconds).Within(1e-4));
            rules.OnSelf((byte)ActionKind.None, 0, 2.95);
            Assert.That(Take(rules), Is.EqualTo(new[] { V2Cue.TerminalKick }));
            rules.OnSelf((byte)ActionKind.Kick, .025f, 5);
            rules.OnSelf((byte)ActionKind.None, 0, 6);
            Assert.That(Take(rules), Is.Empty, "released at about half");
        }

        [Test]
        public void TheKillerClientNeverGetsCaptureRescueExitOpenOrRouteCues()
        {
            var rules = Killer();
            var normal = P((Own, PlayerStatus.Normal), (2, PlayerStatus.Normal));
            Step(rules, World(players: normal), 0);
            List<V2Cue> all = new List<V2Cue>();
            all.AddRange(Step(rules, World(players: P((Own, PlayerStatus.Normal), (2, PlayerStatus.Lagging))), 1));
            all.AddRange(Step(rules, World(players: normal), 2));
            all.AddRange(Step(rules, World(powered: true, open0: true, collapse: 30, players: normal), 3));
            rules.OnRoute(V2Cue.RouteDone);
            rules.OnRoute(V2Cue.RouteError);
            all.AddRange(Take(rules));
            Assert.That(all, Is.EquivalentTo(new[] { V2Cue.ExitsPowered, V2Cue.CollapseStart }));
        }

        [Test]
        public void TheSurvivorsSwitchHoldIsTrackedWithoutAnyCueAndEndsWhenTheActionEnds()
        {
            var rules = Survivor();
            Step(rules, World(), 0);
            rules.OnSelf((byte)ActionKind.Switch, .2f, 1);
            Assert.That(rules.HoldKind, Is.EqualTo(ActionKind.Switch));
            Assert.That(rules.HoldProgress(1.5), Is.EqualTo(.2f + .5f / GameConstants.ExitSwitchSeconds).Within(1e-4));
            Assert.That(rules.HoldProgress(100), Is.EqualTo(1f));
            rules.OnSelf((byte)ActionKind.None, 0, 1 + GameConstants.ExitSwitchSeconds);
            Assert.That(rules.HoldKind, Is.EqualTo(ActionKind.None));
            Assert.That(rules.HoldProgress(9), Is.EqualTo(0f));
            Assert.That(Take(rules), Is.Empty);
            rules.OnSelf(200, float.NaN, 9);
            Assert.That(rules.HoldKind, Is.EqualTo(ActionKind.None), "an undefined kind reads as no action");
        }

        [Test]
        public void NothingIsQueuedOutsideAMatchAndBeginStartsAFreshBaseline()
        {
            var rules = new V2CueRules();
            rules.OnRoute(V2Cue.RouteKey);
            Assert.That(Step(rules, World(powered: true, collapse: 3, hatch: true)), Is.Empty);
            rules.Begin(Role.Survivor, Own);
            Step(rules, World());
            Assert.That(Step(rules, World(powered: true)), Is.EqualTo(new[] { V2Cue.ExitsPowered }));
            rules.End();
            Assert.That(Step(rules, World(powered: true, collapse: 3)), Is.Empty);
            rules.Begin(Role.Survivor, Own);
            Assert.That(Step(rules, World(powered: true)), Is.Empty, "a new match: baseline again");
            Assert.That(Step(rules, World(powered: true, hatch: true)), Is.EqualTo(new[] { V2Cue.HatchOpen }), "the hatch is new per match");
        }

        [Test]
        public void RouteKeysTickCompletionChimesAndAnErrorGlitchesThroughTheTrackersEvents()
        {
            var tracker = new RouteTracker();
            var rules = Survivor();
            // The ClientApp wiring: a right key ticks, a completed route chimes, an error glitches (no tick for the wrong key).
            tracker.Key += right => { if (right) rules.OnRoute(V2Cue.RouteKey); };
            tracker.RouteDone += () => rules.OnRoute(V2Cue.RouteDone);
            tracker.RouteFailed += () => rules.OnRoute(V2Cue.RouteError);
            tracker.OnSelf(1, HackRoute.MaxLength, 0, 0, 0); // Up Up Up Up
            for (int i = 0; i < 3; i++) tracker.TryPress(RouteDirection.Up, i * .2, out _);
            Assert.That(Take(rules), Is.EqualTo(new[] { V2Cue.RouteKey, V2Cue.RouteKey, V2Cue.RouteKey }));
            tracker.TryPress(RouteDirection.Up, .6, out _);
            Assert.That(Take(rules), Is.EqualTo(new[] { V2Cue.RouteKey, V2Cue.RouteDone }));
            Assert.That(tracker.TryPress(RouteDirection.Up, .7, out _), Is.False);
            Assert.That(Take(rules), Is.Empty, "nothing for a finished route");
            tracker.OnSelf(2, HackRoute.MaxLength, 0, 0, 3);
            tracker.TryPress(RouteDirection.Left, 3.1, out _);
            Assert.That(Take(rules), Is.EqualTo(new[] { V2Cue.RouteError }));
            rules.OnRoute(V2Cue.ExitOpen);
            Assert.That(Take(rules), Is.Empty, "OnRoute takes only the three route cues");
        }

        // ---- the MonoBehaviour: volume path and hold loop ---------------------------------------------------------------

        [Test]
        public void TheSourcesFollowTheListenerVolumeIn2DAndTheClipsAreNotScaledBySettingsVolume()
        {
            float before = AudioListener.volume;
            var objects = new List<GameObject>();
            try
            {
                var samples = new List<float[]>();
                foreach (float volume in new[] { .3f, 1f })
                {
                    AudioListener.volume = volume;
                    var host = new GameObject("v2 audio test");
                    objects.Add(host);
                    var audio = host.AddComponent<RulesV2Audio>();
                    audio.Setup();
                    foreach (AudioSource source in new[] { audio.CueSource, audio.HoldSource })
                    {
                        Assert.That(source, Is.Not.Null);
                        Assert.That(source.ignoreListenerVolume, Is.False);
                        Assert.That(source.spatialBlend, Is.EqualTo(0f));
                        Assert.That(source.volume, Is.EqualTo(1f), "no second scaling by the settings volume");
                        Assert.That(source.playOnAwake, Is.False);
                    }
                    Assert.That(audio.HoldSource.loop, Is.True);
                    Assert.That(audio.CueSource.loop, Is.False);
                    Assert.That(host.GetComponents<AudioSource>().Length, Is.EqualTo(2));
                    AudioClip clip = audio.Clip(V2Cue.CollapseStart);
                    var data = new float[clip.samples];
                    clip.GetData(data, 0);
                    samples.Add(data);
                    Assert.That(clip.frequency, Is.EqualTo(Rate));
                    Assert.That(clip.channels, Is.EqualTo(1));
                    foreach (V2Cue cue in Enum.GetValues(typeof(V2Cue))) Assert.That(audio.Clip(cue).samples, Is.EqualTo(V2CueSynth.Length(cue, Rate)));
                    Assert.That(audio.HoldClip.samples, Is.EqualTo(V2CueSynth.BuildHold(Rate).Length));
                }
                Assert.That(samples[0], Is.EqualTo(samples[1]), "same clip samples at volume 0.3 and 1.0");
                float[] synth = V2CueSynth.Build(V2Cue.CollapseStart, Rate);
                for (int i = 0; i < synth.Length; i += 97) Assert.That(samples[0][i], Is.EqualTo(synth[i]).Within(1e-4f));
            }
            finally
            {
                AudioListener.volume = before;
                foreach (GameObject host in objects) Destroy(host);
            }
        }

        [Test]
        public void TheHoldLoopStartsWithTheOwnActionAndStopsWhenItEndsOrTheMatchStops()
        {
            var host = new GameObject("v2 hold test");
            try
            {
                var audio = host.AddComponent<RulesV2Audio>();
                audio.Begin(Role.Survivor, Own);
                audio.OnSelf((byte)ActionKind.Switch, .1f, 1);
                Assert.That(audio.Playing, Is.EqualTo(ActionKind.Switch));
                float early = audio.HoldSource.pitch;
                audio.Frame(2, true);
                Assert.That(audio.HoldSource.pitch, Is.GreaterThan(early), "pitch rises with progress");
                audio.OnSelf((byte)ActionKind.None, 0, 2.1);
                Assert.That(audio.Playing, Is.EqualTo(ActionKind.None), "stops on the packet that ends the action");
                audio.OnSelf((byte)ActionKind.Switch, .1f, 3);
                Assert.That(audio.Playing, Is.EqualTo(ActionKind.Switch));
                audio.Frame(3.1, false);
                Assert.That(audio.Playing, Is.EqualTo(ActionKind.None), "silent outside a match");
                audio.OnSelf((byte)ActionKind.Switch, .1f, 4);
                audio.End();
                Assert.That(audio.Playing, Is.EqualTo(ActionKind.None));
                audio.Frame(5, true);
                Assert.That(audio.Playing, Is.EqualTo(ActionKind.None), "End clears the action");
            }
            finally { Destroy(host); }
        }

        // ---- helpers ----------------------------------------------------------------------------------------------------

        private static V2CueRules Survivor()
        {
            var rules = new V2CueRules();
            rules.Begin(Role.Survivor, Own);
            return rules;
        }

        private static V2CueRules Killer()
        {
            var rules = new V2CueRules();
            rules.Begin(Role.Killer, 9);
            return rules;
        }

        private static (int Id, PlayerStatus Status)[] P(params (int, PlayerStatus)[] players) => players;

        private static S2C_WorldState World(bool powered = false, bool open0 = false, bool open1 = false, float collapse = -1, bool hatch = false,
            bool regressing2 = false, (int Id, PlayerStatus Status)[] players = null)
        {
            var world = new S2C_WorldState { CollapseLeft = collapse, HatchVisible = (byte)(hatch ? 1 : 0), HatchX = 10, HatchZ = 12 };
            byte power = (byte)(powered ? ExitFlags.Powered : 0);
            world.Exits.Add(new ExitState { Id = 0, Flags = (byte)(power | (open0 ? (byte)ExitFlags.Open : 0)) });
            world.Exits.Add(new ExitState { Id = 1, Flags = (byte)(power | (open1 ? (byte)ExitFlags.Open : 0)) });
            for (byte i = 0; i < 4; i++)
                world.Terminals.Add(new TerminalState { Id = i, Progress = 40, Flags = (byte)(i == 2 && regressing2 ? TerminalFlags.Regressing : 0) });
            world.Players.Add(new PlayerState { Id = 9, Role = (byte)Role.Killer });
            if (players != null)
                foreach ((int id, PlayerStatus status) in players) world.Players.Add(new PlayerState { Id = id, Role = (byte)Role.Survivor, Status = (byte)status });
            return world;
        }

        private static List<V2Cue> Step(V2CueRules rules, S2C_WorldState world, double now = 0)
        {
            rules.OnWorld(world, now);
            return Take(rules);
        }

        private static List<V2Cue> Take(V2CueRules rules)
        {
            var cues = new List<V2Cue>(rules.Pending);
            rules.Pending.Clear();
            return cues;
        }

        // The edge gives exactly this cue; the same snapshot repeated gives nothing more.
        private static void AssertOnce(V2CueRules rules, S2C_WorldState world, V2Cue cue)
        {
            Assert.That(Step(rules, world), Is.EqualTo(new[] { cue }), cue.ToString());
            for (int i = 0; i < 4; i++) Assert.That(Step(rules, world), Is.Empty, cue + " repeated");
        }

        private static float Peak(float[] s)
        {
            float peak = 0;
            foreach (float v in s) peak = Math.Max(peak, Math.Abs(v));
            return peak;
        }

        private static float Rms(float[] s)
        {
            double sum = 0;
            foreach (float v in s) sum += v * v;
            return (float)Math.Sqrt(sum / s.Length);
        }

        private static void Destroy(GameObject host) => UnityEngine.Object.DestroyImmediate(host);
    }
}
