using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PingTheKiller.Client.Tests
{
    // U10-motion: the interaction poses (hack, exit switch hold, restore, Killer jam), their holographic props, the masking
    // of both, and the Survivor run locomotion.
    public sealed class InteractionMotionTests
    {
        private const byte N = (byte)PlayerStatus.Normal, L = (byte)PlayerStatus.Lagging, E = (byte)PlayerStatus.Eliminated;
        private const byte Hacking = (byte)PlayerFlags.Hacking, Rescuing = (byte)PlayerFlags.Rescuing, Signal = (byte)PlayerFlags.Signal;
        private const byte Switch = (byte)ActionKind.Switch, Kick = (byte)ActionKind.Kick;

        // ---- flag -> interaction mapping --------------------------------------------------------------------------------

        [Test]
        public void FlagsMapToTheInteractionInTheSurvivorView()
        {
            var S = MotionView.Survivor;
            Assert.That(InteractionMap.Resolve(S, N, Hacking | Signal, MotionPose.Working, 0, false, 0), Is.EqualTo(InteractionKind.Hack));
            Assert.That(InteractionMap.Resolve(S, N, Hacking, MotionPose.Working, 0, true, 0), Is.EqualTo(InteractionKind.Hack), "own and mates alike");
            Assert.That(InteractionMap.Resolve(S, N, Rescuing | Signal, MotionPose.Locomotion, 0, false, 0), Is.EqualTo(InteractionKind.Restore));
            Assert.That(InteractionMap.Resolve(S, N, Rescuing, MotionPose.Injured, .1f, false, 0), Is.EqualTo(InteractionKind.Restore), "a captured-once rescuer too");
            Assert.That(InteractionMap.Resolve(S, N, Signal, MotionPose.Locomotion, 0, true, Switch), Is.EqualTo(InteractionKind.Switch), "own exit switch hold");
            Assert.That(InteractionMap.Resolve(S, N, Signal, MotionPose.Locomotion, 0, false, Switch), Is.EqualTo(InteractionKind.None), "not exposed per mate");
            Assert.That(InteractionMap.Resolve(S, N, 0, MotionPose.Locomotion, 0, true, Kick), Is.EqualTo(InteractionKind.None));
            Assert.That(InteractionMap.Resolve(S, N, 0, MotionPose.Locomotion, 0, false, 0), Is.EqualTo(InteractionKind.None));
            Assert.That(InteractionMap.Resolve(S, N, Hacking, MotionPose.Locomotion, 3, false, 0), Is.EqualTo(InteractionKind.None), "moving");
            Assert.That(InteractionMap.Resolve(S, L, Rescuing, MotionPose.Crawl, 0, false, 0), Is.EqualTo(InteractionKind.None), "Lagging");
            Assert.That(InteractionMap.Resolve(S, E, Hacking, MotionPose.Death, 0, false, 0), Is.EqualTo(InteractionKind.None));
            Assert.That(InteractionMap.Resolve(S, N, Hacking, MotionPose.GettingUp, 0, false, 0), Is.EqualTo(InteractionKind.None), "getting up first");
        }

        [Test]
        public void TheKillerViewNeverShowsAnInteraction()
        {
            // HUD_SPEC 6.3 rule 1: the Killer never reads Hacking or Rescuing; real entries, afterimages and decoys alike.
            for (int flags = 0; flags < 32; flags++)
                foreach (byte status in new[] { N, L })
                    foreach (MotionPose pose in new[] { MotionPose.Locomotion, MotionPose.Working, MotionPose.Injured })
                        foreach (byte action in new[] { (byte)0, Switch, Kick })
                            Assert.That(InteractionMap.Resolve(MotionView.Killer, status, (byte)flags, pose, 0, true, action), Is.EqualTo(InteractionKind.None));
        }

        [Test]
        public void TheKillerViewBuildsNoPropsForReceivedInstances()
        {
            // The Killer client's CharacterViews builds no character from an entry, so nothing can carry a prop there; the
            // Killer-view instances (KillerWorldLayer, MotionView.Killer) resolve to None above.
            var root = new GameObject("killer views");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Killer, CharacterArt.Load());
            try
            {
                characters.Initialize();
                var snapshots = new SnapshotBuffer();
                int before = HoloPropFx.Built;
                for (int tick = 1; tick <= 20; tick++)
                {
                    var state = new S2C_WorldState { Tick = (uint)tick };
                    state.Terminals.Add(new TerminalState { Id = 0, X = 10, Z = 10, Progress = 40 });
                    state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 2, Z = 2 });
                    state.Players.Add(new PlayerState { Id = 5, Role = (byte)Role.Survivor, X = 9, Z = 10, Flags = Hacking | Signal });
                    snapshots.Add(state, tick * .05);
                    characters.Accept(state);
                    characters.Render(tick * .05 + .15, snapshots);
                }
                Assert.That(characters.PropsOf(5), Is.Null, "no instance from an entry");
                Assert.That(HoloPropFx.Built, Is.EqualTo(before), "no prop built");
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        // ---- S1 regression -----------------------------------------------------------------------------------------------

        // Live-like: a Survivor walks to a terminal at run speed, stops and hacks (Hacking + Signal) while the client renders
        // at 60 fps over 20 Hz snapshots with the 100 ms delay. The pose was already ButtonPushing before U10; what failed
        // was legibility (the press sat at hip height behind the body). Now both hands are on a chest-high panel, the
        // panel is drawn in front of them and the hands rise above the hips.
        [Test]
        public void S1_AHackingSurvivorThatStoppedAtATerminalTypesOnAVisibleHoloPanel()
        {
            var root = new GameObject("hack views");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, CharacterArt.Load());
            try
            {
                characters.Initialize();
                var snapshots = new SnapshotBuffer();
                const float terminalX = 10, terminalZ = 10;
                for (int tick = 1; tick <= 80; tick++)
                {
                    double at = tick * .05;
                    float x = tick <= 20 ? terminalX - 5 + tick * .2f : terminalX - 1;
                    byte flags = tick <= 20 ? (byte)0 : (byte)(Hacking | Signal);
                    var state = new S2C_WorldState { Tick = (uint)tick };
                    state.Terminals.Add(new TerminalState { Id = 0, X = terminalX, Z = terminalZ, Progress = 10 });
                    state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = x, Z = terminalZ, Flags = flags });
                    state.Players.Add(new PlayerState { Id = 2, Role = (byte)Role.Survivor, X = x + .2f, Z = terminalZ + 1.2f, Yaw = 90, Flags = flags });
                    snapshots.Add(state, at);
                    characters.Accept(state);
                    for (int frame = 0; frame < 3; frame++)
                    {
                        characters.SetLocalLook(90, false);
                        characters.Render(at + frame / 60.0, snapshots);
                    }
                }
                foreach (int id in new[] { 1, 2 })
                {
                    Transform model = root.transform.Find("Player " + id);
                    Animator animator = model.GetComponentInChildren<Animator>();
                    Assert.That(characters.PoseOf(id), Is.EqualTo(MotionPose.Working), id + " tracked pose");
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(Animator.StringToHash(MotionStates.Working)), id + " animator state");
                    Assert.That(characters.InteractionOf(id), Is.EqualTo(InteractionKind.Hack), id + " interaction");
                    HoloPropFx props = characters.PropsOf(id);
                    Assert.That(props.PanelShown && props.PacketsShown, Is.True, id + " panel and stream");
                    Vector3 panel = model.InverseTransformPoint(props.PanelPosition);
                    Assert.That(panel.y, Is.EqualTo(InteractionTiming.PanelHeight).Within(.01f));
                    Assert.That(panel.z, Is.EqualTo(InteractionTiming.PanelForward).Within(.01f), "in front of the character");
                    float left = model.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position).y;
                    float right = model.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position).y;
                    TestContext.WriteLine("Player " + id + " hands " + left.ToString("0.00") + " / " + right.ToString("0.00") + " m");
                    Assert.That(left, Is.GreaterThan(.95f), "left hand raised to the panel");
                    Assert.That(right, Is.GreaterThan(.95f), "right hand raised to the panel");
                }
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        // ---- restore, jam, pool, colours -------------------------------------------------------------------------------

        [Test]
        public void ARescuerCrouchesOverTheLaggingAllyWithAGreenLink()
        {
            var root = new GameObject("restore views");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, CharacterArt.Load());
            try
            {
                characters.Initialize();
                var snapshots = new SnapshotBuffer();
                for (int tick = 1; tick <= 40; tick++)
                {
                    var state = new S2C_WorldState { Tick = (uint)tick };
                    state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 5, Z = 5, Flags = Rescuing | Signal });
                    state.Players.Add(new PlayerState { Id = 2, Role = (byte)Role.Survivor, Status = L, X = 6, Z = 5, CaptureCount = 1, Flags = Signal });
                    snapshots.Add(state, tick * .05);
                    characters.Accept(state);
                    characters.SetLocalLook(90, false);
                    characters.Render(tick * .05 + .15, snapshots);
                }
                Assert.That(characters.InteractionOf(1), Is.EqualTo(InteractionKind.Restore));
                Assert.That(characters.InteractionOf(2), Is.EqualTo(InteractionKind.None), "the Lagging ally");
                HoloPropFx props = characters.PropsOf(1);
                Assert.That(props.LinkShown, Is.True);
                Assert.That(props.PanelShown, Is.False);
                Transform model = root.transform.Find("Player 1");
                Animator animator = model.GetComponentInChildren<Animator>();
                float hips = model.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position).y;
                float hand = model.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position).y;
                TestContext.WriteLine("rescuer hips " + hips.ToString("0.00") + " hand " + hand.ToString("0.00"));
                Assert.That(hand, Is.LessThan(.7f), "hands reach down toward the ally");
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheKillerModelSlamsOnceWhenANearbyTerminalTurnsRegressingInSight()
        {
            var root = new GameObject("jam views");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, CharacterArt.Load());
            try
            {
                characters.Initialize();
                var snapshots = new SnapshotBuffer();
                bool burst = false;
                for (int tick = 1; tick <= 40; tick++)
                {
                    var state = new S2C_WorldState { Tick = (uint)tick };
                    state.Terminals.Add(new TerminalState { Id = 0, X = 10, Z = 10, Progress = 40, Flags = (byte)(tick >= 20 ? TerminalFlags.Regressing : 0) });
                    state.Terminals.Add(new TerminalState { Id = 1, X = 40, Z = 40, Progress = 40, Flags = (byte)(tick >= 10 ? TerminalFlags.Regressing : 0) });
                    state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 6, Z = 6 });
                    state.Players.Add(new PlayerState { Id = 99, Role = (byte)Role.Killer, X = 9, Z = 10 });
                    snapshots.Add(state, tick * .05);
                    characters.Accept(state);
                    characters.Render(tick * .05 + .15, snapshots);
                    burst |= characters.KillerProps.BurstShown;
                }
                Assert.That(characters.KillerJams, Is.EqualTo(1), "only the terminal next to the drawn model");
                Assert.That(burst, Is.True, "the red glitch burst on the impact");
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void JamNeedsTheModelDrawnAndAnEdge()
        {
            var jam = new JamTracker();
            float[] x = { 0 }, z = { 0 };
            Assert.That(jam.Observe(0, 1, x, z, new[] { true }, true, .5f, 0), Is.False, "already Regressing when first seen");
            Assert.That(jam.Observe(1, 1, x, z, new[] { false }, true, .5f, 0), Is.False);
            Assert.That(jam.Observe(2, 1, x, z, new[] { true }, false, .5f, 0), Is.False, "not drawn: no slam");
            Assert.That(jam.Observe(3, 1, x, z, new[] { false }, true, .5f, 0), Is.False);
            Assert.That(jam.Observe(4, 1, x, z, new[] { true }, true, 5f, 0), Is.False, "too far from the model");
            Assert.That(jam.Observe(5, 1, x, z, new[] { false }, true, .5f, 0), Is.False);
            Assert.That(jam.Observe(6, 1, x, z, new[] { true }, true, .5f, 0), Is.True);
            Assert.That(jam.Active(6.3), Is.True);
            Assert.That(jam.Active(6 + InteractionTiming.JamSeconds + .01), Is.False);
            Assert.That(jam.Count, Is.EqualTo(1));
        }

        [Test]
        public void PropsArePooledPerCharacterAndNeverRebuilt()
        {
            var root = new GameObject("pool");
            var character = new GameObject("character").transform;
            var props = new HoloPropFx(root.transform);
            try
            {
                int before = HoloPropFx.Built;
                props.Show(InteractionKind.Hack, 1, character, Vector3.forward, Vector3.forward, 0, 0);
                int built = HoloPropFx.Built - before;
                Assert.That(built, Is.EqualTo(4 + InteractionTiming.Packets + InteractionTiming.Shards), "one build: root, panel, link, burst root + squares");
                int children = root.GetComponentsInChildren<Transform>(true).Length;
                for (int frame = 1; frame < 600; frame++)
                {
                    double now = frame / 60.0;
                    InteractionKind kind = (InteractionKind)(frame / 100 % 4);
                    props.Show(kind, 1, character, Vector3.forward, Vector3.right, now, now);
                    if (frame % 90 == 0) props.Burst(Vector3.zero, now);
                }
                Assert.That(HoloPropFx.Built - before, Is.EqualTo(built), "nothing built after the first use");
                Assert.That(root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(children));
                props.Hide(100);
                Assert.That(props.PanelShown || props.LinkShown || props.BurstShown, Is.False);
            }
            finally { props.Dispose(); Object.DestroyImmediate(character.gameObject); Object.DestroyImmediate(root); }
        }

        [Test]
        public void PropColoursAreTheHudSpecAllyAndDangerTokens()
        {
            // HUD_SPEC 0.4: ally #3FB8AF (Survivor-team information, Survivor clients only), danger #E5484D (Killer-caused).
            Assert.That((Color32)HoloPropFx.Ally, Is.EqualTo(new Color32(0x3F, 0xB8, 0xAF, 255)));
            Assert.That((Color32)HoloPropFx.Danger, Is.EqualTo(new Color32(0xE5, 0x48, 0x4D, 255)));
            Assert.That((Color32)HoloPropFx.Ally, Is.EqualTo((Color32)SkillFx.Ally), "the same token as every other ally element");
            Assert.That((Color32)HoloPropFx.Danger, Is.EqualTo((Color32)SkillFx.Danger));
            var root = new GameObject("colours");
            var character = new GameObject("character").transform;
            var props = new HoloPropFx(root.transform);
            try
            {
                props.Show(InteractionKind.Hack, 1, character, Vector3.forward, Vector3.forward, 0, 0);
                props.Burst(Vector3.zero, 0);
                Color panel = props.PanelColor;
                Assert.That((Color32)new Color(panel.r, panel.g, panel.b, 1), Is.EqualTo((Color32)HoloPropFx.Ally));
                Assert.That(panel.a, Is.EqualTo(HoloPropFx.PanelAlpha).Within(.001f), "translucent");
                Assert.That((Color32)props.PacketColor, Is.EqualTo((Color32)HoloPropFx.Ally));
                Assert.That((Color32)props.LinkColor, Is.EqualTo((Color32)HoloPropFx.Ally));
                Assert.That((Color32)props.ShardColor, Is.EqualTo((Color32)HoloPropFx.Danger));
                Assert.That(Resources.Load<Material>(HoloArt.FadeResource), Is.Not.Null, "the transparent holo material ships in Resources");
                Assert.That(Resources.Load<Material>(HoloArt.SolidResource), Is.Not.Null);
            }
            finally { props.Dispose(); Object.DestroyImmediate(character.gameObject); Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheHackBeatsAlternateAndPulse()
        {
            int a = InteractionMotion.Beat(0, out _), b = InteractionMotion.Beat(InteractionTiming.KeySeconds * 1.5, out float phase);
            Assert.That(b - a, Is.EqualTo(1));
            Assert.That(phase, Is.EqualTo(.5f).Within(.001f));
            Assert.That(HoloPropFx.Pulse(0), Is.EqualTo(InteractionTiming.PulseScale));
            Assert.That(HoloPropFx.Pulse(InteractionTiming.PulseSeconds), Is.EqualTo(0));
            Assert.That(InteractionMotion.JamHeight(InteractionTiming.JamSeconds * InteractionTiming.JamPeak), Is.EqualTo(InteractionTiming.JamRaise).Within(.01f), "wind-up above the head");
            Assert.That(InteractionMotion.JamHeight(InteractionTiming.JamSeconds * InteractionTiming.JamImpact), Is.EqualTo(InteractionTiming.JamTop).Within(.01f), "slam onto the top");
        }

        // ---- locomotion (U10 scope add: Survivors run) ------------------------------------------------------------------

        // The planted foot's backward speed in the model's space (median over the stance samples) while the character is
        // driven through the real pipeline at `speed` m/s. Equal to `speed` means no foot sliding.
        private static float PlantedSpeed(float speed, byte captureCount, out string state)
        {
            GameObject model = Object.Instantiate(CharacterArt.Load().SurvivorPrefab);
            CharacterMotion motion = CharacterMotion.Attach(model, MotionView.Survivor);
            var instance = new CharacterInstance(model, true);
            state = string.Empty;
            try
            {
                Animator animator = instance.Animator;
                var feet = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
                var samples = new List<Vector3>[2] { new List<Vector3>(), new List<Vector3>() };
                const double dt = 1 / 120.0;
                for (double now = 0; now < 4; now += dt)
                {
                    motion.Observe(now, N, 0, captureCount, instance.Speed);
                    motion.Drive(now);
                    instance.Animate(new Vector3(0, 0, (float)(speed * now)), now, false, false);
                    if (now < 1.5) continue;
                    for (int k = 0; k < 2; k++) samples[k].Add(model.transform.InverseTransformPoint(animator.GetBoneTransform(feet[k]).position));
                }
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                state = info.shortNameHash == Animator.StringToHash(MotionStates.Injured) ? "Injured" : info.shortNameHash == Animator.StringToHash(MotionStates.Locomotion) ? "Locomotion" : "other";
                var planted = new List<float>();
                for (int k = 0; k < 2; k++)
                {
                    float low = float.MaxValue;
                    foreach (Vector3 p in samples[k]) low = Mathf.Min(low, p.y);
                    for (int i = 1; i < samples[k].Count; i++)
                        if (samples[k][i].y < low + .03f && samples[k][i - 1].y < low + .03f)
                            planted.Add(-(samples[k][i].z - samples[k][i - 1].z) / (float)dt);
                }
                planted.Sort();
                return planted.Count > 0 ? planted[planted.Count / 2] : 0;
            }
            finally { motion.Dispose(); instance.Dispose(); Object.DestroyImmediate(model); }
        }

        [TestCase(1.5f, (byte)0)]
        [TestCase(GameConstants.SurvivorSpeed, (byte)0)]
        [TestCase(GameConstants.KillerSpeed, (byte)0)]
        [TestCase(GameConstants.SurvivorSpeed, (byte)1)]
        public void TheFeetDoNotSlideAtTheMovementSpeed(float speed, byte captureCount)
        {
            float planted = PlantedSpeed(speed, captureCount, out string state);
            TestContext.WriteLine("speed " + speed + " capture " + captureCount + " state " + state + " planted foot " + planted.ToString("0.00") + " m/s");
            Assert.That(planted, Is.EqualTo(speed).Within(speed * .1f), "stride x rate within 10 % of the speed");
        }

        [Test]
        public void TheLocomotionThresholdsRunAtSurvivorSpeed()
        {
            Assert.That(LocomotionBlend.WalkSpeed, Is.LessThan(LocomotionBlend.RunSpeed));
            Assert.That(LocomotionBlend.RunSpeed, Is.EqualTo(GameConstants.SurvivorSpeed), "a moving Survivor is fully in the run");
            Assert.That(LocomotionBlend.Rate(LocomotionBlend.RunSpeed, LocomotionBlend.RunNatural) * LocomotionBlend.RunNatural, Is.EqualTo(GameConstants.SurvivorSpeed).Within(.001f));
            Assert.That(LocomotionBlend.Rate(LocomotionBlend.WalkSpeed, LocomotionBlend.WalkNatural) * LocomotionBlend.WalkNatural, Is.EqualTo(LocomotionBlend.WalkSpeed).Within(.001f));
        }
    }
}
