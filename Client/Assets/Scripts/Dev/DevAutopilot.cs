using System.Collections.Generic;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Dev
{
    public enum PilotPhase : byte { Idle, ToTerminal, Hacking, ToSwitch, Switching, ToExit }

    // Dev only (-ptk-dev-hack, -ptk-dev-switch): the Survivor autopilot's goal, pure. Each frame it reads the last
    // WorldState like a player reads the screen and answers where to walk and whether to hold Interact; DevCapture turns
    // that into the normal MoveInput (NavGrid steering) and C2S_Interact. It never sends anything itself.
    public sealed class HackPilot
    {
        // Stop this close to a terminal or switch (both reach 1.5 m); once holding, keep holding up to HoldReach.
        public const float Arrive = 1f, HoldReach = 1.4f, ExitArrive = 1f;
        private readonly float hackSeconds;
        private readonly bool switches;
        public PilotPhase Phase { get; private set; }
        public Vector3 Goal { get; private set; }
        public bool Walk { get; private set; }
        public bool Hold { get; private set; }
        public float Held { get; private set; }

        public HackPilot(float hackSeconds, bool switches)
        {
            this.hackSeconds = Mathf.Max(0, hackSeconds);
            this.switches = switches;
        }

        public void Update(float x, float z, bool normal, S2C_WorldState world, IReadOnlyList<MapPoint> switchPoints, float dt)
        {
            PilotPhase was = Phase;
            Walk = Hold = false;
            if (!normal || world == null) { Phase = PilotPhase.Idle; return; }
            bool powered = false;
            int open = -1;
            if (world.Exits != null)
                for (int i = 0; i < world.Exits.Count; i++)
                {
                    byte flags = world.Exits[i].Flags;
                    if ((flags & 3) != 0) powered = true;
                    if ((flags & 2) != 0 && open < 0) open = i;
                }
            if (switches && powered && open >= 0)
            {
                Phase = PilotPhase.ToExit;
                Goal = new Vector3(world.Exits[open].X, 0, world.Exits[open].Z);
                Walk = Distance(x, z, Goal) > ExitArrive;
                return;
            }
            if (switches && powered && switchPoints != null)
            {
                int best = -1; float bestDistance = float.MaxValue;
                for (int i = 0; i < world.Exits.Count; i++)
                {
                    ExitState exit = world.Exits[i];
                    if ((exit.Flags & 2) != 0 || exit.Id >= switchPoints.Count) continue;
                    float d = Distance(x, z, new Vector3(switchPoints[exit.Id].X, 0, switchPoints[exit.Id].Z));
                    if (d < bestDistance) { bestDistance = d; best = exit.Id; }
                }
                if (best >= 0)
                {
                    Goal = new Vector3(switchPoints[best].X, 0, switchPoints[best].Z);
                    bool at = bestDistance <= Arrive || (was == PilotPhase.Switching && bestDistance <= HoldReach);
                    Phase = at ? PilotPhase.Switching : PilotPhase.ToSwitch;
                    Hold = at; Walk = !at;
                    return;
                }
            }
            if (Held >= hackSeconds || powered || world.Terminals == null) { Phase = PilotPhase.Idle; return; }
            int terminal = -1; float near = float.MaxValue;
            for (int i = 0; i < world.Terminals.Count; i++)
            {
                if (world.Terminals[i].Progress >= 100) continue;
                float d = Distance(x, z, new Vector3(world.Terminals[i].X, 0, world.Terminals[i].Z));
                if (d < near) { near = d; terminal = i; }
            }
            if (terminal < 0) { Phase = PilotPhase.Idle; return; }
            Goal = new Vector3(world.Terminals[terminal].X, 0, world.Terminals[terminal].Z);
            bool reached = near <= Arrive || (was == PilotPhase.Hacking && near <= HoldReach);
            Phase = reached ? PilotPhase.Hacking : PilotPhase.ToTerminal;
            Hold = reached; Walk = !reached;
            if (reached && dt > 0) Held += dt;
        }

        private static float Distance(float x, float z, Vector3 goal) => new Vector2(goal.x - x, goal.z - z).magnitude;

        // -ptk-auto-route (11/17, R2), pure: seconds between scripted route keys (fast and sloppy 0.15 s, slow 0.5 s).
        public const float FastKeySeconds = .15f, SlowKeySeconds = .5f;
        // Sloppy: every SloppyEvery-th scripted key is wrong (20 %).
        public const int SloppyEvery = 5;

        public static float RouteKeySeconds(AutoRoute pace) => pace == AutoRoute.Slow ? SlowKeySeconds : FastKeySeconds;

        // The key the autopilot presses as its n-th key (1-based): the expected direction, or for Sloppy every fifth key the
        // opposite one (always wrong).
        public static RouteDirection RouteKey(AutoRoute pace, int n, RouteDirection expected)
        {
            if (pace != AutoRoute.Sloppy || n % SloppyEvery != 0) return expected;
            switch (expected)
            {
                case RouteDirection.Up: return RouteDirection.Down;
                case RouteDirection.Down: return RouteDirection.Up;
                case RouteDirection.Left: return RouteDirection.Right;
                default: return RouteDirection.Left;
            }
        }
    }
}
