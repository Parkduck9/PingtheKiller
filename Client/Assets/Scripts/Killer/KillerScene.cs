using System;
using System.Collections.Generic;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Killer
{
    public enum BlipClass : byte { InView = 0, Occluded = 1, OffScreen = 2 }

    // One anonymous instance as the Killer view draws it this frame.
    public struct BlipView
    {
        public int Handle;
        public Vector3 Position;   // ground position, tweened Prev -> current over one tick
        public BlipStatus Status;
        public BlipClass Class;    // held 100 ms (HUD_SPEC 6.2)
        public bool Occluded;      // the wall ray alone, held 100 ms; cast even off-screen (sound, 6.5.1)
        public float Distance;     // horizontal, from the camera
        public bool OutOfFrustum;  // raw, this frame: the marker point is off-screen even while the held class is not
        public bool Edge;          // off-screen and among the 6 shown edge indicators
        public float FacingYaw;
        public bool Walking;
        public bool ModelVisible; // Separate bounds test; the point-based marker/edge class is unchanged.
    }

    // The Killer stream, pure (HUD_SPEC 6.2/6.3). It reads only Role, Status, X and Z of each entry: the Killer's own
    // entry is found by Role, every other entry goes into the BlipTracker. Survivor Id, Yaw, StatusTimer, CaptureCount
    // and every flag stay unread, so real Ids, Id = 0 and shuffled order all draw the same.
    public sealed class KillerScene
    {
        public const float MarkerHeight = 1.2f;
        public const int EdgeCount = 6;
        public const double TweenSeconds = .05;
        private readonly BlipTracker tracker;
        private readonly List<BlipInput> inputs = new List<BlipInput>();
        private readonly HeldClass classes = new HeldClass(), occlusion = new HeldClass();
        private readonly List<BlipView> views = new List<BlipView>();
        private readonly List<int> edge = new List<int>();
        private readonly Dictionary<int, bool> occluded = new Dictionary<int, bool>();
        private Func<int, bool> voiced;
        private double acceptedAt = double.NegativeInfinity;
        public bool HasKiller { get; private set; }
        public float KillerX { get; private set; }
        public float KillerZ { get; private set; }
        public IReadOnlyList<Blip> Blips => tracker.Blips;
        public IReadOnlyList<BlipEvent> Events => tracker.Events;
        public IReadOnlyList<BlipView> Views => views;
        // The sound model's occlusion input: the held wall-ray result of the last frame.
        public readonly Func<int, bool> IsOccluded;

        public KillerScene(System.Random rng, IReadOnlyList<(float x, float z)> terminals)
        {
            tracker = new BlipTracker(rng, terminals);
            IsOccluded = handle => occluded.TryGetValue(handle, out bool value) && value;
        }

        public static List<(float x, float z)> TerminalPoints(IReadOnlyList<TerminalState> terminals)
        {
            var points = new List<(float x, float z)>();
            if (terminals != null) foreach (TerminalState terminal in terminals) points.Add((terminal.X, terminal.Z));
            return points;
        }

        public void Accept(S2C_WorldState state, double now)
        {
            inputs.Clear();
            HasKiller = false;
            foreach (PlayerState entry in state.Players)
            {
                if (entry.Role == (byte)Role.Killer) { HasKiller = true; KillerX = entry.X; KillerZ = entry.Z; continue; }
                // Normal or Lagging only; Escaped and Eliminated Survivors are not drawn.
                if (entry.Status == (byte)PlayerStatus.Normal) inputs.Add(new BlipInput(entry.X, entry.Z, BlipStatus.Normal));
                else if (entry.Status == (byte)PlayerStatus.Lagging) inputs.Add(new BlipInput(entry.X, entry.Z, BlipStatus.Lagging));
            }
            tracker.Accept(state.Tick, inputs, KillerX, KillerZ);
            acceptedAt = now;
        }

        public Vector3 Tweened(Blip blip, double now)
        {
            float t = (float)Math.Max(0, Math.Min(1, (now - acceptedAt) / TweenSeconds));
            return new Vector3(Mathf.Lerp(blip.PrevX, blip.X, t), 0, Mathf.Lerp(blip.PrevZ, blip.Z, t));
        }

        // Classifies every instance for the view. isVoiced: the sound model's voice state (voiced instances are chosen
        // first for the 6 edge indicators); null = none voiced.
        public IReadOnlyList<BlipView> Frame(ViewPose view, double now, Func<int, bool> isVoiced)
        {
            voiced = isVoiced;
            classes.BeginFrame(); occlusion.BeginFrame();
            views.Clear(); occluded.Clear();
            IReadOnlyList<Blip> blips = tracker.Blips;
            for (int i = 0; i < blips.Count; i++)
            {
                Blip blip = blips[i];
                Vector3 position = Tweened(blip, now);
                bool blocked = MapGeometry.SegmentBlocked(SharedMap.Map, view.Eye.x, view.Eye.z, position.x, position.z);
                bool inFrustum = view.InFrustum(position + Vector3.up * MarkerHeight);
                byte raw = (byte)(!inFrustum ? BlipClass.OffScreen : blocked ? BlipClass.Occluded : BlipClass.InView);
                var item = new BlipView
                {
                    Handle = blip.Handle, Position = position, Status = blip.Status,
                    Class = (BlipClass)classes.Update(blip.Handle, raw, now),
                    OutOfFrustum = !inFrustum,
                    Occluded = occlusion.Update(blip.Handle, (byte)(blocked ? 1 : 0), now) == 1,
                    Distance = new Vector2(position.x - view.Eye.x, position.z - view.Eye.z).magnitude,
                    FacingYaw = Mathf.Atan2(blip.FacingX, blip.FacingZ) * Mathf.Rad2Deg,
                    Walking = blip.Walking
                };
                item.ModelVisible = !item.Occluded && CharacterLook.InModelFrustum(view, position);
                occluded[blip.Handle] = item.Occluded;
                views.Add(item);
            }
            classes.EndFrame(); occlusion.EndFrame();
            SelectEdges();
            return views;
        }

        public int Count(BlipClass value)
        {
            int count = 0;
            foreach (BlipView view in views) if (value == BlipClass.OffScreen ? view.Edge : view.Class == value) count++;
            return count;
        }

        // The nearest Lagging instance (for execution guidance), from the Killer's snapshot position.
        public int NearestLagging(out float distance)
        {
            int best = -1; distance = float.PositiveInfinity;
            IReadOnlyList<Blip> blips = tracker.Blips;
            for (int i = 0; i < blips.Count; i++)
            {
                if (blips[i].Status != BlipStatus.Lagging) continue;
                float dx = blips[i].X - KillerX, dz = blips[i].Z - KillerZ, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < distance) { distance = d; best = blips[i].Handle; }
            }
            return best;
        }

        public void Reset()
        {
            tracker.Reset(); classes.Clear(); occlusion.Clear(); views.Clear(); occluded.Clear();
            HasKiller = false; acceptedAt = double.NegativeInfinity;
        }

        // HUD_SPEC 6.2 / 6.5.1: only the 6 nearest off-screen instances get edge indicators; voiced instances first.
        // An instance whose held class is still In view or Occluded but whose marker point has left the frustum is drawn as an
        // edge indicator at once (no empty frames); the 100 ms hold of its class keeps running.
        private void SelectEdges()
        {
            edge.Clear();
            for (int i = 0; i < views.Count; i++) if (views[i].Class == BlipClass.OffScreen || views[i].OutOfFrustum) edge.Add(i);
            // Insertion sort by (voiced first, distance): at most a few dozen entries, no allocation, no LINQ.
            for (int i = 1; i < edge.Count; i++)
            {
                int key = edge[i], j = i - 1;
                while (j >= 0 && Before(key, edge[j])) { edge[j + 1] = edge[j]; j--; }
                edge[j + 1] = key;
            }
            for (int i = 0; i < edge.Count && i < EdgeCount; i++)
            {
                BlipView view = views[edge[i]]; view.Edge = true; views[edge[i]] = view;
            }
        }

        private bool Before(int a, int b)
        {
            bool va = voiced != null && voiced(views[a].Handle), vb = voiced != null && voiced(views[b].Handle);
            if (va != vb) return va;
            return views[a].Distance < views[b].Distance;
        }
    }
}
