using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Dev
{
    // Scripted run for screenshot QA: menu shot, join or offline, host setup, lobby shot, start, scripted W or wander,
    // play shots, event shots (hacking, exit open, lagging, result), optional rematch, optional synthetic result preview,
    // quit. Times are realtime seconds from the first frame; only created when DevArgs.Active.
    public sealed class DevCapture : MonoBehaviour
    {
        private DevArgs args;
        private ClientScreens screens;
        private double overloadedAt = -1;
        private double origin = -1, lobbyAt = -1, matchAt = -1, previewAt = -1, laggingAt = -1, resultAt = -1, reopenedAt = -1;
        private double backAt = -1, doneAt = -1;
        private bool menuShot, joined, configured, lobbyShot, started, play1, play4, previewShot, quitting, host, capturing;
        private bool restarted, position2, position5;
        private double orbitAt = -1, aimAt = -1;
        private bool menuWanted;
        private double settingsAt = -1;
        private int settingsStage, settingsPlayStage, howToStage;
        private double leaveAt = -1; // -ptk-menu-leave: when the play menu's Leave match was pressed
        private int participants, match;
        private Role role;
        private Wander wander;
        private bool located;
        private Vector3 position, positionAt2;
        private Vector3 movedFrom;
        private int movedLogged;
        private int killerCounts = -1;
        private double countsAt = -1;
        private byte localStatus;
        private string localHud;
        private byte[] progress = Array.Empty<byte>();
        private int exitState = -1;
        private readonly HashSet<string> taken = new HashSet<string>();
        private readonly Queue<KeyValuePair<string, int>> pending = new Queue<KeyValuePair<string, int>>();
        private readonly Queue<float> pendingAt = new Queue<float>(); // unscaled time each pending shot is not taken before
        // SelfState evidence and the U4d event shots (cooldown, rescue, grace, crowd).
        private ISelfState self;
        private int skillPresses, selfLogged, maxTracked;
        private double nextSkillPress = -1, cooldownSeenAt = -1, countdownLogAt = -1;
        private bool skillUsed;
        private string loggedPrompt;
        // -ptk-chase / -ptk-rescue steering (dev only; the routes come from Server.Core's NavGrid over the shared map).
        private NavGrid nav;
        private int chaseTerminal;
        private double chaseLogAt = -1;
        private readonly List<Vector3> terminalPoints = new List<Vector3>();
        private bool rescueFound;
        private byte threatLevel;
        private Vector3 rescueAt;
        // -ptk-preview-v2-hud: the rules v2 widget preview (its own panel; requests its shots through EventShot).
        private RulesV2Preview v2;
        // -ptk-dev-hack / -ptk-dev-switch autopilot and the rules v2 live shots.
        private HackPilot pilot;
        private S2C_WorldState world;
        private PilotPhase loggedPhase;
        private double objectiveAt = -1, collapseSeenAt = -1;
        public event Action<string> Join;
        public event Action Offline;
        // -ptk-want-skill: leave this offline match (ClientApp disconnects); Update then starts a new one.
        public event Action Reroll;
        private int rerolls;
        private bool rerollPending;
        // Local dispatch only: the handler feeds the packet to its own receive path and never sends it.
        public event Action<S2C_GameResult> PreviewResult;
        public event Action<IPacket> HostSend;

        public void Initialize(DevArgs value, ClientScreens ui = null, ISelfState selfState = null, Func<Vector3, bool> onScreen = null)
        {
            args = value; screens = ui; self = selfState; OnScreen = onScreen;
            foreach (string error in args.Errors) Debug.LogWarning("[DEV] " + error);
            if (args.Shots != null) Directory.CreateDirectory(args.Shots);
            if (args.Chase > 0 || args.Rescue || args.DevHack > 0 || args.DevSwitch) nav = new NavGrid(SharedMap.Map);
            if (args.DevHack > 0 || args.DevSwitch) pilot = new HackPilot(args.DevHack, args.DevSwitch);
            MotionPreview.Active = args.PreviewMotion; // U4j: read by CharacterViews (own Survivor) only
        }

        private bool Wandering => match == 1 && role == Role.Survivor && args.Wander > 0 && InWindow(args.Wander);
        private bool InWindow(float seconds)
        {
            if (matchAt < 0) return false;
            double t = Now - matchAt;
            return t >= 2 && t < 2 + seconds;
        }

        private bool Chasing => role == Role.Killer && args.Chase > 0 && InWindow(args.Chase) && located && resultAt < 0 && !KillerDownStaging;
        // U8 (dev only): -ptk-chase on a Survivor client seeks a capture of the own Survivor for the capture-moment shots and
        // frame-time log: while Normal it walks toward the Killer model's entry when one arrived (line of sight), else the
        // terminals in turn. Never sent anywhere; the server sees only the usual MoveInput.
        private bool Seeking => role == Role.Survivor && args.Chase > 0 && InWindow(args.Chase) && located && resultAt < 0 && localStatus == (byte)PlayerStatus.Normal;
        // The Killer entry's ground position (Survivor clients receive it in line of sight). Read before the local-player
        // guard in OnWorld, used only as the seek target of Chase, never logged (RulesReadoutQaTests checks both).
        private Vector3 killerEntry;
        private double killerCaptureAt = -1; // U8: the Killer client's latest own capture (shots, first 4)
        private int killerShotCaptures;
        private bool killerEntryKnown;
        // -ptk-preview-motion killer-down (U4j): after the first capture the chase stops; the Killer walks away from the
        // nearest instance for RetreatSeconds, then turns to face it, so the downed Survivor is framed at a few metres.
        private const double RetreatSeconds = 1.3, KillerDownShotSeconds = 2.4;
        private bool KillerDownStaging => args.PreviewMotion == MotionPreviewClip.KillerDown && captureSeenAt >= 0 && resultAt < 0;
        private bool Retreating => KillerDownStaging && Now - captureSeenAt < RetreatSeconds;
        // -ptk-dev-hack / -ptk-dev-switch: the autopilot walks (the scripted W) and holds Interact, Survivor only.
        private bool Piloting => pilot != null && role == Role.Survivor && match >= 1 && matchAt >= 0 && resultAt < 0 && Now - matchAt >= 2;
        private bool PilotWalk => Piloting && located && pilot.Walk;
        // -ptk-walk-after-hack: the dev-hack budget ran out (E released, possibly mid-route): walk away for that long.
        private double hackSpentAt = -1;
        private Vector3 hackSpentFrom;
        private bool walkLogged;
        private bool WalkingAway
        {
            get
            {
                if (!Piloting || args.WalkAfterHack <= 0 || args.DevHack <= 0) return false;
                if (hackSpentAt < 0 && pilot.Held >= args.DevHack)
                {
                    hackSpentAt = Now;
                    hackSpentFrom = position;
                    Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " dev-hack budget spent: E released, walking away for " + Stamp(args.WalkAfterHack) + " s from "
                        + Stamp(position.x) + "," + Stamp(position.z));
                }
                bool walking = hackSpentAt >= 0 && Now - hackSpentAt < args.WalkAfterHack;
                if (hackSpentAt >= 0 && !walking && !walkLogged)
                {
                    walkLogged = true;
                    Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " walked away " + Stamp(Vector3.Distance(hackSpentFrom, position)) + " m to "
                        + Stamp(position.x) + "," + Stamp(position.z) + " (route panel shown=" + screens.Survivor.RoutePanel.Shown + ")");
                    EventShot("walked-away.png");
                }
                return walking;
            }
        }
        public bool HoldInteract => (Piloting && pilot.Hold) || RescueHold;
        // R3: -ptk-rescue also holds E once within reach of the Lagging ally (the rescue needs the hold, HUD_SPEC 4.4).
        private const float RescueHoldReach = 1.4f;
        private bool RescueHold => role == Role.Survivor && args.Rescue && rescueFound && located && resultAt < 0 && localStatus == (byte)PlayerStatus.Normal
            && new Vector2(rescueAt.x - position.x, rescueAt.z - position.z).magnitude <= RescueHoldReach;
        // -ptk-rescue: walk while a Lagging ally is farther than RescueStop (inside the 1.5 m rescue radius).
        private const float RescueStop = 1f;
        // Only while the Killer is not near (ThreatLevel 0-1), like the bots: walking into a camped ally only adds a capture.
        private bool RescueWalk => role == Role.Survivor && args.Rescue && rescueFound && threatLevel <= 1 && located && resultAt < 0 && localStatus == (byte)PlayerStatus.Normal
            && new Vector2(rescueAt.x - position.x, rescueAt.z - position.z).magnitude > RescueStop;

        // Scripted forward input from the 2nd second of play: the wander window in the first match, else HoldW seconds;
        // -ptk-chase and -ptk-rescue walk through the same path.
        public bool HoldForward => PilotWalk || WalkingAway || RescueWalk || Chasing || Seeking || Retreating || Wandering || (!(match == 1 && args.Wander > 0 && role == Role.Survivor) && args.HoldW > 0 && InWindow(args.HoldW));

        // Survivor steering: toward a Lagging ally (-ptk-rescue), else the wander yaw; false when neither applies.
        public bool Steer(float currentYaw, float dt, out float yaw)
        {
            yaw = currentYaw;
            if (Piloting)
            {
                pilot.Update(position.x, position.z, localStatus == (byte)PlayerStatus.Normal, world, SharedMap.Map.ExitSwitches, dt);
                if (pilot.Phase != loggedPhase)
                {
                    loggedPhase = pilot.Phase;
                    Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " autopilot " + pilot.Phase + " goal=" + Stamp(pilot.Goal.x) + "," + Stamp(pilot.Goal.z)
                        + " held=" + Stamp(pilot.Held) + " pos=" + Stamp(position.x) + "," + Stamp(position.z));
                }
                if (PilotWalk) return Route(pilot.Goal, out yaw);
            }
            if (RescueWalk) return Route(rescueAt, out yaw);
            if (Seeking) return Chase(killerEntryKnown, killerEntry, out yaw);
            if (!Wandering || !located) return false;
            if (wander == null) wander = new Wander(currentYaw);
            yaw = wander.Next(SharedMap.Map, position.x, position.z, dt);
            return true;
        }

        // The yaw of the NavGrid route from the local position toward a goal (false at the goal).
        private bool Route(Vector3 goal, out float yaw)
        {
            yaw = 0;
            if (nav == null || !nav.Steer(position.x, position.z, goal.x, goal.z, out float dx, out float dz)) return false;
            yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            return true;
        }

        // -ptk-skill-at: true on the frames the own skill should be pressed. The first press is at the given match second;
        // while no cooldown starts (rejected, e.g. Emergency restore without a target) it presses again each second.
        public bool PressSkill(bool able = true)
        {
            if (args.SkillWhenAble && !able) return false; // -ptk-skill-when-able: wait, uncounted
            if (args.SkillAt <= 0 || matchAt < 0 || resultAt >= 0 || skillUsed || skillPresses >= DevArgs.SkillTries) return false;
            if (self != null && self.Available && self.SkillCooldown > 0) { skillUsed = true; return false; }
            double t = Now - matchAt;
            if (t < args.SkillAt || (nextSkillPress >= 0 && Now < nextSkillPress)) return false;
            skillPresses++; nextSkillPress = Now + 1;
            Debug.Log("[DEV] t=" + Stamp(t) + " skill press " + skillPresses + " (scripted Q)");
            return true;
        }

        // Every S2C_SelfState packet: the first, then any that carries a non-zero value (capped), with the countdown.
        public void OnSelfState(S2C_SelfState state, int received)
        {
            bool interesting = received == 1 || state.SkillCooldown > 0 || state.RescueProgress > 0 || state.InvulnerableLeft > 0 || state.ActionKind != 0
                || (state.CollapseLeft >= 0 && collapseSeenAt < 0);
            if (state.CollapseLeft >= 0 && collapseSeenAt < 0) collapseSeenAt = Now;
            if (!interesting || selfLogged >= 300) return;
            selfLogged++;
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " selfstate #" + received + " skillCooldown=" + Stamp(state.SkillCooldown) + " rescue=" + Stamp(state.RescueProgress)
                + " invulnerable=" + Stamp(state.InvulnerableLeft) + " collapse=" + Stamp(state.CollapseLeft) + " action=" + (ActionKind)state.ActionKind + ":" + Stamp(state.ActionProgress));
        }

        // HUD_SPEC 13.2 evidence (11/17, R2): every new own route from SelfState, and every key the client sent with the
        // tracker's prediction (right / wrong, done).
        public void OnRoute(RouteTracker route)
        {
            if (routeLogged >= 400) return;
            routeLogged++;
            string dirs = string.Empty;
            for (int i = 0; i < route.Length; i++) dirs += HackRoute.DirectionAt(route.Directions, i).ToString()[0];
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " route id=" + route.RouteId + " dirs=" + (route.RouteId == 0 ? "-" : dirs) + " index=" + route.Index
                + " completed=" + route.Completed + " errors=" + route.Errors);
        }

        public void OnRouteKey(C2S_HackInput input, RouteTracker route)
        {
            if (routeLogged >= 400) return;
            routeLogged++;
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " route key id=" + input.RouteId + " dir=" + (RouteDirection)input.Direction + " index=" + route.Index
                + " keys=" + route.KeysSent + " completed=" + route.Completed + " errors=" + route.Errors);
        }

        // -ptk-auto-route: the scripted key due this frame, if any (the caller presses it through the same RouteTracker.TryPress
        // as WASD). The pace counts from the route's first answerable frame and from each key after it.
        public bool PressRoute(RouteTracker route, double now, out RouteDirection direction)
        {
            direction = RouteDirection.Up;
            if (args.AutoRoute == AutoRoute.None || route == null || !route.Present) { routeReadyAt = -1; return false; }
            if (routeReadyAt < 0 || route.RouteId != routeReadyId) { routeReadyAt = now; routeReadyId = route.RouteId; }
            if (now - routeReadyAt < HackPilot.RouteKeySeconds(args.AutoRoute)) return false;
            routeReadyAt = now;
            routeKeys++;
            direction = HackPilot.RouteKey(args.AutoRoute, routeKeys, route.Expected);
            return true;
        }

        // -ptk-orbit: once at match start + 2 s, the orbit yaw becomes the character facing plus the offset.
        public bool Orbit(float facing, out float yaw)
        {
            yaw = facing;
            if (!args.Orbit.HasValue || role != Role.Survivor || orbitAt >= 0 || matchAt < 0 || Now - matchAt < 2 || !located) return false;
            orbitAt = Now;
            yaw = facing + args.Orbit.Value;
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " orbit yaw=" + Stamp(Mathf.Repeat(yaw, 360)) + " facing=" + Stamp(facing));
            return true;
        }

        // -ptk-killer-look occluded: the aim target is the nearest blip behind a wall; while there is none the view holds
        // LookYaw (a number given in an earlier -ptk-killer-look, default 0), so a scripted walk can reach a wall first.
        public bool AimOccluded => args.Look == KillerLook.Occluded;

        // -ptk-killer-look: every 0.5 s the Killer view turns toward the nearest blip, away from it, or to a fixed yaw.
        public bool Aim(bool hasTarget, Vector3 from, Vector3 target, out float yaw)
        {
            yaw = 0;
            if (Chasing) return Chase(hasTarget, target, out yaw);
            if (KillerDownStaging)
            {
                if (!hasTarget) return false;
                yaw = Mathf.Atan2(target.x - from.x, target.z - from.z) * Mathf.Rad2Deg + (Retreating ? 180 : 0);
                return true;
            }
            if (args.Look == KillerLook.None || role != Role.Killer || matchAt < 0 || (aimAt >= 0 && Now - aimAt < .5)) return false;
            if (args.Look == KillerLook.Fixed || (args.Look == KillerLook.Occluded && !hasTarget)) yaw = args.LookYaw;
            else
            {
                if (!hasTarget) return false;
                yaw = Mathf.Atan2(target.x - from.x, target.z - from.z) * Mathf.Rad2Deg + (args.Look == KillerLook.Away ? 180 : 0); // nearest and occluded aim at the target
            }
            aimAt = Now;
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " killer look " + args.Look + " yaw=" + Stamp(Mathf.Repeat(yaw, 360))
                + (args.Look == KillerLook.Fixed || !hasTarget ? string.Empty : " from=" + Stamp(from.x) + "," + Stamp(from.z) + " blip=" + Stamp(target.x) + "," + Stamp(target.z)));
            return true;
        }

        // -ptk-chase: every frame, the route toward the nearest tracked instance; with none, toward the terminals in turn
        // (Survivors work there). Logged at most once a second.
        private bool Chase(bool hasTarget, Vector3 target, out float yaw)
        {
            Vector3 goal = target;
            if (!hasTarget)
            {
                if (terminalPoints.Count == 0) { yaw = 0; return false; }
                goal = terminalPoints[chaseTerminal % terminalPoints.Count];
                if (new Vector2(goal.x - position.x, goal.z - position.z).magnitude < 2) { chaseTerminal++; goal = terminalPoints[chaseTerminal % terminalPoints.Count]; }
            }
            bool routed = Route(goal, out yaw);
            if (routed && Now - chaseLogAt >= 1)
            {
                chaseLogAt = Now;
                bool seek = role == Role.Survivor && hasTarget; // a Survivor's seek target is the Killer entry: not logged
                Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " chase " + (seek ? "killer" : hasTarget ? "blip" : "terminal") + " from=" + Stamp(position.x) + "," + Stamp(position.z)
                    + (seek ? string.Empty : " to=" + Stamp(goal.x) + "," + Stamp(goal.z)) + " yaw=" + Stamp(Mathf.Repeat(yaw, 360)));
            }
            return routed;
        }

        // -ptk-menu: true while the play menu should be open (from the given second of the first match, for 2 s).
        public bool MenuChange(out bool open)
        {
            double t = Now - matchAt;
            open = args.Menu > 0 && match == 1 && matchAt >= 0 && t >= args.Menu && t < args.Menu + 2;
            if (open == menuWanted) return false;
            menuWanted = open;
            return true;
        }

        // Killer view counts this frame (from the tracker, never from entries): logged when they change, at most every 0.5 s.
        // Event shots, one per frame and only while no capture is pending: the first frame with an in-view character; the first
        // with a through-wall marker and no in-view character or chevron; the first with chevrons and no in-view character.
        // tracked = instances in the tracker (Signal, Lagging and decoys alike); prompt = the execution guidance shown.
        // killer-crowd: the first frame with at least CrowdCount instances tracked at once.
        public const int CrowdCount = 6;
        public void OnKillerFrame(int inView, int occluded, int edges, int tracked = 0, string prompt = null, int captures = 0, int overloaded = 0)
        {
            if (captures > killerShotCaptures && captures <= 4) { killerShotCaptures = captures; killerCaptureAt = Now; }
            if (args.Shots != null && !capturing && pending.Count == 0 && killerCaptureAt >= 0)
            {
                string strike = "u8-killer-capture-" + killerShotCaptures + "-strike.png", after = "u8-killer-capture-" + killerShotCaptures + "-after.png";
                if (Now - killerCaptureAt >= .2 && !taken.Contains(strike)) EventShot(strike, 0);
                else if (Now - killerCaptureAt >= .5 && !taken.Contains(after)) EventShot(after, 0);
            }
            if (captures > 0 && captureSeenAt < 0 && args.PreviewMotion == MotionPreviewClip.KillerDown)
            {
                captureSeenAt = Now; Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " motion preview: capture seen");
            }
            int counts = tracked * 1000000 + inView * 10000 + occluded * 100 + edges;
            if (counts != killerCounts && Now - countsAt >= .5)
            {
                killerCounts = counts; countsAt = Now;
                Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " blips inView=" + inView + " occluded=" + occluded + " edges=" + edges + " tracked=" + tracked);
            }
            if (tracked > maxTracked)
            {
                maxTracked = tracked;
                Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " blips tracked max=" + tracked);
            }
            lastInView = inView;
            if (prompt != loggedPrompt)
            {
                loggedPrompt = prompt;
                Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " killer prompt=\"" + prompt + "\"");
            }
            if (capturing || pending.Count > 0 || args.Shots == null) return;
            // 0.3 s after the alert first shows, so its pooled tag has been measured and clamped (HUD_SPEC 13.6).
            if (overloaded <= 0) overloadedAt = -1;
            else if (overloadedAt < 0) overloadedAt = Now;
            if (overloaded > 0 && Now - overloadedAt >= .3 && !taken.Contains("v2-killer-overloaded.png")) { Debug.Log("[DEV] overloaded alerts=" + overloaded); EventShot("v2-killer-overloaded.png", 0); return; }
            if (WorldPowered && !taken.Contains("v2-killer-powered.png") && Now - matchAt > 3) { EventShot("v2-killer-powered.png", 0); return; }
            if (collapseSeenAt >= 0 && Now >= collapseSeenAt + 2 && !taken.Contains("v2-killer-collapse.png")) { EventShot("v2-killer-collapse.png", 0); return; }
            if (tracked >= CrowdCount && !taken.Contains("killer-crowd.png")) { EventShot("killer-crowd.png", 0); return; }
            if (inView > 0 && !taken.Contains("killer-inview.png")) EventShot("killer-inview.png", 0);
            else if (occluded > 0 && inView == 0 && edges == 0 && !taken.Contains("killer-occluded.png")) EventShot("killer-occluded.png", 0);
            else if (edges > 0 && inView == 0 && !taken.Contains("killer-offscreen.png")) EventShot("killer-offscreen.png", 0);
        }

        // Survivor feedback evidence (U4e), every Survivor frame, from what the client shows (never from entries): the threat
        // visual level, the own status, the capture banner, the stutter's presented frames and the Killer model's alpha.
        // Event shots, one per frame: threat-1 / threat-2 / threat-3 (Normal, level held 0.3 / 0.3 / 0.2 s), capture-banner (0.6 s after the
        // capture), lagging-stutter (banner gone, stutter running), eliminated-reason (1 s after), killer-in-sight (model fully
        // shown for 0.5 s) and killer-fade (mid-fade after a sighting).
        private byte shownThreat;
        private PlayerStatus shownStatus;
        private double threatSince = -1, capturedAt = -1, eliminatedAt = -1, killerSince = -1;
        private bool killerSeen, killerLogged;
        public void OnSurvivorFrame(byte threat, PlayerStatus status, bool banner, int stutterFrames, float killerAlpha, SurvivorHud hud = null)
        {
            if (hud != null && V2SurvivorShots(hud)) return;
            if (matchAt < 0 || resultAt >= 0) return;
            double t = Now;
            if (threat != shownThreat) { shownThreat = threat; threatSince = t; Debug.Log("[DEV] t=" + Stamp(t - matchAt) + " threat visual level=" + threat); }
            if (status != shownStatus)
            {
                if (status == PlayerStatus.Lagging) capturedAt = t;
                if (status == PlayerStatus.Eliminated) eliminatedAt = t;
                shownStatus = status;
            }
            bool full = killerAlpha >= .999f;
            if (full && killerSince < 0) killerSince = t;
            if (!full) killerSince = -1;
            if (full) killerSeen = true;
            if ((killerAlpha > 0) != killerLogged)
            {
                killerLogged = killerAlpha > 0;
                Debug.Log("[DEV] t=" + Stamp(t - matchAt) + " killer model " + (killerLogged ? "shown" : "hidden"));
            }
            if (args.Shots == null || capturing || pending.Count > 0) return;
            if (Want("threat-1.png", located && threat == 1 && status == PlayerStatus.Normal && t - threatSince >= .3)) return;
            if (Want("threat-2.png", located && threat == 2 && status == PlayerStatus.Normal && t - threatSince >= .3)) return;
            if (Want("threat-3.png", located && threat == 3 && status == PlayerStatus.Normal && t - threatSince >= .2)) return;
            // U8: the capture moment: the hit-stop frame (first Lagging frame), mid-burst and after the burst (steady look).
            if (Want("u8-capture-a-windup.png", status == PlayerStatus.Lagging && capturedAt >= 0 && t - capturedAt >= .1)) return;
            if (Want("u8-capture-0-hit.png", status == PlayerStatus.Lagging && capturedAt >= 0 && t - capturedAt >= .3)) return;
            if (Want("u8-capture-1-burst.png", status == PlayerStatus.Lagging && capturedAt >= 0 && t - capturedAt >= .45)) return;
            if (Want("u8-capture-2-after.png", status == PlayerStatus.Lagging && capturedAt >= 0 && t - capturedAt >= 1.1)) return;
            // R3 (11/17): the Lagging-ally tag and edge arrow, the helper prompt and the downed player's persistent line.
            if (hud != null)
            {
                if (Want("r3-ally-tag.png", located && hud.Allies.OnScreenCount > 0 && status == PlayerStatus.Normal)) return;
                if (Want("r3-ally-edge.png", located && hud.Allies.EdgeCount > 0 && status == PlayerStatus.Normal)) return;
                string line = hud.PromptText;
                if (Want("r3-rescue-prompt.png", line != null && line.StartsWith("Hold [E] to restore"))) return;
                if (Want("r3-restoring.png", line != null && line.StartsWith("Restoring") && self != null && self.RescueProgress >= .3f)) return;
                if (Want("r3-getting-up.png", line != null && line == HelpCopy.GettingUp)) return;
                if (Want("r3-downed-line.png", status == PlayerStatus.Lagging && !banner && line != null && line.StartsWith(HelpCopy.DownedPrefix) && capturedAt >= 0 && t - capturedAt >= 3.3)) return;
            }
            if (Want("capture-banner.png", banner && capturedAt >= 0 && t - capturedAt >= .6)) return;
            if (Want("lagging-stutter.png", status == PlayerStatus.Lagging && !banner && stutterFrames >= 4 && capturedAt >= 0 && t - capturedAt >= 3.3)) return;
            if (Want("eliminated-reason.png", status == PlayerStatus.Eliminated && eliminatedAt >= 0 && t - eliminatedAt >= 1)) return;
            if (Want("killer-in-sight.png", located && killerSince >= 0 && t - killerSince >= .5)) return;
            if (killerSeen && killerAlpha > .25f && killerAlpha < .5f && !taken.Contains("killer-fade.png")) EventShot("killer-fade.png", 0);
        }

        private bool Want(string name, bool condition)
        {
            if (!condition || taken.Contains(name)) return false;
            EventShot(name);
            return true;
        }

        // -ptk-rescue (Survivor client): the nearest Lagging ally from the Survivor HUD's own data, or found = false.
        public void RescueTarget(bool found, Vector3 at) { rescueFound = found; rescueAt = at; }

        // The local player's rendered position this frame (survivor: the interpolated own entry).
        public void Track(bool found, Vector3 value) { located = found; if (found) position = value; }

        public void OnLobby(S2C_LobbyState state, bool isHost)
        {
            if (lobbyAt < 0) lobbyAt = Now;
            host = isHost; participants = 0;
            foreach (Slot slot in state.Slots) if (slot.PlayerId != 0) participants++;
            if (resultAt >= 0 && reopenedAt < 0)
            {
                reopenedAt = Now;
                Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " lobby reopened after the result; back to lobby enabled=" + (screens != null && screens.BackToLobbyEnabled));
            }
            if (configured || !host) return;
            configured = true;
            if (args.Bots >= 0) HostSend?.Invoke(new C2S_SetBots { Count = (byte)args.Bots });
            if (args.RoleChoice.HasValue) HostSend?.Invoke(new C2S_SetRole { RoleChoice = args.RoleChoice.Value });
        }

        public void OnMatchStart(Role value, byte skill)
        {
            matchAt = Now; match++; role = value; wander = null; located = false; orbitAt = aimAt = -1; menuWanted = false; movedLogged = 0; killerCounts = -1;
            laggingAt = resultAt = reopenedAt = backAt = -1; localStatus = 0; localHud = null; progress = Array.Empty<byte>(); exitState = -1;
            skillPresses = 0; skillUsed = false; nextSkillPress = cooldownSeenAt = countdownLogAt = chaseLogAt = -1; maxTracked = 0;
            loggedPrompt = null; chaseTerminal = 0; world = null; loggedPhase = PilotPhase.Idle; objectiveAt = collapseSeenAt = captureSeenAt = -1; terminalPoints.Clear(); rescueFound = false;
            shownThreat = 0; shownStatus = PlayerStatus.Normal; threatSince = capturedAt = eliminatedAt = killerSince = -1; killerSeen = killerLogged = false;
            Debug.Log("[DEV] match " + match + " start role=" + value + " skill=" + skill);
            if (args.WantSkill.HasValue && args.Offline && skill != args.WantSkill.Value && rerolls < DevArgs.WantSkillTries)
            {
                rerolls++; rerollPending = true;
                Debug.Log("[DEV] want skill " + args.WantSkill.Value + ", got " + skill + ": new offline match (" + rerolls + ")");
            }
        }

        // HUD_SPEC 14 evidence: one shot per own role, skill, viewer and screen height, about 0.25 s after the record played
        // (the one-shot is mid-flight and the result line shows).
        public void OnSkillFx(FxRecord record)
        {
            if (args.Shots == null || record.Viewer == FxViewer.None || matchAt < 0 || rerollPending) return;
            string name = "skillfx-" + (role == Role.Killer ? "killer" : "survivor") + "-" + (record.KillerSkill ? "k" : "s") + record.SkillId
                + "-" + record.Viewer.ToString().ToLowerInvariant() + "-" + Screen.height + "p.png";
            if (taken.Contains(name)) return;
            int frames = Mathf.Clamp(Mathf.RoundToInt(.25f / Mathf.Max(.001f, Time.unscaledDeltaTime)), 1, 60);
            EventShot(name, frames);
        }

        // Every WorldState of the match: event shots and the terminal/exit/status log.
        public void OnWorld(S2C_WorldState state, int localId)
        {
            double t = Now - matchAt;
            world = state;
            bool hacking = false, changed = progress.Length != state.Terminals.Count;
            if (changed) progress = new byte[state.Terminals.Count];
            if (terminalPoints.Count != state.Terminals.Count)
            {
                terminalPoints.Clear();
                foreach (TerminalState terminal in state.Terminals) terminalPoints.Add(new Vector3(terminal.X, 0, terminal.Z));
            }
            for (int i = 0; i < state.Terminals.Count; i++)
            {
                byte value = state.Terminals[i].Progress;
                if (value >= 1 && value <= 99) hacking = true;
                if (progress[i] != value) { progress[i] = value; changed = true; }
            }
            bool exitOpen = WorldExits.AnyOpen(state);
            int exit = (exitOpen ? 1 : 0) + (WorldExits.AnyLocked(state) ? 2 : 0) + (WorldExits.Powered(state) ? 4 : 0);
            if (changed || exit != exitState)
            {
                exitState = exit;
                var line = new StringBuilder("[DEV] t=").Append(Stamp(t)).Append(" terminals");
                foreach (TerminalState terminal in state.Terminals) line.Append(' ').Append(terminal.Id).Append(':').Append(terminal.Progress);
                line.Append(" required=").Append(state.TerminalsRequired).Append(" exits=");
                foreach (ExitState e in state.Exits) line.Append(e.Id).Append(':').Append(e.Flags).Append('/').Append(e.SwitchProgress).Append(' ');
                line.Append("exitOpen=").Append(exitOpen ? 1 : 0).Append(" collapse=").Append(Stamp(state.CollapseLeft)).Append(" hatch=").Append(state.HatchVisible)
                    .Append(" hud=\"").Append(HudCopy.TerminalsLine(state.Terminals, state.TerminalsRequired)).Append('"');
                Debug.Log(line.ToString());
            }
            // World shots wait until the camera and avatars are placed (the first ~100 ms of a match render nothing yet).
            threatLevel = state.ThreatLevel;
            if (hacking && located) EventShot("hacking.png");
            if (exitOpen && located) EventShot("exit-open.png");
            ChaseShutterShots(state.Shutters);
            // Survivor client only: the Killer client never reads a Survivor entry (its view logs blip counts from the tracker).
            if (role != Role.Survivor) return;
            // -ptk-chase (Survivor): the Killer entry steers the seek walk only; its position is never logged.
            killerEntryKnown = false;
            foreach (PlayerState player in state.Players)
            {
                ChasePlayerShots(player, localId, state.Shutters);
                if (player.Role == (byte)Role.Killer) { killerEntry = new Vector3(player.X, 0, player.Z); killerEntryKnown = true; continue; }
                if (player.Id != localId) continue;
                if (player.Status == (byte)PlayerStatus.Lagging && localStatus != (byte)PlayerStatus.Lagging && laggingAt < 0) laggingAt = Now;
                string hud = HudCopy.StatusLine((PlayerStatus)player.Status, player.StatusTimer);
                if (player.Status != localStatus || hud != localHud)
                    Debug.Log("[DEV] t=" + Stamp(t) + " local status=" + (PlayerStatus)player.Status + " timer=" + player.StatusTimer.ToString("0.00", CultureInfo.InvariantCulture)
                        + " hud=\"" + hud + "\" pos=" + Stamp(player.X) + "," + Stamp(player.Z));
                localStatus = player.Status; localHud = hud;
            }
        }

        // HUD_SPEC 15 (Q5) event shots, from what this client sees: a shutter changing to Down / Broken, a stunned Killer, a vault
        // (own or seen) and the own prompt beside a prop. M1: a chase shot fires only for a prop within ChaseShotReach of the own
        // position and in front of the camera, and waits ChaseShotDelay seconds (not frames) so the 0.12 s drop has landed.
        public const float ChaseShotReach = 12f, ChaseShotDelay = .25f;
        private byte[] chaseShutters = new byte[0];
        private void ChaseShutterShots(IReadOnlyList<byte> now)
        {
            if (now == null) return;
            for (int i = 0; i < now.Count && i < chaseShutters.Length; i++)
            {
                if (now[i] == chaseShutters[i]) continue;
                if (i >= WorldChaseProps.Shutters.Count || !ChaseShotSeen(WorldChaseProps.Shutters[i].CenterX, WorldChaseProps.Shutters[i].CenterZ)) continue;
                if (now[i] == (byte)ShutterState.Down) EventShot("chase-shutter-down.png", 0, ChaseShotDelay);
                else if (now[i] == (byte)ShutterState.Broken) EventShot("chase-shutter-broken.png", 0, ChaseShotDelay);
            }
            if (chaseShutters.Length != now.Count) chaseShutters = new byte[now.Count];
            for (int i = 0; i < now.Count; i++) chaseShutters[i] = now[i];
        }

        // Set by the caller (it owns the view): true when a world point is inside the drawn view.
        public Func<Vector3, bool> OnScreen;

        // True when the point is within ChaseShotReach of the own position and inside the camera's view.
        private bool ChaseShotSeen(float x, float z)
        {
            if (!located) return false;
            float dx = x - position.x, dz = z - position.z;
            if (dx * dx + dz * dz > ChaseShotReach * ChaseShotReach) return false;
            return OnScreen != null && OnScreen(new Vector3(x, 1f, z));
        }

        // Survivor client only (called from the guarded entry loop): a stunned Killer, a vault, the own prompt beside a prop.
        private void ChasePlayerShots(PlayerState player, int localId, IReadOnlyList<byte> shutters)
        {
            bool seen = player.Id == localId || ((player.Chase & ((byte)ChaseFlags.Stunned | (byte)ChaseFlags.Vaulting)) != 0 && ChaseShotSeen(player.X, player.Z));
            if (seen && (player.Chase & (byte)ChaseFlags.Stunned) != 0) EventShot("chase-stun.png", 0, ChaseShotDelay);
            if (seen && (player.Chase & (byte)ChaseFlags.Vaulting) != 0) EventShot(player.Id == localId ? "chase-vault-own.png" : "chase-vault-seen.png", 0, ChaseShotDelay);
            if (player.Id == localId && player.Status == (byte)PlayerStatus.Normal
                && ChasePropPrompt.Survivor(player.X, player.Z, shutters) != ChasePromptKind.None) EventShot("chase-prompt.png", 2);
        }

        public void OnResult(S2C_GameResult result, ResultV2View view)
        {
            resultAt = Now;
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " match " + match + " end winner=" + (Role)result.WinnerRole + " reason=" + (GameEndReason)result.Reason
                + " escaped=[" + string.Join(",", result.EscapedIds) + "] eliminated=[" + string.Join(",", result.EliminatedIds) + "] back to lobby enabled="
                + (screens != null && screens.BackToLobbyEnabled));
            Debug.Log("[DEV] result screen: \"" + ResultV2Copy.Headline(view.Winner) + "\" / \"" + ResultV2Copy.Outcome(view) + "\" / \""
                + ResultV2Copy.Rule(view.Escaped, view.Starting) + "\" rows=" + view.Rows.Count);
            EventShot("result.png");
        }

        private bool WorldPowered
        {
            get
            {
                if (world?.Exits == null) return false;
                foreach (ExitState exit in world.Exits) if ((exit.Flags & 3) != 0) return true;
                return false;
            }
        }

        // Rules v2 live shots (Survivor, first match): the ring mid-sweep, the result word, the hold row, the objective row
        // once an exit is powered and again with the collapse running. One per frame.
        private bool V2SurvivorShots(SurvivorHud hud)
        {
            if (args.Shots == null || capturing || pending.Count > 0 || match != 1 || resultAt >= 0 || !located) return false;
            // HUD_SPEC 13.2 (11/17, R2): the route panel mid-route, the burst flash, the error line (sloppy) and the own holo
            // panel pulse on a real route key (from the second route on, once the own hack pose has settled).
            RouteTracker route = hud.Route;
            double link = LinkClock.Now;
            if (hud.RoutePanel.Shown && route.Present && route.Index == 2 && !route.ErrorShown(link) && !taken.Contains("v2-route-mid.png"))
            {
                Debug.Log("[DEV] route panel mid-route id=" + route.RouteId + " index=" + route.Index + " label=\"" + hud.RoutePanel.LabelText + "\"");
                EventShot("v2-route-mid.png", 0); return true;
            }
            if (hud.RoutePanel.Shown && route.DoneFlash(link) && !taken.Contains("v2-route-done.png"))
            {
                Debug.Log("[DEV] route done flash label=\"" + hud.RoutePanel.LabelText + "\" completed=" + route.Completed);
                EventShot("v2-route-done.png", 0); return true;
            }
            if (hud.RoutePanel.Shown && route.ErrorShown(link) && route.Shake(link) > .3f && !taken.Contains("v2-route-error.png"))
            {
                Debug.Log("[DEV] route error label=\"" + hud.RoutePanel.LabelText + "\" shake=" + Stamp(hud.RoutePanel.ShakeOffset) + " errors=" + route.Errors);
                EventShot("v2-route-error.png", 0); return true;
            }
            if (hud.RoutePanel.Shown && route.Completed >= 2 && route.Present && route.Index >= 1 && route.Pulse(link) > .5f && !route.ErrorShown(link) && !taken.Contains("holo-route-pulse.png"))
            {
                Debug.Log("[DEV] holo route pulse keys=" + route.KeysSent + " pulse=" + Stamp(route.Pulse(link)));
                EventShot("holo-route-pulse.png", 0); return true;
            }
            if (hud.HoldText != null && !taken.Contains("v2-switch-hold.png") && self != null && self.ActionProgress >= .3f)
            {
                EventShot("v2-switch-hold.png", 0); return true;
            }
            if (hud.ObjectiveShown && objectiveAt < 0) objectiveAt = Now;
            if (objectiveAt >= 0 && Now >= objectiveAt + 1 && !taken.Contains("v2-objective-powered.png"))
            {
                EventShot("v2-objective-powered.png", 0); return true;
            }
            if (collapseSeenAt >= 0 && Now >= collapseSeenAt + 1.5 && !taken.Contains("v2-objective-collapse.png"))
            {
                EventShot("v2-objective-collapse.png", 0); return true;
            }
            return false;
        }

        // -ptk-settings: open the Settings screen, wait for it to lay out, shoot, close. Stage 0 -> 1 -> 2 -> 3 (done).
        private bool SettingsMenuShot(double t)
        {
            if (screens == null) { settingsStage = 3; return false; }
            if (settingsStage == 0) { screens.ShowSettings(); settingsAt = t; settingsStage = 1; return true; }
            if (settingsStage == 1 && t >= settingsAt + .5) { settingsStage = 2; Shot("settings-menu.png"); return true; }
            if (settingsStage == 2) { screens.CloseSettings(); settingsStage = 3; return false; }
            return true;
        }
        // -ptk-howto: open the How to play screen, shoot the Survivor page, the Killer page, close. Stages 0..5 (done).
        private double howToAt = -1;
        private bool HowToShot(double t)
        {
            if (screens == null) { howToStage = 5; return false; }
            switch (howToStage)
            {
                case 0: screens.ShowHowTo(Role.Survivor); howToAt = t; howToStage = 1; return true;
                case 1: if (t < howToAt + .5) return true; howToStage = 2; Shot("howto-survivor.png"); return true;
                case 2: screens.HowTo.SetPage(Role.Killer); howToAt = t; howToStage = 3; return true;
                case 3: if (t < howToAt + .5) return true; howToStage = 4; Shot("howto-killer.png"); return true;
                default: screens.CloseHowTo(); howToStage = 5; return false;
            }
        }
        private bool SettingsPlayShot(double t)
        {
            if (screens == null) { settingsPlayStage = 3; return false; }
            if (settingsPlayStage == 0) { screens.ShowSettings(); settingsAt = t; settingsPlayStage = 1; return true; }
            if (settingsPlayStage == 1 && t >= settingsAt + .4) { settingsPlayStage = 2; Shot("settings-play.png"); return true; }
            if (settingsPlayStage == 2) { screens.CloseSettings(); settingsPlayStage = 3; return false; }
            return true;
        }

        // -ptk-menu-leave: after the play-menu (and Settings) shots, the open menu's real Leave match path; once the main
        // menu shows, menu-after-leave.png and the cursor state. The script is then complete.
        private bool MenuLeave(double t)
        {
            if (!args.MenuLeave || args.Menu <= 0 || screens == null || !taken.Contains("play-menu.png") || (args.Settings && settingsPlayStage < 3)) return false;
            if (leaveAt < 0)
            {
                leaveAt = t;
                bool pressed = screens.PressLeaveMatch();
                if (pressed) Debug.Log("[DEV] t=" + Stamp(t - matchAt) + " play menu: Leave match pressed (role=" + role + ")");
                else Debug.LogWarning("[DEV] play menu: Leave match could not be pressed (menu not open)");
                return true;
            }
            if (taken.Contains("menu-after-leave.png") || !screens.MainMenuShown || t < leaveAt + .5) return false;
            taken.Add("menu-after-leave.png");
            Debug.Log("[DEV] after leave: main menu shown, playing=" + screens.Playing + " cursor lock=" + Cursor.lockState + " visible=" + Cursor.visible);
            Shot("menu-after-leave.png");
            if (doneAt < 0) doneAt = t;
            return true;
        }

        private double Now => Time.realtimeSinceStartupAsDouble - origin;
        private double routeReadyAt = -1;
        private byte routeReadyId;
        private int routeKeys, routeLogged;
        private static string Stamp(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private void Update()
        {
            if (origin < 0) origin = Time.realtimeSinceStartupAsDouble; // engine start-up time does not count
            if (capturing) return; // keep the scripted order: nothing happens while a shot is pending
            double t = Now;
            if (rerollPending)
            {
                rerollPending = false; joined = configured = started = false; lobbyAt = matchAt = -1; match = 0;
                Reroll?.Invoke();
                return;
            }
            bool shots = args.Shots != null;
            if (shots && !menuShot && t >= 1) { menuShot = true; Shot("menu.png"); return; }
            if (args.Settings && shots && menuShot && settingsStage < 3 && SettingsMenuShot(t)) return;
            if (args.HowTo && shots && menuShot && (!args.Settings || settingsStage >= 3) && howToStage < 5 && HowToShot(t)) return;
            if ((args.Offline || args.Join != null) && !joined && (!shots || menuShot) && (!args.Settings || !shots || settingsStage >= 3) && (!args.HowTo || !shots || howToStage >= 5))
            {
                joined = true;
                if (args.Offline) Offline?.Invoke(); else Join?.Invoke(args.Join);
            }
            if (lobbyAt >= 0 && matchAt < 0)
            {
                if (shots && !lobbyShot && t >= lobbyAt + 1) { lobbyShot = true; Shot("lobby.png"); return; }
                if (args.Start && !started && host && participants >= 2 && (!shots || lobbyShot))
                {
                    started = true; HostSend?.Invoke(new C2S_StartMatch());
                }
            }
            if (match == 1 && shots)
            {
                if (!play1 && t >= matchAt + 1) { play1 = true; Shot("play-1s.png"); return; }
                if (!play4 && t >= matchAt + 4) { play4 = true; Shot("play-4s.png"); return; }
                if (role == Role.Survivor && !taken.Contains("survivor-3p.png") && t >= matchAt + 1.8 && taken.Add("survivor-3p.png")) { Shot("survivor-3p.png"); return; }
                if (orbitAt >= 0 && !taken.Contains("survivor-behind.png") && t >= orbitAt + .6 && taken.Add("survivor-behind.png")) { Shot("survivor-behind.png"); return; }
                if (args.Menu > 0 && !taken.Contains("play-menu.png") && t >= matchAt + args.Menu + .5 && taken.Add("play-menu.png")) { Shot("play-menu.png"); return; }
                if (args.Settings && args.Menu > 0 && taken.Contains("play-menu.png") && settingsPlayStage < 3 && SettingsPlayShot(t)) return;
                if (MenuLeave(t)) return;
            }
            if (args.PreviewV2Hud != null && match == 1 && matchAt >= 0 && resultAt < 0 && v2 == null && t >= matchAt + (shots ? 5 : 2) && (!shots || play4))
            {
                v2 = new GameObject("Rules v2 preview").AddComponent<RulesV2Preview>();
                v2.Begin(args.PreviewV2Hud, role, name => EventShot(name, 0));
            }
            if (v2 != null && v2.Done && doneAt < 0) doneAt = t;
            if (SelfShots(t)) return;
            if (MotionShots(t)) return;
            // Movement evidence for every first match: the local position at 1 s and how far it moved by 5 s.
            if (match == 1 && matchAt >= 0 && resultAt < 0 && located && movedLogged < 2 && t >= matchAt + (movedLogged == 0 ? 1 : 5)) LogMoved(t - matchAt);
            // The countdown is only on screen while the match runs; a capture that ends the match leaves no lagging shot.
            if (laggingAt >= 0 && resultAt < 0 && t >= laggingAt + 1) EventShot("lagging.png");
            if (laggingAt >= 0 && resultAt >= 0 && !taken.Contains("lagging.png") && taken.Add("lagging.png (skipped)"))
                Debug.Log("[DEV] lagging shot skipped: the match ended within 1 s of the capture");
            if (args.Rematch) Rematch(t);
            else if (resultAt >= 0 && doneAt < 0 && (!shots || taken.Contains("result.png"))) doneAt = t;
            if (match == 1 && args.PreviewResult.HasValue && previewAt < 0 && t >= matchAt + 5 && (!shots || play4))
            {
                previewAt = t;
                PreviewResult?.Invoke(new S2C_GameResult { WinnerRole = (byte)(args.PreviewResult == (byte)GameEndReason.AllResolved ? Role.Survivor : Role.Killer), Reason = args.PreviewResult.Value });
            }
            if (previewAt >= 0 && shots && !previewShot && t >= previewAt + 1)
            {
                previewShot = true; Shot("result-preview-" + ((GameEndReason)args.PreviewResult.Value).ToString().ToLowerInvariant() + ".png"); return;
            }
            // A run with a quit time ends early once everything it waits for has happened.
            bool done = doneAt >= 0 && t >= doneAt + 2;
            if (args.QuitAfter > 0 && !quitting && (t >= args.QuitAfter || done))
            {
                quitting = true; Debug.Log(done ? "[DEV] script complete, quitting" : "[DEV] quit after " + args.QuitAfter + " s");
                Application.Quit();
            }
        }

        // The local SelfState countdown (logged each second while the cooldown runs) and the SelfState event shots, each
        // taken once while the match runs: survivor-cooldown 1.5 s into a cooldown, survivor-rescue once the rescue bar is
        // at least RescueShotProgress (own rescue or as rescuer).
        public const float RescueShotProgress = .2f;
        private bool SelfShots(double t)
        {
            if (self == null || !self.Available || matchAt < 0 || resultAt >= 0) return false;
            double m = t - matchAt;
            if (self.SkillCooldown > 0)
            {
                if (cooldownSeenAt < 0) cooldownSeenAt = t;
                if (countdownLogAt < 0 || t - countdownLogAt >= 1)
                {
                    countdownLogAt = t;
                    Debug.Log("[DEV] t=" + Stamp(m) + " cooldown local=" + Stamp(self.SkillCooldown) + " (counting down between packets)");
                }
            }
            else if (cooldownSeenAt >= 0)
            {
                cooldownSeenAt = countdownLogAt = -1;
                Debug.Log("[DEV] t=" + Stamp(m) + " cooldown local=0.00 (ready)");
            }
            if (args.Shots == null || !located) return false;
            if (role == Role.Survivor && cooldownSeenAt >= 0 && t >= cooldownSeenAt + 1.5 && !taken.Contains("survivor-cooldown.png"))
            {
                EventShot("survivor-cooldown.png"); return true;
            }
            if (role == Role.Survivor && self.RescueProgress >= RescueShotProgress && !taken.Contains("survivor-rescue.png"))
            {
                Debug.Log("[DEV] t=" + Stamp(m) + " rescue progress=" + Stamp(self.RescueProgress) + " invulnerable=" + Stamp(self.InvulnerableLeft));
                EventShot("survivor-rescue.png"); return true;
            }
            return false;
        }

        // -ptk-preview-motion: motion-<clip>.png at MotionPreview.ShotTimes (Survivor, first match; the second Death shot is
        // motion-death-held.png); killer-down: motion-killer-down.png 2.4 s after the first confirmed capture (1.3 s walking away, then facing it), with an
        // instance in view (the captured Survivor knocked down / crawling, Killer view).
        private double captureSeenAt = -1;
        private int lastInView;
        private bool MotionShots(double t)
        {
            MotionPreviewClip clip = args.PreviewMotion;
            if (clip == MotionPreviewClip.None || args.Shots == null || match != 1 || matchAt < 0 || resultAt >= 0) return false;
            if (clip == MotionPreviewClip.KillerDown)
            {
                if (role != Role.Killer) return false;
                if (captureSeenAt >= 0 && t >= captureSeenAt + KillerDownShotSeconds && lastInView > 0 && !taken.Contains("motion-killer-down.png")) { EventShot("motion-killer-down.png", 0); return true; }
                return false;
            }
            if (role != Role.Survivor || !located) return false;
            double[] times = MotionPreview.ShotTimes(clip);
            for (int i = 0; i < times.Length; i++)
            {
                string name = "motion-" + MotionPreview.Name(clip) + (i == 0 ? string.Empty : "-held") + ".png";
                if (t >= matchAt + times[i] && !taken.Contains(name)) { EventShot(name, 0); return true; }
            }
            return false;
        }

        private void LogMoved(double m)
        {
            if (movedLogged++ == 0) movedFrom = position;
            float moved = Vector2.Distance(new Vector2(movedFrom.x, movedFrom.z), new Vector2(position.x, position.z));
            Debug.Log("[DEV] t=" + Stamp(m) + " local position " + Stamp(position.x) + "," + Stamp(position.z) + " moved=" + Stamp(moved) + " m");
        }

        // After the first result: wait for the lobby, press Back to lobby, then Start; hold W in the second match.
        private void Rematch(double t)
        {
            bool shots = args.Shots != null;
            if (match == 1 && resultAt >= 0 && reopenedAt >= 0 && backAt < 0 && t >= resultAt + 1.5 && (!shots || taken.Contains("result.png")))
            {
                bool pressed = screens != null && screens.PressBackToLobby();
                Debug.Log("[DEV] t=" + Stamp(t - matchAt) + " press back to lobby: " + (pressed ? "done" : "disabled"));
                backAt = t;
                if (pressed) EventShot("lobby-return.png"); else doneAt = t;
            }
            if (match == 1 && backAt >= 0 && !restarted && doneAt < 0 && t >= backAt + 1.5)
            {
                restarted = true;
                bool pressed = screens != null && screens.PressStart();
                Debug.Log("[DEV] t=" + Stamp(t - matchAt) + " press start: " + (pressed ? "done" : "disabled"));
                if (!pressed) doneAt = t;
            }
            if (match != 2) return;
            double m = t - matchAt;
            if (!position2 && m >= 2)
            {
                position2 = true; positionAt2 = position;
                Debug.Log("[DEV] match 2 t=" + Stamp(m) + " local position " + Stamp(position.x) + "," + Stamp(position.z) + " located=" + located);
            }
            if (shots && !taken.Contains("rematch-play.png") && m >= 4) { taken.Add("rematch-play.png"); Shot("rematch-play.png"); }
            if (!position5 && m >= 5)
            {
                position5 = true;
                float moved = Vector2.Distance(new Vector2(positionAt2.x, positionAt2.z), new Vector2(position.x, position.z));
                Debug.Log("[DEV] match 2 t=" + Stamp(m) + " local position " + Stamp(position.x) + "," + Stamp(position.z) + " moved=" + Stamp(moved) + " m");
            }
            if (doneAt < 0 && position5 && m >= 6) doneAt = t;
        }

        // Each event shot is taken once, one frame after the event.
        private void EventShot(string name, int delayFrames = 1, float delaySeconds = 0)
        {
            if (args.Shots == null || !taken.Add(name)) return;
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " event " + name); // match time, like the other [DEV] t= lines
            Shot(name, delayFrames, delaySeconds);
        }

        private void Shot(string name, int delayFrames = 0, float delaySeconds = 0)
        {
            pending.Enqueue(new KeyValuePair<string, int>(name, Time.frameCount + delayFrames));
            pendingAt.Enqueue(Time.unscaledTime + delaySeconds);
            if (!capturing) StartCoroutine(Capture());
        }

        // Synchronous end-of-frame capture, so each file holds exactly its frame before the script moves on.
        private IEnumerator Capture()
        {
            capturing = true;
            while (pending.Count > 0)
            {
                KeyValuePair<string, int> next = pending.Dequeue();
                float notBefore = pendingAt.Count > 0 ? pendingAt.Dequeue() : 0;
                while (Time.frameCount < next.Value || Time.unscaledTime < notBefore) yield return null;
                yield return new WaitForEndOfFrame();
                string path = Path.GetFullPath(Path.Combine(args.Shots, next.Key));
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                try { File.WriteAllBytes(path, frame.EncodeToPNG()); Debug.Log("[DEV] screenshot " + path); }
                catch (Exception error) { Debug.LogWarning("[DEV] screenshot failed: " + error.Message); }
                finally { Destroy(frame); }
            }
            capturing = false;
        }
    }
}
