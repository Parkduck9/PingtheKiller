using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.Tests
{
    public sealed class CharacterArtTests
    {
        [Test]
        public void CatalogLoadsWithBothHumanoidModelsAndLocomotion()
        {
            CharacterArt art = CharacterArt.Load();
            Assert.That(art, Is.Not.Null, "Run CharacterArtSetup.Run before testing/building.");
            Assert.That(art.SurvivorModel, Is.Not.Null);
            Assert.That(art.KillerModel, Is.Not.Null);
            Assert.That(art.Controller, Is.Not.Null);
            Assert.That(art.SurvivorMaterial, Is.Not.Null);
            Assert.That(art.KillerMaterial, Is.Not.Null);
            foreach (GameObject prefab in new[] { art.SurvivorPrefab, art.KillerPrefab })
            {
                Animator animator = prefab.GetComponentInChildren<Animator>(true);
                Assert.That(animator.avatar.isHuman && animator.avatar.isValid, Is.True);
                Assert.That(animator.applyRootMotion, Is.False);
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(art.Controller));
                foreach (string clip in new[] { "Idle", "Walk", "Run" })
                    Assert.That(System.Array.Exists(art.Controller.animationClips, x => x.name == clip), Is.True, clip);
            }
        }

        [TestCase(false, 1.8f)]
        [TestCase(true, 1.98f)]
        public void PrefabBoundsFeetShadowsAndColliders(bool killer, float height)
        {
            CharacterArt art = CharacterArt.Load();
            Assert.That(art, Is.Not.Null);
            GameObject root = Object.Instantiate(killer ? art.KillerPrefab : art.SurvivorPrefab);
            try
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0));
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                }
                Assert.That(bounds.size.y, Is.EqualTo(height).Within(.05f));
                Assert.That(bounds.min.y, Is.EqualTo(0).Within(.025f));
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MissingCatalogStillCreatesAndRendersCapsules()
        {
            var root = new GameObject("fallback");
            var materials = new WorldMaterials(root.transform);
            var characters = new CharacterViews(materials, 1, Role.Survivor, null);
            try
            {
                characters.Initialize();
                var state = new S2C_WorldState { Tick = 1 };
                state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 2, Z = 2 });
                characters.Accept(state);
                var snapshots = new SnapshotBuffer(); snapshots.Add(state, 0);
                characters.Render(.15, snapshots);
                Assert.That(root.transform.Find("Player 1").gameObject.activeSelf, Is.True);
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void AnonymousInstancesUseOneSharedNeutralMaterial()
        {
            CharacterArt art = CharacterArt.Load();
            Assert.That(art, Is.Not.Null);
            var a = Object.Instantiate(art.SurvivorPrefab);
            try
            {
                // Exercise the actual KillerWorldLayer instance factory without a HUD or networking.
                var layer = a.AddComponent<PingTheKiller.Client.Killer.KillerWorldLayer>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(PingTheKiller.Client.Killer.KillerWorldLayer).GetField("art", flags).SetValue(layer, art);
                typeof(PingTheKiller.Client.Killer.KillerWorldLayer).GetField("body", flags).SetValue(layer, art.NeutralMaterial);
                var get = layer.GetType().GetMethod("Get", flags);
                object first = get.Invoke(layer, new object[] { 1 }), second = get.Invoke(layer, new object[] { 2 });
                var field = first.GetType().GetField("Root");
                foreach (object instance in new[] { first, second })
                    foreach (Renderer renderer in ((GameObject)field.GetValue(instance)).GetComponentsInChildren<Renderer>(true))
                        foreach (Material material in renderer.sharedMaterials) Assert.That(material, Is.SameAs(art.NeutralMaterial));
            }
            finally { Object.DestroyImmediate(a); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LocalDitherChangesEveryMaterialAndKeepsLocalFacing(bool useArt)
        {
            CharacterArt art = useArt ? CharacterArt.Load() : null;
            if (useArt) Assert.That(art, Is.Not.Null);
            var root = new GameObject("local dither");
            var characters = new CharacterViews(new WorldMaterials(root.transform), 1, Role.Survivor, art);
            try
            {
                var state = new S2C_WorldState { Tick = 1 };
                state.Players.Add(new PlayerState { Id = 1, Role = (byte)Role.Survivor, X = 2, Z = 2, Yaw = 10 });
                characters.Accept(state);
                var snapshots = new SnapshotBuffer(); snapshots.Add(state, 0);
                characters.SetLocalLook(90, true);
                characters.Render(.15, snapshots);
                Transform local = root.transform.Find("Player 1");
                Assert.That(local.gameObject.activeSelf, Is.True);
                Assert.That(local.forward.x, Is.EqualTo(1).Within(.0001f));
                Renderer[] renderers = local.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty);
                foreach (Renderer renderer in renderers)
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material.shader.name, Is.EqualTo("PingTheKiller/Dither"), renderer.name);
                        Assert.That(material.GetFloat("_Discard"), Is.EqualTo(.4f));
                    }
                characters.SetLocalLook(90, false);
                characters.Render(.16, snapshots);
                foreach (Renderer renderer in renderers)
                    foreach (Material material in renderer.sharedMaterials)
                        Assert.That(material.shader.name, Is.Not.EqualTo("PingTheKiller/Dither"), renderer.name);
            }
            finally { characters.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void AnimationSpeedComesFromMotionAndLaggingDoesNotAdvanceBetweenSteps()
        {
            var root = Object.Instantiate(CharacterArt.Load().SurvivorPrefab);
            var instance = new CharacterInstance(root, true);
            try
            {
                instance.Animate(Vector3.zero, 0, false, false);
                for (int i = 1; i <= 30; i++) instance.Animate(Vector3.forward * (i * .05f * 4.6f), i * .05, false, false);
                Assert.That(instance.Speed, Is.GreaterThan(4));
                Assert.That(instance.Animator.GetFloat("Speed"), Is.EqualTo(instance.Speed).Within(.0001f),
                    "motion must reach the Animator parameter, not a shader property hash");
                instance.Animate(Vector3.zero, 2, true, true);
                float before = instance.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                instance.Animate(Vector3.zero, 2.04, true, true);
                Assert.That(instance.Animator.speed, Is.Zero);
                Assert.That(instance.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(before));
            }
            finally { instance.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void AllKyleMaterialsFadeThenReturnToOpaqueAndDitherKeepsTheTexture()
        {
            var root = Object.Instantiate(CharacterArt.Load().KillerPrefab);
            var instance = new CharacterInstance(root);
            try
            {
                instance.Look(Color.red, .5f, false);
                foreach (Renderer renderer in instance.Renderers)
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material.GetFloat("_Surface"), Is.EqualTo(1));
                        Assert.That(material.color.a, Is.EqualTo(.5f));
                    }
                instance.Look(Color.red, .5f, true);
                foreach (Renderer renderer in instance.Renderers)
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material.shader.name, Is.EqualTo("PingTheKiller/Dither"));
                        Assert.That(material.GetTexture("_BaseMap"), Is.Not.Null);
                        Assert.That(material.color.a, Is.EqualTo(.5f));
                        Assert.That(material.GetFloat("_Discard"), Is.EqualTo(.4f));
                    }
                instance.Look(Color.red, 1, false);
                foreach (Renderer renderer in instance.Renderers)
                    foreach (Material material in renderer.sharedMaterials)
                        Assert.That(material.GetFloat("_Surface"), Is.Zero);
                Assert.That(instance.Animator.applyRootMotion, Is.False);
            }
            finally { instance.Dispose(); Object.DestroyImmediate(root); }
        }
    }
}
