using System.Collections.Generic;
using PingTheKiller.Client.Killer;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Audio
{
    public struct ChasePropSound
    {
        public ChasePropCue Cue;
        public float X, Z;
    }

    // HUD_SPEC 15.5: pure edge rules, snapshots in, positional cues out. The first snapshot of a match (also a late join or
    // a reconnect) is only the baseline: a Shutter that is already Down never slams. ShutterSlam on Up -> Down, ShutterCrunch
    // on any -> Broken, at the Shutter's centre, for every recipient (shutter state is public). VaultThud when an entry the
    // viewer receives starts Vaulting, at that entry. Entries are matched by position, not by Id (the Killer's stream may
    // carry no usable Survivor Id): a Vaulting entry is new when no entry of the previous snapshot was Vaulting within
    // MatchMetres of it. So a vault only the viewer's own stream shows makes a sound, and a Silent Survivor (omitted for the
    // Killer) never does; an acting Survivor is Signal anyway.
    public sealed class ChaseCueRules
    {
        public const float MatchMetres = 1.5f;
        public readonly List<ChasePropSound> Pending = new List<ChasePropSound>();
        private readonly List<byte> shutters = new List<byte>();
        private List<Vector2> vaulting = new List<Vector2>(), next = new List<Vector2>();
        private bool known;

        public void Reset()
        {
            Pending.Clear(); shutters.Clear(); vaulting.Clear(); next.Clear(); known = false;
        }

        public void OnWorld(S2C_WorldState state)
        {
            if (state == null) return;
            IReadOnlyList<ChaseProp> props = WorldChaseProps.Shutters;
            for (int i = 0; i < props.Count; i++)
            {
                byte now = state.Shutters != null && i < state.Shutters.Count ? state.Shutters[i] : (byte)ShutterState.Up;
                byte before = i < shutters.Count ? shutters[i] : now;
                if (known && now != before)
                {
                    if (now == (byte)ShutterState.Down && before == (byte)ShutterState.Up) Add(ChasePropCue.ShutterSlam, props[i].CenterX, props[i].CenterZ);
                    else if (now == (byte)ShutterState.Broken) Add(ChasePropCue.ShutterCrunch, props[i].CenterX, props[i].CenterZ);
                }
                if (i < shutters.Count) shutters[i] = now; else shutters.Add(now);
            }
            next.Clear();
            if (state.Players != null)
                foreach (PlayerState player in state.Players)
                {
                    if ((player.Chase & (byte)ChaseFlags.Vaulting) == 0) continue;
                    var at = new Vector2(player.X, player.Z);
                    next.Add(at);
                    if (known && !Near(vaulting, at)) Add(ChasePropCue.VaultThud, player.X, player.Z);
                }
            List<Vector2> swap = vaulting; vaulting = next; next = swap;
            known = true;
        }

        private static bool Near(List<Vector2> list, Vector2 at)
        {
            for (int i = 0; i < list.Count; i++) if ((list[i] - at).sqrMagnitude <= MatchMetres * MatchMetres) return true;
            return false;
        }

        private void Add(ChasePropCue cue, float x, float z) => Pending.Add(new ChasePropSound { Cue = cue, X = x, Z = z });
    }

    // Plays the ChaseCueRules output: positional one-shots from a small pool (3D, linear roll-off from 2 m to the cue's
    // range), following the listener volume like every other cue.
    public sealed class ChasePropAudio : MonoBehaviour
    {
        public const int Voices = 4;
        public const float Height = .8f;
        private readonly ChaseCueRules rules = new ChaseCueRules();
        private readonly Dictionary<ChasePropCue, AudioClip> clips = new Dictionary<ChasePropCue, AudioClip>();
        private readonly List<AudioSource> voices = new List<AudioSource>();
        private int nextVoice;

        public ChaseCueRules Rules => rules;
        public int Played { get; private set; }
        public AudioClip Clip(ChasePropCue cue) => clips.TryGetValue(cue, out AudioClip clip) ? clip : null;
        public AudioSource Voice(int i) => voices[i];

        private void Awake() => Setup();

        public void Setup()
        {
            if (voices.Count > 0) return;
            foreach (ChasePropCue cue in System.Enum.GetValues(typeof(ChasePropCue)))
            {
                float[] samples = ChasePropSynth.Build(cue, KillerAudio.Rate);
                var clip = AudioClip.Create("chase cue " + cue, samples.Length, 1, KillerAudio.Rate, false);
                clip.SetData(samples, 0);
                clips[cue] = clip;
            }
            for (int i = 0; i < Voices; i++)
            {
                var voice = new GameObject("chase cue voice " + i);
                voice.transform.SetParent(transform, false);
                var source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false;
                source.spatialBlend = 1; source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 2; source.dopplerLevel = 0;
                voices.Add(source);
            }
        }

        public void OnWorld(S2C_WorldState state)
        {
            Setup();
            rules.OnWorld(state);
            foreach (ChasePropSound sound in rules.Pending)
            {
                AudioSource source = voices[nextVoice];
                nextVoice = (nextVoice + 1) % voices.Count;
                source.transform.position = new Vector3(sound.X, Height, sound.Z);
                source.maxDistance = ChasePropSynth.Range(sound.Cue);
                source.clip = clips[sound.Cue];
                source.Play();
                Played++;
                Debug.Log("[audio] chase cue " + sound.Cue + " at " + sound.X.ToString("0.0") + ", " + sound.Z.ToString("0.0"));
            }
            rules.Pending.Clear();
        }

        public void ResetRules() => rules.Reset();

        private void OnDestroy()
        {
            foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
        }
    }
}
