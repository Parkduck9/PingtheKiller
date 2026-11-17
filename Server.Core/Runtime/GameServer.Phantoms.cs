using System;
using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // Fake Signals (Dummy Packet, White Noise). A phantom has the same visibility fields as a real survivor and goes
    // through the same killer-view entry builder and afterimage rule. It walks like a survivor bot (SurvivorSpeed,
    // Map.Move, the same route finding) and stands still only within TerminalRadius of an incomplete terminal.
    // Placement and motion draw only from the private _decoyRandom stream.
    public sealed partial class GameServer
    {
        private sealed class Phantom
        {
            internal int Serial;
            internal bool IsDummy;
            internal double SpawnAt, Until;
            internal float X, Z;
            // Ghost-bot pacing: a burst (walking, or holding still at a terminal), one held frame, then a Silent pause.
            internal byte Pace;
            internal double BurstEnd, PauseEnd;
            // In the previous killer view: keeps its slot when the entry cap binds, so a shown track is never cut.
            internal bool InView;
            internal bool Visible;
            internal double LastVisibleAt = double.NegativeInfinity;
            internal float VisibleX, VisibleZ;
            internal readonly PathFollower Route = new PathFollower();
            // A White Noise walker first follows its landmark route to the far end; afterwards it heads for terminals.
            internal bool Headed;
            internal float HeadX, HeadZ;
        }

        // Ground truth of one phantom for the server tests.
        internal readonly struct PhantomInfo
        {
            internal PhantomInfo(int serial, bool isDummy, double spawnAt, double until, float x, float z, bool visible, bool holding)
            { Serial = serial; IsDummy = isDummy; SpawnAt = spawnAt; Until = until; X = x; Z = z; Visible = visible; Holding = holding; }
            internal readonly int Serial;
            internal readonly bool IsDummy;
            internal readonly double SpawnAt, Until;
            internal readonly float X, Z;
            internal readonly bool Visible;
            // The still frame that ends a burst (the Signal hold of a stopping survivor).
            internal readonly bool Holding;
        }

        private const float WhiteNoiseStaggerSeconds = 0.5f;
        private const float WhiteNoiseKillerClearance = 3f;
        private const float WhiteNoiseSpacing = 6f;
        private const float WhiteNoiseTerminalJitter = 1f;
        private const float WhiteNoiseSegmentReach = 4f;
        // The worker/traveller mix and burst pacing live with the bot tuning in GameServer.Bots.

        private readonly List<Phantom> _decoys = new List<Phantom>();
        private readonly List<MapPoint> _landmarks = new List<MapPoint>();
        // NavGrid routes between landmark cells, found on first use. The landmarks never move, so a route is kept for the
        // whole server lifetime; an empty route means the pair is not connected.
        private readonly Dictionary<long, List<MapPoint>> _landmarkRoutes = new Dictionary<long, List<MapPoint>>();
        private int _nextPhantomSerial = 1;

        internal double Now => _now;

        // Ground truth of every player for the server tests.
        internal readonly struct PlayerInfo
        {
            internal PlayerInfo(Player p, bool exposed)
            {
                Id = p.Id; IsBot = p.IsBot; Role = p.Role; Status = p.Status; X = p.X; Z = p.Z; Hacking = p.Hacking; Visible = p.Visible;
                Rescuing = p.Rescuing; Exposed = exposed; Switching = p.SwitchExit >= 0;
            }
            internal readonly int Id;
            internal readonly bool IsBot;
            internal readonly Role Role;
            internal readonly PlayerStatus Status;
            internal readonly float X, Z;
            internal readonly bool Hacking, Visible, Rescuing;
            // Holding an exit switch (GDD v2 §3): Signal while standing still at a known switch point.
            internal readonly bool Switching;
            // Forced Signal (Ping Scanner, Traffic Overload).
            internal readonly bool Exposed;
        }

        internal List<PlayerInfo> PlayerSnapshot()
        {
            var list = new List<PlayerInfo>(_players.Count);
            foreach (var p in _players) list.Add(new PlayerInfo(p, Active(p.ExposedUntil)));
            return list;
        }

        internal List<PhantomInfo> PhantomSnapshot()
        {
            var list = new List<PhantomInfo>(_decoys.Count);
            foreach (var p in _decoys) list.Add(new PhantomInfo(p.Serial, p.IsDummy, p.SpawnAt, p.Until, p.X, p.Z, p.Visible, p.Pace == PaceHolding));
            return list;
        }

        private Phantom AddPhantom(bool isDummy, double spawnAt, double until, float x, float z)
        {
            var phantom = new Phantom { Serial = _nextPhantomSerial++, IsDummy = isDummy, SpawnAt = spawnAt, Until = until, X = x, Z = z };
            _decoys.Add(phantom);
            return phantom;
        }

        // GDD §4: a fake Signal at the caster's current position for 3 s, whether or not the caster is Signal.
        // A caster in the middle of a vault is over the prop: the phantom starts on the nearer standable end instead.
        private void CastDummy(Player caster)
        {
            float x = caster.X, z = caster.Z;
            if (caster.ChaseKind == (byte)ActionKind.Vault && !Map.CanStand(x, z))
            {
                double t = (_now - caster.ChaseStart) / Math.Max(TimeEpsilon, caster.ChaseEnd - caster.ChaseStart);
                bool from = t < 0.5 && Map.CanStand(caster.ChaseFromX, caster.ChaseFromZ);
                x = from ? caster.ChaseFromX : caster.ChaseToX;
                z = from ? caster.ChaseFromZ : caster.ChaseToZ;
            }
            StartBurst(AddPhantom(true, _now, _now + SkillIds.DummyPacketSeconds, x, z), _now);
        }

        // GDD §4: five fake Signals around the map for 1.5 s. Spawns are staggered over 0-0.5 s and all expire together.
        private void CastWhiteNoise()
        {
            BuildLandmarks();
            var placed = new List<MapPoint>();
            int terminalPoints = WhiteNoiseMinTerminalPoints + _decoyRandom.Next(WhiteNoiseTerminalPointChoices);
            for (int i = 0; i < SkillIds.WhiteNoiseSignalCount; i++)
            {
                bool atTerminal = i < terminalPoints && IncompleteTerminalCount() > 0;
                if (!TryPlaceWhiteNoise(atTerminal, placed, out float x, out float z, out float goalX, out float goalZ))
                {
                    atTerminal = false;
                    if (!TryPlaceWhiteNoise(false, placed, out x, out z, out goalX, out goalZ)) continue;
                }
                placed.Add(new MapPoint(x, z));
                double spawn = _now + _decoyRandom.NextDouble() * WhiteNoiseStaggerSeconds;
                var phantom = AddPhantom(false, spawn, _now + SkillIds.WhiteNoiseSeconds, x, z);
                if (!atTerminal)
                {
                    phantom.Headed = true;
                    phantom.HeadX = goalX;
                    phantom.HeadZ = goalZ;
                }
                StartBurst(phantom, spawn);
            }
        }

        private int IncompleteTerminalCount()
        {
            int count = 0;
            foreach (var terminal in _terminals) if (terminal.Progress < 100) ++count;
            return count;
        }

        // Plausible spots: next to an incomplete terminal (still), or 0-4 m along the NavGrid route from one landmark
        // (terminals, the exit, survivor spawns) to another, walking on along it. Points keep clear of the killer and of
        // each other; the spacing relaxes only if the map cannot hold five spaced points.
        private bool TryPlaceWhiteNoise(bool atTerminal, List<MapPoint> placed, out float x, out float z, out float goalX, out float goalZ)
        {
            for (int attempt = 0; attempt < 240; attempt++)
            {
                float spacing = attempt < 120 ? WhiteNoiseSpacing : attempt < 200 ? WhiteNoiseSpacing / 2 : 0;
                if (atTerminal)
                {
                    int pick = _decoyRandom.Next(IncompleteTerminalCount());
                    var terminal = default(TerminalState);
                    foreach (var t in _terminals)
                        if (t.Progress < 100 && pick-- == 0) { terminal = t; break; }
                    x = terminal.X + (float)(_decoyRandom.NextDouble() * 2 - 1) * WhiteNoiseTerminalJitter;
                    z = terminal.Z + (float)(_decoyRandom.NextDouble() * 2 - 1) * WhiteNoiseTerminalJitter;
                    goalX = x;
                    goalZ = z;
                }
                else
                {
                    if (_landmarks.Count < 2) break;
                    int a = _decoyRandom.Next(_landmarks.Count);
                    int b = _decoyRandom.Next(_landmarks.Count - 1);
                    if (b >= a) ++b;
                    var route = LandmarkRoute(_landmarks[a], _landmarks[b]);
                    float length = RouteLength(route);
                    float along = (float)(_decoyRandom.NextDouble() * Math.Min(WhiteNoiseSegmentReach, length));
                    if (length <= 0) continue;
                    PointAlong(route, along, out x, out z);
                    goalX = _landmarks[b].X;
                    goalZ = _landmarks[b].Z;
                }
                if (!Map.CanStand(x, z)) continue;
                if (_killer != null && DistanceSquared(x, z, _killer.X, _killer.Z) < WhiteNoiseKillerClearance * WhiteNoiseKillerClearance) continue;
                bool crowded = false;
                foreach (var point in placed)
                    if (DistanceSquared(x, z, point.X, point.Z) < spacing * spacing) crowded = true;
                if (!crowded) return true;
            }
            x = z = goalX = goalZ = 0;
            return false;
        }

        private void BuildLandmarks()
        {
            _landmarks.Clear();
            foreach (var terminal in _terminals) _landmarks.Add(new MapPoint(terminal.X, terminal.Z));
            foreach (var exit in Map.Exits) _landmarks.Add(exit);
            for (int i = 1; i < Map.SpawnPoints.Count; i++) _landmarks.Add(Map.SpawnPoints[i]);
        }

        private List<MapPoint> LandmarkRoute(MapPoint from, MapPoint to)
        {
            long key = (long)NavGrid.Cell(from.X, from.Z) * NavGrid.NodeCount + NavGrid.Cell(to.X, to.Z);
            if (_landmarkRoutes.TryGetValue(key, out var route)) return route;
            route = new List<MapPoint>();
            if (!Map.CanStand(from.X, from.Z) || !Nav.FindPath(from.X, from.Z, to.X, to.Z, route)) route.Clear();
            _landmarkRoutes[key] = route;
            return route;
        }

        private static float RouteLength(List<MapPoint> route)
        {
            float length = 0;
            for (int i = 1; i < route.Count; i++)
                length += (float)Math.Sqrt(DistanceSquared(route[i - 1].X, route[i - 1].Z, route[i].X, route[i].Z));
            return length;
        }

        private static void PointAlong(List<MapPoint> route, float along, out float x, out float z)
        {
            x = route[route.Count - 1].X;
            z = route[route.Count - 1].Z;
            for (int i = 1; i < route.Count; i++)
            {
                float leg = (float)Math.Sqrt(DistanceSquared(route[i - 1].X, route[i - 1].Z, route[i].X, route[i].Z));
                if (along <= leg && leg > 0)
                {
                    x = route[i - 1].X + (route[i].X - route[i - 1].X) * along / leg;
                    z = route[i - 1].Z + (route[i].Z - route[i - 1].Z) * along / leg;
                    return;
                }
                along -= leg;
            }
        }

        private bool NearIncompleteTerminal(float x, float z, float radius)
        {
            foreach (var terminal in _terminals)
                if (terminal.Progress < 100 && DistanceSquared(x, z, terminal.X, terminal.Z) <= radius * radius + 0.000001f) return true;
            return false;
        }

        private const byte PaceActive = 0, PaceHolding = 1, PacePaused = 2;

        // The burst length follows the survivor bots: hack pacing at a terminal, walk pacing elsewhere.
        private bool PhantomAtTerminal(Phantom phantom) =>
            NearIncompleteTerminal(phantom.X, phantom.Z, GameConstants.TerminalRadius);

        private void StartBurst(Phantom phantom, double at)
        {
            phantom.Pace = PaceActive;
            phantom.BurstEnd = at + (PhantomAtTerminal(phantom)
                ? Uniform(_decoyRandom, BotHackBurstMin, BotHackBurstMax)
                : Uniform(_decoyRandom, BotWalkBurstMin, BotWalkBurstMax));
        }

        private static double Uniform(Random random, double min, double max) => min + random.NextDouble() * (max - min);

        // Runs with the rules, once per tick. A phantom is a ghost survivor bot without any game effect: it follows the
        // bot route to the nearest incomplete terminal (or the open exit) at SurvivorSpeed and holds still there as if
        // hacking. When a burst ends it keeps one still visible frame (the 0.15 s Signal hold of a stopping survivor),
        // then pauses Silently like a bot and may reappear in place before the skill expires.
        private void MovePhantoms(float dt)
        {
            foreach (var phantom in _decoys)
            {
                if (phantom.SpawnAt > _now - dt + TimeEpsilon || !Active(phantom.Until)) continue;
                if (phantom.Pace == PacePaused)
                {
                    if (_now < phantom.PauseEnd - TimeEpsilon) continue;
                    StartBurst(phantom, _now);
                }
                else if (phantom.Pace == PaceHolding)
                {
                    phantom.Pace = PacePaused;
                    phantom.PauseEnd = _now + (PhantomAtTerminal(phantom)
                        ? Uniform(_decoyRandom, BotHackPauseMin, BotHackPauseMax)
                        : Uniform(_decoyRandom, BotWalkPauseMin, BotWalkPauseMax));
                    continue;
                }
                else if (_now >= phantom.BurstEnd - TimeEpsilon)
                {
                    phantom.Pace = PaceHolding;
                    continue;
                }
                // The last shown frame before expiry is still as well.
                if (!Active(phantom.Until - dt)) continue;
                StepPhantom(phantom, dt);
            }
        }

        // The nearest open exit, or the nearest exit while none is open.
        private MapPoint NearestExit(float x, float z)
        {
            bool anyOpen = AnyExitOpen;
            var result = Map.Exit;
            float best = float.MaxValue;
            for (int i = 0; i < Map.Exits.Count; i++)
            {
                if (anyOpen && !_exitOpen[i]) continue;
                float distance = DistanceSquared(x, z, Map.Exits[i].X, Map.Exits[i].Z);
                if (distance < best) { best = distance; result = Map.Exits[i]; }
            }
            return result;
        }

        private void StepPhantom(Phantom phantom, float dt)
        {
            float goalX, goalZ, stop;
            bool endBurst;
            // Passing a landmark that is not a terminal, the walker carries on to the nearest terminal.
            if (phantom.Headed && !NearIncompleteTerminal(phantom.HeadX, phantom.HeadZ, 0.01f) &&
                DistanceSquared(phantom.X, phantom.Z, phantom.HeadX, phantom.HeadZ) <= BotStopDistance * BotStopDistance)
                phantom.Headed = false;
            int terminal = NearestIncompleteTerminal(phantom.X, phantom.Z);
            if (AnyExitOpen || terminal < 0)
            {
                // A phantom never enters an exit (a real survivor would escape there): it stops at the edge as if pausing.
                var exit = NearestExit(phantom.X, phantom.Z);
                goalX = exit.X;
                goalZ = exit.Z;
                stop = GameConstants.ExitRadius + 0.5f;
                endBurst = true;
            }
            else
            {
                goalX = phantom.Headed ? phantom.HeadX : _terminals[terminal].X;
                goalZ = phantom.Headed ? phantom.HeadZ : _terminals[terminal].Z;
                // Hold still within BotStopDistance of the terminal ("hacking"), exactly where a bot stops.
                stop = BotStopDistance;
                endBurst = false;
            }
            if (DistanceSquared(phantom.X, phantom.Z, goalX, goalZ) <= stop * stop)
            {
                if (endBurst) phantom.Pace = PaceHolding;
                return;
            }
            phantom.Route.Direction(Nav, phantom.X, phantom.Z, goalX, goalZ, GameConstants.SurvivorSpeed, _now,
                out float dx, out float dz);
            float step = GameConstants.SurvivorSpeed * dt;
            var moved = Map.Move(phantom.X, phantom.Z, dx * step, dz * step);
            phantom.X = moved.X;
            phantom.Z = moved.Z;
        }

        private void UpdatePhantomVisibility()
        {
            for (int i = _decoys.Count - 1; i >= 0; --i)
            {
                var phantom = _decoys[i];
                phantom.Visible = phantom.Pace != PacePaused && _now >= phantom.SpawnAt - TimeEpsilon && Active(phantom.Until);
                if (phantom.Visible)
                {
                    phantom.LastVisibleAt = _now;
                    phantom.VisibleX = phantom.X;
                    phantom.VisibleZ = phantom.Z;
                    continue;
                }
                bool pending = _now < phantom.SpawnAt - TimeEpsilon;
                bool resumable = Active(phantom.Until);
                if (!pending && !resumable && !AfterimageWindow(false, phantom.LastVisibleAt)) _decoys.RemoveAt(i);
            }
        }
    }
}