using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace PingTheKiller.Client.World
{
    // U8 hit-stop (HUD_SPEC 4.5): on the own Survivor's capture the world presentation (WorldView.Render time, the camera
    // follow and therefore every Animator, which CharacterInstance advances from that time) holds for Seconds. Input,
    // networking, snapshots, audio and the HUD keep the real clock; Time.timeScale is never touched. One stop per impact:
    // a capture that lands during an active stop does not extend it.
    public sealed class HitStop
    {
        public const double Seconds = .1, EaseSeconds = .15;
        private double from = double.NegativeInfinity, until = double.NegativeInfinity;
        public int Count { get; private set; }

        public void Reset() { from = until = double.NegativeInfinity; Count = 0; }

        public bool Begin(double now)
        {
            if (Active(now)) return false;
            from = now; until = now + Seconds; Count++;
            return true;
        }

        public bool Active(double now) => now >= from && now < until;

        // The presentation time: frozen during the stop, then easing back to the real time over EaseSeconds (it runs
        // 1.67x until the 100 ms are made up), so the world never jumps 100 ms in one frame.
        public double Present(double now)
        {
            if (now < from) return now;
            if (now < until) return from;
            double back = now - until;
            if (back >= EaseSeconds) return now;
            return now - (until - from) * (1 - back / EaseSeconds);
        }
    }

    // U8 Lagging look (HUD_SPEC 4.5), pure timing. Replaces the 8 Hz frame hold: the world always renders at full rate.
    // - Steady look while the own Status is Lagging: veil, vignette, faint scanlines, faded in over FadeInSeconds.
    // - Glitch burst on the capture, BurstSeconds: a Killer-red flash over the first FlashSeconds and scanline tear bars
    //   whose offset decays to 0. Reduced (Reduced motion / -ptk-reduce-stutter): no burst, no tear, no flash.
    public sealed class LaggingLook
    {
        public const float BurstSeconds = .4f, FlashSeconds = .15f, FlashPeak = .2f, FadeInSeconds = .25f;
        public const float VeilAlpha = .2f, VignetteAlpha = .6f, ScanAlpha = .16f, TearPixels = 36;
        public const int TearBars = 3;
        public struct Frame
        {
            public float Steady, Burst, Flash;
            public int TearSeed;
        }

        private double activeSince = double.NegativeInfinity, burstAt = double.NegativeInfinity;
        private bool active;
        public bool Active => active;

        public void Reset() { active = false; activeSince = burstAt = double.NegativeInfinity; }

        public void SetActive(bool on, double now)
        {
            if (on && !active) activeSince = now;
            active = on;
        }

        public void Capture(double now) => burstAt = now;

        public Frame Evaluate(double now, bool reduced)
        {
            var frame = new Frame();
            if (active) frame.Steady = Mathf.Clamp01((float)((now - activeSince) / FadeInSeconds));
            double t = now - burstAt;
            if (reduced || t < 0 || t >= BurstSeconds) return frame;
            float x = (float)(t / BurstSeconds);
            frame.Burst = (1 - x) * (1 - x);
            frame.Flash = t < FlashSeconds ? FlashPeak * (1 - (float)(t / FlashSeconds)) : 0;
            frame.TearSeed = (int)Math.Floor(t / .05) + 1; // new bar positions every 50 ms
            return frame;
        }

        // Tear bar i for a seed: top (0..1 of the screen height), height (0..1) and horizontal direction (-1 or 1).
        public static void Tear(int seed, int bar, out float top, out float height, out float direction)
        {
            uint h = (uint)(seed * 73856093) ^ (uint)((bar + 1) * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            top = (h % 1000) / 1000f * .9f;
            height = .015f + ((h >> 10) % 100) / 100f * .05f;
            direction = (h & 1) == 0 ? -1 : 1;
        }
    }

    // U8 capture impact (HUD_SPEC 4.10): a Killer-red burst of streaks and digital fragments at the captured Survivor's
    // chest, 0.6 s. Pooled: Warm builds PoolSize instances at match start; Play never instantiates (an exhausted pool
    // reuses the oldest). The Survivor client plays it at any captured Survivor that is drawn (full state); the Killer client
    // only at the tracker instance of its own CaptureConfirm, i.e. a position it already receives (no masked Survivor).
    // The project's Hovl packs hold only map markers (no hit or impact prefab), so the burst is two procedural particle
    // systems on the unlit SkillFx ring material, the URP shader every skill ring already uses in the build.
    public sealed class CaptureImpactFx : MonoBehaviour
    {
        public const int PoolSize = 3, PendingSize = 4;
        public const float Seconds = .6f, ChestHeight = 1f;
        // Killer first-person sizes (the capture is about 1 m from the eye): small streaks and fragments, hard-capped on
        // screen. The Survivor third-person sizes are the MakeSystem values.
        public const float FirstPersonMaxSize = .04f, FirstPersonPull = .4f;
        public static readonly Color Killer = SkillFx.Danger; // palette danger (229, 72, 77)

        private sealed class Shot
        {
            public GameObject Root;
            public ParticleSystem Streaks, Fragments;
            public double End;
            public bool Busy;
        }

        private readonly List<Shot> pool = new List<Shot>();
        private readonly Vector3[] pendingAt = new Vector3[PendingSize];
        private readonly double[] pendingTime = new double[PendingSize];
        private int pending;
        // True on the Killer client (first-person sizes); set at match start.
        public bool FirstPerson { get; set; }
        public int Pending => pending;
        private Material material;
        public bool Warmed { get; private set; }
        public int Created { get; private set; }
        public int Plays { get; private set; }
        public int Busy { get { int n = 0; foreach (Shot s in pool) if (s.Busy) n++; return n; } }
        public Vector3 LastPoint { get; private set; }

        // Builds the pool once. template: the unlit SkillFx ring material (null: URP Unlit by name).
        public void Warm(Material template)
        {
            if (Warmed) return;
            Warmed = true;
            Shader fallback = template == null ? Shader.Find("Universal Render Pipeline/Unlit") : null;
            material = template != null ? new Material(template) : fallback != null ? new Material(fallback) : null;
            if (material != null)
            {
                material.name = "capture impact";
                material.color = Killer;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Killer);
            }
            for (int i = 0; i < PoolSize; i++) pool.Add(NewShot(i));
        }

        private Shot NewShot(int index)
        {
            var root = new GameObject("capture impact " + index);
            root.transform.SetParent(transform, false);
            Created++;
            var shot = new Shot { Root = root };
            shot.Streaks = MakeSystem(root, "streaks", 28, new Vector2(.25f, .5f), new Vector2(3.5f, 7f), new Vector2(.035f, .07f), true);
            shot.Fragments = MakeSystem(root, "fragments", 10, new Vector2(.4f, .6f), new Vector2(1.2f, 2.8f), new Vector2(.09f, .16f), false);
            root.SetActive(false);
            return shot;
        }

        private ParticleSystem MakeSystem(GameObject parent, string label, short count, Vector2 life, Vector2 speed, Vector2 size, bool stretched)
        {
            var item = new GameObject(label);
            item.transform.SetParent(parent.transform, false);
            Created++;
            var system = item.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.duration = .1f; main.loop = false; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = Killer;
            main.gravityModifier = stretched ? .8f : .3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            main.useUnscaledTime = true; // presentation feedback; never slowed by a time scale
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .15f;
            ParticleSystem.SizeOverLifetimeModule shrink = system.sizeOverLifetime;
            shrink.enabled = true; shrink.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var renderer = item.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretched) { renderer.velocityScale = .06f; renderer.lengthScale = 1; }
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
            return system;
        }

        public void Play(Vector3 ground) => Play(ground, Time.unscaledTimeAsDouble);

        public void Play(Vector3 ground, double now)
        {
            if (!Warmed) return;
            Shot shot = null;
            foreach (Shot s in pool)
            {
                if (!s.Busy) { shot = s; break; }
                if (shot == null || s.End < shot.End) shot = s;
            }
            if (shot == null) return;
            Size(shot.Streaks, true); Size(shot.Fragments, false);
            shot.Busy = true; shot.End = now + Seconds; Plays++;
            LastPoint = new Vector3(ground.x, ChestHeight, ground.z);
            // Killer view: the body is about 1 m below and in front of the eye, so a burst at its chest is hidden inside it.
            // Pull it FirstPersonPull toward the eye (horizontal) so it reads in front of the body.
            Camera eye = FirstPerson ? Camera.main : null;
            if (eye != null)
            {
                Vector3 toEye = eye.transform.position - LastPoint; toEye.y = 0;
                if (toEye.sqrMagnitude > 1e-4f) LastPoint += toEye.normalized * Mathf.Min(FirstPersonPull, toEye.magnitude * .5f);
            }
            shot.Root.transform.position = LastPoint;
            shot.Root.SetActive(true);
            shot.Streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); shot.Streaks.Play(true);
            shot.Fragments.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); shot.Fragments.Play(true);
        }

        // Survivor (third person) or Killer (first person) sizes, set on the pooled systems at play time (no allocation).
        private void Size(ParticleSystem system, bool streaks)
        {
            ParticleSystem.MainModule main = system.main;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (FirstPerson)
            {
                main.startSize = streaks ? new ParticleSystem.MinMaxCurve(.012f, .025f) : new ParticleSystem.MinMaxCurve(.03f, .06f);
                main.startSpeed = streaks ? new ParticleSystem.MinMaxCurve(1.5f, 3f) : new ParticleSystem.MinMaxCurve(.6f, 1.4f);
                if (streaks) renderer.velocityScale = .03f;
                renderer.maxParticleSize = FirstPersonMaxSize;
            }
            else
            {
                main.startSize = streaks ? new ParticleSystem.MinMaxCurve(.035f, .07f) : new ParticleSystem.MinMaxCurve(.09f, .16f);
                main.startSpeed = streaks ? new ParticleSystem.MinMaxCurve(3.5f, 7f) : new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
                if (streaks) renderer.velocityScale = .06f;
                renderer.maxParticleSize = .5f;
            }
        }

        // Plays at `at` (real time): the burst starts on the swing's contact, not on the capture packet. A full queue
        // drops the oldest request.
        public void Schedule(Vector3 ground, double at)
        {
            if (pending == PendingSize)
            {
                Array.Copy(pendingAt, 1, pendingAt, 0, PendingSize - 1); Array.Copy(pendingTime, 1, pendingTime, 0, PendingSize - 1); pending--;
            }
            pendingAt[pending] = ground; pendingTime[pending] = at; pending++;
        }

        private void Update() => Tick(Time.unscaledTimeAsDouble);

        public void Tick(double now)
        {
            for (int i = 0; i < pending;)
            {
                if (now < pendingTime[i]) { i++; continue; }
                Play(pendingAt[i], now);
                pending--;
                for (int k = i; k < pending; k++) { pendingAt[k] = pendingAt[k + 1]; pendingTime[k] = pendingTime[k + 1]; }
            }
            foreach (Shot shot in pool) if (shot.Busy && now >= shot.End) End(shot);
        }

        public void Clear() { pending = 0; foreach (Shot shot in pool) if (shot.Busy) End(shot); }

        private static void End(Shot shot)
        {
            shot.Streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            shot.Fragments.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            shot.Root.SetActive(false);
            shot.Busy = false;
        }

        private void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }

    // U8 dev evidence (dev runs only): frame times for 1 s before and 1 s after a capture, logged once the window closes.
    public sealed class CaptureFrameLog
    {
        public const double Window = 1, SpikeMs = 33;
        private readonly double[] times = new double[2048];
        private readonly float[] deltas = new float[2048];
        private int count, head;
        private double markAt = double.NaN;
        private string label;

        public void Mark(double now, string what) { if (double.IsNaN(markAt)) { markAt = now; label = what; } }

        // Returns the log line once, when the window after the mark has closed; null otherwise.
        public string Frame(double now, float dt)
        {
            times[head] = now; deltas[head] = dt; head = (head + 1) % times.Length; if (count < times.Length) count++;
            if (double.IsNaN(markAt) || now < markAt + Window) return null;
            float beforeMax = 0, afterMax = 0, sumBefore = 0, sumAfter = 0;
            int nBefore = 0, nAfter = 0, spikes = 0;
            var spikeList = new StringBuilder();
            for (int k = 0; k < count; k++)
            {
                int i = (head - 1 - k + times.Length) % times.Length;
                double t = times[i] - markAt;
                if (t < -Window || t > Window) continue;
                float ms = deltas[i] * 1000;
                if (t < 0) { beforeMax = Mathf.Max(beforeMax, ms); sumBefore += ms; nBefore++; }
                else { afterMax = Mathf.Max(afterMax, ms); sumAfter += ms; nAfter++; }
                if (ms > SpikeMs) { spikes++; spikeList.Append(' ').Append(t.ToString("+0.000;-0.000", CultureInfo.InvariantCulture)).Append('s').Append('=').Append(ms.ToString("0.0", CultureInfo.InvariantCulture)); }
            }
            string line = "[perf] capture frame times (" + label + ") before: n=" + nBefore + " mean=" + (nBefore > 0 ? sumBefore / nBefore : 0).ToString("0.0", CultureInfo.InvariantCulture)
                + " max=" + beforeMax.ToString("0.0", CultureInfo.InvariantCulture) + " ms; after: n=" + nAfter + " mean=" + (nAfter > 0 ? sumAfter / nAfter : 0).ToString("0.0", CultureInfo.InvariantCulture)
                + " max=" + afterMax.ToString("0.0", CultureInfo.InvariantCulture) + " ms; spikes>33ms=" + spikes + spikeList;
            markAt = double.NaN;
            return line;
        }
    }
}
