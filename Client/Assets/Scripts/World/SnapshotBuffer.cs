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
        public void Clear() => frames.Clear();

        public void Add(S2C_WorldState state, double time)
        {
            if (frames.Count > 0 && state.Tick <= frames[frames.Count - 1].State.Tick) return;
            frames.Add(new Frame { State = state, Time = time });
            if (frames.Count > 32) frames.RemoveAt(0);
        }

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

        private static bool Find(S2C_WorldState state, int id, out PlayerState player)
        {
            foreach (PlayerState entry in state.Players)
                if (entry.Id == id) { player = entry; return true; }
            player = default;
            return false;
        }
    }
}
