using System;
using System.Collections.Generic;
using System.IO;
using PingTheKiller.Client.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Editor.Setup
{
    public sealed class CharacterArtBuildSetup : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) => CharacterArtSetup.Run();
    }

    public static class CharacterArtSetup
    {
        private const string Folder = "Assets/Art/Characters";
        private const string Rio = "Assets/ThirdParty/Unity Technologies/LowpolyCharacterRio/Models/Rio.fbx";
        private const string Kyle = "Assets/ThirdParty/UnityTechnologies/SpaceRobotKyle/Models/KyleRobot.fbx";
        private const string Animations = "Assets/ThirdParty/Kevin Iglesias/Human Animations/Animations/Male/";

        [MenuItem("Ping the Killer/Setup Character Art")]
        public static void Run()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterArt>("Assets/Resources/CharacterArt.asset");
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CharacterArt>();
                AssetDatabase.CreateAsset(catalog, "Assets/Resources/CharacterArt.asset");
            }
            catalog.Controller = Controller();
            catalog.SurvivorModel = Required<GameObject>(Rio);
            catalog.KillerModel = Required<GameObject>(Kyle);
            catalog.SurvivorMaterial = Material("Rio", "Assets/ThirdParty/Unity Technologies/LowpolyCharacterRio/Textures/Rio_AlbedoTransparency.png", Color.white);
            catalog.KillerMaterial = Material("Kyle", "Assets/ThirdParty/UnityTechnologies/SpaceRobotKyle/Textures/KyleRobot_BaseMap.png", Color.white);
            // Every anonymous instance uses this exact material, including every submesh.
            catalog.NeutralMaterial = Material("SignalNeutral", null, new Color32(128, 130, 134, 255));
            catalog.SurvivorPrefab = Prefab(catalog.SurvivorModel, catalog.Controller, catalog.SurvivorMaterial, "Rio", CharacterLook.Height, false);
            catalog.KillerPrefab = Prefab(catalog.KillerModel, catalog.Controller, catalog.KillerMaterial, "Kyle", CharacterLook.Height * 1.1f, true);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[CharacterArtSetup] Catalog, normalized Humanoid prefabs and locomotion controller ready.");
        }

        private static T Required<T>(string path) where T : UnityEngine.Object
            => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing character source: " + path);

        // U4i: toon (three bands, no rim, no specular) when the toon shader is available, else URP Lit as before.
        private static Material Material(string name, string texture, Color color)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader toon = ToonShading.Find();
            Shader shader = toon != null ? toon : Shader.Find("Universal Render Pipeline/Lit");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != shader)
            {
                material.shader = shader;
                material.shaderKeywords = new string[0];
            }
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture == null ? null : Required<Texture2D>(texture));
            material.SetFloat("_Cull", 2);
            if (toon != null) ToonShading.Configure(material, ToonShading.Kind.Character);
            else material.SetFloat("_Smoothness", .12f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static AnimationClip Clip(string path, string name)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(Animations + path))
            {
                if (!(asset is AnimationClip source) || source.name.StartsWith("__preview__")) continue;
                string target = Folder + "/" + name + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(target);
                if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, target); }
                EditorUtility.CopySerialized(source, clip);
                clip.name = name;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                settings.keepOriginalPositionXZ = true;
                settings.keepOriginalPositionY = true;
                settings.keepOriginalOrientation = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
                return clip;
            }
            throw new InvalidOperationException("No animation: " + path);
        }

        private static AnimatorController Controller()
        {
            AnimationClip idle = Clip("Idles/HumanM@Idle01.fbx", "Idle");
            AnimationClip walk = Clip("Movement/Walk/HumanM@Walk01_Forward.fbx", "Walk");
            AnimationClip run = Clip("Movement/Run/HumanM@Run01_Forward.fbx", "Run");
            string path = Folder + "/Locomotion.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.parameters = new[] { new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float } };
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState locomotion = null;
            foreach (ChildAnimatorState child in machine.states)
                if (child.state.name == "Locomotion") locomotion = child.state;
            if (locomotion == null) locomotion = machine.AddState("Locomotion");
            var tree = locomotion.motion as BlendTree;
            if (tree == null)
            {
                tree = new BlendTree { name = "Idle Walk Run" };
                AssetDatabase.AddObjectToAsset(tree, controller);
            }
            tree.blendParameter = "Speed";
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            tree.children = new[]
            {
                new ChildMotion { motion = idle, threshold = .49f, timeScale = 1 },
                new ChildMotion { motion = walk, threshold = .5f, timeScale = 1 },
                new ChildMotion { motion = walk, threshold = 4, timeScale = 1 },
                new ChildMotion { motion = run, threshold = 4.1f, timeScale = 1 }
            };
            EditorUtility.SetDirty(tree);
            locomotion.motion = tree;
            machine.defaultState = locomotion;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject Prefab(GameObject source, RuntimeAnimatorController controller, Material material, string name, float height, bool killer)
        {
            var root = new GameObject(name);
            try
            {
                GameObject model = UnityEngine.Object.Instantiate(source, root.transform);
                model.name = "Rig";
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                Animator animator = model.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman) throw new InvalidOperationException(name + " requires a Humanoid avatar");
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float scale = height / bounds.size.y;
                model.transform.localScale *= scale;
                model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                foreach (Renderer renderer in renderers)
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++) slots[i] = material;
                    renderer.sharedMaterials = slots;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
                }
                if (killer) SplitHead(root, renderers, height);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + ".prefab");
                Debug.Log("[CharacterArtSetup] " + name + " rest bounds=" + height + " scale=" + scale + " renderers=" + renderers.Length);
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Kyle has one atlas. Separate its actual head triangles so the existing visor fade probe still reads a real renderer.
        private static void SplitHead(GameObject root, Renderer[] renderers, float height)
        {
            if (!(renderers[0] is SkinnedMeshRenderer body)) throw new InvalidOperationException("Kyle requires a skinned mesh");
            Mesh source = body.sharedMesh;
            // Read the imported mesh without changing vendor import settings.
            using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(source))
            {
                var copy = new Mesh();
                var writable = Mesh.AllocateWritableMeshData(1);
                {
                    Mesh.MeshData destination = writable[0];
                    var descriptors = source.GetVertexAttributes();
                    writable[0].SetVertexBufferParams(source.vertexCount, descriptors);
                    for (int stream = 0; stream < source.vertexBufferCount; stream++) writable[0].GetVertexData<byte>(stream).CopyFrom(data[0].GetVertexData<byte>(stream));
                    writable[0].SetIndexBufferParams(data[0].GetIndexData<byte>().Length / (source.indexFormat == IndexFormat.UInt16 ? 2 : 4), source.indexFormat);
                    writable[0].GetIndexData<byte>().CopyFrom(data[0].GetIndexData<byte>());
                    destination.subMeshCount = source.subMeshCount;
                    for (int i = 0; i < source.subMeshCount; i++) writable[0].SetSubMesh(i, data[0].GetSubMesh(i));
                    Mesh.ApplyAndDisposeWritableMeshData(writable, copy);
                }
                copy.bindposes = source.bindposes;
                Vector3[] vertices = copy.vertices;
                var torso = new List<int>(); var head = new List<int>();
                for (int sub = 0; sub < copy.subMeshCount; sub++)
                {
                    int[] triangles = copy.GetTriangles(sub);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 centre = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3;
                        List<int> target = root.transform.InverseTransformPoint(body.transform.TransformPoint(centre)).y > height * .78f ? head : torso;
                        target.Add(triangles[i]); target.Add(triangles[i + 1]); target.Add(triangles[i + 2]);
                    }
                }
                if (head.Count == 0 || torso.Count == 0) throw new InvalidOperationException("Kyle head separation produced an empty mesh");
                body.transform.SetParent(root.transform, true); body.name = "Killer body";
                var headObject = new GameObject("Killer visor");
                headObject.transform.SetParent(root.transform, false);
                headObject.transform.localPosition = body.transform.localPosition;
                headObject.transform.localRotation = body.transform.localRotation;
                headObject.transform.localScale = body.transform.localScale;
                var visor = headObject.AddComponent<SkinnedMeshRenderer>();
                visor.bones = body.bones; visor.rootBone = body.rootBone;
                visor.localBounds = body.localBounds;
                visor.updateWhenOffscreen = true;
                visor.sharedMaterials = body.sharedMaterials;
                visor.shadowCastingMode = ShadowCastingMode.Off;
                visor.receiveShadows = false;
                Mesh headMesh = UnityEngine.Object.Instantiate(copy);
                body.sharedMesh = SaveMesh(copy, torso, "KyleBody");
                visor.sharedMesh = SaveMesh(headMesh, head, "KyleHead");
            }
        }

        private static Mesh SaveMesh(Mesh mesh, List<int> triangles, string name)
        {
            mesh.subMeshCount = 1; mesh.SetTriangles(triangles, 0); mesh.name = name;
            string path = Folder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }
    }
}

