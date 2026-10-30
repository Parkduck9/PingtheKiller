using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    public sealed class SnapshotBuffer
    {
        private sealed class Frame
        {
            public double Time;
            public S2C_WorldState State;
        }
        private readonly List<Frame> frames = new List<Frame>();
        public const double Delay = .1;
        // Key for the Killer's own entry: found by Role, never by Id (HUD_SPEC 6.3; U2c may anonymise Survivor Ids).
        public const int KillerKey = int.MinValue;
        public void Clear() => frames.Clear();

        public void Add(S2C_WorldState state, double time)
        {
            if (frames.Count > 0 && state.Tick <= frames[frames.Count - 1].State.Tick) return;
            frames.Add(new Frame { State = state, Time = time });
            if (frames.Count > 32) frames.RemoveAt(0);
        }

        // id = a player Id (Survivor stream), or KillerKey for the entry whose Role is Killer.
        public bool Sample(int id, double now, out Vector3 position, out float yaw, out float alpha)
        {
            position = default; yaw = 0; alpha = 0;
            double renderTime = now - Delay;
            int index = 0;
            // Deliberate (QA-05): an entity stays hidden until render time reaches the first snapshot that contains it,
            // instead of clamping to that snapshot. Clamping would reveal a survivor who just turned Signal 100 ms early
            // and out of sync with everyone else. Cost: the world appears 100 ms after match start.
            if (frames.Count == 0 || renderTime < frames[0].Time) return false;
            while (index + 1 < frames.Count && frames[index + 1].Time <= renderTime) index++;
            int present = index;
            while (present >= 0 && !Find(frames[present].State, id, out _)) present--;
            if (present < 0) return false;
            Find(frames[present].State, id, out PlayerState a);
            position = new Vector3(a.X, 1, a.Z); yaw = a.Yaw; alpha = 1;
            if (present < index)
            {
                alpha = ClientMath.Fade(renderTime, frames[present + 1].Time);
                return alpha > 0;
            }
            if (index + 1 < frames.Count && Find(frames[index + 1].State, id, out PlayerState b))
            {
                position = ClientMath.Interpolate(position, new Vector3(b.X, 1, b.Z), frames[index].Time, frames[index + 1].Time, renderTime);
                yaw = ClientMath.InterpolateYaw(a.Yaw, b.Yaw, frames[index].Time, frames[index + 1].Time, renderTime);
            }
            return true;
        }

        // The Killer entry on a Survivor client (U2e contract, HUD_SPEC 4.10 (10/30)): the server sends it only while this
        // Survivor's line of sight to the Killer is clear, and it is found by Role. The model is the only Killer rendering.
        // - present in consecutive snapshots: interpolated like any player, facing its Yaw;
        // - absent: held at the last received position (never extrapolated, never moved) and faded out over KillerFade;
        // - a gap of at most KillerFade between two sightings is bridged by interpolation (no flicker on a wall edge);
        // - a longer gap snaps: the model reappears at the new position, never interpolated across the hidden path.
        public const double KillerFade = .3;
        public bool SampleKiller(double now, out Vector3 position, out float yaw, out float alpha)
        {
            position = default; yaw = 0; alpha = 0;
            double renderTime = now - Delay;
            if (frames.Count == 0 || renderTime < frames[0].Time) return false;
            int index = 0;
            while (index + 1 < frames.Count && frames[index + 1].Time <= renderTime) index++;
            int present = index;
            while (present >= 0 && !Find(frames[present].State, KillerKey, out _)) present--;
            if (present < 0) return false;
            Find(frames[present].State, KillerKey, out PlayerState a);
            position = new Vector3(a.X, 1, a.Z); yaw = a.Yaw; alpha = 1;
            int next = index + 1;
            while (next < frames.Count && !Find(frames[next].State, KillerKey, out _)) next++;
            bool bridged = next < frames.Count && (next == present + 1 || frames[next].Time - frames[present].Time <= KillerFade + 1e-9);
            if (bridged)
            {
                Find(frames[next].State, KillerKey, out PlayerState b);
                position = ClientMath.Interpolate(position, new Vector3(b.X, 1, b.Z), frames[present].Time, frames[next].Time, renderTime);
                yaw = ClientMath.InterpolateYaw(a.Yaw, b.Yaw, frames[present].Time, frames[next].Time, renderTime);
                return true;
            }
            if (present == index) return true; // the newest sighting, or the next snapshot already lacks it: hold, no extrapolation
            alpha = Mathf.Clamp01(1 - (float)((renderTime - frames[present + 1].Time) / KillerFade));
            return alpha > 0;
        }

        private static bool Find(S2C_WorldState state, int id, out PlayerState player)
        {
            foreach (PlayerState entry in state.Players)
                if (id == KillerKey ? entry.Role == (byte)Role.Killer : entry.Id == id) { player = entry; return true; }
            player = default;
            return false;
        }
    }
}
