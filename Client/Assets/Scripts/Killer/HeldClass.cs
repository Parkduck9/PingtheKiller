using System.Collections.Generic;

namespace PingTheKiller.Client.Killer
{
    // HUD_SPEC 6.2: a per-instance classification must hold for 100 ms before it switches (no flicker at wall edges).
    // A new key takes its first value at once. Keys not updated between BeginFrame and EndFrame are forgotten.
    public sealed class HeldClass
    {
        public const double Hold = .1;
        private struct Entry { public byte Value, Pending; public double PendingSince; public int Frame; }
        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private readonly List<int> stale = new List<int>();
        private int frame;

        public int Count => entries.Count;
        public void BeginFrame() => frame++;

        public byte Update(int key, byte raw, double now)
        {
            if (!entries.TryGetValue(key, out Entry e))
            {
                entries[key] = new Entry { Value = raw, Pending = raw, PendingSince = now, Frame = frame };
                return raw;
            }
            if (raw == e.Value) e.Pending = raw;
            else
            {
                if (raw != e.Pending) { e.Pending = raw; e.PendingSince = now; }
                if (now - e.PendingSince >= Hold - 1e-9) e.Value = raw;
            }
            e.Frame = frame;
            entries[key] = e;
            return e.Value;
        }

        public void EndFrame()
        {
            stale.Clear();
            foreach (var entry in entries) if (entry.Value.Frame != frame) stale.Add(entry.Key);
            foreach (int key in stale) entries.Remove(key);
        }

        public void Clear() => entries.Clear();
    }
}
