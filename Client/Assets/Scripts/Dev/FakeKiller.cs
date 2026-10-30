using System;
using System.Collections.Generic;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Dev
{
    // -ptk-fake-killer <m>: dev screenshots of the U2e contract before the server sends the entry. On a Survivor client
    // only, it appends one Killer entry (Role = Killer, Flags / StatusTimer / CaptureCount = 0, like the contract) to each
    // received snapshot's player list, standing `distance` metres ahead of the own Survivor (pulled closer until the line
    // of sight is clear), facing the Survivor. It shows for OnSeconds, then is left out for OffSeconds (the fade), and is
    // re-placed on each showing. It never adds a second Killer entry, never adds one for a spectator (Eliminated or
    // Escaped), and raises the snapshot's threat level to what that distance would give. Nothing is ever sent.
    public sealed class FakeKiller
    {
        public const double OnSeconds = 4, OffSeconds = 2;
        public const int EntryId = 9999;
        private readonly float distance;
        private double start = double.NaN;
        private bool placed;
        private float x, z;
        public bool Showing { get; private set; }
        public int Injected { get; private set; }

        public FakeKiller(float metres) { distance = Math.Max(1, metres); }

        public void Reset() { start = double.NaN; placed = false; Showing = false; }

        // Returns the threat level the injected entry implies (0 when nothing was injected).
        public byte Inject(List<PlayerState> players, int ownId, double now)
        {
            Showing = false;
            if (players == null) return 0;
            if (double.IsNaN(start)) start = now;
            if ((now - start) % (OnSeconds + OffSeconds) >= OnSeconds) { placed = false; return 0; }
            int own = -1;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Role == (byte)Role.Killer) return 0; // a real entry: never a second Killer
                if (players[i].Id == ownId && players[i].Role == (byte)Role.Survivor) own = i;
            }
            if (own < 0) return 0;
            PlayerState self = players[own];
            if (self.Status == (byte)PlayerStatus.Eliminated || self.Status == (byte)PlayerStatus.Escaped) return 0;
            if (!placed) { Place(self.X, self.Z, self.Yaw); placed = true; }
            float dx = self.X - x, dz = self.Z - z;
            players.Add(new PlayerState
            {
                Id = EntryId, Role = (byte)Role.Killer, Status = (byte)PlayerStatus.Normal,
                X = x, Z = z, Yaw = (float)(Math.Atan2(dx, dz) * 180 / Math.PI)
            });
            Showing = true; Injected++;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            return d <= GameConstants.ThreatLevel3Distance ? (byte)3 : d <= GameConstants.ThreatLevel2Distance ? (byte)2 : d <= GameConstants.ThreatLevel1Distance ? (byte)1 : (byte)0;
        }

        // Ahead of the Survivor, SideDegrees off its facing (so the third-person camera behind the character sees it beside
        // the character, not hidden by it); 0.5 m closer per step until no static wall is in between (at least 1 m).
        public const float SideDegrees = 20;
        private void Place(float fromX, float fromZ, float yaw)
        {
            double radians = (yaw + SideDegrees) * Math.PI / 180;
            float sx = (float)Math.Sin(radians), cz = (float)Math.Cos(radians);
            for (float d = distance; ; d -= .5f)
            {
                x = fromX + sx * d; z = fromZ + cz * d;
                if (d <= 1 || !MapGeometry.SegmentBlocked(SharedMap.Map, fromX, fromZ, x, z)) return;
            }
        }
    }
}
