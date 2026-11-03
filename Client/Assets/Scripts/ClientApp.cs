using System;
using System.Collections.Generic;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;

namespace PingTheKiller.Client
{
    // Wires the link, the screens, the camera rigs (RoleCamera: SurvivorOrbitRig / KillerViewRig), the map view and the
    // Killer world layer together. Game rules live on the server; the pieces own their own presentation.
    public sealed class ClientApp : MonoBehaviour
    {
        private IGameLink link;
        private string endpointText = string.Empty;
        private ClientScreens screens;
        private Camera view;
        private RoleCamera rig;
        private WorldView world;
        private KillerWorldLayer killerLayer;
        private ThreatAudio threat;
        private DevCapture dev;
        private int playerId, botCount;
        private MovementInput movement = new MovementInput();
        private readonly InteractInput interact = new InteractInput();
        private readonly CharacterFacing facing = new CharacterFacing();
        private readonly SelfStateHolder selfState = new SelfStateHolder();
        private readonly HashSet<byte> loggedEffects = new HashSet<byte>();
        // Survivor HUD inputs (HUD_SPEC 4): the own entry, the other Survivors, Survivor-bound effect timing.
        private OwnEntry own;
        private readonly List<Mate> mates = new List<Mate>();
        private readonly SurvivorEffects effects = new SurvivorEffects();
        private AudioSource cueSource;
        private AudioClip readyClip;
        // U4e Survivor feedback: threat visuals (HUD_SPEC 4.2), capture banner, elimination reason, cues and the stutter (4.5).
        private readonly ThreatVisual threatVisual = new ThreatVisual();
        private readonly CaptureWatch capture = new CaptureWatch();
        private readonly Dictionary<SurvivorCue, AudioClip> survivorCues = new Dictionary<SurvivorCue, AudioClip>();
        private LaggingStutter stutter;
        private FakeKiller fakeKiller; // dev only (-ptk-fake-killer): injected Killer entries for screenshots
        private Role role;
        private byte skillId;
        private PlayerStatus localStatus;
        private bool playing, exitOpen, releasedCursor, menuOpen;
        private Vector3? hackingTerminal;
        private double joinDeadline;
        private S2C_LobbyState lobby;
        private S2C_WorldState lastWorld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<ClientApp>() == null) new GameObject("Ping the Killer").AddComponent<ClientApp>();
        }
        private void Awake()
        {
            Application.runInBackground = true;
            var cameraObject = new GameObject("Role camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" }; // CharacterViews reads Camera.main
            cameraObject.transform.SetParent(transform, false);
            view = cameraObject.GetComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color32(16, 17, 19, 255);
            view.GetUniversalAdditionalCameraData();
            rig = new RoleCamera(view);
            SceneLighting.Apply(transform);
            if (EventSystem.current == null)
            {
                var events = new GameObject("UI input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            // Survivor skill_ready (HUD_SPEC 4.8): the same soft tick the Killer hears (6.5), 2D, one shot per cooldown.
            cueSource = gameObject.AddComponent<AudioSource>();
            cueSource.playOnAwake = false; cueSource.spatialBlend = 0;
            float[] tick = KillerCueSynth.Build(KillerCue.SkillReady, KillerAudio.Rate);
            readyClip = AudioClip.Create("cue skill_ready", tick.Length, 1, KillerAudio.Rate, false);
            readyClip.SetData(tick, 0);
            foreach (SurvivorCue cue in new[] { SurvivorCue.Captured, SurvivorCue.Eliminated, SurvivorCue.ThreatScratch })
            {
                float[] samples = SurvivorCueSynth.Build(cue, KillerAudio.Rate);
                AudioClip clip = AudioClip.Create("cue " + cue, samples.Length, 1, KillerAudio.Rate, false);
                clip.SetData(samples, 0);
                survivorCues[cue] = clip;
            }
            screens = gameObject.AddComponent<ClientScreens>();
            threat = gameObject.AddComponent<ThreatAudio>();
            screens.Join += Join;
            screens.Offline += StartOffline;
            screens.Leave += () => Disconnect(link != null && link.IsOffline ? string.Empty : ConnectionMessages.Left);
            screens.ChangeBots += delta => { if (IsHost()) link.Send(new C2S_SetBots { Count = (byte)Mathf.Clamp(botCount + delta, 0, 7) }); };
            screens.ChangeRole += choice => { if (IsHost()) link.Send(new C2S_SetRole { RoleChoice = choice }); };
            screens.StartMatch += () => { if (IsHost()) link.Send(new C2S_StartMatch()); };
            screens.BackToLobby += () => { if (lobby != null && link != null && link.IsConnected) screens.ShowLobby(lobby, playerId); };
            // Killer play menu (HUD_SPEC 6.6): Resume, or leave the match like the lobby's Leave.
            screens.Killer.Resume += () => SetMenu(false);
            screens.Killer.LeaveMatch += () => Disconnect(link != null && link.IsOffline ? string.Empty : ConnectionMessages.Left);
            DevArgs devArgs = DevArgs.Parse(Environment.GetCommandLineArgs());
            stutter = gameObject.AddComponent<LaggingStutter>();
            stutter.Initialize(view, screens.StutterBack, screens.StutterFront, devArgs.ReduceStutter);
            if (devArgs.FakeKiller > 0) fakeKiller = new FakeKiller(devArgs.FakeKiller);
            if (devArgs.Active)
            {
                dev = gameObject.AddComponent<DevCapture>(); dev.Initialize(devArgs, screens, selfState);
                dev.Join += value => { screens.Address = value; Join(value); };
                dev.Offline += StartOffline;
                dev.HostSend += packet => { if (IsHost()) link.Send(packet); };
                dev.PreviewResult += Preview;
            }
        }
        private bool IsHost()
        {
            if (lobby == null || link == null || !link.IsConnected || playing) return false;
            foreach (Slot slot in lobby.Slots) if (slot.PlayerId == playerId && slot.IsHost != 0) return true;
            return false;
        }
        private void Join(string value)
        {
            if (!EndpointParser.TryParse(value, out var endpoint, out string error)) { screens.Error(error); return; }
            var client = new NetClient();
            Attach(client, endpoint.ToString());
            screens.SetConnecting(true);
            joinDeadline = LinkClock.Now + 10;
            client.Connect(endpoint);
        }
        // Offline bot mode: the in-process server through InMemoryTransport. No NetClient or socket on this path.
        private void StartOffline()
        {
            OfflineLink offline;
            try { offline = new OfflineLink(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                screens.Error("Offline mode could not start. Restart the game and try Offline again.");
                return;
            }
            offline.ServerLog += line => Debug.Log("[offline server] " + line);
            Attach(offline, "this PC");
            joinDeadline = LinkClock.Now + 5;
        }
        private void Attach(IGameLink next, string endpoint)
        {
            Release();
            link = next; endpointText = endpoint;
            movement = new MovementInput(); // Seq lives as long as the link.
            interact.Reset();
            selfState.Reset();
            link.PacketReceived += Receive;
            link.Disconnected += Disconnect;
        }
        private void Receive(ReceivedPacket received)
        {
            switch (received.Packet)
            {
                case S2C_JoinResult joined:
                    if (joined.Result != 0)
                    {
                        Disconnect(link != null && link.IsOffline ? ConnectionMessages.OfflineStopped : ConnectionMessages.Rejected(joined.Result, endpointText));
                        return;
                    }
                    playerId = joined.PlayerId;
                    break;
                case S2C_LobbyState state:
                    joinDeadline = 0;
                    lobby = state; botCount = 0;
                    foreach (Slot slot in state.Slots) if (slot.PlayerId != 0 && slot.IsBot != 0) botCount++;
                    // After a GameResult the first LobbyState means the server is back in the lobby.
                    if (screens.InResult) screens.SetResultLobby(true, state.IsOffline != 0);
                    else if (!playing) screens.ShowLobby(state, playerId);
                    dev?.OnLobby(state, IsHost());
                    break;
                case S2C_MatchStart match:
                    joinDeadline = 0;
                    DestroyWorld();
                    role = (Role)match.Role; skillId = match.SkillId; localStatus = PlayerStatus.Normal; lastWorld = null;
                    hackingTerminal = null; own = default; mates.Clear(); effects.Reset();
                    playing = true; movement.ResetKeys(); interact.Reset(); releasedCursor = false; menuOpen = false;
                    selfState.Reset(); facing.Reset(0);
                    capture.Reset(); threatVisual.Reset(); fakeKiller?.Reset(); stutter.SetActive(false);
                    var map = new GameObject("Match world"); map.transform.SetParent(transform, false);
                    world = map.AddComponent<WorldView>(); world.Initialize(playerId, role);
                    rig.Begin(role); screens.ShowPlay(role, skillId);
                    if (role == Role.Killer)
                    {
                        killerLayer = map.AddComponent<KillerWorldLayer>();
                        killerLayer.Initialize(playerId, skillId, screens.Killer, selfState, LinkClock.Now);
                    }
                    dev?.OnMatchStart(role, skillId);
                    break;
                case S2C_WorldState state:
                    if (!playing || world == null) break;
                    if (fakeKiller != null && role == Role.Survivor)
                    {
                        byte implied = fakeKiller.Inject(state.Players, playerId, received.Time); // dev screenshots only
                        if (implied > state.ThreatLevel) state.ThreatLevel = implied;
                    }
                    world.Accept(state, received.Time);
                    killerLayer?.Accept(state, received.Time);
                    lastWorld = state;
                    exitOpen = WorldExits.AnyOpen(state);
                    foreach (PlayerState player in state.Players)
                    {
                        // The Killer finds its own entry by Role (Survivor entries may carry no usable Id); a Survivor by Id.
                        if (role == Role.Killer ? player.Role != (byte)Role.Killer : player.Id != playerId) continue;
                        localStatus = (PlayerStatus)player.Status;
                        if (role == Role.Killer) break;
                        byte flags = player.Flags;
                        own = new OwnEntry
                        {
                            Status = localStatus, X = player.X, Z = player.Z, StatusTimer = player.StatusTimer, CaptureCount = player.CaptureCount,
                            Signal = (flags & (byte)PlayerFlags.Signal) != 0, Hacking = (flags & (byte)PlayerFlags.Hacking) != 0,
                            Rescuing = (flags & (byte)PlayerFlags.Rescuing) != 0, Stealthed = (flags & (byte)PlayerFlags.Stealthed) != 0
                        };
                        hackingTerminal = null;
                        if (own.Hacking)
                        {
                            int nearest = HudCopy.NearestTerminal(state.Terminals, player.X, player.Z);
                            if (nearest >= 0) hackingTerminal = new Vector3(state.Terminals[nearest].X, 0, state.Terminals[nearest].Z);
                        }
                    }
                    if (role == Role.Survivor) { CollectMates(state); OnOwnSnapshot(state.ThreatLevel, received.Time); }
                    else threat.SetLevel(0);
                    dev?.OnWorld(state, playerId);
                    break;
                case S2C_SelfState self:
                    // Own-only (HUD_SPEC 11 Q5/Q6/N5). Only inside a match: Available starts with the first packet after MatchStart.
                    if (!playing) break;
                    selfState.Feed(self.SkillCooldown, self.RescueProgress, self.InvulnerableLeft, self.ExecutionGraceLeft, received.Time);
                    killerLayer?.OnSelfState();
                    if (selfState.Received == 1) Debug.Log("[net] SelfState received (first of the match)");
                    dev?.OnSelfState(self.SkillCooldown, self.RescueProgress, self.InvulnerableLeft, self.ExecutionGraceLeft, selfState.Received);
                    break;
                case S2C_SkillEffect effect:
                    killerLayer?.OnEffect(effect, received.Time);
                    if (role == Role.Survivor && playing)
                        effects.Accept(effect.CasterId, effect.SkillId, effect.Duration, received.Time, playerId, IsSurvivorEntry(effect.CasterId));
                    if (loggedEffects.Add(effect.SkillId)) Debug.Log("[net] SkillEffect skill=" + effect.SkillId + (role == Role.Killer ? " (killer HUD)" : " (survivor HUD)"));
                    break;
                case S2C_GameResult result:
                    EndMatch(result, false);
                    break;
            }
        }
        private void EndMatch(S2C_GameResult result, bool preview)
        {
            bool wasPlaying = playing;
            playing = false; joinDeadline = 0; menuOpen = false; selfState.Reset();
            rig.SetCursor(false); threat.SetLevel(0); stutter.SetActive(false);
            if (wasPlaying && link != null && link.IsConnected) link.Send(movement.Final(rig.Yaw));
            if (interact.TryRelease(out C2S_Interact release)) link?.Send(release);
            DestroyWorld();
            string headline = ResultCopy.Headline(result, lobby);
            string outcome = ResultCopy.Outcome(result, playerId, role, localStatus, role == Role.Survivor ? capture.ReasonText() : null);
            screens.ShowResult(headline, outcome, preview);
            screens.SetResultLobby(false, link != null && link.IsOffline);
            if (!preview) dev?.OnResult(result, headline, outcome);
        }
        // Dev only (-ptk-preview-result): a synthetic result handled locally, never sent to any server.
        private void Preview(S2C_GameResult result)
        {
            if (result.WinnerRole == (byte)Role.Survivor && result.EscapedIds.Count == 0)
            {
                if (role == Role.Survivor) result.EscapedIds.Add(playerId);
                // The Killer client never reads a Survivor entry's Id (HUD_SPEC 6.3), so the preview names lobby slots instead.
                if (role == Role.Survivor && lastWorld != null)
                    foreach (PlayerState player in lastWorld.Players)
                        if (player.Role == (byte)Role.Survivor && player.Id != playerId && result.EscapedIds.Count < 2) result.EscapedIds.Add(player.Id);
                if (role == Role.Killer && lobby != null)
                    foreach (Slot slot in lobby.Slots)
                        if (slot.PlayerId != 0 && slot.PlayerId != playerId && result.EscapedIds.Count < 2) result.EscapedIds.Add(slot.PlayerId);
            }
            Debug.Log("[DEV] preview result reason=" + (GameEndReason)result.Reason + " (local only)");
            EndMatch(result, true);
        }
        // HUD_SPEC 6.6: the Killer play menu sends one zero MoveInput, releases the cursor, and the match keeps running.
        private void SetMenu(bool open)
        {
            if (menuOpen == open || !playing || role != Role.Killer) return;
            menuOpen = open;
            if (open && link != null && link.IsConnected) link.Send(movement.Final(rig.Yaw));
            rig.SetCursor(!open);
            screens.Killer.SetMenu(open);
        }
        private void Update()
        {
            link?.Pump();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) screens.DevVisible = !screens.DevVisible;
            if (screens.DevVisible)
                screens.SetDev(link == null ? "Not connected" : link.IsOffline ? "Offline" : link.RttMilliseconds < 0 ? "RTT: waiting" : "RTT: " + Math.Round(link.RttMilliseconds) + " ms");
            if (joinDeadline != 0 && LinkClock.Now > joinDeadline)
            {
                Disconnect(link != null && link.IsOffline ? ConnectionMessages.OfflineStopped : ConnectionMessages.LobbyTimeout);
                return;
            }
            if (!playing || world == null || link == null) return;
            bool survivor = role == Role.Survivor;
            double now = LinkClock.Now;
            float dt = Time.unscaledDeltaTime;
            // SelfState timers count down by real time between packets; the ready edge plays skill_ready once per cooldown.
            selfState.Advance(now);
            if (selfState.TakeSkillReady())
            {
                // A Survivor hears skill_ready only while it can use the skill: not Lagging, spectating or Escaped (U4d F-03).
                bool audible = !survivor || CaptureWatch.SkillReadyAudible(own.Status);
                if (survivor && audible) cueSource.PlayOneShot(readyClip); else if (!survivor) killerLayer?.OnSkillReady();
                Debug.Log("[net] skill_ready (own cooldown reached 0)" + (audible ? string.Empty : ", silent: own status " + own.Status));
            }
            // Survivor: Esc releases the cursor (click resumes). Killer: Esc opens or closes the play menu.
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (survivor) { releasedCursor = true; rig.SetCursor(false); }
                else SetMenu(!menuOpen);
            }
            if (survivor && releasedCursor && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                releasedCursor = false; rig.SetCursor(true);
            }
            bool active = Application.isFocused && !releasedCursor && !menuOpen;
            rig.Look(active);
            bool located = world.LocalPosition(now, out Vector3 position);
            if (dev != null) dev.Track(located, position);
            if (dev != null && survivor)
            {
                int ally = SurvivorReadout.NearestLagging(own, mates, float.MaxValue, out _);
                dev.RescueTarget(ally >= 0, ally >= 0 ? new Vector3(mates[ally].X, 0, mates[ally].Z) : default);
            }
            if (dev != null && dev.MenuChange(out bool devMenu)) SetMenu(devMenu);
            // Dev seams (wander, -ptk-orbit, -ptk-killer-look) only return a yaw; the one Turn below applies it.
            Vector3 aim = default;
            bool aimFound = !survivor && killerLayer != null && killerLayer.NearestBlip(position, dev != null && dev.AimOccluded, out aim);
            float steered = rig.Yaw;
            bool turned = dev != null && (survivor ? dev.Steer(rig.Yaw, dt, out steered) || dev.Orbit(facing.Yaw, out steered) : dev.Aim(aimFound, position, aim, out steered));
            if (turned) rig.Turn(steered);
            // Opposing keys still count as held. MovementInput owns the 30 Hz rate and the release zeros.
            bool keys = active && keyboard != null && (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed);
            bool scripted = dev != null && dev.HoldForward; // dev capture: scripted W through the same 30 Hz path
            bool held = keys || scripted;
            float right = keys ? (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0) : 0;
            float forward = scripted ? 1 : keys ? (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0) : 0;
            // Survivor: keys move relative to the camera yaw; the character turns toward the move (720 deg/s) and its
            // facing is what MoveInput.Yaw carries. Killer: both are the view yaw.
            Vector3 move = held ? ClientMath.InputDirection(right, forward, rig.Yaw) : Vector3.zero;
            float facingYaw = survivor ? facing.Update(move, dt, position, hackingTerminal) : rig.Yaw;
            if (movement.TryCreate(held, right, forward, rig.Yaw, facingYaw, dt, out C2S_MoveInput input)) link.Send(input);
            // Hold E (survivors): one press edge and one release edge; losing focus or the cursor counts as release.
            if (survivor && interact.TryCreate(active && keyboard != null && keyboard.eKey.isPressed, out C2S_Interact use)) link.Send(use);
            bool pressed = (active && keyboard != null && keyboard.qKey.wasPressedThisFrame) || (dev != null && dev.PressSkill()); // dev: -ptk-skill-at
            if (SkillInput.TryCreate(pressed, role, skillId, localStatus, out C2S_UseSkill skill))
                link.Send(skill);
            // TODO: client-side prediction for the local camera (it renders 100 ms in the past).
            if (located) rig.Place(position, dt);
            if (survivor) world.SetLocalLook(facingYaw, rig.Dither);
            world.Render(now);
            if (survivor)
            {
                stutter.SetActive(own.Status == PlayerStatus.Lagging);
                UpdateSurvivorHud(now);
                screens.SetExit(view, SharedMap.Exit + Vector3.up * 2.6f, exitOpen);
                dev?.OnSurvivorFrame(threatVisual.Level, own.Status, capture.BannerVisible(now), stutter.Presented, world.KillerAlpha);
            }
            else if (killerLayer != null && located)
            {
                killerLayer.Render(rig.Pose, now, dt, menuOpen);
                dev?.OnKillerFrame(killerLayer.InView, killerLayer.Occluded, killerLayer.Edges, killerLayer.Tracked, killerLayer.Prompt);
            }
        }
        // Survivor client, each WorldState after the own entry was read: own-status transitions (captured sound + banner,
        // eliminated sound + reason), the threat visual (re-rolled on a level change, threat_scratch on entering level 2) and
        // the beep, both hidden while Eliminated or Escaped (HUD_SPEC 4.1, 4.2, 4.5).
        private void OnOwnSnapshot(byte threatLevel, double time)
        {
            StatusEvent change = capture.Observe(own.Status, own.StatusTimer, own.CaptureCount, time);
            if (change == StatusEvent.Captured) { cueSource.PlayOneShot(survivorCues[SurvivorCue.Captured]); Debug.Log("[client] captured: banner + stutter"); }
            if (change == StatusEvent.Eliminated)
            {
                cueSource.PlayOneShot(survivorCues[SurvivorCue.Eliminated]);
                Debug.Log("[client] eliminated, reason=" + capture.Reason + " (\"" + capture.ReasonText() + "\")");
            }
            if (threatVisual.Update(threatLevel, own.Status) && threatVisual.Level == 2) cueSource.PlayOneShot(survivorCues[SurvivorCue.ThreatScratch]);
            threat.SetLevel(ThreatVisual.Effective(threatLevel, own.Status));
        }

        // Survivor client only (HUD_SPEC 0.1: Survivors receive every Survivor): the others, for rescue and co-hacking prompts.
        private void CollectMates(S2C_WorldState state)
        {
            mates.Clear();
            foreach (PlayerState mate in state.Players)
                if (mate.Role == (byte)Role.Survivor && mate.Id != playerId)
                    mates.Add(new Mate { Id = mate.Id, X = mate.X, Z = mate.Z, Status = (PlayerStatus)mate.Status, Hacking = (mate.Flags & (byte)PlayerFlags.Hacking) != 0 });
        }
        // Survivor client only: whether an effect's caster is a Survivor of the last snapshot (skill ids are role-local).
        private bool IsSurvivorEntry(int casterId)
        {
            if (casterId == playerId) return true;
            foreach (Mate mate in mates) if (mate.Id == casterId) return true;
            return false;
        }
        // Lobby nickname for a player id (prompts ellipsize it).
        private string NameOf(int id)
        {
            if (lobby != null) foreach (Slot slot in lobby.Slots) if (slot.PlayerId == id && slot.PlayerId != 0) return slot.Nickname;
            return null;
        }
        // HUD_SPEC 4.0-4.9, every frame from the last snapshot, the SelfState clock and the effect timing.
        private void UpdateSurvivorHud(double now)
        {
            if (lastWorld == null) return;
            SurvivorHud hud = screens.Survivor;
            bool restore = SurvivorReadout.RestoreHasTarget(own, mates);
            float? cooldown = selfState.Available ? selfState.SkillCooldown : (float?)null;
            hud.SetMeter(SurvivorReadout.Meter(own, selfState, effects.Scanned(now), effects.Overload(now), capture.ReasonText()));
            hud.SetThreat(threatVisual.Effect, threatVisual.StripFraction, threatVisual.Seed);
            hud.SetInfo(lastWorld.TimeLeft, HudCopy.CompletedTerminals(lastWorld.Terminals), lastWorld.TerminalsRequired,
                SurvivorReadout.Exit(lastWorld), effects.Locked(now));
            hud.SetSkill(skillId, SurvivorReadout.SkillState(skillId, own.Status, cooldown, restore), cooldown, effects.OwnActive(now), skillId == SkillIds.TrafficOverload);
            hud.SetPrompt(capture.BannerVisible(now) ? CaptureWatch.BannerPrompt
                : SurvivorReadout.Prompt(own, mates, lastWorld.Terminals, selfState, NameOf, releasedCursor, skillId, restore));
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!playing) return;
            releasedCursor = !focused;
            rig?.SetCursor(focused && !menuOpen);
            // Same-frame release zero; Update keeps calling MovementInput, which sends the backup zero.
            if (!focused && movement.TryCreate(false, 0, 0, rig.Yaw, 0, out C2S_MoveInput input)) link?.Send(input);
            if (!focused && interact.TryRelease(out C2S_Interact release)) link?.Send(release);
        }
        private void DestroyWorld()
        {
            killerLayer = null;
            if (world == null) return;
            world.gameObject.SetActive(false); Destroy(world.gameObject); world = null;
        }
        // Detaches and disposes the current link (sending the E release first while it can still send).
        private void Release()
        {
            IGameLink previous = link; link = null;
            if (previous == null) return;
            if (interact.TryRelease(out C2S_Interact release)) previous.Send(release);
            previous.PacketReceived -= Receive; previous.Disconnected -= Disconnect;
            if (previous is NetClient client && client.FailureDetail != null) Debug.Log("[net] " + endpointText + ": " + client.FailureDetail);
            previous.Dispose();
        }
        private void Disconnect(string notice)
        {
            Release();
            playing = false; joinDeadline = 0; lobby = null; playerId = 0; lastWorld = null; menuOpen = false; selfState.Reset();
            threat.SetLevel(0); rig.SetCursor(false); stutter?.SetActive(false);
            DestroyWorld();
            screens.ShowMenu(notice);
        }
        private void OnDestroy()
        {
            link?.Dispose(); if (readyClip != null) Destroy(readyClip);
            foreach (AudioClip clip in survivorCues.Values) if (clip != null) Destroy(clip);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
    }
}
