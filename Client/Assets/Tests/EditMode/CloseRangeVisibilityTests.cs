using System;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    public sealed class CloseRangeVisibilityTests
    {
        private static ViewPose Level => new ViewPose(new Vector3(5, 1.7f, 36), 0, 0, 59, 16f / 9);

        [TestCase(.8f, true)]
        [TestCase(-2f, false)]
        [TestCase(-.5f, false)]
        public void ModelBoundsRespectTheNearPlaneAndKeepMarkerClassification(float distance, bool visible)
        {
            var state = new S2C_WorldState { Tick = 1 };
            state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Killer, X = 5, Z = 36 });
            state.Players.Add(new PlayerState { Id = 2, Role = (byte)Role.Survivor, X = 5, Z = 36 + distance });
            var scene = new KillerScene(new System.Random(4), Array.Empty<(float, float)>());
            scene.Accept(state, 0);
            BlipView view = scene.Frame(Level, .1, null)[0];
            Assert.That(view.ModelVisible, Is.EqualTo(visible));
            if (distance == .8f) Assert.That(view.OutOfFrustum, Is.True, "marker stays point-based while the head is visible");
            if (distance < 0) Assert.That(view.Class, Is.EqualTo(BlipClass.OffScreen));
            var root = new GameObject("model visibility");
            try
            {
                var layer = root.AddComponent<KillerWorldLayer>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(KillerWorldLayer).GetField("markers", flags).SetValue(layer, new PingTheKiller.Client.UI.MarkerLayer(null));
                CharacterArt art = CharacterArt.Load();
                Assert.That(art, Is.Not.Null);
                typeof(KillerWorldLayer).GetField("art", flags).SetValue(layer, art);
                typeof(KillerWorldLayer).GetField("body", flags).SetValue(layer, art.NeutralMaterial);
                layer.GetType().GetMethod("DrawBlips", flags).Invoke(layer, new object[]
                {
                    new[] { view }, Level, new Vector2(1280, 720), new Rect(0, 0, 1280, 720), default(Rect), .1
                });
                Transform model = root.transform.Find("Signal instance");
                Assert.That(model.gameObject.activeSelf, Is.EqualTo(visible));
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                    Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.EqualTo(visible));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CameraInsideAnotherCharacterUsesDitherAndKeepsItVisible(bool killer)
            => CheckCloseRange(killer, true, 0);

        [TestCase(false)]
        [TestCase(true)]
        public void CameraInsideFallbackCharacterUsesDitherAndKeepsItVisible(bool killer)
            => CheckCloseRange(killer, false, 0);

        [TestCase(false, true, .29f)]
        [TestCase(true, true, .29f)]
        [TestCase(false, false, .29f)]
        [TestCase(true, false, .29f)]
        [TestCase(false, true, .31f)]
        [TestCase(true, true, .31f)]
        [TestCase(false, false, .31f)]
        [TestCase(true, false, .31f)]
        public void CameraMarginChangesEveryMaterialAndPreservesKillerFade(bool killer, bool useArt, float gap)
            => CheckCloseRange(killer, useArt, gap);

        private static void CheckCloseRange(bool killer, bool useArt, float gap)
        {
            var host = new GameObject("close range");
            // The EditMode scene may already hold a "Main Camera"; Camera.main must resolve to this test's camera.
            var otherCameras = new System.Collections.Generic.List<Camera>();
            foreach (Camera other in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (other.CompareTag("MainCamera")) { other.tag = "Untagged"; otherCameras.Add(other); }
            var cameraObject = new GameObject("active camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            Assert.That(Camera.main, Is.SameAs(cameraObject.GetComponent<Camera>()));
            cameraObject.transform.position = new Vector3(5, 1, 36);
            var materials = new WorldMaterials(host.transform);
            CharacterArt art = useArt ? CharacterArt.Load() : null;
            if (useArt) Assert.That(art, Is.Not.Null);
            var characters = new CharacterViews(materials, 1, Role.Survivor, art);
            try
            {
                characters.Initialize();
                var state = new S2C_WorldState { Tick = 1 };
                state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 8, Z = 36 });
                state.Players.Add(new PlayerState { Id = 2, Role = killer ? (byte)Role.Killer : (byte)Role.Survivor, X = 5, Z = 36 });
                characters.Accept(state);
                var snapshots = new SnapshotBuffer(); snapshots.Add(state, 0);
                characters.Render(.15, snapshots);
                Transform target = host.transform.Find(killer ? "Killer (line of sight)" : "Player 2");
                if (gap > 0)
                {
                    Bounds bounds = BoundsFromLocalCorners(target);
                    cameraObject.transform.position = bounds.center + Vector3.right * (bounds.extents.x + gap);
                    characters.Render(.16, snapshots);
                }
                Assert.That(target.gameObject.activeSelf, Is.True);
                Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty);
                bool expectedDither = gap <= .3f;
                foreach (Renderer renderer in renderers)
                {
                    Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.True, renderer.name);
                    Assert.That(renderer.sharedMaterials, Is.Not.Empty, renderer.name);
                    foreach (Material material in renderer.sharedMaterials)
                        Assert.That(material.shader.name == "PingTheKiller/Dither", Is.EqualTo(expectedDither), renderer.name);
                }
                if (killer) Assert.That(characters.KillerAlpha, Is.EqualTo(1));
                if (killer && gap > 0)
                {
                    var hidden = new S2C_WorldState { Tick = 2 };
                    hidden.Players.Add(state.Players[0]);
                    snapshots.Add(hidden, .1);
                    characters.Render(.3, snapshots);
                    Assert.That(characters.KillerAlpha, Is.GreaterThan(0).And.LessThan(1));
                    foreach (Renderer renderer in renderers)
                        foreach (Material material in renderer.sharedMaterials)
                        {
                            Assert.That(material.shader.name == "PingTheKiller/Dither", Is.EqualTo(expectedDither), renderer.name);
                            Assert.That(material.color.a, Is.EqualTo(characters.KillerAlpha).Within(.0001f), renderer.name);
                        }
                    Assert.That(characters.KillerVisorAlpha, Is.EqualTo(characters.KillerAlpha).Within(.0001f));
                }
                cameraObject.transform.position = new Vector3(10, 1, 36);
                characters.Render(.31, snapshots);
                foreach (Renderer renderer in renderers)
                    foreach (Material material in renderer.sharedMaterials)
                        Assert.That(material.shader.name, Is.Not.EqualTo("PingTheKiller/Dither"), renderer.name);
            }
            finally
            {
                characters.Dispose(); UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(cameraObject);
                foreach (Camera other in otherCameras) if (other != null) other.tag = "MainCamera";
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProximityUsesCurrentSkinnedTransformsEvenWhenWorldBoundsAreStale(bool killer)
        {
            CharacterArt art = CharacterArt.Load();
            Assert.That(art, Is.Not.Null);
            var root = UnityEngine.Object.Instantiate(killer ? art.KillerPrefab : art.SurvivorPrefab);
            var instance = new CharacterInstance(root, true);
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(5, 0, 36), Quaternion.Euler(0, 75, 0));
                root.transform.localScale = new Vector3(1.2f, 1, .8f);
                Bounds current = BoundsFromLocalCorners(root.transform);
                foreach (Renderer renderer in instance.Renderers)
                    renderer.bounds = new Bounds(Vector3.zero, Vector3.one);
                Assert.That(instance.NearCamera(current.center), Is.True);
                Assert.That(instance.NearCamera(current.center + Vector3.right * (current.extents.x + .29f)), Is.True);
                Assert.That(instance.NearCamera(current.center + Vector3.right * (current.extents.x + .31f)), Is.False);
                Assert.That(instance.NearCamera(Vector3.zero), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Bounds BoundsFromLocalCorners(Transform target)
        {
            Bounds result = default;
            bool first = true;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local = renderer.localBounds;
                Transform space = renderer is SkinnedMeshRenderer skinned && skinned.rootBone != null
                    ? skinned.rootBone : renderer.transform;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = space.TransformPoint(local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            Assert.That(first, Is.False, "character has renderers");
            return result;
        }

        [TestCase(.29f, true)]
        [TestCase(.31f, false)]
        public void ProximityIncludesThePointThreeMetreMargin(float gap, bool dither)
        {
            var root = UnityEngine.Object.Instantiate(CharacterArt.Load().SurvivorPrefab);
            var instance = new CharacterInstance(root, true);
            try
            {
                // Renderer.bounds of a skinned mesh is stale before its first render; compute from the current transforms.
                Bounds bounds = BoundsFromLocalCorners(root.transform);
                Assert.That(instance.NearCamera(bounds.center + Vector3.right * (bounds.extents.x + gap)), Is.EqualTo(dither));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
