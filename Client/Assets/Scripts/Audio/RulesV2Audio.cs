using System.Collections.Generic;
using PingTheKiller.Client.Killer;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Audio
{
    // Rules v2 cues on both roles: V2CueRules decides, this plays. Both sources are 2D and follow the listener volume
    // (AudioListener.volume = the settings volume, set by ClientApp), so the clips are never scaled by the setting again.
    public sealed class RulesV2Audio : MonoBehaviour
    {
        private readonly V2CueRules rules = new V2CueRules();
        private readonly Dictionary<V2Cue, AudioClip> clips = new Dictionary<V2Cue, AudioClip>();
        private AudioClip holdClip;
        private AudioSource cues, hold;
        private ActionKind playing = ActionKind.None;

        public V2CueRules Rules => rules;
        public AudioSource CueSource => cues;
        public AudioSource HoldSource => hold;
        public AudioClip HoldClip => holdClip;
        public ActionKind Playing => playing;
        public AudioClip Clip(V2Cue cue) => clips.TryGetValue(cue, out AudioClip clip) ? clip : null;

        private void Awake() => Setup();

        // Idempotent; Awake calls it (EditMode tests call it directly).
        public void Setup()
        {
            if (cues != null) return;
            cues = Source(false);
            hold = Source(true);
            foreach (V2Cue cue in System.Enum.GetValues(typeof(V2Cue))) clips[cue] = Create("v2 cue " + cue, V2CueSynth.Build(cue, KillerAudio.Rate));
            holdClip = Create("v2 hold", V2CueSynth.BuildHold(KillerAudio.Rate));
            hold.clip = holdClip;
        }

        public void Begin(Role role, int ownId)
        {
            Setup();
            StopHold();
            rules.Begin(role, ownId);
        }

        public void OnWorld(S2C_WorldState state, double now) { rules.OnWorld(state, now); Flush(); }

        public void OnSelf(byte kind, float progress, double now)
        {
            rules.OnSelf(kind, progress, now);
            Flush();
            Frame(now, rules.Active);
        }

        public void OnRoute(V2Cue cue) { rules.OnRoute(cue); Flush(); }

        // Every frame: the hold loop follows the own action; outside a match it is silent.
        public void Frame(double now, bool inMatch)
        {
            ActionKind kind = inMatch ? rules.HoldKind : ActionKind.None;
            if (kind != playing)
            {
                StopHold();
                if (kind != ActionKind.None && hold != null)
                {
                    playing = kind;
                    hold.pitch = V2CueSynth.HoldPitch(kind, rules.HoldProgress(now));
                    hold.Play();
                    Debug.Log("[audio] v2 hold start " + kind);
                }
            }
            if (playing != ActionKind.None) hold.pitch = V2CueSynth.HoldPitch(playing, rules.HoldProgress(now));
        }

        public void End()
        {
            rules.End();
            StopHold();
        }

        private void Flush()
        {
            foreach (V2Cue cue in rules.Pending)
            {
                if (cues != null && clips.TryGetValue(cue, out AudioClip clip)) cues.PlayOneShot(clip);
                Debug.Log("[audio] v2 cue " + cue);
            }
            rules.Pending.Clear();
        }

        private void StopHold()
        {
            if (playing == ActionKind.None) return;
            Debug.Log("[audio] v2 hold stop " + playing);
            playing = ActionKind.None;
            if (hold != null) hold.Stop();
        }

        private AudioSource Source(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0;
            source.ignoreListenerVolume = false;
            return source;
        }

        private static AudioClip Create(string label, float[] samples)
        {
            var clip = AudioClip.Create(label, samples.Length, 1, KillerAudio.Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
            if (holdClip != null) Destroy(holdClip);
        }
    }
}
