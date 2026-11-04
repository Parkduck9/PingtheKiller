using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // U6c-hatch: the escape hatch is drawn for both roles from WorldState.HatchVisible / HatchX / HatchZ.
    public sealed class HatchViewTests
    {
        private const int OwnId = 3;
        private GameObject host;
        private WorldView view;
        private uint tick;

        [SetUp]
        public void Create()
        {
            host = new GameObject("U6c hatch world");
        }

        [TearDown]
        public void Cleanup()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Object.DestroyImmediate(host);
        }

        private WorldView Build(Role role)
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // edit mode: WorldView's collider Destroy() logs
            view = host.AddComponent<WorldView>();
            view.Initialize(OwnId, role);
            view.Hatch.ReducedMotionOverride = false;
            return view;
        }

        private S2C_WorldState Snapshot(bool hatch, float x = 0, float z = 0, Role role = Role.Survivor)
        {
            var state = new S2C_WorldState { Tick = ++tick, HatchVisible = (byte)(hatch ? 1 : 0), HatchX = x, HatchZ = z };
            state.Players.Add(new PlayerState { Id = OwnId, Role = (byte)role, X = 5, Z = 36 });
            return state;
        }

        private int Objects() => host.GetComponentsInChildren<Transform>(true).Length;

        [TestCase(Role.Survivor)]
        [TestCase(Role.Killer)]
        public void NoHatchBeforeTheFirstSnapshotAndNoObjectForIt(Role role)
        {
            Build(role);
            Assert.That(view.HatchShown, Is.False);
            Assert.That(view.Hatch.Built, Is.False);
            view.Accept(Snapshot(false), .05); // the first snapshot also builds the terminal list
            int before = Objects();
            for (int i = 0; i < 5; i++) view.Accept(Snapshot(false), .1 + i * .05);
            Assert.That(view.HatchShown, Is.False);
            Assert.That(view.Hatch.Built, Is.False);
            Assert.That(Objects(), Is.EqualTo(before), "an absent hatch is never built");
        }

        [TestCase(Role.Survivor)]
        [TestCase(Role.Killer)]
        public void ShowsOnVisibleAtTheServerPosition(Role role)
        {
            Build(role);
            view.Accept(Snapshot(true, 31, 10, role), .05);
            Assert.That(view.HatchShown, Is.True);
            Assert.That(view.HatchPosition, Is.EqualTo(new Vector3(31, 0, 10)));
            var renderers = host.GetComponentsInChildren<Renderer>(false);
            Assert.That(System.Array.Exists(renderers, r => r.transform.IsChildOf(view.Hatch.Root.transform) && r.gameObject.activeInHierarchy), Is.True);
        }

        [Test]
        public void HidesOnTheFirstSnapshotWithoutIt()
        {
            Build(Role.Survivor);
            view.Accept(Snapshot(true, 25, 63), .05);
            view.Accept(Snapshot(false), .10);
            Assert.That(view.HatchShown, Is.False);
            Assert.That(view.Hatch.Root.activeSelf, Is.False);
        }

        [Test]
        public void ReshowsAtANewPositionAndSnapsToIt()
        {
            Build(Role.Survivor);
            view.Accept(Snapshot(true, 31, 10), .05);
            view.Accept(Snapshot(false), .10);
            view.Accept(Snapshot(true, 66, 38.5f), .15);
            Assert.That(view.HatchShown, Is.True);
            Assert.That(view.HatchPosition, Is.EqualTo(new Vector3(66, 0, 38.5f)));
            view.Accept(Snapshot(true, 12, 39), .20);
            Assert.That(view.HatchPosition, Is.EqualTo(new Vector3(12, 0, 39)), "a moved hatch snaps without interpolation");
        }

        [Test]
        public void HundredRepeatedSnapshotsCreateNothingNew()
        {
            Build(Role.Survivor);
            view.Accept(Snapshot(true, 56, 76), .05);
            int objects = Objects();
            GameObject root = view.Hatch.Root;
            for (int i = 0; i < 100; i++)
            {
                view.Accept(Snapshot(true, 56, 76), .1 + i * .05);
                view.Render(.1 + i * .05);
            }
            Assert.That(Objects(), Is.EqualTo(objects));
            Assert.That(view.Hatch.Root, Is.SameAs(root));
            for (int i = 0; i < 100; i++) view.Accept(Snapshot(i % 2 == 0, 56, 76), 10 + i * .05);
            Assert.That(Objects(), Is.EqualTo(objects), "toggling reuses the one object");
        }

        [Test]
        public void TheRimPulsesAtMostOnceASecondAndStaysStaticUnderReducedMotion()
        {
            Assert.That(HatchView.PulseHertz, Is.LessThanOrEqualTo(1f));
            Build(Role.Survivor);
            view.Accept(Snapshot(true, 31, 10), .05);
            var colors = new System.Collections.Generic.HashSet<Color>();
            for (int i = 0; i < 40; i++) { view.Render(i * .05); colors.Add(view.Hatch.RimColor); }
            Assert.That(colors.Count, Is.GreaterThan(5), "the rim eases between two tones");
            view.Hatch.ReducedMotionOverride = false;
            view.Accept(Snapshot(false), 5);
            view.Hatch.ReducedMotionOverride = true;
            view.Accept(Snapshot(true, 31, 10), 5.05);
            colors.Clear();
            for (int i = 0; i < 40; i++) { view.Render(i * .05); colors.Add(view.Hatch.RimColor); }
            Assert.That(colors.Count, Is.EqualTo(1));
        }

        [Test]
        public void NothingOnTheHatchIsEmissiveAndTheOpeningIsTheDarkestPart()
        {
            Build(Role.Killer);
            view.Accept(Snapshot(true, 31, 10, Role.Killer), .05);
            foreach (Renderer renderer in view.Hatch.Root.GetComponentsInChildren<Renderer>(true))
            {
                Material material = renderer.sharedMaterial;
                Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.False, renderer.name);
            }
            Assert.That(HatchView.Opening.maxColorComponent, Is.LessThan(HatchView.Steel.maxColorComponent));
        }
    }
}
