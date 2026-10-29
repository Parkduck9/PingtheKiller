using System;
using System.Collections.Generic;

namespace PingTheKiller.Client.Killer
{
    // Anonymous blip tracking for the Killer view (HUD_SPEC §6.3, §6.5). Engine-free; no LINQ.
    // Reads only position and Status of killer-bound Survivor entries. Identity is positional:
    // entries are shuffled, then greedily matched to the previous snapshot's blips by distance.

    public enum BlipStatus : byte { Normal = 0, Lagging = 1 }

    // One Survivor entry of a killer-bound WorldState (killer's own entry already removed by Role).
    public struct BlipInput
    {
        public float X, Z;
        public BlipStatus Status;

        public BlipInput(float x, float z, BlipStatus status)
        {
            X = x;
            Z = z;
            Status = status;
        }
    }

    public sealed class Blip
    {
        public int Handle;            // client-local, never derived from Id or wire order
        public float X, Z;            // current snapshot position
        public float PrevX, PrevZ;    // previous snapshot position (== X,Z when just spawned)
        public BlipStatus Status;
        public float Speed;           // mean of the last <= 4 matched per-tick displacements / 0.05 s; 0 until first match
        public float FacingX, FacingZ;// unit vector (§6.3 rule 5)
        public bool Walking;          // Speed > 0.5

        // Tracker-private state.
        internal readonly float[] SpeedHistory = new float[BlipTracker.SpeedWindow];
        internal int HistoryCount;
        internal int HistoryNext;
        internal bool HasMoved;
    }

    public enum BlipEventKind : byte { Spawned, Despawned, StatusNormalToLagging, CaptureConfirm, ExecuteConfirm }

    public struct BlipEvent
    {
        public BlipEventKind Kind;
        public int Handle;

        public BlipEvent(BlipEventKind kind, int handle)
        {
            Kind = kind;
            Handle = handle;
        }
    }

    public sealed class BlipTracker
    {
        public const float GatePerTick = 0.4f;           // 2 x 4.0 m/s x 0.05 s
        public const int MaxTickGap = 10;                // larger gap: nothing matches
        public const float TickSeconds = 0.05f;
        public const float CaptureRadius = 1.2f;
        public const float FacingMinDisplacement = 0.03f;
        public const float WalkingSpeed = 0.5f;
        public const int SpeedWindow = 4;
        public const int CaptureConfirmMinTicks = 10;    // 0.5 s of tick time

        private struct Pair
        {
            public int Old, New, Key;
            public float Dist2;
        }

        private readonly Random rng;
        private readonly float[] terminalX;
        private readonly float[] terminalZ;
        private readonly List<Blip> blips = new List<Blip>(16);
        private readonly List<Blip> scratch = new List<Blip>(16);
        private readonly List<BlipEvent> events = new List<BlipEvent>(16);

        private BlipInput[] entries = new BlipInput[16];
        private int[] entryMatch = new int[16];
        private bool[] oldMatched = new bool[16];
        private Pair[] pairs = new Pair[64];

        private bool hasSnapshot;
        private uint prevTick;
        private float prevKillerX, prevKillerZ;
        private bool hasCaptureConfirm;
        private uint lastCaptureTick;
        private int nextHandle = 1;

        public BlipTracker(Random rng, IReadOnlyList<(float x, float z)> terminals)
        {
            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));
            int count = terminals == null ? 0 : terminals.Count;
            terminalX = new float[count];
            terminalZ = new float[count];
            for (int i = 0; i < count; i++)
            {
                terminalX[i] = terminals[i].x;
                terminalZ[i] = terminals[i].z;
            }
        }

        // Current instances. Order carries nothing (it follows the shuffled entry order).
        public IReadOnlyList<Blip> Blips => blips;

        // Events produced by the last Accept.
        public IReadOnlyList<BlipEvent> Events => events;

        // New match / reconnect. Handles keep increasing so a stale handle is never reused.
        public void Reset()
        {
            blips.Clear();
            scratch.Clear();
            events.Clear();
            hasSnapshot = false;
            prevTick = 0;
            hasCaptureConfirm = false;
            lastCaptureTick = 0;
        }

        // tick = S2C_WorldState.Tick; killerX/Z = killer's own entry position.
        public void Accept(uint tick, IReadOnlyList<BlipInput> input, float killerX, float killerZ)
        {
            events.Clear();

            // 1. Copy finite entries, then shuffle before anything else.
            int inputCount = input == null ? 0 : input.Count;
            EnsureEntryCapacity(inputCount);
            int n = 0;
            for (int i = 0; i < inputCount; i++)
            {
                BlipInput e = input[i];
                if (!IsFinite(e.X) || !IsFinite(e.Z)) continue;
                entries[n++] = e;
            }
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                BlipInput t = entries[i];
                entries[i] = entries[j];
                entries[j] = t;
            }

            int oldCount = blips.Count;
            EnsureOldCapacity(oldCount);
            for (int i = 0; i < n; i++) entryMatch[i] = -1;
            for (int i = 0; i < oldCount; i++) oldMatched[i] = false;

            // A snapshot is continuous with the previous one when the tick advanced by at most MaxTickGap.
            // Cue events (capture/execute confirm) need continuity: after a reset, a big gap or a tick
            // regression nothing is observable as "changed", so only Spawned/Despawned are emitted.
            bool continuous = hasSnapshot && tick >= prevTick && tick - prevTick <= MaxTickGap;
            uint gap = continuous ? Math.Max(1u, tick - prevTick) : 1u;

            // 2. Greedy nearest matching within the gate; ties broken by a random key.
            if (continuous && oldCount > 0 && n > 0)
            {
                float gate = GatePerTick * gap;
                float gate2 = gate * gate;
                int pairCount = 0;
                for (int o = 0; o < oldCount; o++)
                {
                    Blip b = blips[o];
                    for (int e = 0; e < n; e++)
                    {
                        float dx = entries[e].X - b.X;
                        float dz = entries[e].Z - b.Z;
                        float d2 = dx * dx + dz * dz;
                        if (!(d2 <= gate2)) continue;
                        if (pairCount == pairs.Length) Array.Resize(ref pairs, pairs.Length * 2);
                        pairs[pairCount++] = new Pair { Old = o, New = e, Dist2 = d2, Key = rng.Next() };
                    }
                }
                SortPairs(pairCount);
                for (int p = 0; p < pairCount; p++)
                {
                    Pair pair = pairs[p];
                    if (oldMatched[pair.Old] || entryMatch[pair.New] >= 0) continue;
                    oldMatched[pair.Old] = true;
                    entryMatch[pair.New] = pair.Old;
                }
            }

            // 3. Unmatched old blips despawn at once.
            for (int o = 0; o < oldCount; o++)
            {
                if (oldMatched[o]) continue;
                Blip b = blips[o];
                events.Add(new BlipEvent(BlipEventKind.Despawned, b.Handle));
                if (continuous && b.Status == BlipStatus.Lagging &&
                    Dist2(b.X, b.Z, prevKillerX, prevKillerZ) <= CaptureRadius * CaptureRadius)
                {
                    events.Add(new BlipEvent(BlipEventKind.ExecuteConfirm, b.Handle));
                }
            }

            // 4. Matched pairs update; unmatched entries spawn. Order follows the shuffled entries.
            int captureHandle = 0;
            bool captureCandidate = false;
            scratch.Clear();
            for (int e = 0; e < n; e++)
            {
                BlipInput entry = entries[e];
                int o = entryMatch[e];
                if (o >= 0)
                {
                    Blip b = blips[o];
                    BlipStatus before = b.Status;
                    UpdateMatched(b, entry, gap);
                    if (before == BlipStatus.Normal && entry.Status == BlipStatus.Lagging)
                    {
                        events.Add(new BlipEvent(BlipEventKind.StatusNormalToLagging, b.Handle));
                        if (continuous && !captureCandidate)
                        {
                            captureCandidate = true;
                            captureHandle = b.Handle;
                        }
                    }
                    scratch.Add(b);
                }
                else
                {
                    Blip b = Spawn(entry);
                    events.Add(new BlipEvent(BlipEventKind.Spawned, b.Handle));
                    if (continuous && !captureCandidate && entry.Status == BlipStatus.Lagging &&
                        Dist2(entry.X, entry.Z, killerX, killerZ) <= CaptureRadius * CaptureRadius)
                    {
                        captureCandidate = true;
                        captureHandle = b.Handle;
                    }
                    scratch.Add(b);
                }
            }

            if (captureCandidate &&
                (!hasCaptureConfirm || tick < lastCaptureTick || tick - lastCaptureTick >= CaptureConfirmMinTicks))
            {
                events.Add(new BlipEvent(BlipEventKind.CaptureConfirm, captureHandle));
                hasCaptureConfirm = true;
                lastCaptureTick = tick;
            }

            // Keep the identity of the list exposed by Blips (B may cache the reference).
            blips.Clear();
            for (int i = 0; i < scratch.Count; i++) blips.Add(scratch[i]);
            scratch.Clear();

            hasSnapshot = true;
            prevTick = tick;
            prevKillerX = killerX;
            prevKillerZ = killerZ;
        }

        private void UpdateMatched(Blip b, BlipInput entry, uint gap)
        {
            float dx = entry.X - b.X;
            float dz = entry.Z - b.Z;
            float disp = (float)Math.Sqrt(dx * dx + dz * dz);

            b.PrevX = b.X;
            b.PrevZ = b.Z;
            b.X = entry.X;
            b.Z = entry.Z;
            b.Status = entry.Status;

            // Speed history holds per-tick displacements, so a multi-tick gap does not inflate v.
            b.SpeedHistory[b.HistoryNext] = disp / gap;
            b.HistoryNext = (b.HistoryNext + 1) % SpeedWindow;
            if (b.HistoryCount < SpeedWindow) b.HistoryCount++;
            float sum = 0f;
            for (int i = 0; i < b.HistoryCount; i++) sum += b.SpeedHistory[i];
            b.Speed = sum / b.HistoryCount / TickSeconds;
            b.Walking = b.Speed > WalkingSpeed;

            if (disp >= FacingMinDisplacement)
            {
                b.FacingX = dx / disp;
                b.FacingZ = dz / disp;
                b.HasMoved = true;
            }
            else if (!b.HasMoved)
            {
                FaceNearestTerminal(b);
            }
        }

        private Blip Spawn(BlipInput entry)
        {
            var b = new Blip
            {
                Handle = nextHandle++,
                X = entry.X,
                Z = entry.Z,
                PrevX = entry.X,
                PrevZ = entry.Z,
                Status = entry.Status,
                Speed = 0f,
                Walking = false,
            };
            FaceNearestTerminal(b);
            return b;
        }

        private void FaceNearestTerminal(Blip b)
        {
            int best = -1;
            float bestD2 = float.PositiveInfinity;
            for (int i = 0; i < terminalX.Length; i++)
            {
                float d2 = Dist2(terminalX[i], terminalZ[i], b.X, b.Z);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }
            if (best < 0 || bestD2 < 1e-8f)
            {
                b.FacingX = 0f;
                b.FacingZ = 1f;
                return;
            }
            float len = (float)Math.Sqrt(bestD2);
            b.FacingX = (terminalX[best] - b.X) / len;
            b.FacingZ = (terminalZ[best] - b.Z) / len;
        }

        // Insertion sort by (Dist2, Key): allocation-free and pair counts are small (<= ~100).
        private void SortPairs(int count)
        {
            for (int i = 1; i < count; i++)
            {
                Pair item = pairs[i];
                int j = i - 1;
                while (j >= 0 && Less(item, pairs[j]))
                {
                    pairs[j + 1] = pairs[j];
                    j--;
                }
                pairs[j + 1] = item;
            }
        }

        private static bool Less(Pair a, Pair b) => a.Dist2 < b.Dist2 || (a.Dist2 == b.Dist2 && a.Key < b.Key);

        private void EnsureEntryCapacity(int count)
        {
            if (entries.Length < count) entries = new BlipInput[Math.Max(count, entries.Length * 2)];
            if (entryMatch.Length < count) entryMatch = new int[Math.Max(count, entryMatch.Length * 2)];
        }

        private void EnsureOldCapacity(int count)
        {
            if (oldMatched.Length < count) oldMatched = new bool[Math.Max(count, oldMatched.Length * 2)];
        }

        private static float Dist2(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
