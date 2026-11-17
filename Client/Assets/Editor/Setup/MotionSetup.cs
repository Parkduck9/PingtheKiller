using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PingTheKiller.Client.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PingTheKiller.Editor.Setup
{
    // Runs after CharacterArtSetup (order 0) so the Idle/Walk/Run clips it copies exist.
    public sealed class MotionBuildSetup : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => 1;
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) => MotionSetup.Run();
    }

    // U4j: imports the Mixamo clips as Humanoid (one source avatar, copied to the others; loop flags; root motion baked
    // into the pose) and builds Resources/Motion/Motion.controller (locomotion + state clips + an upper-body action layer).
    // Idempotent: importer settings are written only when they differ, and controller states are found by name.
    public static class MotionSetup
    {
        public const string Folder = "Assets/Art/Characters/Mixamo";
        public const string ControllerPath = "Assets/Resources/Motion/Motion.controller";
        public const string MaskPath = Folder + "/UpperBody.mask";
        private const string Locomotion = "Assets/Art/Characters/";

        public static string ClipPath(string file) => Folder + "/Mixamo@" + file + ".fbx";

        // QA only: PTK_MOTION_A1=1 re-imports with the attempt-1 settings (every XZ baked as authored, the full swipe take)
        // and turns the hips centring off, so before/after renders come from the same code. Renders are prefixed a1- / a2-.
        public static bool A1 => Environment.GetEnvironmentVariable("PTK_MOTION_A1") == "1";
        private static string Prefix => A1 ? "a1-" : "a2-";

        [MenuItem("Ping the Killer/Setup Character Motion")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            Import(MotionLibrary.AvatarSource, null);
            Avatar avatar = SourceAvatar();
            foreach (MotionLibrary.Entry entry in MotionLibrary.Clips)
                if (entry.File != MotionLibrary.AvatarSource) Import(entry.File, avatar);
            Controller();
            AssetDatabase.SaveAssets();
            Debug.Log("[MotionSetup] Mixamo clips imported as Humanoid and Motion.controller ready.");
        }

        // -executeMethod PingTheKiller.Editor.Setup.MotionSetup.Batch
        public static void Batch()
        {
            int code = 0;
            try
            {
                MotionGrounding.CentreHips = !A1;
                Run(); Analyze();
                string folder = Environment.GetEnvironmentVariable("PTK_RENDER_DIR") ?? "../docs/qa/U4j-screens";
                Sheets(folder); Shots(folder); Transition(folder);
            }
            catch (Exception error) { Debug.LogException(error); code = 1; }
            EditorApplication.Exit(code);
        }

        public static Avatar SourceAvatar()
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ClipPath(MotionLibrary.AvatarSource)))
                if (asset is Avatar avatar) return avatar;
            throw new InvalidOperationException("No avatar imported from " + ClipPath(MotionLibrary.AvatarSource));
        }

        public static AnimationClip LoadClip(string file)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ClipPath(file)))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            throw new InvalidOperationException("No clip imported from " + ClipPath(file));
        }

        private static void Import(string file, Avatar source)
        {
            string path = ClipPath(file);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing Mixamo clip " + path);
            MotionLibrary.Entry entry = Array.Find(MotionLibrary.Clips, x => x.File == file);
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; changed = true; }
            ModelImporterAvatarSetup setup = source == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
            if (importer.avatarSetup != setup) { importer.avatarSetup = setup; changed = true; }
            if (source != null && importer.sourceAvatar != source) { importer.sourceAvatar = source; changed = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (importer.importCameras || importer.importLights) { importer.importCameras = false; importer.importLights = false; changed = true; }
            if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }
            if (changed) importer.SaveAndReimport();

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) throw new InvalidOperationException("No take in " + path);
            ModelImporterClipAnimation clip = clips[0];
            // Root motion is never applied (positions are server-owned). Rotation and height are baked into the pose as
            // authored; XZ per MotionLibrary: falls are not baked (centre of mass, so the body falls in place), the crawl
            // is baked around its centre of mass, the rest are baked as authored.
            bool bakeXZ = A1 || !MotionLibrary.FallsInPlace(file), originalXZ = A1 || !MotionLibrary.CentredOnMass(file);
            ModelImporterClipAnimation take = importer.defaultClipAnimations[0];
            float first = take.firstFrame, last = take.lastFrame;
            if (entry.State == MotionStates.Swipe && !A1)
            {
                first = Mathf.Round(MotionTiming.SwipeFromSeconds * MixamoFps);
                last = first + Mathf.Round(MotionTiming.SwipeClipSeconds * MixamoFps);
            }
            bool same = importer.clipAnimations != null && importer.clipAnimations.Length == 1 && clip.name == file
                && clip.loopTime == entry.Loop && !clip.loopPose && clip.lockRootRotation && clip.keepOriginalOrientation
                && clip.lockRootHeightY && clip.keepOriginalPositionY && !clip.heightFromFeet
                && clip.lockRootPositionXZ == bakeXZ && clip.keepOriginalPositionXZ == originalXZ
                && Mathf.Approximately(clip.firstFrame, first) && Mathf.Approximately(clip.lastFrame, last);
            if (same) return;
            clip.name = file; clip.loopTime = entry.Loop; clip.loopPose = false;
            clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true; clip.keepOriginalPositionY = true; clip.heightFromFeet = false;
            clip.lockRootPositionXZ = bakeXZ; clip.keepOriginalPositionXZ = originalXZ;
            clip.firstFrame = first; clip.lastFrame = last;
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
        }

        // Mixamo FBX takes are 30 fps (the KillerSwipe take: frames 0..80 = 2.667 s).
        public const float MixamoFps = 30;

        private static AvatarMask UpperBody()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, MaskPath); }
            for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm
                    || part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                    || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                if (mask.GetHumanoidBodyPartActive(part) != upper) mask.SetHumanoidBodyPartActive(part, upper);
            }
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static AnimationClip Shared(string name)
            => AssetDatabase.LoadAssetAtPath<AnimationClip>(Locomotion + name + ".anim")
               ?? throw new InvalidOperationException("Run CharacterArtSetup first: missing " + name + ".anim");

        public static AnimatorController Controller()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (controller.parameters.Length != 1 || controller.parameters[0].name != MotionStates.SpeedParameter)
                controller.parameters = new[] { new AnimatorControllerParameter { name = MotionStates.SpeedParameter, type = AnimatorControllerParameterType.Float } };

            AnimationClip idle = Shared("Idle"), walk = Shared("Walk"), run = Shared("Run");
            AnimatorStateMachine body = controller.layers[0].stateMachine;
            // U10: speed-driven idle -> walk -> run. Each child's time scale = its threshold speed / the clip's own ground
            // speed (LocomotionBlend), so the feet do not slide; a Survivor at 4.0 m/s is fully in the run (it played the
            // walk up to 4.0 before), the Killer at 4.6 / 6.9 (Overclock) runs faster. There is no Survivor speed above 4.0,
            // so no sprint clip is needed.
            AnimatorState locomotion = State(body, MotionStates.Locomotion, Tree(controller, "Idle Walk Run", new[]
            {
                new ChildMotion { motion = idle, threshold = LocomotionBlend.IdleBelow, timeScale = 1 },
                new ChildMotion { motion = walk, threshold = LocomotionBlend.WalkFrom, timeScale = LocomotionBlend.Rate(LocomotionBlend.WalkFrom, LocomotionBlend.WalkNatural) },
                new ChildMotion { motion = walk, threshold = LocomotionBlend.WalkSpeed, timeScale = LocomotionBlend.Rate(LocomotionBlend.WalkSpeed, LocomotionBlend.WalkNatural) },
                new ChildMotion { motion = run, threshold = LocomotionBlend.RunSpeed, timeScale = LocomotionBlend.Rate(LocomotionBlend.RunSpeed, LocomotionBlend.RunNatural) },
                new ChildMotion { motion = run, threshold = LocomotionBlend.FastSpeed, timeScale = LocomotionBlend.Rate(LocomotionBlend.FastSpeed, LocomotionBlend.RunNatural) }
            }), 1);
            // Injured (survivor view only): the limp when still or slow, then the same run at full speed (U10: a sped-up
            // limp read wrong at 4.0 m/s).
            AnimationClip injuredWalk = LoadClip("InjuredWalk");
            State(body, MotionStates.Injured, Tree(controller, "Injured", new[]
            {
                new ChildMotion { motion = LoadClip("InjuredIdle"), threshold = LocomotionBlend.IdleBelow, timeScale = 1 },
                new ChildMotion { motion = injuredWalk, threshold = LocomotionBlend.WalkFrom, timeScale = LocomotionBlend.Rate(LocomotionBlend.WalkFrom, LocomotionBlend.InjuredWalkNatural) },
                new ChildMotion { motion = injuredWalk, threshold = LocomotionBlend.InjuredLimpSpeed, timeScale = LocomotionBlend.Rate(LocomotionBlend.InjuredLimpSpeed, LocomotionBlend.InjuredWalkNatural) },
                new ChildMotion { motion = run, threshold = LocomotionBlend.RunSpeed, timeScale = LocomotionBlend.Rate(LocomotionBlend.RunSpeed, LocomotionBlend.RunNatural) },
                new ChildMotion { motion = run, threshold = LocomotionBlend.FastSpeed, timeScale = LocomotionBlend.Rate(LocomotionBlend.FastSpeed, LocomotionBlend.RunNatural) }
            }), 1);
            // Lagging states run under CharacterInstance's 0.55 Lagging time scale; the speed undoes it so the knock-down
            // takes its real length (the 8 Hz stepping stays).
            float lagging = 1 / MotionTiming.LaggingRate;
            State(body, MotionStates.Hit, LoadClip("HitReaction"), lagging);
            State(body, MotionStates.KnockedDown, LoadClip("KnockedDown"), lagging);
            // Crawl rate follows the Speed parameter (min(speed, 1) while Lagging): 0 when still freezes the last crawl pose.
            AnimatorState crawl = State(body, MotionStates.Crawl, LoadClip("Crawling"), lagging / MotionTiming.CrawlMetresPerSecond);
            if (!crawl.speedParameterActive || crawl.speedParameter != MotionStates.SpeedParameter)
            {
                crawl.speedParameterActive = true; crawl.speedParameter = MotionStates.SpeedParameter;
            }
            State(body, MotionStates.GettingUp, LoadClip("GettingUp"), 1);
            State(body, MotionStates.Death, LoadClip("FallingBackDeath"), 1);
            State(body, MotionStates.Working, LoadClip("ButtonPushing"), 1);
            if (body.defaultState != locomotion) body.defaultState = locomotion;

            AnimatorControllerLayer[] layers = controller.layers;
            // The base layer's IK pass runs MotionReach and the U10 InteractionRig (hack panel, restore, switch, jam).
            if (!layers[0].iKPass) { layers[0].iKPass = true; controller.layers = layers; layers = controller.layers; }
            if (layers.Length < 2)
            {
                controller.AddLayer(MotionStates.ActionLayer);
                layers = controller.layers;
            }
            AnimatorControllerLayer action = layers[1];
            action.name = MotionStates.ActionLayer;
            action.avatarMask = UpperBody();
            action.blendingMode = AnimatorLayerBlendingMode.Override;
            action.defaultWeight = 0; // the driver raises it only while an attack plays
            controller.layers = layers;
            AnimatorStateMachine upper = controller.layers[1].stateMachine;
            AnimatorState empty = State(upper, MotionStates.Empty, null, 1);
            empty.writeDefaultValues = false;
            State(upper, MotionStates.Swipe, LoadClip("KillerSwipe"), 0); // U8: CharacterMotion.Drive sets its time every frame
            RemoveState(upper, "Punch"); // attempt 1 alternated a punch; the swipe alone is used now
            if (upper.defaultState != empty) upper.defaultState = empty;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static BlendTree Tree(AnimatorController controller, string name, ChildMotion[] children)
        {
            BlendTree tree = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (asset is BlendTree existing && existing.name == name) tree = existing;
            if (tree == null)
            {
                tree = new BlendTree { name = name, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(tree, controller);
            }
            tree.blendParameter = MotionStates.SpeedParameter;
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            tree.children = children;
            EditorUtility.SetDirty(tree);
            return tree;
        }

        private static void RemoveState(AnimatorStateMachine machine, string name)
        {
            foreach (ChildAnimatorState child in machine.states) if (child.state.name == name) machine.RemoveState(child.state);
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion, float speed)
        {
            AnimatorState state = null;
            foreach (ChildAnimatorState child in machine.states) if (child.state.name == name) state = child.state;
            if (state == null) state = machine.AddState(name);
            if (state.motion != motion) state.motion = motion;
            if (!Mathf.Approximately(state.speed, speed)) state.speed = speed;
            // Foot IK puts the feet where the source rig had them relative to the body (Rio's and Kyle's legs differ).
            bool footIk = Environment.GetEnvironmentVariable("PTK_FOOTIK") != "0";
            if (state.iKOnFeet != footIk) state.iKOnFeet = footIk;
            EditorUtility.SetDirty(state);
            return state;
        }

        // Batch evidence: for each state on Rio and Kyle, the clip length, the lowest skinned vertex over the clip (floor
        // sinking), the lowest hand / knee / foot bone, the hips start/end and facing, and for the in-place loops the
        // ground speed of the planted limbs.
        public static void Analyze()
        {
            CharacterArt art = AssetDatabase.LoadAssetAtPath<CharacterArt>("Assets/Resources/CharacterArt.asset");
            var log = new StringBuilder("[MotionSetup] analysis\n");
            var parts = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.Head };
            foreach (GameObject prefab in new[] { art.SurvivorPrefab, art.KillerPrefab })
            {
                var actor = new Actor(prefab, null, Vector3.zero, 0);
                try
                {
                    Animator animator = actor.Animator;
                    var skinned = actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    var mesh = new Mesh();
                    actor.Pose(MotionStates.Locomotion, 0);
                    log.Append(prefab.name).Append(" idle ground=").Append(F(actor.Motion.GroundOffset)).Append(" minY=").Append(F(Lowest(skinned, mesh))).Append('\n');
                    foreach (MotionLibrary.Entry entry in MotionLibrary.Clips)
                    {
                        if (entry.State == MotionStates.InjuredIdle || entry.State == MotionStates.InjuredWalk) continue;
                        AnimationClip clip = LoadClip(entry.File);
                        const int samples = 60;
                        float minY = float.MaxValue, minAt = 0;
                        var limbs = new Vector3[samples + 1, parts.Length];
                        var lowestLimb = new float[parts.Length];
                        for (int k = 0; k < parts.Length; k++) lowestLimb[k] = float.MaxValue;
                        Vector3 hipsStart = default, hipsEnd = default;
                        float faceStart = 0, faceEnd = 0;
                        string layerInfo = string.Empty;
                        float rawMin = float.MaxValue, offsetMin = float.MaxValue, offsetMax = float.MinValue;
                        for (int i = 0; i <= samples; i++)
                        {
                            float t = i / (float)samples;
                            actor.Grounded = false;
                            actor.Pose(entry.State, t);
                            rawMin = Mathf.Min(rawMin, Lowest(skinned, mesh));
                            actor.Motion.GroundAs(Actor.PoseOf(entry.State));
                            offsetMin = Mathf.Min(offsetMin, actor.Motion.GroundOffset); offsetMax = Mathf.Max(offsetMax, actor.Motion.GroundOffset);
                            if (i == samples / 2 && entry.State == MotionStates.Swipe)
                            {
                                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(1);
                                layerInfo = " layer1 weight=" + F(animator.GetLayerWeight(1)) + " state=" + (info.shortNameHash == Animator.StringToHash(entry.State)) + " t=" + F(info.normalizedTime)
                                    + " rightHand=" + V(animator.GetBoneTransform(HumanBodyBones.RightHand).position);
                            }
                            float lowest = Lowest(skinned, mesh);
                            if (lowest < minY) { minY = lowest; minAt = t; }
                            for (int k = 0; k < parts.Length; k++)
                            {
                                limbs[i, k] = animator.GetBoneTransform(parts[k]).position;
                                lowestLimb[k] = Mathf.Min(lowestLimb[k], limbs[i, k].y);
                            }
                            Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                            if (i == 0) { hipsStart = hips; faceStart = FaceUp(animator); }
                            if (i == samples) { hipsEnd = hips; faceEnd = FaceUp(animator); }
                        }
                        var planted = new List<float>();
                        if (entry.Loop)
                            for (int k = 0; k < 8; k++)
                                for (int i = 1; i <= samples; i++)
                                    if (limbs[i, k].y < lowestLimb[k] + .03f && limbs[i - 1, k].y < lowestLimb[k] + .03f)
                                        planted.Add(-(limbs[i, k].z - limbs[i - 1, k].z) / (clip.length / samples));
                        planted.Sort();
                        log.Append(prefab.name).Append(' ').Append(entry.State).Append(" length=").Append(F(clip.length)).Append(" loop=").Append(clip.isLooping)
                            .Append(" human=").Append(clip.isHumanMotion).Append(" rawMinY=").Append(F(rawMin)).Append(" ground=").Append(F(offsetMin)).Append("..").Append(F(offsetMax)).Append(" minY=").Append(F(minY)).Append('@').Append(F(minAt))
                            .Append(" hands=").Append(F(Mathf.Min(lowestLimb[0], lowestLimb[1]))).Append(" knees=").Append(F(Mathf.Min(lowestLimb[2], lowestLimb[3])))
                            .Append(" feet=").Append(F(Mathf.Min(lowestLimb[4], lowestLimb[5]))).Append(" toes=").Append(F(Mathf.Min(lowestLimb[6], lowestLimb[7]))).Append(" head=").Append(F(lowestLimb[8]))
                            .Append(" hipsStart=").Append(V(hipsStart)).Append(" hipsEnd=").Append(V(hipsEnd))
                            .Append(" faceDownStart=").Append(F(faceStart)).Append(" faceDownEnd=").Append(F(faceEnd)).Append(layerInfo);
                        if (planted.Count > 0) log.Append(" plantedSpeed p25/50/75=").Append(F(planted[planted.Count / 4])).Append('/').Append(F(planted[planted.Count / 2])).Append('/').Append(F(planted[planted.Count * 3 / 4])).Append(" n=").Append(planted.Count);
                        float lMin = float.MaxValue, lMax = float.MinValue, rMin = float.MaxValue, rMax = float.MinValue;
                        for (int i = 0; i <= samples; i++)
                        {
                            lMin = Mathf.Min(lMin, limbs[i, 0].y); lMax = Mathf.Max(lMax, limbs[i, 0].y);
                            rMin = Mathf.Min(rMin, limbs[i, 1].y); rMax = Mathf.Max(rMax, limbs[i, 1].y);
                        }
                        log.Append(" leftHandY=").Append(F(lMin)).Append("..").Append(F(lMax)).Append(" rightHandY=").Append(F(rMin)).Append("..").Append(F(rMax))
                            .Append(" hipsXZtravel=").Append(F(new Vector2(hipsEnd.x - hipsStart.x, hipsEnd.z - hipsStart.z).magnitude));
                        log.Append('\n');
                    }
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                finally { UnityEngine.Object.DestroyImmediate(actor.Root); }
            }
            Debug.Log(log.ToString());
        }


        // The lowest skinned vertex in world space (BakeMesh with scale, then position and rotation only).
        public static float Lowest(SkinnedMeshRenderer[] skinned, Mesh mesh)
        {
            float lowest = float.MaxValue;
            foreach (SkinnedMeshRenderer renderer in skinned)
            {
                renderer.BakeMesh(mesh, true);
                Matrix4x4 m = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                foreach (Vector3 v in mesh.vertices) lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v).y);
            }
            return lowest;
        }

        private static string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";

        // The y of the chest's front normal: +1 lying face up, -1 face down, about 0 upright.
        private static float FaceUp(Animator animator)
        {
            Vector3 up = animator.GetBoneTransform(HumanBodyBones.Head).position - animator.GetBoneTransform(HumanBodyBones.Hips).position;
            Vector3 across = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position - animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            return Vector3.Cross(across, up).normalized.y;
        }

        // Single 1280x720 editor frames at gameplay-like distance: the Killer mid-swipe over a survivor taking the hit, the
        // knock-down, the crawl, the get-up, button pushing at the imported terminal, and the death (mid fall and held).
        private static void Shots(string folder)
        {
            CharacterArt art = AssetDatabase.LoadAssetAtPath<CharacterArt>("Assets/Resources/CharacterArt.asset");
            using (var stage = new Stage(Width, Height))
            {
                stage.Camera.fieldOfView = 45;
                var eye = new Vector3(3.2f, 2.1f, -2.6f);
                var killer = new Actor(art.KillerPrefab, stage.Root.transform, Vector3.zero, 0);
                var victim = new Actor(art.SurvivorPrefab, stage.Root.transform, new Vector3(0, 0, .95f), 180);
                killer.Pose(MotionStates.Swipe, .5f); victim.Pose(MotionStates.Hit, .45f);
                killer.Bake(); victim.Bake();
                Shot(stage, folder, "shot-swipe.png", eye, new Vector3(0, .9f, .45f));
                killer.Unbake(); UnityEngine.Object.DestroyImmediate(killer.Root);

                var plan = new (string State, float T, string Name)[]
                {
                    (MotionStates.KnockedDown, .45f, "shot-knockdown.png"), (MotionStates.Crawl, .5f, "shot-crawl.png"),
                    (MotionStates.GettingUp, .55f, "shot-getup.png"), (MotionStates.Death, .45f, "shot-death.png"),
                    (MotionStates.Death, 1f, "shot-death-held.png"),
                };
                victim.Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 200, 0));
                foreach (var item in plan)
                {
                    victim.Pose(item.State, item.T); victim.Bake();
                    Shot(stage, folder, item.Name, eye, new Vector3(0, .45f, -.3f));
                }
                victim.Unbake();

                // Loaded after the stage: NewScene unloads unreferenced assets.
                WorldArt world = AssetDatabase.LoadAssetAtPath<WorldArt>("Assets/Resources/WorldArt.asset");
                GameObject terminal = world != null && world.Terminal != null ? UnityEngine.Object.Instantiate(world.Terminal, stage.Root.transform) : null;
                victim.Root.transform.SetPositionAndRotation(new Vector3(0, 0, -.72f), Quaternion.identity);
                victim.Pose(MotionStates.Working, .4f); victim.Bake();
                Shot(stage, folder, "shot-button.png", new Vector3(2.3f, 1.6f, -1.9f), new Vector3(0, .75f, -.45f));
                victim.Unbake();
                if (terminal == null) Debug.LogWarning("[MotionSetup] no WorldArt terminal; shot-button.png has no device");
            }
        }

        public static void RenderShots()
        {
            int code = 0;
            try { Shots(Environment.GetEnvironmentVariable("PTK_RENDER_DIR") ?? "../docs/qa/U4j-screens"); }
            catch (Exception error) { Debug.LogException(error); code = 1; }
            EditorApplication.Exit(code);
        }

        // The capture as the game plays it (the driver: Observe, Drive, Animator update at the Lagging rate, Ground), from
        // the side, with a red disc at the authoritative position: Hit -> KnockedDown -> Crawl at 6 times after the capture.
        private static void Transition(string folder)
        {
            CharacterArt art = AssetDatabase.LoadAssetAtPath<CharacterArt>("Assets/Resources/CharacterArt.asset");
            double[] times = { .15, .6, 1.1, 1.7, 2.45, 3.2 };
            const double capture = 1, step = 1 / 30.0;
            using (var stage = new Stage(426, 360))
            {
                GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.transform.SetParent(stage.Root.transform, false);
                disc.transform.localScale = new Vector3(.18f, .004f, .18f);
                disc.transform.localPosition = new Vector3(0, .006f, 0);
                disc.GetComponent<Renderer>().sharedMaterial = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WorldLit.mat")) { color = new Color32(220, 40, 40, 255) };
                var actor = new Actor(art.SurvivorPrefab, stage.Root.transform, Vector3.zero, 0);
                var sheet = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                sheet.SetPixels(new Color[Width * Height]);
                var log = new StringBuilder("[MotionSetup] transition (" + Prefix.TrimEnd('-') + ") hipsXZ:");
                int shot = 0;
                double now = 0;
                for (; shot < times.Length; now += step)
                {
                    byte status = (byte)(now < capture ? 0 : 1); // PlayerStatus Normal, Lagging (Shared is not referenced here)
                    actor.Motion.Observe(now, status, 0, 0, 0);
                    actor.Motion.Drive(now);
                    actor.Animator.SetFloat(MotionStates.SpeedParameter, 0);
                    actor.Animator.Update((float)step * (now < capture ? 1 : MotionTiming.LaggingRate));
                    actor.Motion.Ground();
                    if (now - capture < times[shot]) continue;
                    Vector3 hips = actor.Root.transform.InverseTransformPoint(actor.Animator.GetBoneTransform(HumanBodyBones.Hips).position);
                    log.Append(' ').Append(F((float)(now - capture))).Append("s=").Append(V(new Vector3(hips.x, 0, hips.z)));
                    actor.Bake();
                    stage.Camera.fieldOfView = 50;
                    Texture2D tile = stage.Render(new Vector3(3.2f, 1.1f, 0), new Vector3(0, .5f, 0));
                    sheet.SetPixels((shot % 3) * 426, (1 - shot / 3) * 360, 426, 360, tile.GetPixels());
                    UnityEngine.Object.DestroyImmediate(tile);
                    shot++;
                }
                actor.Unbake();
                Debug.Log(log.ToString());
                sheet.Apply();
                Save(sheet, folder, "transition-capture.png");
                UnityEngine.Object.DestroyImmediate(actor.Root);
            }
        }

        private static void Shot(Stage stage, string folder, string name, Vector3 eye, Vector3 target)
        {
            Texture2D image = stage.Render(eye, target);
            Save(image, folder, name);
        }

        // ---- QA renders (editor batch only; -executeMethod PingTheKiller.Editor.Setup.MotionSetup.RenderSheets) -------------

        private const int Width = 1280, Height = 720;

        public static void RenderSheets()
        {
            int code = 0;
            try { string folder = Environment.GetEnvironmentVariable("PTK_RENDER_DIR") ?? "../docs/qa/U4j-screens"; Sheets(folder); Shots(folder); }
            catch (Exception error) { Debug.LogException(error); code = 1; }
            EditorApplication.Exit(code);
        }

        // U8 QA (-executeMethod PingTheKiller.Editor.Setup.MotionSetup.SwipeFrames): every imported frame of the Killer
        // swipe (1/30 s apart), 5 x 8 tiles of 256x240, row-major from frame 0, plus the right hand's position and speed per
        // frame in the log, so the swipe keys (MotionTiming.SwipeRealKeys / SwipeClipKeys) are chosen from frames.
        public static void SwipeFrames()
        {
            int code = 0;
            try
            {
                string folder = Environment.GetEnvironmentVariable("PTK_RENDER_DIR") ?? "../docs/qa/U8-screens/a2";
                CharacterArt art = AssetDatabase.LoadAssetAtPath<CharacterArt>("Assets/Resources/CharacterArt.asset");
                AnimationClip clip = LoadClip("KillerSwipe");
                int frames = Mathf.RoundToInt(clip.length * MixamoFps) + 1;
                const int columns = 5, tileW = 256, tileH = 240;
                int rows = (frames + columns - 1) / columns;
                var log = new StringBuilder("[MotionSetup] swipe frames length=" + clip.length.ToString("0.000", CultureInfo.InvariantCulture) + "\n");
                using (var stage = new Stage(tileW, tileH))
                {
                    var actor = new Actor(art.KillerPrefab, stage.Root.transform, Vector3.zero, 0);
                    var sheet = new Texture2D(columns * tileW, rows * tileH, TextureFormat.RGB24, false);
                    sheet.SetPixels(new Color[sheet.width * sheet.height]);
                    Vector3 last = Vector3.zero;
                    for (int k = 0; k < frames; k++)
                    {
                        actor.Pose(MotionStates.Swipe, 0);
                        actor.Animator.Play(MotionStates.Swipe, 1, Mathf.Min(k / MixamoFps / clip.length, .999f));
                        actor.Animator.Update(0);
                        Vector3 hand = actor.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                        float speed = k == 0 ? 0 : (hand - last).magnitude * MixamoFps;
                        last = hand;
                        log.Append("frame ").Append(k).Append(" t=").Append((k / MixamoFps).ToString("0.000", CultureInfo.InvariantCulture))
                            .Append(" hand=").Append(hand.x.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(hand.y.ToString("0.00", CultureInfo.InvariantCulture))
                            .Append(',').Append(hand.z.ToString("0.00", CultureInfo.InvariantCulture)).Append(" speed=").Append(speed.ToString("0.00", CultureInfo.InvariantCulture)).Append("\n");
                        actor.Bake();
                        Texture2D tile = stage.Render(new Vector3(2.4f, 1.5f, 1.6f), new Vector3(0, .9f, -.2f));
                        sheet.SetPixels((k % columns) * tileW, (rows - 1 - k / columns) * tileH, tileW, tileH, tile.GetPixels());
                        UnityEngine.Object.DestroyImmediate(tile);
                    }
                    actor.Unbake();
                    sheet.Apply();
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.GetFullPath(Path.Combine(folder, "swipe-frames.png")), sheet.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(sheet);
                    UnityEngine.Object.DestroyImmediate(actor.Root);
                }
                Debug.Log(log.ToString());
            }
            catch (Exception error) { Debug.LogException(error); code = 1; }
            EditorApplication.Exit(code);
        }

        private sealed class Stage : IDisposable
        {
            public readonly GameObject Root;
            public readonly Camera Camera;
            public readonly RenderTexture Target;
            public Stage(int width, int height)
            {
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                Root = new GameObject("Motion render stage");
                SceneLighting.Apply(Root.transform);
                RenderSettings.fog = false;
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.SetParent(Root.transform, false);
                floor.transform.localScale = new Vector3(4, 1, 4);
                var lit = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WorldLit.mat");
                var floorMaterial = new Material(lit) { color = new Color32(70, 74, 80, 255) };
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                // 0.5 m grid lines on the floor, so a sliding foot or a sinking hand is visible against them.
                for (int i = -8; i <= 8; i++)
                {
                    Line(new Vector3(i * .5f, .002f, 0), new Vector3(.012f, .001f, 8), lit);
                    Line(new Vector3(0, .002f, i * .5f), new Vector3(8, .001f, .012f), lit);
                }
                var cameraObject = new GameObject("Render camera", typeof(Camera));
                cameraObject.transform.SetParent(Root.transform, false);
                Camera = cameraObject.GetComponent<Camera>();
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = SceneLighting.Background;
                Camera.fieldOfView = 40;
                Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                Camera.targetTexture = Target;
            }

            private void Line(Vector3 at, Vector3 scale, Material lit)
            {
                GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.transform.SetParent(Root.transform, false);
                line.transform.localPosition = at; line.transform.localScale = scale;
                line.GetComponent<Renderer>().sharedMaterial = new Material(lit) { color = new Color32(110, 116, 124, 255) };
                UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
            }

            public Texture2D Render(Vector3 eye, Vector3 target)
            {
                Camera.aspect = Target.width / (float)Target.height;
                Camera.transform.position = eye;
                Camera.transform.LookAt(target);
                Camera.Render();
                Camera.Render(); // the first render after a material change can still show the fallback shader
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = Target;
                var image = new Texture2D(Target.width, Target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Target.width, Target.height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                return image;
            }

            public void Dispose()
            {
                Camera.targetTexture = null;
                Target.Release();
                UnityEngine.Object.DestroyImmediate(Root);
            }
        }

        public sealed class Actor
        {
            public GameObject Root;
            public Animator Animator;
            public CharacterMotion Motion;
            public bool Grounded = true;
            public Actor(GameObject prefab, Transform parent, Vector3 at, float yaw)
            {
                Root = UnityEngine.Object.Instantiate(prefab, parent);
                Root.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
                Motion = CharacterMotion.Attach(Root, MotionView.Survivor);
                Animator = Root.GetComponentInChildren<Animator>(true);
                Animator.applyRootMotion = false;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.Rebind();
            }

            // Evaluates one state at a normalized time the way the game does: Play at 0, then one Animator.Update over the
            // elapsed clip time at the state's rate (so Foot IK runs with a real time step), then the grounding.
            public void Pose(string state, float normalized, float speed = -1)
            {
                bool action = state == MotionStates.Swipe;
                AnimatorState found = FindState(state);
                float length = found != null && found.motion != null ? found.motion.averageDuration : 1;
                float stateSpeed = found != null ? found.speed : 1;
                if (speed < 0) speed = found != null && found.speedParameterActive ? 1 / stateSpeed : 0; // crawl: rate 1
                float rate = stateSpeed * (found != null && found.speedParameterActive ? speed : 1);
                Animator.SetFloat(MotionStates.SpeedParameter, speed);
                Animator.SetLayerWeight(1, action ? 1 : 0);
                MotionReach reach = Animator.GetComponent<MotionReach>();
                if (reach != null) reach.Weight = state == MotionStates.Working ? 1 : 0;
                Animator.Play(action ? MotionStates.Locomotion : state, 0, 0);
                Animator.Play(action ? state : MotionStates.Empty, 1, 0);
                Animator.Update(0);
                Animator.Update(Mathf.Max(.0001f, Mathf.Min(normalized, .999f) * length / Mathf.Max(.01f, rate)));
                if (Grounded) Motion.GroundAs(PoseOf(state));
            }

            // Batch-mode Camera.Render does not re-skin after a manual Animator.Update, so the renders draw a baked copy of
            // every skinned mesh (BakeMesh with scale, placed with position and rotation only) and hide the skinned renderers.
            private readonly List<GameObject> baked = new List<GameObject>();

            public void Bake()
            {
                Unbake();
                foreach (SkinnedMeshRenderer renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.gameObject.activeInHierarchy) continue;
                    var mesh = new Mesh();
                    renderer.BakeMesh(mesh, true);
                    var copy = new GameObject("Baked " + renderer.name, typeof(MeshFilter), typeof(MeshRenderer));
                    copy.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    copy.GetComponent<MeshFilter>().sharedMesh = mesh;
                    copy.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                    renderer.enabled = false;
                    baked.Add(copy);
                }
            }

            public void Unbake()
            {
                foreach (GameObject copy in baked)
                {
                    UnityEngine.Object.DestroyImmediate(copy.GetComponent<MeshFilter>().sharedMesh);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
                baked.Clear();
                foreach (SkinnedMeshRenderer renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.enabled = true;
            }

            public static MotionPose PoseOf(string state)
            {
                switch (state)
                {
                    case MotionStates.Hit: return MotionPose.Hit;
                    case MotionStates.KnockedDown: return MotionPose.KnockedDown;
                    case MotionStates.Crawl: return MotionPose.Crawl;
                    case MotionStates.GettingUp: return MotionPose.GettingUp;
                    case MotionStates.Death: return MotionPose.Death;
                    case MotionStates.Working: return MotionPose.Working;
                    case MotionStates.Injured: return MotionPose.Injured;
                    default: return MotionPose.Locomotion;
                }
            }

            private static AnimatorState FindState(string name)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                foreach (AnimatorControllerLayer layer in controller.layers)
                    foreach (ChildAnimatorState child in layer.stateMachine.states)
                        if (child.state.name == name) return child.state;
                return null;
            }
        }

        private static void Save(Texture2D image, string folder, string name)
        {
            Directory.CreateDirectory(folder);
            string path = Path.GetFullPath(Path.Combine(folder, Prefix + name));
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("[MotionSetup] render " + path);
        }

        // One 1280x720 contact sheet per state (3 x 2 tiles of 426x360): top = a raised three-quarter view, bottom = a
        // floor-level side view (a gap under a sole, or a hand cut off by the floor, shows there), at 3 normalized times.
        private static void Sheets(string folder)
        {
            CharacterArt art = AssetDatabase.LoadAssetAtPath<CharacterArt>("Assets/Resources/CharacterArt.asset");
            float[] times = { .2f, .55f, 1f };
            using (var stage = new Stage(426, 360))
            {
                foreach (MotionLibrary.Entry entry in MotionLibrary.Clips)
                {
                    bool killer = entry.State == MotionStates.Swipe;
                    bool injured = entry.State == MotionStates.InjuredIdle || entry.State == MotionStates.InjuredWalk;
                    var actor = new Actor(killer ? art.KillerPrefab : art.SurvivorPrefab, stage.Root.transform, Vector3.zero, 0);
                    var sheet = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    sheet.SetPixels(new Color[Width * Height]);
                    for (int i = 0; i < 6; i++)
                    {
                        bool strip = killer || entry.State == MotionStates.Working; // 6 times, all from above
                        float t = strip ? .1f + .15f * i : times[i % 3];
                        if (injured) actor.Pose(MotionStates.Injured, t, entry.State == MotionStates.InjuredIdle ? 0 : 1.2f);
                        else actor.Pose(entry.State, t);
                        actor.Bake();
                        stage.Camera.fieldOfView = i < 3 || strip ? 50 : 36;
                        Texture2D tile = i < 3 || strip ? stage.Render(new Vector3(2.4f, 1.5f, 1.6f), new Vector3(0, .55f, -.4f))
                            : stage.Render(new Vector3(3.4f, .04f, -.4f), new Vector3(0, .04f, -.4f));
                        sheet.SetPixels((i % 3) * 426, (1 - i / 3) * 360, 426, 360, tile.GetPixels());
                        UnityEngine.Object.DestroyImmediate(tile);
                    }
                    actor.Unbake();
                    sheet.Apply();
                    Save(sheet, folder, "sheet-" + entry.File + ".png");
                    UnityEngine.Object.DestroyImmediate(actor.Root);
                }
            }
        }
    }
}
