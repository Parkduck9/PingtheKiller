using System;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // The killer bot. It decides only from its KillerBotView: anonymous blips by position and status (phantoms and
    // afterimages look the same as real Signals), terminals as done or not done, and the exit. It is imperfect like a
    // human: it looks at the view only once per reaction interval, notices blips mainly in front of it (first-person view),
    // chases the last blip it noticed, looks around where the trail ends, and patrols terminals that are not done.
    internal sealed class KillerBrain : BotBrain
    {
        private const double ReactionMin = 0.7, ReactionMax = 1.3;
        private const double ChaseMemorySeconds = 1.5;
        private const double LookMin = 1.5, LookMax = 4;
        private const float LookTurnDegreesPerSecond = 120f;
        // First-person attention: blips inside the half-angle in front, or within NoticeRadius in any direction.
        private const float FovHalfDegrees = 60f, NoticeRadius = 3f, NoticeRange = 14f;
        private const double MissChance = 0.4;
        // Difficulty scales with the public terminal count (five for one or two survivors, seven for three to five):
        // against a small team it is less perceptive, so a lone survivor is not
        // found at once.
        private const int SmallLobbyTerminals = 5, MidLobbyTerminals = 7;
        private const float SmallLobbyNoticeRange = 7f, MidLobbyNoticeRange = 10f;
        // U6d-a3: mid lobby 0.65 -> 0.6 keeps 1v3 inside its win band once survivors commit (seeds 1001-1040 hit 78 %).
        private const double SmallLobbyMissChance = 0.75, MidLobbyMissChance = 0.6;
        // U6d-a3: from LateAttentionStart of the public match clock the small and mid lobby attention approaches the
        // large lobby values (NoticeRange, MissChance) by the time limit.
        internal const double LateAttentionStart = 600;
        // Where it thinks a noticed blip was is off by up to this much (reading a blip off the view is not exact).
        private const float SmallLobbyAimError = 2.2f, MidLobbyAimError = 1.2f, AimError = 0.6f;
        private const float ArriveDistance = 0.8f;
        private const double PatrolTimeout = 12;
        private const float PatrolStandOff = 3f, FarSpotDistance = 15f;
        private const double SweepSignalBonus = 20;
        private const double SmallLobbyRoamChance = 0.7, MidLobbyRoamChance = 0.3, RoamChance = 0;
        private const double ScannerDelaySeconds = 1;
        private const float OverclockMinDistance = 3f, OverclockMaxDistance = 10f;
        private const float FirewallExitDistance = 12f;
        // R1 (11/17): it swings at a Normal blip within SwingTrigger metres and SwingFrontDegrees of its facing (a decoy
        // can bait it), aiming the look yaw at that blip; Packet trace when a blip was near recently but none is now.
        internal const float SwingTrigger = 2.2f, SwingFrontDegrees = 45f;
        private const float TraceRecentDistance = 10f;
        private const double StrikeHoldSeconds = GameConstants.SwingWindUpSeconds + 0.1, TrailStrikeSeconds = 1.0;
        private const float StrikeTrackDistance = 3.5f;
        private const double TraceRecentSeconds = 4;
        // Firewall on a terminal (no switch in progress): a Normal blip within this of an unfinished terminal.
        private const float FirewallTerminalDistance = 6f;
        // ... and only when the bot itself is at least this far from that terminal (it cannot get there in time).
        private const float FirewallTerminalKillerDistance = 20f;
        // ... and not once FirewallTerminalMaxDone terminals are finished (an all-bot endgame lock loop runs the clock out).
        private const int FirewallTerminalMaxDone = 2;
        // U6d-a3 search mode: no Normal blip noticed for SearchSeconds (phantom tracks keep blips on the view, so the
        // view alone never runs dry) means it stops circling one place and sweeps the map,
        // cell by cell (SearchCell metres), the least recently visited cell first with a pull toward nearer ones.
        internal const double SearchSeconds = 45;
        private const float SearchCell = 20f;
        private const double SearchAgeCap = 300, SearchSecondsPerMetre = 0.5;
        // Closes a visible hatch from this close (inside GameConstants.HatchRadius).
        private const float HatchReach = GameConstants.HatchRadius - 0.3f;

        private double _thinkAt, _targetUntil, _lookUntil, _patrolUntil;
        private double _lastBlipAt = double.NegativeInfinity, _noticedAt;
        private bool _hasTarget;
        private float _targetX, _targetZ;
        private int _patrol = -1;
        private float _patrolX, _patrolZ;
        private float _observedX, _observedZ, _velocityX, _velocityZ, _aimX, _aimZ, _searchX, _searchZ;
        private double _observedAt = double.NegativeInfinity, _kickUntil;
        private double[] _signalAt = Array.Empty<double>(), _visitedAt = Array.Empty<double>(), _kickedAt = Array.Empty<double>();
        private bool _guardSwitch;
        private float[] _cellX = Array.Empty<float>(), _cellZ = Array.Empty<float>();
        private double[] _cellAt = Array.Empty<double>();
        private int _openExitTurn;
        private double _nearBlipAt = double.NegativeInfinity, _strikeUntil = double.NegativeInfinity, _struckObservation = double.NegativeInfinity;
        private float _strikeX, _strikeZ;
        // Set by Strike: the look yaw the swing needs this tick (the chase-prop layer must not turn it away).
        internal bool Striking;
        internal float StrikeYaw;

        internal BotIntent Update(KillerBotView view, NavGrid nav, FixedMap map, Random random)
        {
            var intent = new BotIntent { Yaw = view.Yaw };
            double t = view.Time;
            if (!Started)
            {
                Started = true;
                Face(ref intent, view.Yaw);
                _lastBlipAt = _noticedAt = t;
                BuildCells(nav, t);
            }
            MarkCell(view.X, view.Z, t);
            bool anyNormal = false;
            foreach (var blip in view.Blips) if (!blip.Lagging) anyNormal = true;
            if (anyNormal) _lastBlipAt = t;
            if (_signalAt.Length != view.TerminalCount)
            {
                _signalAt = new double[view.TerminalCount];
                _visitedAt = new double[view.TerminalCount];
                _kickedAt = new double[view.TerminalCount];
                for (int i = 0; i < view.TerminalCount; i++) _signalAt[i] = _visitedAt[i] = _kickedAt[i] = double.NegativeInfinity;
            }
            foreach (var blip in view.Blips)
                if (!blip.Lagging)
                    for (int i = 0; i < view.TerminalCount; i++)
                        if (Distance(blip.X, blip.Z, view.Terminals[i].X, view.Terminals[i].Z) <= 6) _signalAt[i] = t;
            // GDD v2 §4: a Lagging survivor cannot be touched into the next stage, so Lagging blips are never targets.
            if (t >= _thinkAt) Look(view, random);
            if (_hasTarget)
            {
                _kickUntil = 0;
                double ahead = Math.Min(1.5, Math.Max(0, t - _observedAt) + 0.25);
                _targetX = _observedX + _velocityX * (float)ahead + _aimX;
                _targetZ = _observedZ + _velocityZ * (float)ahead + _aimZ;
            }
            if (view.HatchVisible)
            {
                // GDD v2 §3: a hatch in sight is closed at once (hold Interact standing still).
                if (Distance(view.X, view.Z, view.HatchX, view.HatchZ) <= HatchReach)
                {
                    Stop(ref intent, view.X, view.Z);
                    intent.Interact = true;
                }
                else MoveTo(ref intent, nav, view.X, view.Z, view.HatchX, view.HatchZ, HatchReach - 0.2f, view.Speed, t, random);
                UseSkills(ref intent, view);
                return intent;
            }
            if (_hasTarget && (Distance(view.X, view.Z, _targetX, _targetZ) < ArriveDistance || t >= _targetUntil))
            {
                _hasTarget = false;
                _searchX = _targetX;
                _searchZ = _targetZ;
                _lookUntil = t + Uniform(random, LookMin, LookMax);
            }
            if (_hasTarget) MoveTo(ref intent, nav, view.X, view.Z, _targetX, _targetZ, 0, view.Speed, t, random);
            else if (t < _lookUntil)
            {
                if (MoveTo(ref intent, nav, view.X, view.Z, _searchX, _searchZ, ArriveDistance, view.Speed, t, random))
                    Face(ref intent, Facing + LookTurnDegreesPerSecond / GameConstants.TickRate);
            }
            else Patrol(ref intent, view, nav, map, random);
            Strike(ref intent, view);
            UseSkills(ref intent, view);
            return intent;
        }

        // A Normal blip close and roughly in front: face it exactly and swing (the server drops presses mid-swing).
        // Its own swing: while the wind-up runs (StrikeHoldSeconds after the press) it keeps the look on the nearest
        // Normal blip within StrikeTrackDistance, else on the point it swung at. A Survivor that went Silent right in
        // front of it is swung at once, at the predicted spot of the trail it follows (its own memory, like a human).
        private void Strike(ref BotIntent intent, KillerBotView view)
        {
            double t = view.Time;
            Striking = false;
            float best = float.MaxValue, nearest = float.MaxValue;
            bool found = false, near = false;
            float yaw = 0, nx = 0, nz = 0;
            foreach (var blip in view.Blips)
            {
                if (blip.Lagging) continue;
                float d = Distance(view.X, view.Z, blip.X, blip.Z);
                if (d <= TraceRecentDistance) _nearBlipAt = t;
                float px = blip.X, pz = blip.Z;
                if (d < nearest && d <= StrikeTrackDistance) { nearest = d; nx = px; nz = pz; near = true; }
                if (d > SwingTrigger || d >= best) continue;
                float toward = YawOf(px - view.X, pz - view.Z);
                float delta = Math.Abs(toward - Facing) % 360f;
                if (delta > 180f) delta = 360f - delta;
                if (d > 0.3f && delta > SwingFrontDegrees) continue;
                best = d;
                yaw = toward;
                found = true;
            }
            if (t < _strikeUntil)
            {
                if (near) { _strikeX = nx; _strikeZ = nz; }
                Aim(ref intent, view, _strikeX, _strikeZ);
                return;
            }
            if (!found && _hasTarget && _observedAt > _struckObservation && t - _observedAt <= TrailStrikeSeconds &&
                Distance(view.X, view.Z, _targetX, _targetZ) <= SwingTrigger)
            {
                _struckObservation = _observedAt;
                _strikeX = _targetX;
                _strikeZ = _targetZ;
                yaw = YawOf(_targetX - view.X, _targetZ - view.Z);
                found = true;
            }
            else if (found)
            {
                _strikeX = view.X + (float)Math.Sin(yaw * Math.PI / 180) * best;
                _strikeZ = view.Z + (float)Math.Cos(yaw * Math.PI / 180) * best;
            }
            if (!found) return;
            _strikeUntil = t + StrikeHoldSeconds;
            Aim(ref intent, view, _strikeX, _strikeZ);
            intent.Attack = true;
        }

        private void Aim(ref BotIntent intent, KillerBotView view, float x, float z)
        {
            if (Distance(view.X, view.Z, x, z) > 0.05f) Face(ref intent, YawOf(x - view.X, z - view.Z));
            Striking = true;
            StrikeYaw = intent.Yaw;
        }

        private bool Noticed(KillerBotView view, float x, float z, Random random)
        {
            float d = Distance(view.X, view.Z, x, z);
            if (d <= NoticeRadius) return true;
            float range = view.TerminalCount <= SmallLobbyTerminals ? SmallLobbyNoticeRange
                : view.TerminalCount <= MidLobbyTerminals ? MidLobbyNoticeRange : NoticeRange;
            double miss = view.TerminalCount <= SmallLobbyTerminals ? SmallLobbyMissChance
                : view.TerminalCount <= MidLobbyTerminals ? MidLobbyMissChance : MissChance;
            // Late in the match (public clock) the small-lobby handicap fades toward the large-lobby attention, so a
            // lone survivor that is neither finishing nor caught does not run the clock out.
            double late = Math.Max(0, Math.Min(1, (view.Time - LateAttentionStart) / (GameConstants.MatchLengthSeconds - LateAttentionStart)));
            range += (NoticeRange - range) * (float)late;
            miss += (MissChance - miss) * late;
            if (d > range || random.NextDouble() < miss) return false;
            float delta = Math.Abs(YawOf(x - view.X, z - view.Z) - Facing) % 360f;
            if (delta > 180f) delta = 360f - delta;
            return delta <= FovHalfDegrees;
        }

        // Chases the closest noticed Normal blip, preferring one near the trail it already follows.
        private void Look(KillerBotView view, Random random)
        {
            double t = view.Time;
            _thinkAt = t + Uniform(random, ReactionMin, ReactionMax);
            float best = float.MaxValue;
            bool found = false;
            float bx = 0, bz = 0;
            foreach (var blip in view.Blips)
            {
                if (blip.Lagging || !Noticed(view, blip.X, blip.Z, random)) continue;
                float score = Distance(view.X, view.Z, blip.X, blip.Z);
                if (_hasTarget) score = Distance(blip.X, blip.Z, _targetX, _targetZ);
                if (score >= best) continue;
                best = score;
                bx = blip.X;
                bz = blip.Z;
                found = true;
            }
            if (!found) return;
            _noticedAt = t;
            float error = view.TerminalCount <= SmallLobbyTerminals ? SmallLobbyAimError
                : view.TerminalCount <= MidLobbyTerminals ? MidLobbyAimError : AimError;
            double angle = random.NextDouble() * 2 * Math.PI, reach = random.NextDouble() * error;
            double elapsed = t - _observedAt;
            if (_hasTarget && elapsed > 0 && elapsed <= ChaseMemorySeconds &&
                Distance(bx, bz, _observedX, _observedZ) <= 6 * elapsed + 1)
            {
                _velocityX = (bx - _observedX) / (float)elapsed;
                _velocityZ = (bz - _observedZ) / (float)elapsed;
                float speed = Distance(0, 0, _velocityX, _velocityZ);
                if (speed > 6) { _velocityX *= 6 / speed; _velocityZ *= 6 / speed; }
            }
            else _velocityX = _velocityZ = 0;
            _observedAt = t;
            _observedX = bx;
            _observedZ = bz;
            _aimX = (float)(Math.Sin(angle) * reach);
            _aimZ = (float)(Math.Cos(angle) * reach);
            _hasTarget = true;
            _targetX = _observedX;
            _targetZ = _observedZ;
            _targetUntil = t + ChaseMemorySeconds;
        }

        // How long a checked terminal stays off the patrol list, and how long a kicked one is not kicked again. Both scale
        // with the public terminal count, like the other difficulty knobs.
        private static double Revisit(KillerBotView view) => view.TerminalCount <= SmallLobbyTerminals ? 150
            : view.TerminalCount <= MidLobbyTerminals ? 110 : 45;

        // Public Signal history ranks unfinished terminals; during the end game alternate the leading switch and nearby work.
        // The killer always has a destination: when every unfinished terminal was checked recently it sweeps them again
        // (recent Signal first, then the least recently checked), but does not kick one it kicked within Revisit.
        private void Patrol(ref BotIntent intent, KillerBotView view, NavGrid nav, FixedMap map, Random random)
        {
            double t = view.Time;
            bool arrived = _patrol >= 0 && Distance(view.X, view.Z, _patrolX, _patrolZ) < ArriveDistance;
            if (arrived)
            {
                if (_patrol < view.TerminalCount && !view.Terminals[_patrol].Done && t - _kickedAt[_patrol] >= Revisit(view))
                {
                    foreach (var blip in view.Blips)
                        if (!blip.Lagging && Distance(view.X, view.Z, blip.X, blip.Z) <= 6 &&
                            !map.SegmentBlocked(view.X, view.Z, blip.X, blip.Z))
                        {
                            // A Signal in sight beside the terminal: turn to it and let the attention model pick it up
                            // (no kick, and no waiting forever for a blip it keeps missing).
                            _kickUntil = 0;
                            _visitedAt[_patrol] = t;
                            _patrol = -1;
                            _lookUntil = t + Uniform(random, LookMin, LookMax);
                            _searchX = view.X; _searchZ = view.Z;
                            Stop(ref intent, view.X, view.Z);
                            Face(ref intent, YawOf(blip.X - view.X, blip.Z - view.Z));
                            return;
                        }
                    if (_kickUntil == 0) _kickUntil = t + GameConstants.KickHoldSeconds + 0.15;
                    if (t < _kickUntil)
                    {
                        Stop(ref intent, view.X, view.Z);
                        intent.Interact = true;
                        return;
                    }
                    _kickedAt[_patrol] = t;
                }
                if (_patrol < view.TerminalCount) _visitedAt[_patrol] = t;
                _kickUntil = 0;
                _patrol = -1;
                _lookUntil = t + Uniform(random, LookMin, LookMax);
                _searchX = view.X;
                _searchZ = view.Z;
                Stop(ref intent, view.X, view.Z);
                return;
            }
            if (_patrol < 0 || t >= _patrolUntil)
            {
                // Guard the best unopened powered switch (the switch of an open exit needs no guard, QA m1). Once an exit
                // is open every survivor heads there: it patrols the open exits, and guards an unopened switch only while
                // that switch has progress (someone works it).
                int exitIndex = -1;
                bool anyOpen = false;
                for (int i = 0; i < view.ExitCount; i++)
                {
                    if (view.Exits[i].Open) { anyOpen = true; continue; }
                    if (view.Exits[i].Powered && (exitIndex < 0 || view.Exits[i].Progress > view.Exits[exitIndex].Progress)) exitIndex = i;
                }
                bool powered = exitIndex >= 0 && (!anyOpen || view.Exits[exitIndex].Progress > 0);
                // Search when the view showed no Normal blip at all for SearchSeconds, or instead of guarding (the place it
                // would otherwise circle) once nothing was noticed for that long.
                bool search = t - _lastBlipAt >= SearchSeconds || (powered && t - _noticedAt >= SearchSeconds);
                float best = float.MaxValue;
                double revisit = Revisit(view);
                _patrol = -1;
                for (int i = 0; i < view.TerminalCount && !search && !anyOpen; i++)
                {
                    var terminal = view.Terminals[i];
                    if (terminal.Done || t - _visitedAt[i] < revisit) continue;
                    if (powered && Distance(terminal.X, terminal.Z, view.Exits[exitIndex].SwitchX, view.Exits[exitIndex].SwitchZ) > 18) continue;
                    float score = Distance(view.X, view.Z, terminal.X, terminal.Z);
                    if (t - _signalAt[i] <= 60) score -= 200;
                    if (score < best) { best = score; _patrol = i; }
                }
                if (search) _guardSwitch = false;
                else if (powered && (!_guardSwitch || (_patrol < 0 && !anyOpen))) { _patrol = view.TerminalCount; _guardSwitch = true; }
                else _guardSwitch = false;
                // Every unfinished terminal was checked recently. Against a small team it often roams the map instead.
                double roam = view.TerminalCount <= SmallLobbyTerminals ? SmallLobbyRoamChance
                    : view.TerminalCount <= MidLobbyTerminals ? MidLobbyRoamChance : RoamChance;
                if (!search && anyOpen && _patrol < 0) _patrol = view.TerminalCount + 2;
                if (!search && _patrol < 0 && random.NextDouble() >= roam)
                {
                    // Sweep them again, the least recently checked first (a recent Signal counts as SweepSignalBonus
                    // seconds older), skipping the one underfoot.
                    double oldest = double.MaxValue;
                    for (int i = 0; i < view.TerminalCount; i++)
                    {
                        var terminal = view.Terminals[i];
                        if (terminal.Done || Distance(view.X, view.Z, terminal.X, terminal.Z) < PatrolStandOff + 1) continue;
                        double checkedAt = _visitedAt[i] - (t - _signalAt[i] <= 60 ? SweepSignalBonus : 0);
                        if (checkedAt < oldest) { oldest = checkedAt; _patrol = i; }
                    }
                }
                float cx, cz;
                if (search)
                {
                    _patrol = view.TerminalCount + 3;
                    SearchSpot(view, out cx, out cz);
                }
                else if (_patrol == view.TerminalCount + 2)
                {
                    // Patrol between the open exits.
                    int open = -1;
                    for (int k = 0; k < view.ExitCount && open < 0; k++)
                    {
                        int i = (_openExitTurn + k) % view.ExitCount;
                        if (view.Exits[i].Open) open = i;
                    }
                    _openExitTurn = open + 1;
                    cx = view.Exits[open].X;
                    cz = view.Exits[open].Z;
                }
                else if (_patrol >= 0 && _patrol < view.TerminalCount) { cx = view.Terminals[_patrol].X; cz = view.Terminals[_patrol].Z; }
                else if (_patrol == view.TerminalCount)
                {
                    // A powered exit: its switch, where survivors have to stand.
                    var exit = view.Exits[exitIndex];
                    cx = exit.SwitchX;
                    cz = exit.SwitchZ;
                }
                else
                {
                    // No unfinished work away from here: search a random map spot some distance off.
                    _patrol = view.TerminalCount + 1;
                    FarSpot(view, nav, random, out cx, out cz);
                }
                // It checks a place from a few metres off (any standable side), not from on top of it.
                _patrolX = cx;
                _patrolZ = cz;
                for (int attempt = 0; attempt < 8 && (_patrol == view.TerminalCount || _patrol == view.TerminalCount + 2); attempt++)
                {
                    double angle = random.NextDouble() * 2 * Math.PI;
                    float x = cx + (float)Math.Sin(angle) * PatrolStandOff, z = cz + (float)Math.Cos(angle) * PatrolStandOff;
                    if (!map.CanStand(x, z)) continue;
                    _patrolX = x;
                    _patrolZ = z;
                    break;
                }
                _patrolUntil = t + PatrolTimeout;
            }
            MoveTo(ref intent, nav, view.X, view.Z, _patrolX, _patrolZ, 0, view.Speed, t, random);
        }

        // One coverage point per SearchCell square: the lattice spot nearest the centre of the square.
        private void BuildCells(NavGrid nav, double t)
        {
            int side = (int)Math.Ceiling(FixedMap.Size / SearchCell);
            var xs = new float[side * side];
            var zs = new float[side * side];
            var found = new bool[side * side];
            var best = new float[side * side];
            foreach (var spot in nav.Spots)
            {
                int c = CellOf(spot.X, spot.Z, side);
                float cx = (c % side + 0.5f) * SearchCell, cz = (c / side + 0.5f) * SearchCell;
                float d = Distance(spot.X, spot.Z, cx, cz);
                if (found[c] && d >= best[c]) continue;
                found[c] = true;
                best[c] = d;
                xs[c] = spot.X;
                zs[c] = spot.Z;
            }
            int count = 0;
            for (int c = 0; c < found.Length; c++) if (found[c]) count++;
            _cellX = new float[count];
            _cellZ = new float[count];
            _cellAt = new double[count];
            for (int c = 0, k = 0; c < found.Length; c++)
            {
                if (!found[c]) continue;
                _cellX[k] = xs[c];
                _cellZ[k] = zs[c];
                _cellAt[k] = t - SearchAgeCap;
                k++;
            }
        }

        private static int CellOf(float x, float z, int side)
        {
            int cx = Math.Max(0, Math.Min(side - 1, (int)(x / SearchCell)));
            int cz = Math.Max(0, Math.Min(side - 1, (int)(z / SearchCell)));
            return cz * side + cx;
        }

        // Coverage points within half a cell of the killer count as searched now.
        private void MarkCell(float x, float z, double t)
        {
            for (int k = 0; k < _cellX.Length; k++)
                if (Distance(x, z, _cellX[k], _cellZ[k]) <= SearchCell * 0.5f) _cellAt[k] = t;
        }

        // The least recently searched coverage point, less SearchSecondsPerMetre per metre of distance.
        private void SearchSpot(KillerBotView view, out float x, out float z)
        {
            x = view.X;
            z = view.Z;
            double best = double.MinValue;
            for (int k = 0; k < _cellX.Length; k++)
            {
                float d = Distance(view.X, view.Z, _cellX[k], _cellZ[k]);
                if (d <= SearchCell * 0.5f) continue;
                double score = Math.Min(SearchAgeCap, view.Time - _cellAt[k]) - d * SearchSecondsPerMetre;
                if (score <= best) continue;
                best = score;
                x = _cellX[k];
                z = _cellZ[k];
            }
        }

        // A random lattice spot at least FarSpotDistance away (the farthest one when none is).
        private static void FarSpot(KillerBotView view, NavGrid nav, Random random, out float x, out float z)
        {
            x = view.X;
            z = view.Z;
            int options = nav.Spots.Count;
            if (options == 0) return;
            int start = random.Next(options);
            float farthest = -1;
            for (int k = 0; k < options; k++)
            {
                var spot = nav.Spots[(start + k) % options];
                float distance = Distance(view.X, view.Z, spot.X, spot.Z);
                if (distance <= farthest) continue;
                farthest = distance;
                x = spot.X;
                z = spot.Z;
                if (distance >= FarSpotDistance) return;
            }
        }

        private void UseSkills(ref BotIntent intent, KillerBotView view)
        {
            if (!view.SkillReady) return;
            switch (view.Skill)
            {
                case SkillIds.PingScanner:
                    intent.UseSkill = view.Time - _lastBlipAt >= ScannerDelaySeconds;
                    break;
                case SkillIds.Overclock:
                    if (_hasTarget)
                    {
                        float d = Distance(view.X, view.Z, _targetX, _targetZ);
                        intent.UseSkill = d >= OverclockMinDistance && d <= OverclockMaxDistance;
                    }
                    break;
                case SkillIds.FirewallBlock:
                {
                    bool switchInProgress = false;
                    for (int i = 0; i < view.ExitCount; i++)
                    {
                        var exit = view.Exits[i];
                        if (exit.Open || exit.Progress == 0) continue;
                        switchInProgress = true;
                        foreach (var blip in view.Blips)
                            if (!blip.Lagging && Distance(blip.X, blip.Z, exit.SwitchX, exit.SwitchZ) <= FirewallExitDistance) intent.UseSkill = true;
                    }
                    // R1 (11/17): otherwise the server locks the terminal with the most progress, which it cannot see; a
                    // Signal beside an unfinished terminal is its cue that someone hacks.
                    if (switchInProgress) break;
                    int done = 0;
                    for (int i = 0; i < view.TerminalCount; i++) if (view.Terminals[i].Done) done++;
                    if (done >= FirewallTerminalMaxDone) break;
                    for (int i = 0; i < view.TerminalCount && !intent.UseSkill; i++)
                    {
                        var terminal = view.Terminals[i];
                        if (terminal.Done || terminal.Locked ||
                            Distance(view.X, view.Z, terminal.X, terminal.Z) < FirewallTerminalKillerDistance) continue;
                        foreach (var blip in view.Blips)
                            if (!blip.Lagging && Distance(blip.X, blip.Z, terminal.X, terminal.Z) <= FirewallTerminalDistance) intent.UseSkill = true;
                    }
                    break;
                }
                case SkillIds.PacketTrace:
                {
                    // A blip was near a moment ago but none shows now: someone close went Silent.
                    bool anyNear = false;
                    foreach (var blip in view.Blips)
                        if (!blip.Lagging && Distance(view.X, view.Z, blip.X, blip.Z) <= TraceRecentDistance) anyNear = true;
                    intent.UseSkill = !anyNear && view.Time - _nearBlipAt <= TraceRecentSeconds;
                    break;
                }
            }
        }

    }
}
