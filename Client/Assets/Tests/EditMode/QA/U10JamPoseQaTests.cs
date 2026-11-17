using System.Globalization;
using System.Text;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PingTheKiller.Client.QA.Tests
{
    // U10-motion QA (attempt 1): the implementer's jam test checks only the slam count and the burst. This one measures
    // the Killer model's hands during the slam (no live or preview shot of the jam exists).
    public sealed class U10JamPoseQaTests
    {
        [Test]
        public void TheKillerModelsHandsRiseAndLandOnTheTerminalTop()
        {
            var root = new GameObject("u10 qa jam");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, CharacterArt.Load());
            try
            {
                characters.Initialize();
                var snapshots = new SnapshotBuffer();
                float highest = 0, restBefore = 0, atImpactY = -1, atImpactXZ = -1;
                double jamAt = -1;
                var trace = new StringBuilder();
                for (int tick = 1; tick <= 60; tick++)
                {
                    var state = new S2C_WorldState { Tick = (uint)tick };
                    state.Terminals.Add(new TerminalState { Id = 0, X = 10, Z = 10, Progress = 40, Flags = (byte)(tick >= 30 ? TerminalFlags.Regressing : 0) });
                    state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 6, Z = 6 });
                    state.Players.Add(new PlayerState { Id = 99, Role = (byte)Role.Killer, X = 9, Z = 10, Yaw = 90 });
                    snapshots.Add(state, tick * .05);
                    characters.Accept(state);
                    for (int frame = 0; frame < 3; frame++)
                    {
                        double now = tick * .05 + .15 + frame / 60.0;
                        characters.Render(now, snapshots);
                        Transform model = root.transform.Find("Killer (line of sight)");
                        Assert.That(model, Is.Not.Null, "the imported Killer model");
                        Animator animator = model.GetComponentInChildren<Animator>();
                        Vector3 l = animator.GetBoneTransform(HumanBodyBones.LeftHand).position, r = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                        Vector3 mid = (l + r) * .5f;
                        if (characters.KillerJams == 0) { restBefore = mid.y; continue; }
                        if (jamAt < 0) jamAt = now;
                        double t = now - jamAt;
                        if (t > InteractionTiming.JamSeconds) continue;
                        trace.Append(t.ToString("0.00", CultureInfo.InvariantCulture)).Append(':').Append(mid.y.ToString("0.00", CultureInfo.InvariantCulture)).Append(' ');
                        if (mid.y > highest) highest = mid.y;
                        if (atImpactY < 0 && t >= InteractionTiming.JamSeconds * InteractionTiming.JamImpact + .03)
                        {
                            atImpactY = mid.y;
                            atImpactXZ = new Vector2(mid.x - 10, mid.z - 10).magnitude;
                        }
                    }
                }
                string report = "rest=" + restBefore.ToString("0.00", CultureInfo.InvariantCulture) + " highest=" + highest.ToString("0.00", CultureInfo.InvariantCulture)
                    + " impactY=" + atImpactY.ToString("0.00", CultureInfo.InvariantCulture) + " impactDistToTerminalXZ=" + atImpactXZ.ToString("0.00", CultureInfo.InvariantCulture) + "\n" + trace;
                TestContext.WriteLine(report);
                Debug.Log("[U10-QA] jam pose " + report);
                Assert.That(characters.KillerJams, Is.EqualTo(1));
                Assert.That(highest, Is.GreaterThan(1.4f), "wind-up: hands above the head line\n" + report);
                Assert.That(atImpactY, Is.EqualTo(InteractionTiming.JamTop).Within(.25f), "impact: hands on the terminal top\n" + report);
                Assert.That(atImpactXZ, Is.LessThan(.75f), "impact: hands over the terminal\n" + report);
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        // The settled own exit switch pose (no settled live shot exists): torso tilt from vertical (hips -> head), the right
        // hand height and the head height, facing the switch and facing 90 degrees away from it.
        [TestCase(0f)]
        [TestCase(90f)]
        public void TheOwnSwitchHoldPoseSettled(float facingOff)
        {
            var root = new GameObject("u10 qa switch");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, CharacterArt.Load());
            try
            {
                characters.Initialize();
                var snapshots = new SnapshotBuffer();
                var sw = SharedMap.Map.ExitSwitches[0];
                float x = sw.X - 1f, z = sw.Z;
                float idleTilt = 0, idleHead = 0;
                for (int tick = 1; tick <= 80; tick++)
                {
                    var state = new S2C_WorldState { Tick = (uint)tick };
                    state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = x, Z = z });
                    snapshots.Add(state, tick * .05);
                    characters.Accept(state);
                    if (tick == 40) characters.SetOwnAction((byte)ActionKind.Switch);
                    for (int frame = 0; frame < 3; frame++)
                    {
                        characters.SetLocalLook(90 + facingOff, false); // +x is yaw 90: toward the switch
                        characters.Render(tick * .05 + .15 + frame / 60.0, snapshots);
                    }
                    if (tick == 39) Measure(root, out idleTilt, out _, out idleHead);
                }
                Assert.That(characters.InteractionOf(1), Is.EqualTo(InteractionKind.Switch));
                Measure(root, out float tilt, out float hand, out float head);
                string report = "facingOff=" + facingOff + " idleTilt=" + idleTilt.ToString("0", CultureInfo.InvariantCulture) + " idleHead=" + idleHead.ToString("0.00", CultureInfo.InvariantCulture)
                    + " switchTilt=" + tilt.ToString("0", CultureInfo.InvariantCulture) + " rightHandY=" + hand.ToString("0.00", CultureInfo.InvariantCulture) + " headY=" + head.ToString("0.00", CultureInfo.InvariantCulture);
                TestContext.WriteLine(report);
                Debug.Log("[U10-QA] switch pose " + report);
                Assert.That(hand, Is.EqualTo(InteractionTiming.SwitchHeight).Within(.2f), report);
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        private static void Measure(GameObject root, out float tilt, out float hand, out float head)
        {
            Animator animator = root.transform.Find("Player 1").GetComponentInChildren<Animator>();
            Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position, top = animator.GetBoneTransform(HumanBodyBones.Head).position;
            tilt = Vector3.Angle(top - hips, Vector3.up);
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand).position.y;
            head = top.y;
        }
    }
}
