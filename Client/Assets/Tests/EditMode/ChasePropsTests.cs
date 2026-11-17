using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Audio;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PingTheKiller.Client.Tests
{
    // Q5b (HUD_SPEC 15): Shutters and Rails on the client. State -> look, prompts by range and role, flags -> poses, the
    // stun timing, the cues, and the props at exactly the Shared boxes (catalog and primitive fallback).
    public sealed class ChasePropsTests
    {
        private GameObject host;

        [SetUp]
        public void Create() => host = new GameObject("Q5b chase props");

        [TearDown]
        public void Cleanup()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Object.DestroyImmediate(host);
        }

        private static List<byte> States(params (int id, ShutterState state)[] set)
        {
            var list = new List<byte>();
            for (int i = 0; i < WorldChaseProps.ShutterCount; i++) list.Add((byte)ShutterState.Up);
            foreach (var (id, state) in set) list[id] = (byte)state;
            return list;
        }

        // A point at `metres` from shutter `id`'s box, straight out along its crossing axis.
        private static Vector2 Beside(ChaseProp prop, float metres)
        {
            float off = prop.HalfThickness + metres;
            return new Vector2(prop.CenterX + prop.CrossX * off, prop.CenterZ + prop.CrossZ * off);
        }

        private ChasePropsView View(ChasePropArt catalog, bool reduced = false)
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var view = new ChasePropsView(new WorldMaterials(host.transform), catalog) { ReducedMotionOverride = reduced };
            view.Initialize();
            return view;
        }

        // ---- state -> visual ---------------------------------------------------------------------------------------------

        [Test]
        public void ShutterStatesMapToTheirLooksAndUnknownOrMissingValuesDrawUp()
        {
            Assert.That(ChasePropLooks.Of((byte)ShutterState.Up), Is.EqualTo(ShutterLook.Up));
            Assert.That(ChasePropLooks.Of((byte)ShutterState.Down), Is.EqualTo(ShutterLook.Down));
            Assert.That(ChasePropLooks.Of((byte)ShutterState.Broken), Is.EqualTo(ShutterLook.Broken));
            Assert.That(ChasePropLooks.Of(7), Is.EqualTo(ShutterLook.Up));
            Assert.That(ChasePropLooks.Of(null, 3), Is.EqualTo(ShutterLook.Up));
            Assert.That(ChasePropLooks.Of(new List<byte> { 1 }, 4), Is.EqualTo(ShutterLook.Up), "a short list");
            Assert.That(ChasePropLooks.Group(ShutterLook.Down), Is.EqualTo(ChasePropShapes.Down));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TheViewShowsExactlyOneGroupPerShutterAndTheFirstSnapshotNeverAnimates(bool art)
        {
            ChasePropsView view = View(art ? ChasePropArt.Load() : null);
            Assert.That(view.UsesArt, Is.EqualTo(art));
            // Late join / reconnect: the first snapshot already carries a Down and a Broken shutter.
            view.Accept(States((3, ShutterState.Down), (5, ShutterState.Broken)), 10);
            for (int i = 0; i < WorldChaseProps.ShutterCount; i++)
            {
                ShutterLook expected = i == 3 ? ShutterLook.Down : i == 5 ? ShutterLook.Broken : ShutterLook.Up;
                Assert.That(view.LookOf(i), Is.EqualTo(expected), "shutter " + i);
                foreach (ShutterLook look in new[] { ShutterLook.Up, ShutterLook.Down, ShutterLook.Broken })
                    Assert.That(view.GroupShown(i, look), Is.EqualTo(look == expected), "shutter " + i + " group " + look);
            }
            Assert.That(view.DropOffset(3), Is.EqualTo(0).Within(1e-5f), "a shutter already Down is on the floor at once");
            // A later Up -> Down drops the panel out of the housing; Broken is permanent and shown at once.
            view.Accept(States((3, ShutterState.Down), (5, ShutterState.Broken), (1, ShutterState.Down)), 11);
            Assert.That(view.DropOffset(1), Is.GreaterThan(1f), "the drop starts under the housing");
            view.Render(11 + ChasePropsView.DropSeconds * .5);
            Assert.That(view.DropOffset(1), Is.InRange(.01f, 1.39f));
            view.Render(11 + ChasePropsView.DropSeconds);
            Assert.That(view.DropOffset(1), Is.EqualTo(0).Within(1e-5f));
            view.Accept(States((3, ShutterState.Broken), (5, ShutterState.Broken), (1, ShutterState.Down)), 12);
            Assert.That(view.GroupShown(3, ShutterLook.Broken), Is.True);
            Assert.That(view.GroupShown(3, ShutterLook.Down), Is.False);
        }

        [Test]
        public void ReducedMotionDropsThePanelAtOnce()
        {
            ChasePropsView view = View(null, reduced: true);
            view.Accept(States(), 1);
            view.Accept(States((2, ShutterState.Down)), 2);
            Assert.That(view.DropOffset(2), Is.EqualTo(0).Within(1e-5f));
        }

        // ---- placement ----------------------------------------------------------------------------------------------------

        [TestCase(false)]
        [TestCase(true)]
        public void PropsStandExactlyAtTheSharedBoxesAndStayLow(bool art)
        {
            ChasePropsView view = View(art ? ChasePropArt.Load() : null);
            Assert.That(view.ShutterCount, Is.EqualTo(WorldChaseProps.ShutterCount));
            Assert.That(view.RailCount, Is.EqualTo(WorldChaseProps.RailCount));
            view.Accept(States(), 0);
            for (int i = 0; i < view.ShutterCount; i++)
            {
                ChaseProp box = WorldChaseProps.Shutters[i];
                Transform root = view.ShutterRoot(i).transform;
                Assert.That(Vector3.Distance(root.position, new Vector3(box.CenterX, 0, box.CenterZ)), Is.LessThan(1e-4f), "shutter " + i);
                Assert.That(Mathf.DeltaAngle(root.eulerAngles.y, box.AlongX ? 0 : 90), Is.EqualTo(0).Within(.01f));
                AssertInside(root.Find(ChasePropShapes.Frame), box, ChasePropShapes.PostHeight + ChasePropShapes.HousingHeight, "shutter frame " + i);
                AssertInside(root.Find(ChasePropShapes.Down), box, GameConstants.ShutterHeight, "shutter panel " + i);
            }
            for (int i = 0; i < view.RailCount; i++)
            {
                ChaseProp box = WorldChaseProps.Rails[i];
                Transform root = view.RailRoot(i).transform;
                Assert.That(Vector3.Distance(root.position, new Vector3(box.CenterX, 0, box.CenterZ)), Is.LessThan(1e-4f), "rail " + i);
                AssertInside(root.Find(ChasePropShapes.Rail), box, GameConstants.RailHeight, "rail " + i);
                Assert.That(root.Find(ChasePropShapes.Marker), Is.Not.Null, "vault marker " + i);
            }
        }

        private static void AssertInside(Transform group, ChaseProp box, float height, string label)
        {
            Assert.That(group, Is.Not.Null, label);
            bool was = group.gameObject.activeSelf;
            group.gameObject.SetActive(true);
            foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>())
            {
                // The panel group may sit raised mid-drop; measure the parts in the group's resting frame.
                Bounds b = renderer.bounds;
                float lift = group.localPosition.y;
                Assert.That(b.min.x, Is.GreaterThanOrEqualTo(box.MinX - .03f), label + " " + renderer.name);
                Assert.That(b.max.x, Is.LessThanOrEqualTo(box.MaxX + .03f), label + " " + renderer.name);
                Assert.That(b.min.z, Is.GreaterThanOrEqualTo(box.MinZ - .03f), label + " " + renderer.name);
                Assert.That(b.max.z, Is.LessThanOrEqualTo(box.MaxZ + .03f), label + " " + renderer.name);
                Assert.That(b.max.y - lift, Is.LessThanOrEqualTo(height + .01f), label + " " + renderer.name + " height");
                Assert.That(renderer.GetComponent<Collider>(), Is.Null, label + " has no collider");
            }
            group.gameObject.SetActive(was);
        }

        [Test]
        public void TheCatalogMatchesTheSharedBoxesAndTheWorldArtCatalogStillMatchesTheMap()
        {
            ChasePropArt art = ChasePropArt.Load();
            Assert.That(art, Is.Not.Null, "Run PingTheKiller.Editor.Setup.ChasePropSetup.Run");
            Assert.That(art.Matches(), Is.True);
            var moved = new List<ChaseProp>(WorldChaseProps.Shutters);
            ChaseProp first = moved[0];
            moved[0] = new ChaseProp(first.Id, first.MinX + .5f, first.MinZ, first.MaxX + .5f, first.MaxZ);
            Assert.That(art.Matches(moved, WorldChaseProps.Rails), Is.False, "one moved box");
            Assert.That(art.Matches(WorldChaseProps.Rails, WorldChaseProps.Rails), Is.False, "wrong count");
            WorldArt world = WorldArt.Load();
            Assert.That(world, Is.Not.Null);
            Assert.That(world.Matches(new FixedMap()), Is.True);
        }

        // ---- prompts ------------------------------------------------------------------------------------------------------

        [Test]
        public void SurvivorPromptsFollowTheServerRangesAndTheShutterState()
        {
            ChaseProp shutter = WorldChaseProps.Shutters[4];
            Vector2 near = Beside(shutter, 1.2f), close = Beside(shutter, .8f), far = Beside(shutter, 1.6f);
            Assert.That(ChasePropPrompt.Survivor(near.x, near.y, States()), Is.EqualTo(ChasePromptKind.Pull));
            Assert.That(ChasePropPrompt.Survivor(far.x, far.y, States()), Is.EqualTo(ChasePromptKind.None));
            Assert.That(ChasePropPrompt.Survivor(near.x, near.y, States((4, ShutterState.Down))), Is.EqualTo(ChasePromptKind.None), "vault range is 1.0 m");
            Assert.That(ChasePropPrompt.Survivor(close.x, close.y, States((4, ShutterState.Down))), Is.EqualTo(ChasePromptKind.VaultShutter));
            Assert.That(ChasePropPrompt.Survivor(close.x, close.y, States((4, ShutterState.Broken))), Is.EqualTo(ChasePromptKind.None));
            ChaseProp rail = WorldChaseProps.Rails[2];
            Vector2 atRail = Beside(rail, .5f), railFar = new Vector2(rail.CenterX + rail.CrossX * 1.1f + (rail.AlongX ? 0 : 0), rail.CenterZ + rail.CrossZ * 1.1f);
            Assert.That(ChasePropPrompt.Survivor(atRail.x, atRail.y, States()), Is.EqualTo(ChasePromptKind.VaultRail));
            Assert.That(ChasePropPrompt.Survivor(railFar.x, railFar.y, States()), Is.EqualTo(ChasePromptKind.None), "measured to the vault point");
            Vector2 railEnd = new Vector2(rail.AlongX ? rail.MaxX : rail.CenterX + .3f, rail.AlongX ? rail.CenterZ + .3f : rail.MaxZ);
            Assert.That(ChasePropPrompt.Survivor(railEnd.x, railEnd.y, States()), Is.EqualTo(ChasePromptKind.None), "the rail end is not the vault point");
            Assert.That(ChasePropPrompt.SurvivorText(ChasePromptKind.Pull), Is.EqualTo(ChasePropCopy.Pull));
            Assert.That(ChasePropPrompt.SurvivorText(ChasePromptKind.None), Is.Null);
        }

        [Test]
        public void KillerPromptsForceDownShuttersAndVaultRailsOnly()
        {
            ChaseProp shutter = WorldChaseProps.Shutters[6];
            Vector2 near = Beside(shutter, 1.4f);
            Assert.That(ChasePropPrompt.Killer(near.x, near.y, States()), Is.EqualTo(ChasePromptKind.None), "the Killer never pulls");
            Assert.That(ChasePropPrompt.Killer(near.x, near.y, States((6, ShutterState.Down))), Is.EqualTo(ChasePromptKind.Force));
            Assert.That(ChasePropPrompt.Killer(near.x, near.y, States((6, ShutterState.Broken))), Is.EqualTo(ChasePromptKind.None));
            Vector2 far = Beside(shutter, 1.6f);
            Assert.That(ChasePropPrompt.Killer(far.x, far.y, States((6, ShutterState.Down))), Is.EqualTo(ChasePromptKind.None));
            ChaseProp rail = WorldChaseProps.Rails[0];
            Vector2 atRail = Beside(rail, .4f);
            Assert.That(ChasePropPrompt.Killer(atRail.x, atRail.y, States()), Is.EqualTo(ChasePromptKind.VaultRail));
            InteractPromptText force = ChasePropPrompt.Killer(ChasePromptKind.Force);
            Assert.That(force.Text, Is.EqualTo(ChasePropCopy.Force));
            foreach (string line in new[] { ChasePropCopy.KillerVaultRail, ChasePropCopy.KillerVaultHint, ChasePropCopy.Force, ChasePropCopy.ForceHint,
                ChasePropCopy.ForcingLabel(100), ChasePropCopy.ForcingHint, ChasePropCopy.KillerVaulting, ChasePropCopy.KillerVaultingHint, ChasePropCopy.Stunned })
                Assert.That(line.Length, Is.LessThanOrEqualTo(38), line);
        }

        [Test]
        public void CopyFollowsTheSlopRules()
        {
            foreach (string line in new[] { ChasePropCopy.Pull, ChasePropCopy.VaultShutter, ChasePropCopy.VaultRail, ChasePropCopy.Pulling, ChasePropCopy.Vaulting,
                ChasePropCopy.KillerVaultRail, ChasePropCopy.Force, ChasePropCopy.Forcing, ChasePropCopy.Stunned })
            {
                Assert.That(line, Does.Not.Contain("!"), line);
                Assert.That(line.Length, Is.LessThanOrEqualTo(60), line);
            }
            Assert.That(ChasePropCopy.Pull, Does.Contain("Signal"), "a Survivor line names the Signal cost");
            Assert.That(ChasePropCopy.VaultRail, Does.Contain("Signal"));
        }

        [Test]
        public void SurvivorHudTakesAChaseActionAndOnlyFillsAnEmptyPromptSlot()
        {
            ChaseProp shutter = WorldChaseProps.Shutters[0];
            Vector2 near = Beside(shutter, 1f);
            var prompt = new PromptState(); var hold = new HoldActionView();
            ChasePropsLayer.SurvivorPrompt(ref prompt, ref hold, 0, true, false, near.x, near.y, States());
            Assert.That(prompt.Text, Is.EqualTo(ChasePropCopy.Pull));
            prompt = new PromptState { Text = "Hold [E] to rescue Bot 2" };
            ChasePropsLayer.SurvivorPrompt(ref prompt, ref hold, 0, true, false, near.x, near.y, States());
            Assert.That(prompt.Text, Is.EqualTo("Hold [E] to rescue Bot 2"), "a more urgent prompt keeps the slot");
            prompt = new PromptState { Text = "Skills are off while Lagging.", Muted = true };
            ChasePropsLayer.SurvivorPrompt(ref prompt, ref hold, 0, false, false, near.x, near.y, States());
            Assert.That(prompt.Text, Is.EqualTo("Skills are off while Lagging."), "not Normal: no Shutter prompt");
            prompt = new PromptState();
            ChasePropsLayer.SurvivorPrompt(ref prompt, ref hold, 0, true, true, near.x, near.y, States());
            Assert.That(prompt.Text, Is.Null, "a banner or a released cursor blocks it");
            hold = new HoldActionView { Kind = HoldKind.ExitSwitch, Progress01 = .5f };
            ChasePropsLayer.SurvivorPrompt(ref prompt, ref hold, (byte)ActionKind.Vault, true, false, near.x, near.y, States());
            Assert.That(hold.Kind, Is.EqualTo(HoldKind.None));
            Assert.That(prompt.Text, Is.EqualTo(ChasePropCopy.Vaulting));
            ChasePropsLayer.SurvivorPrompt(ref prompt, ref hold, (byte)ActionKind.Pull, true, false, near.x, near.y, States());
            Assert.That(prompt.Text, Is.EqualTo(ChasePropCopy.Pulling));
        }

        // ---- animation mapping ---------------------------------------------------------------------------------------------

        [Test]
        public void FlagsMapToOnePoseWithTheServerDurations()
        {
            Assert.That(ChaseActionMap.Pose(0), Is.EqualTo(ChasePose.None));
            Assert.That(ChaseActionMap.Pose((byte)ChaseFlags.Pulling), Is.EqualTo(ChasePose.Pull));
            Assert.That(ChaseActionMap.Pose((byte)ChaseFlags.Vaulting), Is.EqualTo(ChasePose.Vault));
            Assert.That(ChaseActionMap.Pose((byte)ChaseFlags.Breaking), Is.EqualTo(ChasePose.Break));
            Assert.That(ChaseActionMap.Pose((byte)ChaseFlags.Stunned), Is.EqualTo(ChasePose.Stunned));
            Assert.That(ChaseActionMap.Pose((byte)(ChaseFlags.Stunned | ChaseFlags.Breaking)), Is.EqualTo(ChasePose.Stunned));
            Assert.That(ChaseActionMap.Pose((byte)(ChaseFlags.Vaulting | ChaseFlags.Pulling)), Is.EqualTo(ChasePose.Vault));
            Assert.That(ChaseActionMap.Seconds(ChasePose.Pull, Role.Survivor, 0, 0, States()), Is.EqualTo(.3f));
            Assert.That(ChaseActionMap.Seconds(ChasePose.Stunned, Role.Killer, 0, 0, States()), Is.EqualTo(2f));
            ChaseProp rail = WorldChaseProps.Rails[3];
            Assert.That(ChaseActionMap.Seconds(ChasePose.Vault, Role.Survivor, rail.CenterX, rail.CenterZ, States()), Is.EqualTo(.8f));
            Assert.That(ChaseActionMap.Seconds(ChasePose.Vault, Role.Killer, rail.CenterX, rail.CenterZ, States()), Is.EqualTo(1.6f));
            ChaseProp shutter = WorldChaseProps.Shutters[7];
            Vector2 side = Beside(shutter, .5f);
            Assert.That(ChaseActionMap.Seconds(ChasePose.Vault, Role.Survivor, side.x, side.y, States((7, ShutterState.Down))), Is.EqualTo(1f));
            Assert.That(ChaseActionMap.HopHeight(1f), Is.GreaterThan(ChaseActionMap.HopHeight(.8f)), "a Down shutter is higher than a rail");
        }

        [Test]
        public void ThePoseClockStartsOnTheFlagEdgeAndThePoseRigHopsOverTheProp()
        {
            var clock = new ChasePoseClock();
            Assert.That(clock.Observe(ChasePose.Vault, .8f, 5), Is.True);
            Assert.That(clock.Observe(ChasePose.Vault, .8f, 5.05), Is.False, "the same pose is not a new start");
            Assert.That(clock.Phase(5.4), Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(clock.Phase(9), Is.EqualTo(1f));
            var body = new GameObject("capsule body");
            body.transform.SetParent(host.transform, false);
            var rig = new ChasePoseRig(body.transform);
            Assert.That(rig.Humanoid, Is.False);
            rig.Apply(clock, 5.4, false);
            Assert.That(rig.LastHop, Is.EqualTo(ChaseActionMap.HopHeight(.8f)).Within(1e-3f), "the top of the arc at mid-vault");
            Assert.That(body.transform.position.y, Is.EqualTo(rig.LastHop).Within(1e-4f));
            body.transform.position = Vector3.zero;
            rig.Apply(clock, 5.8, false);
            Assert.That(rig.LastHop, Is.EqualTo(0).Within(1e-3f), "landed at the server duration");
            Assert.That(clock.Observe(ChasePose.None, 0, 6), Is.False);
            rig.Apply(clock, 6.1, false);
            Assert.That(rig.LastHop, Is.EqualTo(0));
            clock.Observe(ChasePose.Pull, .3f, 7);
            rig.Apply(clock, 7.1, false);
            float start = rig.LastArm;
            Assert.That(start, Is.LessThan(-90f), "the arms start high");
            rig.Apply(clock, 7.3, false);
            Assert.That(rig.LastArm, Is.GreaterThan(start), "the arms come down over the pull");
        }

        [Test]
        public void TheImportedCharacterIsHumanoidSoThePoseMovesBones()
        {
            CharacterArt art = CharacterArt.Load();
            if (art == null || art.SurvivorPrefab == null) Assert.Ignore("no CharacterArt");
            GameObject model = Object.Instantiate(art.SurvivorPrefab, host.transform);
            var rig = new ChasePoseRig(model.transform);
            Assert.That(rig.Humanoid, Is.True);
            Animator animator = model.GetComponentInChildren<Animator>();
            Transform arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Quaternion before = arm.rotation;
            var clock = new ChasePoseClock();
            clock.Observe(ChasePose.Break, 2, 0);
            rig.Apply(clock, .2, false);
            Assert.That(Quaternion.Angle(before, arm.rotation), Is.GreaterThan(20));
        }

        // ---- stun feedback -------------------------------------------------------------------------------------------------

        [Test]
        public void StunFeedbackFollowsTheFlagAndIsBrief()
        {
            var stun = new StunFeedback();
            Assert.That(stun.Line(0), Is.Null);
            Assert.That(stun.Observe(true, 10), Is.True);
            Assert.That(stun.Observe(true, 10.05), Is.False, "one edge per stun");
            Assert.That(stun.Line(10.5), Is.EqualTo(ChasePropCopy.Stunned));
            Assert.That(stun.Showing(11.9), Is.True);
            Assert.That(stun.Showing(10 + GameConstants.ShutterStunSeconds + StunFeedback.Grace + .01), Is.False, "never longer than the stun, even with a stale flag");
            Assert.That(stun.FovOffset(10.03, false), Is.LessThan(-1f), "the impulse punches in at once");
            Assert.That(stun.FovOffset(10 + StunFeedback.ImpulseSeconds, false), Is.EqualTo(0));
            Assert.That(stun.FovOffset(10.03, true), Is.EqualTo(0), "Reduced motion: no impulse");
            Assert.That(Mathf.Abs(stun.FovOffset(10.0375, false)), Is.LessThanOrEqualTo(StunFeedback.ImpulseDegrees + 1e-4f));
            stun.Observe(false, 11.2);
            Assert.That(stun.Line(11.3), Is.Null, "the line ends with the flag");
            Assert.That(stun.Observe(true, 20), Is.True, "a new stun is a new edge");
        }

        [Test]
        public void TheStunBurstUsesTheHovlCircleOrAStillRingUnderReducedMotion()
        {
            var burst = new StunBurst(host.transform);
            burst.Set(true, new Vector3(1, 2, 3), false);
            Assert.That(burst.Shown, Is.True);
            if (SkillFxArt.Load()?.Prefab(FxPrefab.Circle) != null)
            {
                Assert.That(burst.StillRing, Is.False);
                Assert.That(burst.Root.transform.position, Is.EqualTo(new Vector3(1, 2 + StunBurst.AboveHead, 3)));
                foreach (ParticleSystem system in burst.Root.GetComponentsInChildren<ParticleSystem>())
                {
                    Color c = system.main.startColor.color;
                    Assert.That(new Color(c.r, c.g, c.b), Is.EqualTo(new Color(StunBurst.Tint.r, StunBurst.Tint.g, StunBurst.Tint.b)));
                }
            }
            burst.Set(false, Vector3.zero, false);
            Assert.That(burst.Shown, Is.False);
            var still = new StunBurst(host.transform);
            still.Set(true, Vector3.zero, true);
            Assert.That(still.StillRing, Is.True);
            burst.Dispose(); still.Dispose();
        }

        // ---- sounds ---------------------------------------------------------------------------------------------------------

        [Test]
        public void ChaseCuesRenderBoundedClickFreeDeterministicClips()
        {
            foreach (ChasePropCue cue in Enum.GetValues(typeof(ChasePropCue)))
            {
                float[] s = ChasePropSynth.Build(cue, KillerAudio.Rate);
                Assert.That(s.Length, Is.EqualTo((int)(KillerAudio.Rate * ChasePropSynth.Seconds(cue))), cue.ToString());
                Assert.That(s[0] == 0f && s[s.Length - 1] == 0f, Is.True, cue + " ends at 0");
                float peak = 0, sum = 0;
                foreach (float v in s) { peak = Math.Max(peak, Math.Abs(v)); sum += v * v; }
                Assert.That(peak, Is.LessThanOrEqualTo(.95f), cue + " peak");
                Assert.That(Math.Sqrt(sum / s.Length), Is.GreaterThan(.01), cue + " audible");
                Assert.That(ChasePropSynth.Build(cue, KillerAudio.Rate), Is.EqualTo(s), cue + " deterministic");
            }
        }

        private static S2C_WorldState Snapshot(List<byte> shutters, params PlayerState[] players)
        {
            var state = new S2C_WorldState { Shutters = shutters };
            state.Players.AddRange(players);
            return state;
        }

        [Test]
        public void CueRulesFireOnShutterAndVaultEdgesButNotOnTheBaseline()
        {
            var rules = new ChaseCueRules();
            rules.OnWorld(Snapshot(States((2, ShutterState.Down)), new PlayerState { Id = 1, X = 5, Z = 5, Chase = (byte)ChaseFlags.Vaulting }));
            Assert.That(rules.Pending, Is.Empty, "a late join hears nothing for what already happened");
            rules.OnWorld(Snapshot(States((2, ShutterState.Down), (4, ShutterState.Down)), new PlayerState { Id = 1, X = 5.2f, Z = 5, Chase = (byte)ChaseFlags.Vaulting }));
            Assert.That(rules.Pending.Count, Is.EqualTo(1));
            Assert.That(rules.Pending[0].Cue, Is.EqualTo(ChasePropCue.ShutterSlam));
            Assert.That(rules.Pending[0].X, Is.EqualTo(WorldChaseProps.Shutters[4].CenterX));
            rules.Pending.Clear();
            rules.OnWorld(Snapshot(States((2, ShutterState.Broken), (4, ShutterState.Down)), new PlayerState { Id = 3, X = 20, Z = 20, Chase = (byte)ChaseFlags.Vaulting }));
            Assert.That(rules.Pending.Count, Is.EqualTo(2));
            Assert.That(rules.Pending[0].Cue, Is.EqualTo(ChasePropCue.ShutterCrunch));
            Assert.That(rules.Pending[1].Cue, Is.EqualTo(ChasePropCue.VaultThud));
            Assert.That(rules.Pending[1].X, Is.EqualTo(20));
            rules.Pending.Clear();
            rules.OnWorld(Snapshot(States((2, ShutterState.Broken), (4, ShutterState.Down)), new PlayerState { Id = 3, X = 20.2f, Z = 20, Chase = (byte)ChaseFlags.Vaulting }));
            Assert.That(rules.Pending, Is.Empty, "a running vault is one thud");
        }

        [Test]
        public void TheAudioComponentPlaysPositionalVoices()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var audio = host.AddComponent<ChasePropAudio>();
            audio.Setup();
            Assert.That(audio.Voice(0).spatialBlend, Is.EqualTo(1f));
            audio.OnWorld(Snapshot(States()));
            audio.OnWorld(Snapshot(States((9, ShutterState.Down))));
            Assert.That(audio.Played, Is.EqualTo(1));
            Assert.That(audio.Voice(0).transform.position.x, Is.EqualTo(WorldChaseProps.Shutters[9].CenterX));
            Assert.That(audio.Voice(0).maxDistance, Is.EqualTo(ChasePropSynth.SlamRange));
        }
    }
}
