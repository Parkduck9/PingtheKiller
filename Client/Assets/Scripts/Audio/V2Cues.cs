using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Audio
{
    // Rules v2 event cues (2D one-shots). The hold loop (exit switch, kick, hatch close) is separate: V2CueSynth.BuildHold.
    public enum V2Cue : byte { SyncGreat, SyncGood, SyncFail, ExitsPowered, ExitOpen, CollapseStart, HatchOpen, HatchClose, Capture, Rescue, TerminalKick }

    // Pure trigger rules: snapshots in, edges out. The caller plays whatever lands in Pending and clears it. The first
    // WorldState after Begin only sets the baseline; every cue fires on an edge, at most once per kind and snapshot.
    //
    // HatchClose (the server closes the hatch only through the Killer's 1 s hold; closing powers both exits and starts the
    // collapse unless it already runs; an exit opening starts the collapse in the same tick):
    // - both roles: the collapse starts in a snapshot where no exit is Open, so the start can only be the hatch closing;
    // - the Killer also: its own HatchClose hold ended at (predicted) progress >= CompleteAt and the hatch it saw left the
    //   snapshot within CloseWindowSeconds of that, in either order (covers a close while the collapse already runs).
    // A hatch that only left sight range never counts. A Survivor cannot observe a close while the collapse already runs.
    //
    // TerminalKick: a terminal's Regressing flag rising (Survivors only receive that flag); on the Killer, its own Kick hold
    // ending at (predicted) progress >= CompleteAt. A Killer release in the last tenth of the hold reads the same.
    public sealed class V2CueRules
    {
        public const float CompleteAt = .9f;
        public const double CloseWindowSeconds = .5;
        public readonly List<V2Cue> Pending = new List<V2Cue>();
        private readonly Dictionary<byte, bool> exitOpen = new Dictionary<byte, bool>();
        private readonly Dictionary<byte, bool> regressing = new Dictionary<byte, bool>();
        private Dictionary<int, PlayerStatus> survivors = new Dictionary<int, PlayerStatus>(), seen = new Dictionary<int, PlayerStatus>();
        private bool active, killer, known, powered, hatchSeen, hatchVisible, hatchClosed, collapseRunning;
        private int ownId, snapshotCues;
        private double closeArmedAt, hatchGoneAt, holdAt;
        private float holdProgress;

        public ActionKind HoldKind { get; private set; }
        public bool Active => active;

        public V2CueRules() => Reset();

        // MatchStart: the own role and (Survivor) the own Id.
        public void Begin(Role role, int ownPlayerId)
        {
            Reset();
            active = true; killer = role != Role.Survivor; ownId = ownPlayerId;
        }

        // GameResult or leaving: nothing pending, no hold.
        public void End() => Reset();

        private void Reset()
        {
            Pending.Clear(); exitOpen.Clear(); regressing.Clear(); survivors.Clear(); seen.Clear();
            active = killer = known = powered = hatchSeen = hatchVisible = hatchClosed = collapseRunning = false;
            ownId = 0; snapshotCues = 0;
            closeArmedAt = hatchGoneAt = double.NegativeInfinity;
            HoldKind = ActionKind.None; holdProgress = 0; holdAt = 0;
        }

        // SyncCheckTracker.Resolved: the first result of the current check (Survivor only).
        public void OnSync(SyncResult result)
        {
            if (!active || killer) return;
            Pending.Add(result == SyncResult.Great ? V2Cue.SyncGreat : result == SyncResult.Good ? V2Cue.SyncGood : V2Cue.SyncFail);
        }

        public void OnWorld(S2C_WorldState state, double now)
        {
            if (!active || state == null) return;
            snapshotCues = 0;
            bool anyPowered = false, anyOpen = false, openEdge = false;
            foreach (ExitState exit in state.Exits)
            {
                bool open = (exit.Flags & (byte)ExitFlags.Open) != 0;
                anyPowered |= (exit.Flags & (byte)ExitFlags.Powered) != 0;
                anyOpen |= open;
                if (known && open && exitOpen.TryGetValue(exit.Id, out bool was) && !was) openEdge = true;
                exitOpen[exit.Id] = open;
            }
            if (known && anyPowered && !powered) Add(V2Cue.ExitsPowered);
            powered |= anyPowered;
            if (openEdge && !killer) Add(V2Cue.ExitOpen);

            bool running = state.CollapseLeft >= 0;
            if (known && running && !collapseRunning)
            {
                Add(V2Cue.CollapseStart);
                if (!anyOpen) CloseHatch();
            }
            collapseRunning = running;

            bool visible = state.HatchVisible == 1;
            if (known && visible && !hatchVisible && !hatchSeen) Add(V2Cue.HatchOpen);
            if (killer && known && hatchVisible && !visible) { hatchGoneAt = now; TryKillerClose(now); }
            hatchSeen |= visible; hatchVisible = visible;

            bool kicked = false;
            foreach (TerminalState terminal in state.Terminals)
            {
                bool isRegressing = (terminal.Flags & (byte)TerminalFlags.Regressing) != 0;
                if (known && isRegressing && regressing.TryGetValue(terminal.Id, out bool was) && !was) kicked = true;
                regressing[terminal.Id] = isRegressing;
            }
            if (kicked) Add(V2Cue.TerminalKick);

            // Survivor client only (the Killer never reads a Survivor entry's Id). An entry missing from a snapshot is
            // forgotten, so its return is a new baseline and not a status edge.
            if (!killer)
            {
                bool captured = false, rescued = false;
                seen.Clear();
                foreach (PlayerState player in state.Players)
                {
                    if (player.Role != (byte)Role.Survivor) continue;
                    var status = (PlayerStatus)player.Status;
                    if (known && survivors.TryGetValue(player.Id, out PlayerStatus before))
                    {
                        if (before == PlayerStatus.Normal && status == PlayerStatus.Lagging && player.Id != ownId) captured = true;
                        if (before == PlayerStatus.Lagging && status == PlayerStatus.Normal) rescued = true;
                    }
                    seen[player.Id] = status;
                }
                Dictionary<int, PlayerStatus> swap = survivors; survivors = seen; seen = swap;
                if (captured) Add(V2Cue.Capture);
                if (rescued) Add(V2Cue.Rescue);
            }
            known = true;
        }

        // Each S2C_SelfState: the own hold action (exit switch, kick, hatch close).
        public void OnSelf(byte kind, float progress, double now)
        {
            if (!active) return;
            ActionKind next = kind <= (byte)ActionKind.HatchClose ? (ActionKind)kind : ActionKind.None;
            if (HoldKind != ActionKind.None && next != HoldKind && killer && HoldProgress(now) >= CompleteAt)
            {
                if (HoldKind == ActionKind.Kick) Pending.Add(V2Cue.TerminalKick);
                else if (HoldKind == ActionKind.HatchClose) { closeArmedAt = now; TryKillerClose(now); }
            }
            HoldKind = next;
            holdProgress = next == ActionKind.None || float.IsNaN(progress) ? 0 : Math.Max(0, Math.Min(1, progress));
            holdAt = now;
        }

        // The hold progress at a local time: the last received value advanced at the server's own rate (SelfState is sent
        // only when the value departs from that prediction).
        public float HoldProgress(double now)
        {
            if (HoldKind == ActionKind.None) return 0;
            return (float)Math.Min(1, holdProgress + Math.Max(0, now - holdAt) * Rate(HoldKind));
        }

        public static double Rate(ActionKind kind)
            => kind == ActionKind.Switch ? 1 / GameConstants.ExitSwitchSeconds
            : kind == ActionKind.Kick ? 1 / GameConstants.KickHoldSeconds
            : kind == ActionKind.HatchClose ? 1 / GameConstants.HatchCloseSeconds : 0;

        private void TryKillerClose(double now)
        {
            if (now - closeArmedAt <= CloseWindowSeconds && now - hatchGoneAt <= CloseWindowSeconds) CloseHatch();
        }

        private void CloseHatch()
        {
            if (hatchClosed) return;
            hatchClosed = true;
            Add(V2Cue.HatchClose);
        }

        private void Add(V2Cue cue)
        {
            int bit = 1 << (int)cue;
            if ((snapshotCues & bit) != 0) return;
            snapshotCues |= bit;
            Pending.Add(cue);
        }
    }

    // Procedural rules v2 cues: mono, fixed seeds, deterministic. Amplitude 0.4 is the 0 dB reference, as for the other
    // cues; stingers sit a little above it and nothing reaches 0.95. Every one-shot starts and ends at exactly 0.
    public static class V2CueSynth
    {
        public const float Reference = .4f;
        public const float EdgeSeconds = .004f;
        // The hold loop: every partial and the tick envelope complete whole cycles in HoldSeconds, so the wrap is continuous.
        public const float HoldSeconds = .5f;

        public static float Seconds(V2Cue cue)
        {
            switch (cue)
            {
                case V2Cue.SyncGreat: return .22f;
                case V2Cue.SyncGood: return .12f;
                case V2Cue.SyncFail: return .28f;
                case V2Cue.ExitsPowered: return 1f;
                case V2Cue.ExitOpen: return .7f;
                case V2Cue.CollapseStart: return 1.2f;
                case V2Cue.HatchOpen:
                case V2Cue.HatchClose: return .35f;
                case V2Cue.Capture: return .3f;
                case V2Cue.Rescue: return .5f;
                default: return .18f; // TerminalKick
            }
        }

        public static int Length(V2Cue cue, int rate) => Math.Max(2, (int)(rate * Seconds(cue)));

        public static float[] Build(V2Cue cue, int rate)
        {
            int n = Length(cue, rate);
            var s = new float[n];
            var noise = new Random(31 + (int)cue); // fixed seed: the same sound every time
            float edge = Math.Max(1, rate * EdgeSeconds), held = 0, low = 0, hz = 0;
            double p1 = 0, p2 = 0, p3 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1), time = (float)i / rate, v;
                switch (cue)
                {
                    case V2Cue.SyncGreat: // bright two notes up: B5 then E6
                        v = .9f * (Note(time, 0, 987.77f, 24) + Note(time, .08f, 1318.51f, 12));
                        break;
                    case V2Cue.SyncGood: // one clean D6
                        v = .9f * Ramp(time, .004f) * (float)Math.Exp(-time * 18) * Sin(1174.66f * time);
                        break;
                    case V2Cue.SyncFail: // low dull buzz falling 160 -> 90 Hz
                        p1 += 2 * Math.PI * (160 - 70 * t) / rate;
                        low += .15f * ((float)Math.Tanh(2.5 * Math.Sin(p1)) - low);
                        v = 1.1f * low * (1 - .5f * t);
                        break;
                    case V2Cue.ExitsPowered: // rising hum swell 70 -> 140 Hz, peak at 80 %
                        p1 += 2 * Math.PI * (70 + 70 * t) / rate;
                        float swell = t < .8f ? t / .8f : (1 - t) / .2f;
                        v = .95f * swell * (float)((Math.Sin(p1) + .5 * Math.Sin(2 * p1) + .25 * Math.Sin(3 * p1)) / 1.75);
                        break;
                    case V2Cue.ExitOpen: // open chord G4 D5 A5, strummed 30 ms apart
                        v = .5f * (Note(time, 0, 392f, 4) + Note(time, .03f, 587.33f, 4) + Note(time, .06f, 880f, 4));
                        break;
                    case V2Cue.CollapseStart: // low alarm: three 280 ms pulses every 400 ms
                        float pt = time % .4f;
                        float gate = pt < .28f ? Math.Min(1, Math.Min(pt / .01f, (.28f - pt) / .01f)) : 0;
                        v = .75f * gate * (float)(Math.Tanh(3 * Math.Sin(2 * Math.PI * 165 * time)) + .4 * Math.Sin(2 * Math.PI * 330 * time)) / 1.4f;
                        break;
                    case V2Cue.HatchOpen: // metallic clunk, pitch up
                    case V2Cue.HatchClose: // the same clunk, pitch down
                        float f = cue == V2Cue.HatchOpen ? 260 * (1 + .5f * t) : 390 * (1 - .4f * t);
                        p1 += 2 * Math.PI * f / rate; p2 += 2 * Math.PI * f * 2.76 / rate; p3 += 2 * Math.PI * f * 5.4 / rate;
                        float metal = (float)(Math.Sin(p1) * Math.Exp(-9 * time) + .5 * Math.Sin(p2) * Math.Exp(-14 * time) + .3 * Math.Sin(p3) * Math.Exp(-22 * time));
                        float knock = (float)(noise.NextDouble() * 2 - 1) * (float)Math.Exp(-60 * time);
                        v = Ramp(time, .002f) * (.7f * metal + .4f * knock);
                        break;
                    case V2Cue.Capture: // harsh glitch: a square jumping between random pitches every 10 ms, sample-held
                        if (i % (rate / 100) == 0) hz = 200 + (float)noise.NextDouble() * 1200;
                        p1 += 2 * Math.PI * hz / rate;
                        if (i % 6 == 0) held = Math.Sin(p1) >= 0 ? 1 : -1;
                        v = .8f * held * (float)Math.Exp(-3 * t);
                        break;
                    case V2Cue.Rescue: // warm rising arpeggio E4 G#4 B4 E5
                        v = .42f * (Note(time, 0, 329.63f, 5) + Note(time, .08f, 415.3f, 5) + Note(time, .16f, 493.88f, 5) + Note(time, .24f, 659.25f, 5));
                        break;
                    default: // TerminalKick: digital crunch over a short thump
                        if (i % 14 == 0) held = (float)Math.Round((noise.NextDouble() * 2 - 1) * 2) / 2;
                        float thump = Sin(90 * (1 - .5f * t) * time) * (float)Math.Exp(-18 * time);
                        v = .55f * held * (float)Math.Exp(-12 * time) + .7f * thump;
                        break;
                }
                float fade = Math.Min(1, Math.Min(i / edge, (n - 1 - i) / edge));
                s[i] = Reference * fade * v;
            }
            return s;
        }

        // The hold loop: a 220 Hz hum with two harmonics, ticking at 8 Hz. Pitch is set on the AudioSource (HoldPitch).
        public static float[] BuildHold(int rate)
        {
            var s = new float[Math.Max(2, (int)(rate * HoldSeconds))];
            for (int i = 0; i < s.Length; i++)
            {
                float time = (float)i / rate;
                float tick = .5f + .5f * (float)Math.Cos(2 * Math.PI * 8 * time);
                float hum = (Sin(220 * time) + .35f * Sin(440 * time) + .15f * Sin(662 * time)) / 1.5f;
                s[i] = Reference * .8f * (.55f + .45f * tick * tick) * hum;
            }
            return s;
        }

        // Base pitch per hold (Switch 1, Kick and HatchClose lower), rising 60 % over the progress 0..1.
        public static float HoldPitch(ActionKind kind, float progress)
        {
            float basePitch = kind == ActionKind.Kick ? .8f : kind == ActionKind.HatchClose ? .66f : 1f;
            float p = float.IsNaN(progress) ? 0 : Math.Max(0, Math.Min(1, progress));
            return basePitch * (1 + .6f * p);
        }

        private static float Sin(float cycles) => (float)Math.Sin(2 * Math.PI * cycles);

        private static float Ramp(float time, float attack) => Math.Min(1, time / attack);

        // A plucked note from t0: 4 ms attack, exponential decay, a soft second harmonic.
        private static float Note(float time, float t0, float hz, float decay)
        {
            if (time < t0) return 0;
            float nt = time - t0;
            return Ramp(nt, .004f) * (float)Math.Exp(-nt * decay) * (Sin(hz * nt) + .2f * Sin(2 * hz * nt)) / 1.2f;
        }
    }
}
