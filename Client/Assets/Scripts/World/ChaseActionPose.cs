using PingTheKiller.Client.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 15.3 (Q5): the Shutter and Rail actions as a procedural pose overlay on a drawn character (Survivor client:
    // the Survivors and the line-of-sight Killer model). A new file instead of a Motion.controller state, because
    // CharacterMotion / CharacterViews / MotionSetup belong to a parallel unit (U8-hitfeel); nothing there changes.
    // ChasePropsLayer calls Apply from LateUpdate, after the Animator wrote the locomotion pose, so the overlay adds to it
    // for this frame only (CharacterViews places the root again next frame; offsets never accumulate).
    // - Pull (0.3 s): both arms reach up and pull down.
    // - Vault (0.8 / 1.0 / 1.6 s): a hop over the prop with tucked legs and a forward lean; the server moves the body across.
    // - Break (Killer, while held): the right arm hammers at the Shutter twice a second.
    // - Stunned (2 s): a stagger, leaning back and swaying (still under Reduced motion).
    // The capsule fallback has no bones: it gets the hop and a whole-body lean only.
    public sealed class ChasePoseRig
    {
        public const float FadeSeconds = .08f, BreakCycleSeconds = .5f;
        private readonly Transform root;
        private readonly Transform spine, head, leftArm, rightArm, leftLeg, rightLeg, leftShin, rightShin;
        public bool Humanoid { get; }
        public Transform Root => root;
        // Last applied hop (metres) and the main arm angle (degrees), for tests and the dev log.
        public float LastHop { get; private set; }
        public float LastArm { get; private set; }

        public ChasePoseRig(Transform root)
        {
            this.root = root;
            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman || animator.avatar == null) return;
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            leftShin = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightShin = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            Humanoid = spine != null && leftArm != null && rightArm != null;
        }

        public void Apply(ChasePoseClock clock, double now, bool reduced)
        {
            LastHop = 0; LastArm = 0;
            if (clock.Pose == ChasePose.None || root == null) return;
            float elapsed = (float)(now - clock.StartedAt);
            float weight = Mathf.Clamp01(elapsed / FadeSeconds);
            float u = clock.Phase(now);
            Vector3 right = root.right, forward = root.forward;
            switch (clock.Pose)
            {
                case ChasePose.Vault:
                {
                    float arc = 4 * u * (1 - u), tuck = Mathf.Sin(Mathf.PI * u);
                    LastHop = ChaseActionMap.HopHeight(clock.Seconds) * arc;
                    root.position += Vector3.up * LastHop;
                    Lean(right, 22 * tuck * weight);
                    Turn(leftArm, right, -70 * tuck * weight); Turn(rightArm, right, -70 * tuck * weight);
                    Turn(leftLeg, right, -75 * tuck * weight); Turn(rightLeg, right, -60 * tuck * weight);
                    Turn(leftShin, right, 85 * tuck * weight); Turn(rightShin, right, 70 * tuck * weight);
                    LastArm = -70 * tuck * weight;
                    break;
                }
                case ChasePose.Pull:
                {
                    float eased = u * u * (3 - 2 * u);
                    float arm = Mathf.Lerp(-160, -35, eased) * weight;
                    Turn(leftArm, right, arm); Turn(rightArm, right, arm);
                    Lean(right, 10 * Mathf.Sin(Mathf.PI * u) * weight);
                    LastArm = arm;
                    break;
                }
                case ChasePose.Break:
                {
                    // Raise over 70 % of the cycle, strike in the last 30 %.
                    float c = Mathf.Repeat(elapsed, BreakCycleSeconds) / BreakCycleSeconds;
                    float arm = (c < .7f ? Mathf.Lerp(-40, -150, c / .7f) : Mathf.Lerp(-150, -40, (c - .7f) / .3f)) * weight;
                    Turn(rightArm, right, arm); Turn(leftArm, right, -45 * weight);
                    Lean(right, 12 * weight);
                    LastArm = arm;
                    break;
                }
                case ChasePose.Stunned:
                {
                    float sway = reduced ? 0 : 9 * Mathf.Sin(elapsed * 2 * Mathf.PI * 1.4f);
                    Lean(right, -14 * weight);
                    if (Humanoid) spine.rotation = Quaternion.AngleAxis(sway * weight, forward) * spine.rotation;
                    else root.rotation = Quaternion.AngleAxis(sway * weight, forward) * root.rotation;
                    if (head != null) head.rotation = Quaternion.AngleAxis(18 * weight, right) * head.rotation;
                    Turn(leftArm, forward, 28 * weight); Turn(rightArm, forward, -28 * weight);
                    LastArm = 28 * weight;
                    break;
                }
            }
        }

        // Positive degrees lean the upper body forward.
        private void Lean(Vector3 right, float degrees)
        {
            if (Humanoid) spine.rotation = Quaternion.AngleAxis(degrees, right) * spine.rotation;
            else root.rotation = Quaternion.AngleAxis(degrees * .5f, right) * root.rotation;
        }

        private static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }
    }

    // HUD_SPEC 15.4: the burst over a stunned Killer model's head (Survivor client, only while the model is drawn): the Hovl
    // Marker 5 Circle from the SkillFx catalog, 0.9 m across, tinted `danger` (the Killer colour), the low-emission marker
    // material and the glow children off (SLOP V3). Under Reduced motion a still `danger` ring instead of particles.
    public sealed class StunBurst
    {
        public const float Diameter = 1.1f, AboveHead = .1f;
        public static readonly Color Tint = new Color32(229, 72, 77, 255);
        private static readonly string[] GlowChildren = { "Flash", "EndSparks" };
        private readonly Transform parent;
        private GameObject root, ring;
        private Material ringMaterial;
        private ParticleSystem[] systems = new ParticleSystem[0];
        private bool shown, reducedShown;

        public StunBurst(Transform parent) { this.parent = parent; }

        public bool Shown => shown;
        public bool StillRing => shown && reducedShown;
        public GameObject Root => root;

        public void Set(bool show, Vector3 headTop, bool reduced)
        {
            if (!show)
            {
                if (!shown) return;
                shown = false;
                foreach (ParticleSystem system in systems) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (root != null) root.SetActive(false);
                if (ring != null) ring.SetActive(false);
                return;
            }
            if (!shown)
            {
                shown = true; reducedShown = reduced;
                // The halo ring is always drawn (a flat circle of particles alone is edge-on and invisible from the Survivor camera);
                // the particles are added on top unless Reduced motion is on.
                BuildRing(); ring.SetActive(true);
                if (!reduced && Build()) { root.SetActive(true); foreach (ParticleSystem system in systems) system.Play(false); }
                else reducedShown = true;
            }
            Vector3 at = headTop + Vector3.up * AboveHead;
            if (root != null && root.activeSelf) root.transform.position = at;
            if (ring != null && ring.activeSelf)
            {
                ring.transform.position = at;
                // A flat ring is edge-on from the Survivor camera (about head height): the halo faces the camera instead.
                Camera camera = Camera.main;
                if (camera != null) ring.transform.rotation = Quaternion.LookRotation(camera.transform.forward, Vector3.up);
            }
        }

        private bool Build()
        {
            if (root != null) return true;
            SkillFxArt art = SkillFxArt.Load();
            GameObject prefab = art != null ? art.Prefab(FxPrefab.Circle) : null;
            if (prefab == null) return false;
            root = Object.Instantiate(prefab, parent);
            root.name = "Stun burst";
            root.transform.localScale = Vector3.one * (Diameter / SkillFeedback.PrefabDiameters[(int)FxPrefab.Circle]);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(GlowChildren, child.name) >= 0) child.gameObject.SetActive(false);
            var list = new System.Collections.Generic.List<ParticleSystem>();
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!system.gameObject.activeSelf) continue;
                ParticleSystem.MainModule main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = true; main.playOnAwake = false;
                main.startColor = new Color(Tint.r, Tint.g, Tint.b, main.startColor.color.a);
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                list.Add(system);
            }
            foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                if (art.Marker == null) continue;
                Material[] used = renderer.sharedMaterials;
                for (int i = 0; i < used.Length; i++) used[i] = art.Marker;
                renderer.sharedMaterials = used;
            }
            systems = list.ToArray();
            root.SetActive(false);
            return true;
        }

        private void BuildRing()
        {
            if (ring != null) return;
            ring = new GameObject("Stun ring");
            ring.transform.SetParent(parent, false);
            ring.transform.rotation = Quaternion.Euler(90, 0, 0);
            var line = ring.AddComponent<LineRenderer>();
            const int points = 32;
            line.useWorldSpace = false; line.loop = true; line.positionCount = points;
            line.alignment = LineAlignment.View; line.widthMultiplier = .1f;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            SkillFxArt art = SkillFxArt.Load();
            Material template = art != null ? art.Ring : null;
            ringMaterial = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ringMaterial.name = "stun ring"; ringMaterial.color = Tint; // the shared ring material is white; tint an instance
            line.sharedMaterial = ringMaterial;
            line.startColor = line.endColor = Tint;
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2 / points;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * Diameter * .5f);
            }
            ring.SetActive(false);
        }

        public void Dispose()
        {
            CharacterLook.Release(root); CharacterLook.Release(ring);
            CharacterLook.Release(ringMaterial);
            ringMaterial = null; root = ring = null; shown = false;
        }
    }
}
