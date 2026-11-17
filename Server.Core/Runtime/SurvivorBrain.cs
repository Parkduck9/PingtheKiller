using System;
using System.Collections.Generic;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.ServerCore
{
    // A survivor bot. It decides only from its SurvivorBotView: ThreatLevel, teammates, terminals, the exit, and the killer
    // while its own line of sight is clear (plus where it last saw it). It is deliberately imperfect: it re-plans once per
    // reaction interval (a per-bot pace drawn from the private stream), hacks in bursts with Silent pauses, and reacts to
    // danger by slipping away from the killer, going Silent and waiting before it resumes.
    internal sealed class SurvivorBrain : BotBrain
    {
        // Walk and hack pacing. GameServer.Phantoms copies these through the GameServer.Bot* constants, so phantom tracks
        // keep the same burst and pause shape as real survivor bots.
        internal const double WalkBurstMin = 2, WalkBurstMax = 5, WalkPauseMin = 1, WalkPauseMax = 3;
        // U6d-a3: hack pauses 0.5-1.5 s -> 1-2 s. Committed bots no longer idle, so the slower duty cycle keeps 1v7 in its
        // median band; phantoms follow through the GameServer.Bot* constants.
        internal const double HackBurstMin = 1.5, HackBurstMax = 2, HackPauseMin = 1.0, HackPauseMax = 2.0;
        // Hacking spot: inside TerminalRadius but beyond CaptureRadius of the terminal centre, where a patrolling killer stops.
        internal const float HackStopDistance = 1.4f;
        // 11/17 (R2): about how long this bot needs for a terminal while it hacks (passive part plus its route solving).
        internal const double ExpectedHackSeconds = 42;
        // The bot's own packet-routing solver (GameServer.Bots feeds it the own route from the view).
        internal readonly RouteSolver Route = new RouteSolver();

        private const double ReactionMin = 0.25, ReactionMax = 0.6;
        // A Silent look around before the first move, letting the killer commit to a direction first.
        private const double OpeningMin = 2, OpeningMax = 8;
        // Sight or Threat 2 triggers cover and a Silent hold; a recently exposed bot also reacts to an approaching chase.
        private const float WatchRange = 27f, WatchHalfAngle = 70f, ComingHalfAngle = 30f, ComingRange = 32f, SlipDistance = 14f;
        private const double NoticedWindow = 6, BlindNoticedWindow = 4;
        private const float SlipMinDistance = 6f, ExitCaution = 10f;
        private const byte ThreatFleeLevel = 2, BlindFleeLevel = 3;
        // ThreatLevel 2+ stays acute danger only while something changes: ThreatLevel rose, or the killer was seen moving
        // (or newly sighted, or found elsewhere) within CampSeconds. A killer that neither comes closer nor is seen moving
        // for that long is camping or idle: the bot leaves a terminal near it for other work (AvoidTerminal) and, when no
        // other work is left, works there anyway. A rise or a sighted move makes it acute again at once.
        private const double CampSeconds = 20;
        private const float KillerShiftDistance = 0.5f;
        // A terminal the killer was last seen this close to is never worked on the fallback pass (it would be captured there).
        private const float KillerAtTerminalRadius = GameConstants.CaptureRadius + HackStopDistance + 1f;
        private const float TerminalLeaveRadius = 3f, HideMinReach = 5f, HideMaxReach = 14f;
        private const float HideInPlaceDistance = GameConstants.ThreatLevel1Distance;
        private const double HoldMin = 3, HoldMax = 6;
        private const double FleeMin = 2.5, FleeMax = 4.5;
        private const float FleeMinReach = 5f, FleeMaxReach = 16f;
        private const double CalmMin = 2, CalmMax = 4;
        private const double KillerMemorySeconds = 3;
        private const double TerminalDangerMemorySeconds = 12;
        private const float TerminalDangerRadius = 10f;
        private const double AvoidTerminalSeconds = 20;
        // Normal survivors (self included) from which a team counts as big: others can take over a terminal.
        private const int BigTeamSurvivors = 5;
        private const double BigTeamAvoidSeconds = 45;
        private const double SwitchStallSeconds = 30;
        // Quiet rescues are preferred; the timer can override these gates when arrival and the hold still fit.
        private const double RescueQuietSeconds = 5;
        // Every available Normal survivor works. Lower Id wins a contested slot, using public teammate positions only.
        private const int TerminalCap = 2;
        private const float OccupiedRadius = 3f;
        private const double WaitPauseMin = 25, WaitPauseMax = 50;
        private const float HideoutDistance = 15f;

        // U6d-a3 progress guarantee. A bot that has not advanced anything itself (hack progress at its terminal, switch
        // progress while holding, a rescue) for CommitSeconds commits to work until it does: a killer in sight that is still
        // or far no longer stops it; only ThreatLevel 3 or a sighted killer closing in within CommitThreatDistance does
        // (it hides briefly, and the clock keeps running). It works the target farthest from where it last saw the killer.
        internal const double CommitSeconds = 25;
        // Deviation (U6d-a3 report): with BigTeamSurvivors or more Normal survivors the team keeps the match moving
        // without this bot, and a 25 s commit made 1v7 far too efficient (median 420-470 s against the 540 s floor).
        internal const double BigTeamCommitSeconds = 90;
        private const float CommitThreatDistance = 10f;
        private const byte CommitThreatLevel = 3;
        internal const int CommitReleasePoints = 15;
        // A re-pick happens when the remembered killer position moved this far since the pick.
        private const float CommitRepickDistance = 8f, CommitKeepDistance = 12f;
        private const float CommitAtPickDistance = 4f;
        private const double CommitSightMemorySeconds = 10;
        private const float CommitFarEnough = 30f, CommitProgressWeight = 0.5f, CommitTravelWeight = 0.4f;
        // A teammate with no Signal and no movement for this long is idle (a hiding bot, an idle human): it does not
        // hold a terminal slot, a switch or a rescue for the others.
        internal const double AllyIdleSeconds = 12;
        private const float AllyMoveEpsilon = 0.05f;

        private const byte ModePause = 0, ModeWalk = 1, ModeHack = 2;

        private double _reaction;
        private double _thinkAt, _burstUntil, _pauseUntil, _calmUntil, _fleeUntil, _holdUntil;
        private bool _fleeing, _hiding, _hideInPlace, _interrupted;
        private double _exposedNearAt = double.NegativeInfinity;
        private double _killerSeenAt = double.NegativeInfinity;
        private double _threatRoseAt = double.NegativeInfinity;
        private double _killerMovedAt = double.NegativeInfinity;
        private bool _campNoted;
        private byte _lastThreat;
        private float _killerX, _killerZ, _killerYaw;
        private bool _killerMoving;
        private byte _mode;
        private float _goalX, _goalZ, _goalStop;
        private bool _goalHack;
        private int _terminal = -1, _avoidTerminal = -1;
        private double _avoidUntil;
        private float _hideX, _hideZ;
        private bool _wantSkill, _coverHack;
        private readonly List<MapPoint> _escapePath = new List<MapPoint>(128);
        private int[] _switchHolders = new int[2];
        private int _switchProgress;
        private double _switchProgressAt = double.NaN;
        private double _lastProgressAt;
        private bool _committed;
        private int _commitGain;
        private int _workKey = -1, _workValue;
        private bool _killerClosing;
        private int _commitPick = -1;
        private float _commitKillerX, _commitKillerZ;
        // Per teammate Id: last Signal or movement, last position, last status and when it became Lagging.
        private double[] _allyActiveAt = new double[0], _allyLaggingSince = new double[0];
        private float[] _allyX = new float[0], _allyZ = new float[0];
        private byte[] _allyStatus = new byte[0];
        private bool[] _allySeen = new bool[0];

        internal BotIntent Update(SurvivorBotView view, NavGrid nav, FixedMap map, Random random)
        {
            var intent = new BotIntent();
            double t = view.Time;
            if (!Started)
            {
                Started = true;
                _reaction = Uniform(random, ReactionMin, ReactionMax);
                _pauseUntil = t + Uniform(random, OpeningMin, OpeningMax);
                _burstUntil = _pauseUntil + Uniform(random, WalkBurstMin, WalkBurstMax);
                PickHideout(view, nav, random);
                _lastProgressAt = t;
            }
            Remember(view);
            TrackTeam(view);
            if (view.Status == PlayerStatus.Lagging)
            {
                _terminal = -1;
                // Being rescued restarts the commitment clock (the bot reached a goal of sorts: it is back in play).
                _lastProgressAt = t;
                _committed = false;
                _commitPick = -1;
                UpdateLagging(ref intent, view, nav, map, random);
                return intent;
            }
            if (view.Status != PlayerStatus.Normal)
            {
                Stop(ref intent, view.X, view.Z);
                return intent;
            }
            TrackProgress(view);
            if (t >= _thinkAt) Think(view, nav, map, random);
            if (_mode == ModeWalk)
            {
                if (MoveTo(ref intent, nav, view.X, view.Z, _goalX, _goalZ, _goalStop, view.Speed, t, random) && _goalHack)
                {
                    _mode = ModeHack;
                    _coverHack = true;
                    intent.Interact = true;
                }
            }
            else
            {
                Stop(ref intent, view.X, view.Z);
                intent.Interact = _mode == ModeHack;
            }
            // R3: a rescue needs the E hold. The old automatic rescue worked for any Normal survivor standing within reach, so a
            // bot holds Interact whenever a Lagging ally is within reach (a planned rescue or just passing close), as it did by standing near.
            if (LaggingAllyInReach(view)) intent.Interact = true;
            UseSkills(ref intent, view);
            if (intent.Interact && _terminal >= 0 && Occupants(view, _terminal) >= TerminalCap)
            {
                intent.Interact = false;
                _thinkAt = t;
            }
            return intent;
        }

        private void Remember(SurvivorBotView view)
        {
            if (view.Threat > _lastThreat) _threatRoseAt = view.Time;
            _lastThreat = view.Threat;
            foreach (var entry in view.Team)
                if (entry.Id == view.SelfId && (entry.Flags & (byte)PlayerFlags.Signal) != 0)
                {
                    // Signal with the killer this close (ThreatLevel or sight): it may well have noticed this bot.
                    if (view.Threat >= 1 || (view.KillerVisible && Distance(view.X, view.Z, view.KillerX, view.KillerZ) <= GameConstants.ThreatLevel1Distance))
                        _exposedNearAt = view.Time;
                }
            if (!view.KillerVisible) { _killerClosing = false; return; }
            // Moving: the position changed since the sighting one tick ago (the entry carries no velocity).
            float shift = Distance(_killerX, _killerZ, view.KillerX, view.KillerZ);
            _killerMoving = view.Time - _killerSeenAt < 0.2 && shift > 0.001f;
            // Closing in: the killer's own move (measured from where this bot stands now) shortened the distance.
            _killerClosing = _killerMoving &&
                Distance(view.X, view.Z, view.KillerX, view.KillerZ) < Distance(view.X, view.Z, _killerX, _killerZ) - 0.001f;
            // News about the killer: seen moving, sighted again after a while out of sight, or found somewhere else.
            if (_killerMoving || view.Time - _killerSeenAt > KillerMemorySeconds || shift > KillerShiftDistance)
                _killerMovedAt = view.Time;
            _killerSeenAt = view.Time;
            _killerX = view.KillerX;
            _killerZ = view.KillerZ;
            _killerYaw = view.KillerYaw;
        }

        // Teammates from the public WorldState entries: Signal, Hacking, Rescuing or movement marks one active; the Lagging
        // start is kept.
        private void TrackTeam(SurvivorBotView view)
        {
            double t = view.Time;
            foreach (var entry in view.Team)
            {
                int id = entry.Id;
                if (id < 0) continue;
                if (id >= _allySeen.Length)
                {
                    int size = Math.Max(id + 1, 8);
                    Array.Resize(ref _allyActiveAt, size);
                    Array.Resize(ref _allyLaggingSince, size);
                    Array.Resize(ref _allyX, size);
                    Array.Resize(ref _allyZ, size);
                    Array.Resize(ref _allyStatus, size);
                    Array.Resize(ref _allySeen, size);
                }
                if (!_allySeen[id] || (entry.Flags & (byte)(PlayerFlags.Signal | PlayerFlags.Hacking | PlayerFlags.Rescuing)) != 0 ||
                    Distance(entry.X, entry.Z, _allyX[id], _allyZ[id]) > AllyMoveEpsilon || entry.Status != _allyStatus[id])
                    _allyActiveAt[id] = t;
                if (entry.Status == (byte)PlayerStatus.Lagging && (!_allySeen[id] || _allyStatus[id] != (byte)PlayerStatus.Lagging))
                    _allyLaggingSince[id] = t;
                _allySeen[id] = true;
                _allyX[id] = entry.X;
                _allyZ[id] = entry.Z;
                _allyStatus[id] = entry.Status;
            }
        }

        // A teammate (not this bot) that showed no Signal, Hacking or Rescuing flag and did not move for AllyIdleSeconds.
        private bool AllyIdle(SurvivorBotView view, int id) =>
            id != view.SelfId && id >= 0 && id < _allySeen.Length && _allySeen[id] && view.Time - _allyActiveAt[id] > AllyIdleSeconds;

        // Own progress: hack progress rising at the terminal it stands at, switch progress rising while it holds, or
        // its own entry Rescuing.
        private void TrackProgress(SurvivorBotView view)
        {
            int key = -1, value = 0;
            if (_mode == ModeHack)
            {
                for (int i = 0; i < view.ExitCount && key < 0; i++)
                {
                    var exit = view.Exits[i];
                    if (exit.Powered && !exit.Open && Distance(view.X, view.Z, exit.SwitchX, exit.SwitchZ) <= GameConstants.ExitSwitchRadius)
                    { key = 1000 + i; value = exit.Progress; }
                }
                float best = GameConstants.TerminalRadius;
                for (int i = 0; i < view.TerminalCount && key < 1000; i++)
                {
                    float d = Distance(view.X, view.Z, view.Terminals[i].X, view.Terminals[i].Z);
                    if (d > best) continue;
                    best = d;
                    key = i;
                    value = view.Terminals[i].Progress;
                }
            }
            if (key >= 0 && key == _workKey && value > _workValue) MadeProgress(view.Time, value - _workValue);
            _workKey = key;
            _workValue = value;
            foreach (var entry in view.Team)
                if (entry.Id == view.SelfId && (entry.Flags & (byte)PlayerFlags.Rescuing) != 0) MadeProgress(view.Time, CommitReleasePoints);
        }

        // Progress ends commit mode; the commit target stays preferred (ChooseTerminal) so the bot does not walk back
        // across the map to a more advanced terminal the killer keeps visiting.
        // Outside commit mode any own progress restarts the clock. Commit mode ends only once the bot has gained
        // CommitReleasePoints since it began (a rescue or a completed terminal always does), so a single point before
        // the next scare does not drop it back into hiding at every sighting.
        private void MadeProgress(double t, int points)
        {
            if (_committed)
            {
                _commitGain += points;
                if (_commitGain < CommitReleasePoints) return;
                _committed = false;
            }
            _lastProgressAt = t;
        }

        private bool Committed(SurvivorBotView view)
        {
            double t = view.Time;
            if (!_committed && t - _lastProgressAt >= CommitDelay(view))
            {
                _committed = true;
                _commitGain = 0;
            }
            return _committed;
        }

        // The match clock (public) no longer leaves room for the work left: the most advanced unfinished terminals still
        // needed (shared among the Normal survivors, one per terminal) plus a switch hold, UrgencyFactor times over, and
        // UrgencyMarginSeconds.
        private const double UrgencyFactor = 3.5, UrgencyMarginSeconds = 60;
        private bool Urgent(SurvivorBotView view) => TimeShort(view, UrgencyFactor, UrgencyMarginSeconds);

        private static bool TimeShort(SurvivorBotView view, double factor, double margin)
        {
            if (view.Team.Count == 0) return false;
            for (int i = 0; i < view.ExitCount; i++) if (view.Exits[i].Open) return false;
            int done = 0;
            for (int i = 0; i < view.TerminalCount; i++) if (view.Terminals[i].Progress >= 100) done++;
            int needed = Math.Max(0, view.TerminalsRequired - done);
            double work = 0;
            // The needed terminals with the most progress (selection by repeated maximum; at most eight terminals).
            int taken = 0;
            for (int k = 0; k < needed; k++)
            {
                int best = -1;
                for (int i = 0; i < view.TerminalCount; i++)
                    if (view.Terminals[i].Progress < 100 && (taken & (1 << i)) == 0 &&
                        (best < 0 || view.Terminals[i].Progress > view.Terminals[best].Progress)) best = i;
                if (best < 0) break;
                taken |= 1 << best;
                work += (100 - view.Terminals[best].Progress) / 100.0 * ExpectedHackSeconds;
            }
            work = work / Math.Max(1, Math.Min(NormalCount(view), needed)) + GameConstants.ExitSwitchSeconds;
            return view.Time + factor * work + margin >= GameConstants.MatchLengthSeconds;
        }

        private static double CommitDelay(SurvivorBotView view) => NormalCount(view) >= BigTeamSurvivors ? BigTeamCommitSeconds : CommitSeconds;

        // Degrees between the killer's facing and the direction from the killer to this bot.
        private float KillerAngleTo(SurvivorBotView view)
        {
            float delta = Math.Abs(YawOf(view.X - _killerX, view.Z - _killerZ) - _killerYaw) % 360f;
            return delta > 180f ? 360f - delta : delta;
        }

        private bool KillerSeenWithin(double t, double seconds) => t - _killerSeenAt <= seconds;

        private bool Acute(double t) => t - _threatRoseAt <= CampSeconds || t - _killerMovedAt <= CampSeconds;

        private void Think(SurvivorBotView view, NavGrid nav, FixedMap map, Random random)
        {
            double t = view.Time;
            _thinkAt = t + _reaction * Uniform(random, 0.7, 1.4);
            bool fresh = KillerSeenWithin(t, KillerMemorySeconds);
            float killerDistance = fresh ? Distance(view.X, view.Z, _killerX, _killerZ) : float.MaxValue;

            // Commit mode (see CommitSeconds): only an acute chase interrupts it. Running out of match time commits too.
            bool commit = Committed(view) || Urgent(view);
            bool threatened = (view.Threat >= CommitThreatLevel ||
                (view.KillerVisible && killerDistance <= CommitThreatDistance && _killerClosing));
            // Escape keeps moving, but routes around a visible killer. Affordable close rescues come first.
            bool cautious = commit ? threatened : view.KillerVisible && killerDistance <= ExitCaution && _killerMoving;
            int open = EscapeExit(view);
            if (open >= 0)
            {
                int rescue = RescueTarget(view, t);
                if (rescue >= 0 && CanRescueBeforeEscape(view, rescue, open))
                {
                    PlanRescue(view, rescue);
                    return;
                }
                PlanEscape(view, open, nav);
                return;
            }
            // The last survivor in play goes for a hatch it can see and presses Interact on it.
            if (view.HatchVisible && LastInPlay(view) && !cautious && t >= _fleeUntil)
            {
                if (Distance(view.X, view.Z, view.HatchX, view.HatchZ) <= HatchStop + 0.05f) _mode = ModeHack;
                else Plan(view.HatchX, view.HatchZ, HatchStop, true);
                return;
            }
            // Powered exits: the Normal survivor nearest to an unopened switch holds it; the others carry on.
            int duty = SwitchDuty(view);
            // Switch holders used to return before considering rescue, even as an ally's timer expired.
            int poweredRescue = duty >= 0 ? RescueTarget(view, t) : -1;
            if (poweredRescue >= 0 && (!cautious || RescueUrgent(view, poweredRescue)))
            {
                PlanRescue(view, poweredRescue);
                return;
            }
            if (duty >= 0 && !cautious && t >= _fleeUntil)
            {
                HoldSwitch(view, duty);
                return;
            }
            if (t < _fleeUntil)
            {
                if (Distance(view.X, view.Z, _goalX, _goalZ) > StopDistance) { _mode = ModeWalk; return; }
                _fleeUntil = t;
            }
            // Committed and not chased: no hiding, holding or calm wait; rescue, else the commit target.
            if (commit && !threatened)
            {
                int pick = CommitPick(view, map);
                if (pick >= 0)
                {
                    _fleeing = _hiding = _hideInPlace = false;
                    _holdUntil = _calmUntil = t;
                    if (_interrupted)
                    {
                        _interrupted = false;
                        _pauseUntil = t;
                        _burstUntil = t + Uniform(random, WalkBurstMin, WalkBurstMax);
                    }
                    int ally = RescueTarget(view, t);
                    if (ally >= 0) { PlanRescue(view, ally); return; }
                    if (pick >= SwitchKey) HoldSwitch(view, pick - SwitchKey);
                    else WorkAt(view, nav, random, pick);
                    return;
                }
            }
            // Reaching cover, rather than the reaction timer alone, ends a flee.
            if (_fleeing && Distance(view.X, view.Z, _goalX, _goalZ) > StopDistance)
            {
                _mode = ModeWalk;
                _fleeUntil = Math.Max(_fleeUntil, t + _reaction);
                return;
            }
            if (_fleeing && t >= _fleeUntil)
            {
                // The flee is over: hold still and Silent (running again next to the killer only shows where it is).
                _fleeing = false;
                _holdUntil = t + Uniform(random, HoldMin, HoldMax);
                _calmUntil = Math.Max(_calmUntil, _holdUntil + Uniform(random, CalmMin, CalmMax));
            }
            bool holding = t < _holdUntil;
            // The killer is in sight, faces this bot and walks at it shortly after this bot was Signal: it is coming.
            float angle = view.KillerVisible ? KillerAngleTo(view) : 180f;
            bool watched = view.KillerVisible && killerDistance <= WatchRange && angle <= WatchHalfAngle;
            bool coming = view.KillerVisible && killerDistance <= ComingRange && _killerMoving && angle <= ComingHalfAngle &&
                t - _exposedNearAt <= NoticedWindow;
            // Moving is safe enough while the killer is in sight and either looks elsewhere or is still far off.
            bool canSlip = view.KillerVisible && killerDistance > SlipMinDistance && (!watched || killerDistance > SlipDistance);
            bool acute = Acute(t);
            // An unseen killer gives no direction to run in, and a still survivor is Silent (not on the killer's view): at
            // ThreatLevel 2 the bot freezes (the hide branch below); only at ThreatLevel 3, about to be walked into, it runs.
            bool blind = !view.KillerVisible && view.Threat >= BlindFleeLevel && acute && t - _exposedNearAt <= BlindNoticedWindow;
            if (!holding && ((coming && !canSlip) || blind))
            {
                StartFlee(view, nav, map, random, fresh);
                return;
            }
            // Sight triggers hiding until a stationary, non-approaching killer is established as camping.
            bool danger = acute && (view.KillerVisible || view.Threat >= ThreatFleeLevel);
            // Every new stale phase (after anything acute) notes the camp once.
            if (acute) _campNoted = false;
            else if ((view.KillerVisible || view.Threat >= ThreatFleeLevel) && !_campNoted)
            {
                // The killer camps within ThreatLevel 2 of this bot: leave a terminal this close for other work if any.
                _campNoted = true;
                if (_terminal >= 0 && _terminal < view.TerminalCount &&
                    Distance(view.X, view.Z, view.Terminals[_terminal].X, view.Terminals[_terminal].Z) <= GameConstants.ThreatLevel1Distance)
                {
                    _avoidTerminal = _terminal;
                    _avoidUntil = t + AvoidTerminalSeconds;
                    _terminal = -1;
                }
            }
            int urgentRescue = RescueTarget(view, t);
            if (urgentRescue >= 0 && RescueUrgent(view, urgentRescue) && !coming)
            {
                PlanRescue(view, urgentRescue);
                return;
            }
            if (holding || danger || t < _calmUntil || (acute && _hiding && KillerSeenWithin(t, 3)))
            {
                if (danger) _calmUntil = Math.Max(_calmUntil, t + Uniform(random, CalmMin, CalmMax));
                _interrupted = true;
                // With a remembered position, move behind cover. A distance band alone supplies no escape direction.
                // A killer still beyond ThreatLevel 1 cannot see a still (Silent) survivor at all, so the bot freezes
                // where it is and only moves behind cover if the killer comes within that distance.
                if (danger && fresh && (!_hiding || (_hideInPlace && killerDistance <= HideInPlaceDistance)))
                {
                    bool wasHiding = _hiding;
                    _hiding = true;
                    // In a big team others can take over: leave the terminal the killer came to for a while. A small
                    // team goes back to it as soon as the danger is over (it regresses while nobody hacks).
                    if (_terminal >= 0 && NormalCount(view) >= BigTeamSurvivors)
                    {
                        _avoidTerminal = _terminal;
                        _avoidUntil = t + BigTeamAvoidSeconds;
                    }
                    _terminal = -1;
                    if (!wasHiding && killerDistance > HideInPlaceDistance)
                    {
                        _hideInPlace = true;
                        _mode = ModePause;
                        return;
                    }
                    _hideInPlace = false;
                    var spot = PickRefuge(view, nav, map, random, fresh, HideMinReach, HideMaxReach);
                    Plan(spot.X, spot.Z, StopDistance, false);
                    return;
                }
                if (_hiding && _mode == ModeWalk && Distance(view.X, view.Z, _goalX, _goalZ) > StopDistance) return;
                _mode = ModePause;
                return;
            }
            _hiding = false;
            _hideInPlace = false;
            if (_interrupted)
            {
                // Back to work at once after a scare (no extra pause on top of the calm wait).
                _interrupted = false;
                _pauseUntil = t;
                _burstUntil = t + Uniform(random, WalkBurstMin, WalkBurstMax);
            }

            int lagging = RescueTarget(view, t);
            if (lagging >= 0)
            {
                PlanRescue(view, lagging);
                return;
            }

            WorkAt(view, nav, random, ChooseTerminal(view, t, map));
        }

        // Burst and pause pacing at a terminal (or the hideout wait when there is none).
        private void WorkAt(SurvivorBotView view, NavGrid nav, Random random, int terminal)
        {
            double t = view.Time;
            _terminal = terminal;
            bool atTerminal = terminal >= 0 &&
                Distance(view.X, view.Z, view.Terminals[terminal].X, view.Terminals[terminal].Z) <= HackStopDistance + 0.05f;
            if (t >= _burstUntil)
            {
                double pause = atTerminal ? Uniform(random, HackPauseMin, HackPauseMax)
                    : terminal < 0 ? Uniform(random, WaitPauseMin, WaitPauseMax) : Uniform(random, WalkPauseMin, WalkPauseMax);
                _pauseUntil = t + pause;
                _burstUntil = _pauseUntil + (atTerminal ? Uniform(random, HackBurstMin, HackBurstMax) : Uniform(random, WalkBurstMin, WalkBurstMax));
                if (terminal < 0) PickHideout(view, nav, random);
            }
            if (t < _pauseUntil) { _mode = ModePause; return; }
            if (terminal < 0)
            {
                if (Distance(view.X, view.Z, _hideX, _hideZ) > StopDistance) Plan(_hideX, _hideZ, StopDistance, false);
                else _mode = ModePause;
                return;
            }
            if (atTerminal)
            {
                if (_mode != ModeHack) _coverHack = true;
                _mode = ModeHack;
            }
            else Plan(view.Terminals[terminal].X, view.Terminals[terminal].Z, HackStopDistance, true);
        }

        private void HoldSwitch(SurvivorBotView view, int index)
        {
            var exit = view.Exits[index];
            _terminal = -1;
            if (Distance(view.X, view.Z, exit.SwitchX, exit.SwitchZ) <= SwitchStop + 0.05f)
            {
                if (_mode != ModeHack) _coverHack = true;
                _mode = ModeHack;
            }
            else Plan(exit.SwitchX, exit.SwitchZ, SwitchStop, true);
        }

        // Commit targets: SwitchKey + exit for a powered, unopened switch; a terminal index before the exits are powered.
        private const int SwitchKey = 1000;

        private static float PickDistance(SurvivorBotView view, int pick, float x, float z) => pick >= SwitchKey
            ? Distance(view.Exits[pick - SwitchKey].SwitchX, view.Exits[pick - SwitchKey].SwitchZ, x, z)
            : Distance(view.Terminals[pick].X, view.Terminals[pick].Z, x, z);

        private bool CommitValid(SurvivorBotView view, int pick)
        {
            if (pick >= SwitchKey)
            {
                int exit = pick - SwitchKey;
                return exit < view.ExitCount && view.Exits[exit].Powered && !view.Exits[exit].Open;
            }
            return pick < view.TerminalCount && view.Terminals[pick].Progress < 100 && Occupants(view, pick) < TerminalCap &&
                !AnyPowered(view);
        }

        private static bool AnyPowered(SurvivorBotView view)
        {
            for (int i = 0; i < view.ExitCount; i++) if (view.Exits[i].Powered && !view.Exits[i].Open) return true;
            return false;
        }

        // Another active Normal teammate already stands at this switch (holders do not stack).
        private bool SwitchHeldByOther(SurvivorBotView view, int exit)
        {
            foreach (var entry in view.Team)
                if (entry.Id != view.SelfId && entry.Status == (byte)PlayerStatus.Normal && !AllyIdle(view, entry.Id) &&
                    Distance(entry.X, entry.Z, view.Exits[exit].SwitchX, view.Exits[exit].SwitchZ) <= GameConstants.ExitSwitchRadius)
                    return true;
            return false;
        }

        // The work target farthest from where this bot last saw the killer (up to CommitFarEnough), preferring one out
        // of that spot's line of sight (but never refusing every target), with a pull toward nearer and more advanced
        // work (an abandoned kicked terminal regresses). The pick sticks until it is invalid, or the killer was just seen
        // within CommitKeepDistance of it after moving CommitRepickDistance since the pick.
        private int CommitPick(SurvivorBotView view, FixedMap map)
        {
            bool known = !double.IsNegativeInfinity(_killerSeenAt);
            // Working at the pick, only the threat check (flee) moves the bot; a pick is re-made only from elsewhere.
            if (_commitPick >= 0 && CommitValid(view, _commitPick) &&
                (!known || !KillerSeenWithin(view.Time, KillerMemorySeconds) ||
                 PickDistance(view, _commitPick, view.X, view.Z) <= CommitAtPickDistance ||
                 Distance(_killerX, _killerZ, _commitKillerX, _commitKillerZ) < CommitRepickDistance ||
                 PickDistance(view, _commitPick, _killerX, _killerZ) >= CommitKeepDistance))
                return _commitPick;
            // Line of sight from a stale sighting says little: it only counts while the sighting is recent.
            bool sight = known && KillerSeenWithin(view.Time, CommitSightMemorySeconds);
            int best = -1;
            float bestScore = float.MinValue;
            bool powered = AnyPowered(view);
            int count = powered ? view.ExitCount : view.TerminalCount;
            for (int i = 0; i < count; i++)
            {
                float x, z, score = 0;
                if (powered)
                {
                    var exit = view.Exits[i];
                    if (!exit.Powered || exit.Open) continue;
                    x = exit.SwitchX; z = exit.SwitchZ;
                    score += exit.Progress * CommitProgressWeight;
                    if (SwitchHeldByOther(view, i)) score -= 500f;
                }
                else
                {
                    var terminal = view.Terminals[i];
                    if (terminal.Progress >= 100 || Occupants(view, i) >= TerminalCap) continue;
                    x = terminal.X; z = terminal.Z;
                    score += terminal.Progress * CommitProgressWeight;
                }
                score -= CommitTravelWeight * Distance(view.X, view.Z, x, z);
                if (known)
                {
                    score += Math.Min(CommitFarEnough, Distance(x, z, _killerX, _killerZ));
                    if (sight && map.SegmentBlocked(_killerX, _killerZ, x, z)) score += 1000f;
                }
                if (score <= bestScore) continue;
                bestScore = score;
                best = powered ? SwitchKey + i : i;
            }
            _commitPick = best;
            _commitKillerX = _killerX;
            _commitKillerZ = _killerZ;
            return best;
        }

        private const float SwitchStop = GameConstants.ExitSwitchRadius - 0.5f;
        private const float HatchStop = GameConstants.HatchRadius - 0.5f;

        private static float SegmentDistance(float x, float z, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float square = dx * dx + dz * dz;
            float along = square <= 0.0001f ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * dx + (z - az) * dz) / square));
            return Distance(x, z, ax + along * dx, az + along * dz);
        }

        private static int EscapeExit(SurvivorBotView view)
        {
            int best = -1;
            float score = float.MaxValue;
            for (int i = 0; i < view.ExitCount; i++)
            {
                var exit = view.Exits[i];
                if (!exit.Open) continue;
                float candidate = Distance(view.X, view.Z, exit.X, exit.Z);
                if (view.KillerVisible && SegmentDistance(view.KillerX, view.KillerZ, view.X, view.Z, exit.X, exit.Z) <
                    GameConstants.ThreatLevel3Distance) candidate += FixedMap.Size * 4;
                if (candidate < score) { score = candidate; best = i; }
            }
            return best;
        }

        // Validate the actual static-map route, including its shortcuts, against the visible capture zone.
        private bool SafeEscapePath(NavGrid nav, SurvivorBotView view, float x, float z, float tx, float tz, out float length)
        {
            length = 0;
            if (!nav.FindPath(x, z, tx, tz, _escapePath)) return false;
            for (int i = 1; i < _escapePath.Count; i++)
            {
                var a = _escapePath[i - 1]; var b = _escapePath[i];
                if (SegmentDistance(view.KillerX, view.KillerZ, a.X, a.Z, b.X, b.Z) < GameConstants.CaptureRadius + 2) return false;
                length += Distance(a.X, a.Z, b.X, b.Z);
            }
            return _escapePath.Count > 0;
        }

        // QA m6: a detour (up to 48 candidates, two A* searches each) is reused for EscapePlanSeconds for the same exit
        // until its waypoint is reached.
        private const double EscapePlanSeconds = 1;
        private double _escapePlanAt = double.NegativeInfinity;
        private int _escapePlanExit = -1;
        private float _escapePlanX, _escapePlanZ;

        private void PlanEscape(SurvivorBotView view, int index, NavGrid nav)
        {
            var exit = view.Exits[index];
            _terminal = -1;
            if (!view.KillerVisible || SafeEscapePath(nav, view, view.X, view.Z, exit.X, exit.Z, out _))
            {
                Plan(exit.X, exit.Z, 0, false);
                return;
            }
            if (index == _escapePlanExit && view.Time - _escapePlanAt < EscapePlanSeconds &&
                Distance(view.X, view.Z, _escapePlanX, _escapePlanZ) > StopDistance)
            {
                Plan(_escapePlanX, _escapePlanZ, 0, false);
                return;
            }
            float best = float.MaxValue, waypointX = exit.X, waypointZ = exit.Z;
            // Two safe map routes joined outside the capture zone form a detour. Only the first
            // straight route segment becomes the immediate goal, preventing follower shortcuts across the killer.
            for (int ring = 0; ring < 3; ring++)
                for (int side = 0; side < 16; side++)
                {
                    double angle = side * Math.PI / 8;
                    float radius = GameConstants.CaptureRadius + 3 + ring * 3;
                    float x = view.KillerX + (float)Math.Cos(angle) * radius;
                    float z = view.KillerZ + (float)Math.Sin(angle) * radius;
                    if (!SafeEscapePath(nav, view, view.X, view.Z, x, z, out float first)) continue;
                    MapPoint next = _escapePath.Count > 1 ? _escapePath[1] : new MapPoint(x, z);
                    if (!SafeEscapePath(nav, view, x, z, exit.X, exit.Z, out float second)) continue;
                    if (first + second >= best) continue;
                    best = first + second; waypointX = next.X; waypointZ = next.Z;
                }
            // If every route is unsafe, still make progress toward escape rather than hiding until collapse.
            _escapePlanAt = view.Time;
            _escapePlanExit = index;
            _escapePlanX = waypointX;
            _escapePlanZ = waypointZ;
            Plan(waypointX, waypointZ, 0, false);
        }

        // The nearest exit that is open (open = true) or powered and not yet open (open = false); -1 when none.
        private static int NearestExit(SurvivorBotView view, bool open)
        {
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < view.ExitCount; i++)
            {
                var exit = view.Exits[i];
                if (open ? !exit.Open : !exit.Powered || exit.Open) continue;
                float distance = open ? Distance(view.X, view.Z, exit.X, exit.Z) : Distance(view.X, view.Z, exit.SwitchX, exit.SwitchZ);
                if (distance < best) { best = distance; nearest = i; }
            }
            return nearest;
        }

        private static int NormalCount(SurvivorBotView view)
        {
            int count = 0;
            foreach (var entry in view.Team) if (entry.Status == (byte)PlayerStatus.Normal) ++count;
            return count;
        }

        // No other survivor entry of the WorldState is Normal or Lagging.
        private static bool LastInPlay(SurvivorBotView view)
        {
            foreach (var entry in view.Team)
                if (entry.Id != view.SelfId && (entry.Status == (byte)PlayerStatus.Normal || entry.Status == (byte)PlayerStatus.Lagging))
                    return false;
            return true;
        }

        // Switch holders. The most advanced powered, unopened switch takes its nearest Normal survivor (lower Id breaks
        // ties; equal progress goes to the switch with the nearer candidate). While no switch has gained progress for
        // SwitchStallSeconds (the killer guards it, or its holder was caught) every other powered switch takes its own
        // nearest Normal survivor too: the killer can guard only one of them.
        private int SwitchDuty(SurvivorBotView view)
        {
            double t = view.Time;
            int lead = -1;
            for (int i = 0; i < view.ExitCount; i++)
                if (view.Exits[i].Powered && !view.Exits[i].Open && (lead < 0 || view.Exits[i].Progress > view.Exits[lead].Progress)) lead = i;
            if (lead < 0) { _switchProgressAt = double.NaN; return -1; }
            // The stall clock follows the leading switch only, so progress on another switch does not recall its holder.
            int progress = lead * 256 + view.Exits[lead].Progress;
            if (double.IsNaN(_switchProgressAt) || progress != _switchProgress) { _switchProgress = progress; _switchProgressAt = t; }
            bool stalled = t - _switchProgressAt >= SwitchStallSeconds;
            int rounds = stalled ? view.ExitCount : 1;
            // U6d-a3: while stalled (the killer guards the leading switch, or a lone holder cannot get to it), the switch
            // farthest from where this bot last saw the killer is staffed first, so a lone survivor moves over to it.
            bool away = stalled && !double.IsNegativeInfinity(_killerSeenAt);
            if (_switchHolders.Length < view.ExitCount) _switchHolders = new int[view.ExitCount];
            int taken = 0, holders = 0;
            for (int round = 0; round < rounds; round++)
            {
                int exitIndex = -1, holder = int.MaxValue;
                float holderDistance = float.MaxValue, bestAway = float.MinValue;
                for (int i = 0; i < view.ExitCount; i++)
                {
                    var exit = view.Exits[i];
                    if (!exit.Powered || exit.Open || (taken & (1 << i)) != 0) continue;
                    if (away)
                    {
                        float killerGap = Distance(exit.SwitchX, exit.SwitchZ, _killerX, _killerZ);
                        if (exitIndex >= 0 && killerGap <= bestAway) continue;
                        int candidate = NearestFreeHolder(view, i, holders, out float reach);
                        if (candidate == int.MaxValue) continue;
                        exitIndex = i;
                        holder = candidate;
                        holderDistance = reach;
                        bestAway = killerGap;
                        continue;
                    }
                    if (exitIndex >= 0 && exit.Progress < view.Exits[exitIndex].Progress) continue;
                    int id = NearestFreeHolder(view, i, holders, out float distance);
                    if (id == int.MaxValue) continue;
                    if (exitIndex >= 0 && exit.Progress == view.Exits[exitIndex].Progress &&
                        (distance > holderDistance + 0.0001f || (Math.Abs(distance - holderDistance) <= 0.0001f && id >= holder))) continue;
                    exitIndex = i;
                    holder = id;
                    holderDistance = distance;
                }
                if (exitIndex < 0) return -1;
                if (holder == view.SelfId) return exitIndex;
                taken |= 1 << exitIndex;
                _switchHolders[holders++] = holder;
            }
            return -1;
        }

        // The Normal survivor nearest to exit's switch that is not among the first count holders (int.MaxValue if none).
        private int NearestFreeHolder(SurvivorBotView view, int exit, int count, out float best)
        {
            int bestId = int.MaxValue;
            best = float.MaxValue;
            foreach (var entry in view.Team)
            {
                if (entry.Status != (byte)PlayerStatus.Normal) continue;
                // An idle teammate is not coming to the switch (it still counts while it stands at it).
                if (AllyIdle(view, entry.Id) &&
                    Distance(entry.X, entry.Z, view.Exits[exit].SwitchX, view.Exits[exit].SwitchZ) > GameConstants.ExitSwitchRadius) continue;
                bool holding = false;
                for (int k = 0; k < count; k++) if (_switchHolders[k] == entry.Id) holding = true;
                if (holding) continue;
                float distance = Distance(entry.X, entry.Z, view.Exits[exit].SwitchX, view.Exits[exit].SwitchZ);
                if (distance < best - 0.0001f || (Math.Abs(distance - best) <= 0.0001f && entry.Id < bestId))
                {
                    best = distance;
                    bestId = entry.Id;
                }
            }
            return bestId;
        }

        private void Plan(float x, float z, float stop, bool hack)
        {
            _mode = ModeWalk;
            _goalX = x;
            _goalZ = z;
            _goalStop = stop;
            _goalHack = hack;
        }

        // A lattice spot minReach-maxReach off that does not lead toward the killer (when it was seen recently), away from
        // incomplete terminals (where the killer patrols), preferring far from the killer and out of its line of sight.
        private MapPoint PickRefuge(SurvivorBotView view, NavGrid nav, FixedMap map, Random random, bool known, float minReach, float maxReach)
        {
            float bestScore = float.MinValue, bx = view.X, bz = view.Z;
            float kd = known ? Distance(view.X, view.Z, _killerX, _killerZ) : 0;
            foreach (var spot in nav.Spots)
            {
                float d = Distance(view.X, view.Z, spot.X, spot.Z);
                if (d < minReach || d > maxReach) continue;
                float score = (float)random.NextDouble() * 3f;
                if (NearIncompleteTerminal(view, spot.X, spot.Z, TerminalLeaveRadius)) score -= 8f;
                if (known && kd > 0.01f)
                {
                    float dot = ((spot.X - view.X) * (_killerX - view.X) + (spot.Z - view.Z) * (_killerZ - view.Z)) / (d * kd);
                    if (dot > 0.2f) continue;
                    score += Distance(spot.X, spot.Z, _killerX, _killerZ) - 0.3f * d +
                        (map.SegmentBlocked(_killerX, _killerZ, spot.X, spot.Z) ? 1000f : 0f);
                }
                if (score <= bestScore) continue;
                bestScore = score;
                bx = spot.X;
                bz = spot.Z;
            }
            return new MapPoint(bx, bz);
        }

        private static bool NearIncompleteTerminal(SurvivorBotView view, float x, float z, float radius)
        {
            for (int i = 0; i < view.TerminalCount; i++)
                if (view.Terminals[i].Progress < 100 && Distance(x, z, view.Terminals[i].X, view.Terminals[i].Z) <= radius) return true;
            return false;
        }

        // Run: visible while it moves, so it goes for a refuge and then holds still and Silent.
        private void StartFlee(SurvivorBotView view, NavGrid nav, FixedMap map, Random random, bool known)
        {
            double t = view.Time;
            float kd = known ? Distance(view.X, view.Z, _killerX, _killerZ) : 0;
            var spot = PickRefuge(view, nav, map, random, known, FleeMinReach, FleeMaxReach);
            Plan(spot.X, spot.Z, StopDistance, false);
            _fleeUntil = t + Uniform(random, FleeMin, FleeMax);
            _fleeing = true;
            _hiding = false;
            _interrupted = true;
            if (_terminal >= 0) { _avoidTerminal = _terminal; _avoidUntil = t + AvoidTerminalSeconds; }
            _terminal = -1;
            _wantSkill = view.Skill == SkillIds.DummyPacket || view.Skill == SkillIds.StealthRelay || view.Skill == SkillIds.WhiteNoise ||
                (view.Skill == SkillIds.TrafficOverload && known && kd <= 8f);
        }

        // The closest reachable Lagging ally for which this bot is the nearest Normal rescuer (lower Id breaks ties).
        private int RescueTarget(SurvivorBotView view, double t)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < view.Team.Count; i++)
            {
                var ally = view.Team[i];
                if (ally.Id == view.SelfId || ally.Status != (byte)PlayerStatus.Lagging) continue;
                float mine = Distance(view.X, view.Z, ally.X, ally.Z);
                if (mine >= bestDistance) continue;
                double arrival = mine * 1.4 / Math.Max(0.1f, view.Speed) + GameConstants.RescueTimeSeconds + 1;
                if (ally.StatusTimer <= arrival) continue;
                bool urgent = ally.StatusTimer < arrival + 12;
                if (!urgent && Acute(t) && (view.Threat > 1 || KillerSeenWithin(t, RescueQuietSeconds))) continue;
                bool closer = false;
                foreach (var other in view.Team)
                {
                    if (other.Id == view.SelfId || other.Status != (byte)PlayerStatus.Normal || AllyIdle(view, other.Id)) continue;
                    float theirs = Distance(other.X, other.Z, ally.X, ally.Z);
                    if (theirs < mine || (theirs == mine && other.Id < view.SelfId)) { closer = true; break; }
                }
                if (closer) continue;
                bestDistance = mine;
                best = i;
            }
            return best;
        }

        private int Occupants(SurvivorBotView view, int terminal)
        {
            int count = 0;
            foreach (var other in view.Team)
                if (other.Id < view.SelfId && other.Status == (byte)PlayerStatus.Normal && !AllyIdle(view, other.Id) &&
                    Distance(other.X, other.Z, view.Terminals[terminal].X, view.Terminals[terminal].Z) <= OccupiedRadius) ++count;
            return count;
        }

        // Prefer progress, then distance, while avoiding danger and respecting two slots at every terminal.
        private bool TerminalAllowed(SurvivorBotView view, double t, FixedMap map, int i, int pass)
        {
            var terminal = view.Terminals[i];
            if (terminal.Progress >= 100 || Occupants(view, i) >= TerminalCap) return false;
            bool visibleCamp = view.KillerVisible && !Acute(t);
            if ((pass == 0 || visibleCamp) && i == _avoidTerminal && t < _avoidUntil) return false;
            if (visibleCamp && !map.SegmentBlocked(_killerX, _killerZ, terminal.X, terminal.Z)) return false;
            if (KillerSeenWithin(t, TerminalDangerMemorySeconds) &&
                Distance(terminal.X, terminal.Z, _killerX, _killerZ) < TerminalDangerRadius &&
                (pass == 0 || Acute(t))) return false;
            if (pass == 1 && !double.IsNegativeInfinity(_killerSeenAt) &&
                Distance(terminal.X, terminal.Z, _killerX, _killerZ) <= KillerAtTerminalRadius) return false;
            return true;
        }

        private int ChooseTerminal(SurvivorBotView view, double t, FixedMap map)
        {
            int current = _terminal;
            _terminal = -1;
            float best = float.MaxValue;
            for (int pass = 0; pass < 2 && _terminal < 0; pass++)
                for (int i = 0; i < view.TerminalCount; i++)
                {
                    var terminal = view.Terminals[i];
                    if (!TerminalAllowed(view, t, map, i, pass)) continue;
                    float score = -terminal.Progress * 200f + Distance(view.X, view.Z, terminal.X, terminal.Z);
                    if (score < best) { best = score; _terminal = i; }
                }
            if (current >= 0 && current < view.TerminalCount && TerminalAllowed(view, t, map, current, 0) &&
                (_terminal < 0 || view.Terminals[_terminal].Progress < view.Terminals[current].Progress + 15))
                _terminal = current;
            return _terminal;
        }

        private void PlanRescue(SurvivorBotView view, int target)
        {
            var ally = view.Team[target];
            _terminal = -1;
            if (Distance(view.X, view.Z, ally.X, ally.Z) <= GameConstants.RescueRadius - 0.5f) _mode = ModePause;
            else Plan(ally.X, ally.Z, GameConstants.RescueRadius - 0.7f, false);
        }

        private static bool LaggingAllyInReach(SurvivorBotView view)
        {
            foreach (var ally in view.Team)
                if (ally.Id != view.SelfId && ally.Status == (byte)PlayerStatus.Lagging &&
                    Distance(view.X, view.Z, ally.X, ally.Z) <= GameConstants.RescueRadius - 0.1f) return true;
            return false;
        }

        private static bool RescueUrgent(SurvivorBotView view, int target)
        {
            var ally = view.Team[target];
            double arrival = Distance(view.X, view.Z, ally.X, ally.Z) * 1.4 / Math.Max(0.1f, view.Speed);
            return ally.StatusTimer < arrival + GameConstants.RescueTimeSeconds + 13;
        }

        private static bool CanRescueBeforeEscape(SurvivorBotView view, int target, int exit)
        {
            var ally = view.Team[target];
            double arrive = Distance(view.X, view.Z, ally.X, ally.Z) * 1.4 / Math.Max(0.1f, view.Speed);
            double leave = Distance(ally.X, ally.Z, view.Exits[exit].X, view.Exits[exit].Z) * 1.4 / Math.Max(0.1f, view.Speed);
            // A Lagging ally consumes twice as much collapse clock until the rescue completes.
            return arrive < 4 && (view.CollapseLeft < 0 ||
                (arrive + GameConstants.RescueTimeSeconds) * GameConstants.CollapseLaggingRate + leave + 3 < view.CollapseLeft);
        }

        // A random lattice spot (NavGrid.Spots) at least HideoutDistance away (the farthest one if none is).
        private void PickHideout(SurvivorBotView view, NavGrid nav, Random random)
        {
            int options = nav.Spots.Count;
            if (options == 0) { _hideX = view.X; _hideZ = view.Z; return; }
            int start = random.Next(options);
            float bestDistance = -1;
            for (int k = 0; k < options; k++)
            {
                var spot = nav.Spots[(start + k) % options];
                float distance = Distance(view.X, view.Z, spot.X, spot.Z);
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                _hideX = spot.X;
                _hideZ = spot.Z;
                if (distance >= HideoutDistance) break;
            }
        }

        // Lagging: always visible and slow, so it moves openly toward the nearest Normal teammate, away from a killer it sees.
        private void UpdateLagging(ref BotIntent intent, SurvivorBotView view, NavGrid nav, FixedMap map, Random random)
        {
            double t = view.Time;
            if (view.KillerVisible && Distance(view.X, view.Z, view.KillerX, view.KillerZ) <= 6f)
            {
                float dx = view.X - view.KillerX, dz = view.Z - view.KillerZ;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len > 0.01f && MoveTo(ref intent, nav, view.X, view.Z, view.X + dx / len * 3, view.Z + dz / len * 3, 0.2f, view.Speed, t, random)) { }
                return;
            }
            float best = float.MaxValue, tx = view.X, tz = view.Z;
            foreach (var ally in view.Team)
            {
                if (ally.Id == view.SelfId || ally.Status != (byte)PlayerStatus.Normal) continue;
                float d = Distance(view.X, view.Z, ally.X, ally.Z);
                if (d < best) { best = d; tx = ally.X; tz = ally.Z; }
            }
            if (best == float.MaxValue) Stop(ref intent, view.X, view.Z);
            else MoveTo(ref intent, nav, view.X, view.Z, tx, tz, StopDistance, view.Speed, t, random);
        }

        private void UseSkills(ref BotIntent intent, SurvivorBotView view)
        {
            if (!view.SkillReady) return;
            // A flee skill is wanted only while that flee lasts. A decoy also covers the start of a hack burst (hacking is
            // Signal, so fake Signals elsewhere make the real one harder to pick).
            bool use = _wantSkill && view.Time < _fleeUntil;
            if (view.Skill == SkillIds.TrafficOverload &&
                ((view.KillerVisible && Distance(view.X, view.Z, view.KillerX, view.KillerZ) <= 10) ||
                (view.Threat >= 3 && (intent.DirX != 0 || intent.DirZ != 0)))) use = true;
            if (_mode != ModeHack) _coverHack = false;
            else if (_coverHack && (view.Skill == SkillIds.DummyPacket || view.Skill == SkillIds.WhiteNoise))
            {
                use = true;
                _coverHack = false;
            }
            if (view.Skill == SkillIds.EmergencyRestore)
            {
                use = false;
                foreach (var ally in view.Team)
                    if (ally.Id != view.SelfId && ally.Status == (byte)PlayerStatus.Lagging &&
                        Distance(view.X, view.Z, ally.X, ally.Z) <= SkillIds.EmergencyRestoreRadius) use = true;
            }
            else if (view.Skill == SkillIds.StealthRelay && _mode == ModeHack && view.Threat >= 1)
            {
                use = true;
            }
            if (!use) return;
            intent.UseSkill = true;
            _wantSkill = false;
        }
    }
}
