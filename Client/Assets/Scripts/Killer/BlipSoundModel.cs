using System;
using System.Collections.Generic;

namespace PingTheKiller.Client.Killer
{
    // Positional Survivor sound for the Killer (HUD_SPEC §6.5.1). Engine-free; no LINQ.
    // Decoy safety: every output depends only on position, Speed and Status of an instance, plus a
    // client RNG passed in by the caller (never seeded from wire data). Handles are only used as keys.

    public enum StepVariant : byte { Normal, Lagging }

    public enum BreathVariant : byte { None, Calm, Heavy, Ragged }

    public struct StepEvent
    {
        public int Handle;
        public StepVariant Variant;
        public int Sample;      // 0..3
        public float Pitch;     // 0.95..1.05
        public float GainDb;    // attenuation incl. -4 dB when occluded
        public bool Occluded;   // B applies the 1.5 kHz low-pass
    }

    public struct VoiceState
    {
        public int Handle;
        public BreathVariant Breath;   // None when out of breathing range
        public float BreathGainDb;     // NegativeInfinity when Breath == None
        public bool Occluded;
    }

    public sealed class BlipSoundModel
    {
        public const int MaxVoices = 4;
        public const float VoiceHysteresisMeters = 1f;
        public const float OcclusionDb = -4f;
        public const float PitchJitter = 0.05f;
        public const float FloorDb = -40f;
        public const float FullLevelDistance = 2f;

        public const float StepLevelDb = -20f;
        public const float StepRange = 20f;
        public const float NormalStepStart = 0.5f, NormalStepStop = 0.4f;
        public const float LaggingStepStart = 0.2f, LaggingStepStop = 0.15f;

        public const float CalmLevelDb = -30f, CalmRange = 6f;
        public const float HeavyLevelDb = -24f, HeavyRange = 8f;
        public const float RaggedLevelDb = -24f, RaggedRange = 10f;

        private sealed class Instance
        {
            public int Handle;
            public int Seen;
            public float Distance;
            public float Speed;
            public BlipStatus Status;
            public bool Voiced;
            public bool Stepping;
            public BlipStatus StepStatus;
            public float StepTimer;
            public bool Heavy;
        }

        private readonly Random rng;
        private readonly Dictionary<int, Instance> instances = new Dictionary<int, Instance>(16);
        private readonly List<Instance> active = new List<Instance>(16);
        private readonly List<int> stale = new List<int>(16);
        private readonly List<StepEvent> steps = new List<StepEvent>(8);
        private readonly List<VoiceState> voices = new List<VoiceState>(MaxVoices);
        private int frame;

        public BlipSoundModel(Random rng)
        {
            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        // Steps to play this frame.
        public IReadOnlyList<StepEvent> Steps => steps;

        // Up to 4 voiced instances and their breathing.
        public IReadOnlyList<VoiceState> Voices => voices;

        public bool IsVoiced(int handle) => instances.TryGetValue(handle, out Instance inst) && inst.Voiced;

        public void Reset()
        {
            instances.Clear();
            active.Clear();
            steps.Clear();
            voices.Clear();
        }

        // Stated level up to 2 m, linear in dB down to -40 dB at rangeLimit, muted beyond it.
        public static float AttenuationDb(float levelAt2mDb, float distance, float rangeLimit)
        {
            if (float.IsNaN(distance) || distance > rangeLimit) return float.NegativeInfinity;
            if (distance <= FullLevelDistance || rangeLimit <= FullLevelDistance) return levelAt2mDb;
            float t = (distance - FullLevelDistance) / (rangeLimit - FullLevelDistance);
            return levelAt2mDb + (FloorDb - levelAt2mDb) * t;
        }

        // Normal: 1.2/v clamped 0.25-0.9 s. Lagging: 0.8/v clamped 0.5-1.5 s.
        public static float StepInterval(BlipStatus status, float speed)
        {
            if (status == BlipStatus.Lagging)
                return speed > 0f ? Clamp(0.8f / speed, 0.5f, 1.5f) : 1.5f;
            return speed > 0f ? Clamp(1.2f / speed, 0.25f, 0.9f) : 0.9f;
        }

        // Called every rendered frame. dt = frame seconds. listener = killer camera position (x,z).
        // occluded(handle) = the §6.2 wall-ray result (with its 100 ms hysteresis); called for voiced instances only.
        public void Update(float dt, IReadOnlyList<Blip> blips, float listenerX, float listenerZ, Func<int, bool> occluded)
        {
            steps.Clear();
            voices.Clear();
            active.Clear();
            frame++;
            if (!(dt > 0f) || float.IsInfinity(dt)) dt = 0f;

            int count = blips == null ? 0 : blips.Count;
            for (int i = 0; i < count; i++)
            {
                Blip b = blips[i];
                if (b == null) continue;
                if (!instances.TryGetValue(b.Handle, out Instance inst))
                {
                    inst = new Instance { Handle = b.Handle };
                    instances.Add(b.Handle, inst);
                }
                else if (inst.Seen == frame)
                {
                    continue; // duplicate handle in the list
                }
                inst.Seen = frame;
                float dx = b.X - listenerX;
                float dz = b.Z - listenerZ;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                inst.Distance = float.IsNaN(d) || float.IsInfinity(d) ? float.PositiveInfinity : d;
                inst.Speed = float.IsNaN(b.Speed) || float.IsInfinity(b.Speed) ? 0f : b.Speed;
                inst.Status = b.Status;
                active.Add(inst);
            }

            // Despawned instances stop at once (B fades them over 50 ms).
            stale.Clear();
            foreach (KeyValuePair<int, Instance> kv in instances)
                if (kv.Value.Seen != frame) stale.Add(kv.Key);
            for (int i = 0; i < stale.Count; i++) instances.Remove(stale[i]);

            SelectVoices();

            for (int i = 0; i < active.Count; i++)
            {
                Instance inst = active[i];
                UpdateBreathHysteresis(inst);
                bool stepDue = AdvanceSteps(inst, dt);
                if (!inst.Voiced) continue;

                bool occ = occluded != null && occluded(inst.Handle);
                float occDb = occ ? OcclusionDb : 0f;

                if (stepDue)
                {
                    float gain = AttenuationDb(StepLevelDb, inst.Distance, StepRange);
                    if (!float.IsNegativeInfinity(gain))
                    {
                        steps.Add(new StepEvent
                        {
                            Handle = inst.Handle,
                            Variant = inst.StepStatus == BlipStatus.Lagging ? StepVariant.Lagging : StepVariant.Normal,
                            Sample = rng.Next(4),
                            Pitch = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * PitchJitter,
                            GainDb = gain + occDb,
                            Occluded = occ,
                        });
                    }
                }

                BreathVariant breath;
                float level, range;
                if (inst.Status == BlipStatus.Lagging) { breath = BreathVariant.Ragged; level = RaggedLevelDb; range = RaggedRange; }
                else if (inst.Heavy) { breath = BreathVariant.Heavy; level = HeavyLevelDb; range = HeavyRange; }
                else { breath = BreathVariant.Calm; level = CalmLevelDb; range = CalmRange; }
                float breathGain = AttenuationDb(level, inst.Distance, range);
                if (float.IsNegativeInfinity(breathGain)) breath = BreathVariant.None;
                else breathGain += occDb;

                voices.Add(new VoiceState { Handle = inst.Handle, Breath = breath, BreathGainDb = breathGain, Occluded = occ });
            }
        }

        // 4 nearest are voiced; a voiced instance keeps its voice until another is at least 1 m nearer.
        private void SelectVoices()
        {
            int voicedCount = 0;
            for (int i = 0; i < active.Count; i++) if (active[i].Voiced) voicedCount++;
            while (voicedCount > MaxVoices)
            {
                Instance far = Farthest(true);
                far.Voiced = false;
                voicedCount--;
            }

            for (int guard = 0; guard < 64; guard++)
            {
                if (voicedCount < MaxVoices)
                {
                    Instance near = Nearest(false);
                    if (near == null) break;
                    near.Voiced = true;
                    voicedCount++;
                    continue;
                }
                Instance candidate = Nearest(false);
                Instance victim = Farthest(true);
                if (candidate == null || victim == null) break;
                if (!(candidate.Distance <= victim.Distance - VoiceHysteresisMeters)) break;
                victim.Voiced = false;
                candidate.Voiced = true;
            }
        }

        private Instance Nearest(bool voiced)
        {
            Instance best = null;
            for (int i = 0; i < active.Count; i++)
            {
                Instance inst = active[i];
                if (inst.Voiced != voiced) continue;
                if (best == null || inst.Distance < best.Distance) best = inst;
            }
            return best;
        }

        private Instance Farthest(bool voiced)
        {
            Instance best = null;
            for (int i = 0; i < active.Count; i++)
            {
                Instance inst = active[i];
                if (inst.Voiced != voiced) continue;
                if (best == null || inst.Distance > best.Distance) best = inst;
            }
            return best;
        }

        // Calm <-> Heavy: up above 0.5 m/s, down below 0.4 m/s (Normal only).
        private static void UpdateBreathHysteresis(Instance inst)
        {
            if (inst.Status == BlipStatus.Lagging)
            {
                inst.Heavy = false;
                return;
            }
            if (!inst.Heavy && inst.Speed > NormalStepStart) inst.Heavy = true;
            else if (inst.Heavy && inst.Speed < NormalStepStop) inst.Heavy = false;
        }

        // Runs for every instance (voiced or not) so step phase never depends on voicing.
        // Returns true when a step is due this frame (at most one per frame).
        private bool AdvanceSteps(Instance inst, float dt)
        {
            if (inst.Stepping && inst.StepStatus != inst.Status) inst.Stepping = false; // variant change restarts

            bool lagging = inst.Status == BlipStatus.Lagging;
            float start = lagging ? LaggingStepStart : NormalStepStart;
            float stop = lagging ? LaggingStepStop : NormalStepStop;

            if (inst.Stepping && inst.Speed < stop) inst.Stepping = false;
            if (!inst.Stepping)
            {
                if (!(inst.Speed > start)) return false;
                inst.Stepping = true;
                inst.StepStatus = inst.Status;
                inst.StepTimer = (float)rng.NextDouble() * StepInterval(inst.Status, inst.Speed); // [0, interval)
            }

            float interval = StepInterval(inst.Status, inst.Speed);
            inst.StepTimer -= dt;
            if (inst.StepTimer > 0f) return false;
            inst.StepTimer += interval;
            if (inst.StepTimer <= 0f) inst.StepTimer = interval; // long frame hitch: no burst of steps
            return true;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
