using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    // All imported character dependencies enter the player through this Resources catalog.
    public sealed class CharacterArt : ScriptableObject
    {
        public GameObject SurvivorPrefab, KillerPrefab;
        public GameObject SurvivorModel, KillerModel;
        public RuntimeAnimatorController Controller;
        public Material SurvivorMaterial, KillerMaterial, NeutralMaterial;
        public static CharacterArt Load() => Resources.Load<CharacterArt>("CharacterArt");
    }

    // Cached renderer/material arrays: posing and changing appearance allocate nothing per frame.
    public sealed class CharacterInstance
    {
        public readonly GameObject Root;
        public readonly Animator Animator;
        public readonly Renderer[] Renderers;
        private readonly Material[][] opaque, fade, dither;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly bool sharedNeutral;
        private Vector3 previous;
        private double previousTime = double.NaN, steppedAt = double.NaN;
        private float speed;
        private int appearance = -1;
        public bool Dithered { get; private set; }
        public float Speed => speed;
        private static readonly int SpeedId = UnityEngine.Animator.StringToHash("Speed");

        public bool NearCamera(Vector3 eye)
        {
            if (Renderers.Length == 0) return false;
            Bounds bounds = CurrentBounds(Renderers[0]);
            for (int i = 1; i < Renderers.Length; i++) bounds.Encapsulate(CurrentBounds(Renderers[i]));
            return bounds.SqrDistance(eye) <= .3f * .3f;
        }

        private static Bounds CurrentBounds(Renderer renderer)
        {
            // Skinned world bounds can still describe the previous render after a snapshot moves the root.
            // Transform the local bounds now so proximity also works on the first visible frame and after pooling.
            Bounds local = renderer.localBounds;
            // Skinned bounds use the root bone's space; static mesh bounds use the renderer's space.
            Transform space = renderer is SkinnedMeshRenderer skinned && skinned.rootBone != null
                ? skinned.rootBone : renderer.transform;
            Matrix4x4 matrix = space.localToWorldMatrix;
            Vector3 x = matrix.MultiplyVector(Vector3.right * local.extents.x);
            Vector3 y = matrix.MultiplyVector(Vector3.up * local.extents.y);
            Vector3 z = matrix.MultiplyVector(Vector3.forward * local.extents.z);
            Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), extents * 2);
        }

        public void ResetMotion()
        {
            previousTime = steppedAt = double.NaN; speed = 0;
            if (Animator != null) { Animator.Rebind(); Animator.Update(0); }
        }

        public CharacterInstance(GameObject root, bool neutral = false)
        {
            Root = root; sharedNeutral = neutral;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) CharacterLook.Release(collider);
            Animator = root.GetComponentInChildren<Animator>(true);
            if (Animator != null)
            {
                Animator.applyRootMotion = false;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.speed = 0; // Explicit evaluation keeps both role views on the same clock.
                Animator.Rebind();
                Animator.Update(0);
            }
            Renderers = root.GetComponentsInChildren<Renderer>(true);
            opaque = new Material[Renderers.Length][];
            fade = new Material[Renderers.Length][];
            dither = new Material[Renderers.Length][];
            for (int i = 0; i < Renderers.Length; i++)
            {
                Renderer renderer = Renderers[i];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                opaque[i] = renderer.sharedMaterials;
                if (neutral) continue;
                fade[i] = new Material[opaque[i].Length];
                dither[i] = new Material[opaque[i].Length];
                for (int j = 0; j < opaque[i].Length; j++)
                {
                    fade[i][j] = new Material(opaque[i][j]);
                    CharacterLook.Transparent(fade[i][j]);
                    dither[i][j] = CharacterLook.Dither(opaque[i][j], .4f);
                }
            }
        }

        public void Look(Color tint, float alpha, bool useDither)
        {
            if (sharedNeutral) return;
            Dithered = useDither;
            int mode = useDither ? 2 : alpha < .9999f ? 1 : 0;
            tint.a = alpha;
            block.SetColor("_BaseColor", tint);
            for (int i = 0; i < Renderers.Length; i++)
            {
                if (appearance != mode) Renderers[i].sharedMaterials = mode == 2 ? dither[i] : mode == 1 ? fade[i] : opaque[i];
                // The real Kyle body/head material alpha remains observable to the existing capture/QA probes.
                for (int j = 0; j < fade[i].Length; j++)
                {
                    Color color = fade[i][j].color; color.a = alpha; fade[i][j].color = color;
                    color = dither[i][j].color; color.a = alpha; dither[i][j].color = color;
                }
                Renderers[i].SetPropertyBlock(block);
            }
            appearance = mode;
        }

        public void Animate(Vector3 position, double now, bool lagging, bool stepped, bool walking = true)
        {
            float dt = double.IsNaN(previousTime) ? 0 : (float)Math.Max(0, Math.Min(.2, now - previousTime));
            float measured = dt > .00001f ? Vector3.Distance(position, previous) / dt : 0;
            speed = Mathf.Lerp(speed, walking ? measured : 0, 1 - Mathf.Exp(-12 * dt));
            previous = position; previousTime = now;
            if (Animator == null) return;
            Animator.SetFloat(SpeedId, lagging ? Mathf.Min(speed, 1) : speed);
            if (stepped && lagging)
            {
                double tick = Math.Floor(now * 8) / 8;
                if (double.IsNaN(steppedAt) || tick > steppedAt)
                {
                    Animator.speed = 1;
                    Animator.Update(double.IsNaN(steppedAt) ? 0 : (float)Math.Min(.25, tick - steppedAt) * .55f);
                    Animator.speed = 0;
                    steppedAt = tick;
                }
            }
            else
            {
                steppedAt = double.NaN;
                Animator.speed = 1;
                Animator.Update(dt * (lagging ? .55f : 1));
                Animator.speed = 0;
            }
        }

        public void Dispose()
        {
            if (sharedNeutral) return;
            for (int i = 0; i < fade.Length; i++)
                for (int j = 0; j < fade[i].Length; j++)
                {
                    CharacterLook.Release(fade[i][j]); CharacterLook.Release(dither[i][j]);
                }
        }
    }
}
