using System.Collections.Generic;
using UnityEngine;

namespace PingTheKiller.Client.Killer
{
    // Killer-only audio. UI cues (HUD_SPEC 6.5) are 2D one-shots. Positional Survivor sound (6.5.1) plays the
    // BlipSoundModel output through 4 footstep voices and 4 breathing loops, each moved to its instance's tweened
    // position: 3D panning by azimuth, no Doppler, flat rolloff (the model's dB already holds the attenuation), a 1.5 kHz
    // low-pass cross-faded over 100 ms while occluded, a 6 dB duck for 300 ms under every UI cue, and a 50 ms fade-out
    // when an instance loses its voice or despawns. Only created on a Killer client.
    public sealed class KillerAudio : MonoBehaviour
    {
        public const int Rate = 44100;
        public const float DuckDb = -6, DuckSeconds = .3f, FadeOutSeconds = .05f, OcclusionFadeSeconds = .1f, BreathSwapSeconds = .3f;
        public const float OpenCutoff = 22000, OccludedCutoff = 1500;
        public const float StepHeight = .1f, BreathHeight = 1.5f;
        private sealed class Voice
        {
            public AudioSource Step, Breath;
            public AudioLowPassFilter StepFilter, BreathFilter;
            public int Handle = -1;
            public bool Active;
            public float Fade, Occlusion, BreathGain, BreathLevel;
            public BreathVariant Variant = BreathVariant.None, Playing = BreathVariant.None;
        }
        private readonly Voice[] voices = new Voice[BlipSoundModel.MaxVoices];
        private readonly Dictionary<KillerCue, AudioClip> cues = new Dictionary<KillerCue, AudioClip>();
        private readonly List<AudioClip> clips = new List<AudioClip>();
        private readonly AudioClip[] steps = new AudioClip[SurvivorSoundSynth.VariantCount], laggingSteps = new AudioClip[SurvivorSoundSynth.VariantCount];
        private readonly AudioClip[] breaths = new AudioClip[4];
        private readonly Dictionary<int, Vector3> positions = new Dictionary<int, Vector3>();
        private readonly HashSet<int> voiced = new HashSet<int>();
        private AudioSource ui;
        private AnimationCurve flat;
        private double duckUntil = double.NegativeInfinity;

        private void Awake()
        {
            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false; ui.spatialBlend = 0;
            foreach (KillerCue cue in System.Enum.GetValues(typeof(KillerCue))) cues[cue] = Clip("cue " + cue, KillerCueSynth.Build(cue, Rate), false);
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i] = Clip("step " + i, SurvivorSoundSynth.Step(i, Rate), false);
                laggingSteps[i] = Clip("lagging step " + i, SurvivorSoundSynth.LaggingStep(i, Rate), false);
            }
            for (int i = 1; i < breaths.Length; i++) breaths[i] = Clip("breath " + (BreathVariant)i, SurvivorSoundSynth.Breath((BreathVariant)i, Rate), true);
            flat = AnimationCurve.Constant(0, 1, 1);
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = new Voice();
                voice.Step = Source("Footstep voice " + i, out voice.StepFilter);
                voice.Breath = Source("Breathing voice " + i, out voice.BreathFilter);
                voice.Breath.loop = true;
                voices[i] = voice;
            }
        }

        private AudioClip Clip(string label, float[] samples, bool loop)
        {
            var clip = AudioClip.Create(label, Mathf.Max(1, samples.Length), 1, Rate, false);
            clip.SetData(samples, 0);
            clips.Add(clip);
            return clip;
        }

        private AudioSource Source(string label, out AudioLowPassFilter filter)
        {
            var item = new GameObject(label, typeof(AudioSource), typeof(AudioLowPassFilter));
            item.transform.SetParent(transform, false);
            var source = item.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1;
            source.dopplerLevel = 0;
            source.spread = 0;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, flat);
            source.maxDistance = 100;
            source.volume = 0;
            filter = item.GetComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = OpenCutoff;
            return source;
        }

        public void Play(KillerCue cue)
        {
            if (!cues.TryGetValue(cue, out AudioClip clip)) return;
            ui.PlayOneShot(clip);
            duckUntil = Time.unscaledTimeAsDouble + DuckSeconds;
        }

        public static float Linear(float db) => float.IsNegativeInfinity(db) || float.IsNaN(db) ? 0 : Mathf.Pow(10, db / 20);

        // Every rendered frame, after BlipSoundModel.Update. views carry the tweened positions.
        public void UpdateWorld(BlipSoundModel model, IReadOnlyList<BlipView> views, float dt)
        {
            if (!(dt > 0) || float.IsInfinity(dt)) dt = 0;
            float duck = Time.unscaledTimeAsDouble < duckUntil ? Linear(DuckDb) : 1;
            positions.Clear();
            foreach (BlipView view in views) positions[view.Handle] = view.Position;
            voiced.Clear();
            foreach (VoiceState state in model.Voices) voiced.Add(state.Handle);
            // Free voices whose instance lost its voice or despawned (they fade out first).
            foreach (Voice voice in voices) if (voice.Handle >= 0 && (!voiced.Contains(voice.Handle) || !positions.ContainsKey(voice.Handle))) voice.Active = false;
            foreach (VoiceState state in model.Voices)
            {
                if (!positions.ContainsKey(state.Handle)) continue;
                Voice voice = Find(state.Handle) ?? Claim(state.Handle);
                if (voice == null) continue;
                voice.Active = true;
                voice.Variant = state.Breath;
                voice.BreathGain = Linear(state.BreathGainDb);
                voice.Occlusion = Mathf.MoveTowards(voice.Occlusion, state.Occluded ? 1 : 0, dt / OcclusionFadeSeconds);
            }
            foreach (StepEvent step in model.Steps)
            {
                Voice voice = Find(step.Handle);
                if (voice == null || !voice.Active || !positions.TryGetValue(step.Handle, out Vector3 at)) continue;
                voice.Step.transform.position = at + Vector3.up * StepHeight;
                voice.Step.pitch = step.Pitch;
                AudioClip[] set = step.Variant == StepVariant.Lagging ? laggingSteps : steps;
                voice.Step.PlayOneShot(set[Mathf.Clamp(step.Sample, 0, set.Length - 1)], Linear(step.GainDb));
            }
            foreach (Voice voice in voices) Mix(voice, duck, dt);
        }

        private void Mix(Voice voice, float duck, float dt)
        {
            if (voice.Handle < 0) return;
            voice.Fade = voice.Active ? 1 : Mathf.MoveTowards(voice.Fade, 0, dt / FadeOutSeconds);
            if (positions.TryGetValue(voice.Handle, out Vector3 at))
            {
                voice.Step.transform.position = at + Vector3.up * StepHeight;
                voice.Breath.transform.position = at + Vector3.up * BreathHeight;
            }
            float cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(OccludedCutoff), voice.Occlusion));
            voice.StepFilter.cutoffFrequency = voice.BreathFilter.cutoffFrequency = cutoff;
            voice.Step.volume = voice.Fade * duck;
            // Breathing: a variant change fades the loop out and the new one in, 300 ms in all.
            float step = dt / (BreathSwapSeconds * .5f);
            if (voice.Playing != voice.Variant)
            {
                voice.BreathLevel = Mathf.MoveTowards(voice.BreathLevel, 0, step);
                if (voice.BreathLevel <= 0)
                {
                    voice.Playing = voice.Variant;
                    if (voice.Playing == BreathVariant.None) voice.Breath.Stop();
                    else { voice.Breath.clip = breaths[(int)voice.Playing]; voice.Breath.Play(); }
                }
            }
            else if (voice.Playing != BreathVariant.None) voice.BreathLevel = Mathf.MoveTowards(voice.BreathLevel, 1, step);
            voice.Breath.volume = voice.BreathLevel * voice.BreathGain * voice.Fade * duck;
            if (!voice.Active && voice.Fade <= 0)
            {
                voice.Step.Stop(); voice.Breath.Stop();
                voice.Handle = -1; voice.Playing = voice.Variant = BreathVariant.None; voice.BreathLevel = 0; voice.Occlusion = 0;
            }
        }

        private Voice Find(int handle)
        {
            foreach (Voice voice in voices) if (voice.Handle == handle) return voice;
            return null;
        }

        private Voice Claim(int handle)
        {
            foreach (Voice voice in voices)
                if (voice.Handle < 0) { voice.Handle = handle; voice.Fade = 1; voice.Occlusion = 0; voice.BreathLevel = 0; return voice; }
            return null; // all four still fading out; it gets a voice next frame
        }

        public void Silence()
        {
            foreach (Voice voice in voices)
            {
                if (voice == null) continue;
                voice.Step.Stop(); voice.Breath.Stop(); voice.Handle = -1; voice.Active = false;
                voice.Playing = voice.Variant = BreathVariant.None;
            }
        }

        private void OnDestroy()
        {
            foreach (AudioClip clip in clips) if (clip != null) Destroy(clip);
        }
    }
}
