using System;
using System.Collections.Generic;
using PingTheKiller.Client.Audio;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.Settings;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
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
        private RulesV2Audio v2Audio;
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
        private GameSettings options; // U7a player options (screens.Options): FOV, sensitivity, Invert Y, Reduced motion, volume
        private readonly ExitCache exitCache = new ExitCache(); // HUD_SPEC 13.4: one exit buffer per WorldState
        // HUD_SPEC 14 skill feedback: the pooled world effects and skill sounds (kept across matches), the Survivor-side
        // line, relay and refused-press state (the Killer side lives in KillerWorldLayer.Fx).
        private SkillFx skillFx;
        private readonly SurvivorFxState survivorFx = new SurvivorFxState();
        private readonly List<FxRecord> fxOut = new List<FxRecord>();
        private readonly HashSet<int> loggedFx = new HashSet<int>();
        private bool reduceArg;
        private FakeKiller fakeKiller; // dev only (-ptk-fake-killer): injected Killer entries for screenshots
        private Role role;
        private byte skillId;
        private PlayerStatus localStatus;
        private bool playing, releasedCursor;
        // HUD_SPEC 5 / 6.6 play menu, both roles: open state and the look / key gate.
        private readonly PlayMenuGate menuGate = new PlayMenuGate();
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
            // Play menu, both roles (HUD_SPEC 5, 6.6): Resume, or leave the match like the lobby's Leave.
            screens.Killer.Resume += () => SetMenu(false);
            screens.Killer.LeaveMatch += () => Disconnect(link != null && link.IsOffline ? string.Empty : ConnectionMessages.Left);
            screens.Survivor.Menu.Resume += () => SetMenu(false);
            screens.Survivor.Menu.LeaveMatch += () => Disconnect(link != null && link.IsOffline ? string.Empty : ConnectionMessages.Left);
            // HUD_SPEC 13.2: one C2S_SyncPress per check (SyncCheckState raises Press at most once per CheckId).
            screens.Survivor.Sync.State.Press += SendSyncPress;
            v2Audio = gameObject.AddComponent<RulesV2Audio>(); screens.Survivor.Sync.Resolved += v2Audio.OnSync;
            DevArgs devArgs = DevArgs.Parse(Environment.GetCommandLineArgs());
            var fxObject = new GameObject("Skill FX");
            fxObject.transform.SetParent(transform, false);
            skillFx = fxObject.AddComponent<SkillFx>();
            reduceArg = devArgs.ReduceStutter;
            stutter = gameObject.AddComponent<LaggingStutter>();
            stutter.Initialize(view, screens.StutterBack, screens.StutterFront, devArgs.ReduceStutter);
            options = screens.Options;
            rig.Options = options;
            options.Changed += ApplyOptions;
            ApplyOptions();
            if (devArgs.FakeKiller > 0) fakeKiller = new FakeKiller(devArgs.FakeKiller);
            if (devArgs.Active)
            {
                dev = gameObject.AddComponent<DevCapture>(); dev.Initialize(devArgs, screens, selfState);
                dev.Join += value => { screens.Address = value; Join(value); };
                dev.Offline += StartOffline;
                dev.HostSend += packet => { if (IsHost()) link.Send(packet); };
                dev.PreviewResult += Preview;
            }
            if (dev != null) dev.Reroll += () => Disconnect(string.Empty); // -ptk-want-skill: leave and start a new offline match
        }
        // FOV, sensitivity and Invert Y are read by the rig each frame; the rest is pushed here at start and on every change.
        private void ApplyOptions()
        {
            AudioListener.volume = options.Volume01;
            stutter.SetReducedOption(options.ReducedMotion);
            skillFx.Reduced = options.ReducedMotion || reduceArg; // HUD_SPEC 14.1 Reduced motion
        }
        private void OnApplicationQuit() => options?.Save();
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
                    // Bot 1..N with no gaps; once here, so the lobby list, NameOf and the result screen show the same names.
                    state = LobbyCopy.ContiguousBotNames(state);
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
                    playing = true; movement.ResetKeys(); interact.Reset(); releasedCursor = false; ResetMenu();
                    selfState.Reset(); facing.Reset(0); screens.Survivor.Sync.Reset();
                    capture.Reset(); exitCache.Reset(); threatVisual.Reset(); fakeKiller?.Reset(); stutter.SetActive(false);
                    skillFx.Warm(SkillFxArt.Load()); skillFx.Clear(); survivorFx.Reset(); rig.KillerFovOffset = 0;
                    var map = new GameObject("Match world"); map.transform.SetParent(transform, false);
                    world = map.AddComponent<WorldView>(); world.Initialize(playerId, role);
                    rig.Begin(role); screens.ShowPlay(role, skillId);
                    if (role == Role.Killer)
                    {
                        killerLayer = map.AddComponent<KillerWorldLayer>();
                        killerLayer.Initialize(playerId, skillId, screens.Killer, selfState, LinkClock.Now);
                    }
                    v2Audio.Begin(role, playerId);
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
                    v2Audio.OnWorld(state, received.Time);
                    foreach (PlayerState player in state.Players)
                    {
                        // The Killer finds its own entry by Role (Survivor entries may carry no usable Id); a Survivor by Id.
                        if (role == Role.Killer ? player.Role != (byte)Role.Killer : player.Id != playerId) continue;
                        localStatus = (PlayerStatus)player.Status;
                        if (role == Role.Killer) break;
                        // HUD_SPEC 13.2: a sync check only exists while Normal (the server cancels it otherwise).
                        if (localStatus != PlayerStatus.Normal) screens.Survivor.Sync.Clear();
                        selfState.CollapseRate = localStatus == PlayerStatus.Lagging ? GameConstants.CollapseLaggingRate : 1;
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
                    if (role == Role.Survivor)
                    {
                        CollectMates(state); OnOwnSnapshot(state.ThreatLevel, received.Time);
                        survivorFx.OnWorld(Frame(), Spectating, received.Time, fxOut);
                        foreach (FxRecord record in fxOut) PlayFx(record);
                        fxOut.Clear();
                    }
                    else threat.SetLevel(0);
                    dev?.OnWorld(state, playerId);
                    break;
                case S2C_SelfState self:
                    // Own-only (HUD_SPEC 11 Q5/Q6/N5). Only inside a match: Available starts with the first packet after MatchStart.
                    if (!playing) break;
                    selfState.Feed(self.SkillCooldown, self.RescueProgress, self.InvulnerableLeft, self.CollapseLeft, self.ActionProgress, self.ActionKind, received.Time);
                    v2Audio.OnSelf(self.ActionKind, self.ActionProgress, received.Time);
                    if (selfState.Received == 1) Debug.Log("[net] SelfState received (first of the match)");
                    dev?.OnSelfState(self, selfState.Received);
                    break;
                case S2C_SyncCheck check:
                    // Own-only (U6a R3). Ignored outside a match, for the Killer, and while not Normal.
                    if (!playing || role != Role.Survivor || localStatus != PlayerStatus.Normal) break;
                    bool accepted = screens.Survivor.Sync.OnCheck(check, received.Time);
                    dev?.OnSyncCheck(check, accepted);
                    break;
                case S2C_SyncResult sync:
                    if (!playing || role != Role.Survivor) break;
                    bool matched = screens.Survivor.Sync.OnResult(sync.CheckId, sync.Result, received.Time);
                    dev?.OnSyncResult(sync, matched);
                    break;
                case S2C_SkillEffect effect:
                    if (killerLayer != null) { killerLayer.OnEffect(effect, received.Time); PlayFx(killerLayer.LastEffect); }
                    if (role == Role.Survivor && playing)
                    {
                        effects.Accept(effect.CasterId, effect.SkillId, effect.Duration, received.Time, playerId, IsSurvivorEntry(effect.CasterId));
                        PlayFx(survivorFx.OnEffect(Frame(), Spectating, effect.CasterId, effect.SkillId, effect.X, effect.Z, effect.Duration, received.Time));
                    }
                    if (loggedEffects.Add(effect.SkillId)) Debug.Log("[net] SkillEffect skill=" + effect.SkillId + (role == Role.Killer ? " (killer HUD)" : " (survivor HUD)"));
                    break;
                case S2C_SkillRejected rejected:
                    // Own-only (Q3), on the main thread like the effects.
                    if (playing) OnSkillRejected(rejected, received.Time);
                    break;
                case S2C_GameResult result:
                    EndMatch(result, false);
                    break;
            }
        }
        private void EndMatch(S2C_GameResult result, bool preview)
        {
            bool wasPlaying = playing;
            playing = false; joinDeadline = 0; ResetMenu(); selfState.Reset(); screens.Survivor.Sync.Reset();
            rig.SetCursor(false); threat.SetLevel(0); stutter.SetActive(false); v2Audio.End();
            skillFx.Clear(); survivorFx.Reset(); rig.KillerFovOffset = 0;
            if (wasPlaying && link != null && link.IsConnected) link.Send(movement.Final(rig.Yaw));
            if (interact.TryRelease(out C2S_Interact release)) link?.Send(release);
            DestroyWorld();
            ResultV2View view = V2Wire.Result(result, lobby, playerId, role);
            if (!ResultV2Copy.Consistent(view)) Debug.LogWarning("[client] result winner does not match the majority rule for the listed rows");
            screens.ShowResult(view, preview);
            screens.SetResultLobby(false, link != null && link.IsOffline);
            if (!preview) dev?.OnResult(result, view);
        }
        // Dev only (-ptk-preview-result): a synthetic result handled locally, never sent to any server. Every lobby player
        // but the Killer is a starting Survivor: a Survivors win lists half of them (rounded up, the own Survivor first) as
        // escaped, a Killer win fewer than half.
        private void Preview(S2C_GameResult result)
        {
            if (result.EscapedIds.Count == 0 && result.EliminatedIds.Count == 0)
                PreviewRows(result, SurvivorIds(), result.WinnerRole == (byte)Role.Survivor, role == Role.Survivor ? playerId : -1);
            Debug.Log("[DEV] preview result reason=" + (GameEndReason)result.Reason + " escaped=" + result.EscapedIds.Count + " eliminated=" + result.EliminatedIds.Count + " (local only)");
            EndMatch(result, true);
        }
        // The starting Survivors this client can name: Survivor entries of the last snapshot (Survivor client), or every
        // other lobby player (Killer client: it never reads a Survivor entry's Id, HUD_SPEC 6.3).
        private List<int> SurvivorIds()
        {
            var ids = new List<int>();
            if (role == Role.Survivor && lastWorld != null)
                foreach (PlayerState player in lastWorld.Players) if (player.Role == (byte)Role.Survivor && !ids.Contains(player.Id)) ids.Add(player.Id);
            if (role == Role.Survivor && !ids.Contains(playerId)) ids.Insert(0, playerId);
            if (role == Role.Killer && lobby != null)
                foreach (Slot slot in lobby.Slots) if (slot.PlayerId != 0 && slot.PlayerId != playerId) ids.Add(slot.PlayerId);
            return ids;
        }
        public static void PreviewRows(S2C_GameResult result, List<int> survivors, bool survivorsWin, int ownId)
        {
            int escaped = survivorsWin ? ResultV2Copy.Needed(survivors.Count) : System.Math.Max(0, ResultV2Copy.Needed(survivors.Count) - 1);
            var order = new List<int>(survivors);
            if (order.Remove(ownId)) order.Insert(0, ownId);
            for (int i = 0; i < order.Count; i++) (i < escaped ? result.EscapedIds : result.EliminatedIds).Add(order[i]);
        }
        private void SendSyncPress(uint checkId)
        {
            if (!playing || role != Role.Survivor || link == null || !link.IsConnected) return;
            link.Send(new C2S_SyncPress { CheckId = checkId });
            Debug.Log("[net] SyncPress check=" + checkId + " needle=" + screens.Survivor.Sync.State.PressFraction.ToString("0.000"));
        }
        // HUD_SPEC 5 / 6.6, both roles: opening sends one zero MoveInput and the Interact release (when held) and frees the
        // cursor; closing locks it again (when focused). The match keeps running.
        private void SetMenu(bool open)
        {
            if (!playing || !menuGate.Set(open, Time.frameCount)) return;
            if (open && link != null && link.IsConnected) PlayMenuGate.Stop(movement, interact, rig.Yaw, packet => link.Send(packet));
            rig.SetCursor(!open);
            if (role == Role.Killer) screens.Killer.SetMenu(open); else screens.Survivor.SetMenu(open);
        }
        // Leave, disconnect, game result and the next match start: the menu is closed and hidden for both roles.
        private void ResetMenu()
        {
            menuGate.Reset();
            screens.Killer.SetMenu(false); screens.Survivor.SetMenu(false);
        }
        private void Update()
        {
            link?.Pump();
            v2Audio.Frame(LinkClock.Now, playing && world != null);
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
            // Both roles: Esc / Start backs out of Settings first (focus returns to the play menu), otherwise opens or closes
            // the play menu; gamepad B on the open menu is Resume.
            Gamepad gamepad = Gamepad.current;
            bool back = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (gamepad != null && gamepad.startButton.wasPressedThisFrame);
            bool cancel = gamepad != null && gamepad.buttonEast.wasPressedThisFrame && (screens.SettingsOpen || menuGate.Open);
            if (back || cancel)
            {
                if (screens.SettingsOpen) screens.CloseSettings();
                else SetMenu(!cancel && !menuGate.Open);
            }
            // After a focus loss a click captures the cursor again, but never under the open menu (it clicks its buttons).
            if (survivor && releasedCursor && !menuGate.Open && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                releasedCursor = false; rig.SetCursor(true);
            }
            bool active = menuGate.Active(Application.isFocused, releasedCursor);
            bool presses = menuGate.Presses(active, Time.frameCount); // press edges skip the frame a Submit closed the menu
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
            // Hold E (both roles: Survivors hack, hold a switch, take the hatch; the Killer kicks or closes the hatch): one
            // press edge and one release edge; losing focus, the cursor or the play menu counts as release. -ptk-dev-hack
            // holds it through the same path.
            bool interactHeld = (active && keyboard != null && keyboard.eKey.isPressed) || (dev != null && dev.HoldInteract);
            if (interact.TryCreate(interactHeld, out C2S_Interact use)) link.Send(use);
            // HUD_SPEC 13.2: Space or gamepad A presses the sync check; nothing else is bound to them. -ptk-auto-sync presses
            // through the same TryPress.
            if (survivor)
            {
                SyncCheckTracker sync = screens.Survivor.Sync;
                sync.Tick(now);
                Gamepad pad = Gamepad.current;
                bool syncKey = presses && ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame));
                if (syncKey || (dev != null && dev.PressSync(sync))) sync.TryPress();
                screens.Survivor.SetSync(pad != null && keyboard == null ? "A" : "Space");
            }
            bool pressed = (presses && keyboard != null && keyboard.qKey.wasPressedThisFrame) || (dev != null && dev.PressSkill(DevSkillAble())); // dev: -ptk-skill-at (-ptk-skill-when-able)
            bool sent = SkillInput.TryCreate(pressed, role, skillId, localStatus, out C2S_UseSkill skill);
            if (sent) link.Send(skill);
            if (pressed) OnSkillPress(sent, now);
            SkillFeedbackFrame(now, position);
            // TODO: client-side prediction for the local camera (it renders 100 ms in the past).
            if (located) rig.Place(position, dt);
            if (survivor) world.SetLocalLook(facingYaw, rig.Dither);
            world.Render(now);
            if (survivor)
            {
                stutter.SetActive(own.Status == PlayerStatus.Lagging);
                UpdateSurvivorHud(now);
                screens.Survivor.DrawTerminalFlags(lastWorld, rig.Pose);
                dev?.OnSurvivorFrame(threatVisual.Level, own.Status, capture.BannerVisible(now), stutter.Presented, world.KillerAlpha, screens.Survivor);
            }
            else if (killerLayer != null && located)
            {
                killerLayer.Render(rig.Pose, now, dt, menuGate.Open);
                dev?.OnKillerFrame(killerLayer.InView, killerLayer.Occluded, killerLayer.Edges, killerLayer.Tracked, killerLayer.Prompt, killerLayer.Captures, killerLayer.Overloaded);
            }
        }
        // Survivor client, each WorldState after the own entry was read: own-status transitions (captured sound + banner,
        // eliminated sound + reason), the threat visual (re-rolled on a level change, threat_scratch on entering level 2) and
        // the beep, both hidden while Eliminated or Escaped (HUD_SPEC 4.1, 4.2, 4.5).
        private void OnOwnSnapshot(byte threatLevel, double time)
        {
            StatusEvent change = capture.Observe(own.Status, own.StatusTimer, own.CaptureCount, time, selfState.Available && selfState.CollapseLeft >= 0);
            if (change == StatusEvent.Captured) { cueSource.PlayOneShot(survivorCues[SurvivorCue.Captured]); Debug.Log("[client] captured: banner + stutter"); }
            if (change == StatusEvent.Eliminated)
            {
                cueSource.PlayOneShot(survivorCues[SurvivorCue.Eliminated]);
                Debug.Log("[client] eliminated, reason=" + capture.Reason + " (\"" + capture.ReasonText() + "\")");
            }
            if (threatVisual.Update(threatLevel, own.Status) && threatVisual.Level == 2) cueSource.PlayOneShot(survivorCues[SurvivorCue.ThreatScratch]);
            threat.SetLevel(ThreatVisual.Effective(threatLevel, own.Status));
        }

        // HUD_SPEC 14.3: a press the client consumes itself shows its reason at once; only a press sent while the client
        // derived Ready arms the 0.5 s refused-press watch (never passive, not-Normal or cooldown presses).
        private void OnSkillPress(bool sent, double now)
        {
            float? cooldown = selfState.Available ? selfState.SkillCooldown : (float?)null;
            bool survivor = role == Role.Survivor;
            bool precondition = survivor ? skillId != SkillIds.EmergencyRestore || SurvivorReadout.RestoreHasTarget(own, mates)
                : skillId != SkillIds.FirewallBlock || V2Wire.SwitchInProgress(lastWorld);
            string reason = SkillFeedback.PressReason(role, skillId, localStatus, cooldown, precondition, out bool watch);
            if (survivor)
            {
                // A reason the prompt row already shows steadily (4.8 Disabled) is not repeated on the result line.
                if (reason != null && reason != screens.Survivor.PromptText) survivorFx.OnReason(reason, now);
                survivorFx.Press.OnPress(now, sent && watch);
            }
            else if (killerLayer != null)
            {
                killerLayer.Fx.OnReason(reason, now);
                killerLayer.Fx.Press.OnPress(now, sent && watch);
            }
            Debug.Log("[fx] skill press sent=" + sent + " watch=" + (sent && watch) + (reason != null ? " reason=\"" + reason + "\"" : string.Empty));
        }

        // Dev only (-ptk-skill-when-able): the same derived precondition OnSkillPress uses (Restore target, switch in progress).
        private bool DevSkillAble() => role == Role.Survivor ? skillId != SkillIds.EmergencyRestore || SurvivorReadout.RestoreHasTarget(own, mates)
            : skillId != SkillIds.FirewallBlock || V2Wire.SwitchInProgress(lastWorld);

        // HUD_SPEC 14.3 (Q3): the server's reason for refusing the own pending press replaces the 0.5 s inference.
        private void OnSkillRejected(S2C_SkillRejected rejected, double time)
        {
            var reason = (SkillRejectReason)rejected.Reason;
            string shown = null;
            if (role == Role.Survivor) shown = survivorFx.OnRejected(skillId, rejected.SkillId, reason, localStatus, screens.Survivor.PromptText, time);
            else if (killerLayer != null) shown = killerLayer.Fx.OnRejected(skillId, rejected.SkillId, reason, time);
            Debug.Log("[fx] skill rejected id=" + rejected.SkillId + " reason=" + reason + (shown != null ? " line=\"" + shown + "\"" : " (no line)"));
        }

        // Every playing frame: the refused-press inference, the result lines, the Recovering cue and the Overclock FOV.
        private void SkillFeedbackFrame(double now, Vector3 position)
        {
            float? cooldown = selfState.Available ? selfState.SkillCooldown : (float?)null;
            if (role == Role.Survivor)
            {
                if (survivorFx.Press.Tick(now, cooldown)) { survivorFx.OnReason(SkillFeedback.SurvivorRefused, now); Debug.Log("[fx] skill press refused (no effect, rejection or cooldown in 0.5 s)"); }
                screens.Survivor.SetResult(survivorFx.Line.Current(now));
                return;
            }
            if (killerLayer == null) return;
            KillerFxState fx = killerLayer.Fx;
            if (fx.Press.Tick(now, cooldown)) { fx.OnReason(SkillFeedback.KillerRefused, now); Debug.Log("[fx] skill press refused (no effect, rejection or cooldown in 0.5 s)"); }
            if (fx.TakeRecoverCue(now)) skillFx.Play(SkillFeedback.RecoverCue(), position);
            rig.KillerFovOffset = fx.FovOffset(now, skillFx.Reduced);
        }

        private bool Spectating => own.Status == PlayerStatus.Eliminated || own.Status == PlayerStatus.Escaped;

        private SurvivorFrame Frame() => new SurvivorFrame
        {
            OwnId = playerId, Own = own, Mates = mates, Names = NameOf,
            ActionKind = selfState.Available ? (byte)selfState.ActionKind : (byte)0
        };

        // Resolves the record's anchor (HUD_SPEC 14.0: never a Killer position on a Survivor client) and plays it.
        private void PlayFx(FxRecord record)
        {
            if (record.Viewer == FxViewer.None || world == null) return;
            // The own rendered position, for FxAnchor.Own only; SkillFx places everything else at the effect's point.
            if (!world.LocalPosition(LinkClock.Now, out Vector3 self)) self = new Vector3(own.X, 0, own.Z);
            self.y = 0;
            skillFx.Play(record, self);
            if (SkillSignature.IsOwnCast(record)) // U9 own-cast flash beside the skill block (HUD_SPEC 14.1b)
                (record.KillerSkill ? screens.Killer.CastFlash : screens.Survivor.CastFlash).Show(record.KillerSkill, record.SkillId, skillFx.Reduced, Time.unscaledTimeAsDouble);
            dev?.OnSkillFx(record);
            int key = (record.KillerSkill ? 100 : 0) + record.SkillId * 10 + (int)record.Viewer;
            if (loggedFx.Add(key))
                Debug.Log("[fx] " + (record.KillerSkill ? "killer" : "survivor") + " skill=" + record.SkillId + " viewer=" + record.Viewer
                    + " line=\"" + record.Line.Text + "\" visual=" + SkillSignature.Plan(record, skillFx.Reduced).Shape
                    + " sound=" + record.Sound.Clip + (record.Sound.Positional ? " (positional)" : string.Empty) + " reduced=" + skillFx.Reduced);
        }

        // Survivor client only (HUD_SPEC 0.1: Survivors receive every Survivor): the others, for rescue and co-hacking prompts.
        private void CollectMates(S2C_WorldState state)
        {
            mates.Clear();
            foreach (PlayerState mate in state.Players)
                if (mate.Role == (byte)Role.Survivor && mate.Id != playerId)
                    mates.Add(new Mate { Id = mate.Id, X = mate.X, Z = mate.Z, Status = (PlayerStatus)mate.Status, Hacking = (mate.Flags & (byte)PlayerFlags.Hacking) != 0,
                        Stealthed = (mate.Flags & (byte)PlayerFlags.Stealthed) != 0 });
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
        // HUD_SPEC 4.0-4.9 and 13, every frame from the last snapshot, the SelfState clock and the effect timing.
        private void UpdateSurvivorHud(double now)
        {
            if (lastWorld == null) return;
            SurvivorHud hud = screens.Survivor;
            bool restore = SurvivorReadout.RestoreHasTarget(own, mates);
            float? cooldown = selfState.Available ? selfState.SkillCooldown : (float?)null;
            hud.SetMeter(SurvivorReadout.Meter(own, selfState, effects.Scanned(now), effects.Overload(now), capture.ReasonText()));
            hud.SetThreat(threatVisual.Effect, threatVisual.StripFraction, threatVisual.Seed);
            hud.SetInfo(lastWorld.TimeLeft, HudCopy.CompletedTerminals(lastWorld.Terminals), lastWorld.TerminalsRequired);
            ExitView[] exits = exitCache.For(lastWorld, effects.Locked(now) ?? -1);
            hud.SetObjective(exits, V2Wire.Collapse(lastWorld.CollapseLeft, selfState.Available ? selfState.CollapseLeft : -1, own.Status == PlayerStatus.Lagging));
            hud.SetSkill(skillId, SurvivorReadout.SkillState(skillId, own.Status, cooldown, restore), cooldown, effects.OwnActive(now), skillId == SkillIds.TrafficOverload);
            PromptState prompt = capture.BannerVisible(now) ? capture.Prompt
                : SurvivorReadout.Prompt(own, mates, lastWorld.Terminals, selfState, NameOf, releasedCursor, skillId, restore);
            // HUD_SPEC 13.3: the exit switch and hatch prompts, when nothing more urgent (or only a muted skill reason) shows.
            if (!releasedCursor && own.Status == PlayerStatus.Normal && !capture.BannerVisible(now) && (string.IsNullOrEmpty(prompt.Text) || prompt.Muted))
            {
                InteractPrompt reach = InteractCopy.Survivor(own.X, own.Z, exits, SharedMap.Map.ExitSwitches, lastWorld.HatchVisible == 1, lastWorld.HatchX, lastWorld.HatchZ);
                if (!string.IsNullOrEmpty(reach.Text)) prompt = new PromptState { Text = reach.Text, Muted = reach.Muted };
            }
            hud.SetPrompt(prompt, V2Wire.Hold(selfState));
            hud.SetMenuNote(PlayMenuCopy.CaptureNote(own.Status, own.CaptureCount)); // HUD_SPEC 5 line 3
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!playing) return;
            releasedCursor = !focused;
            rig?.SetCursor(focused && !menuGate.Open);
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
            playing = false; joinDeadline = 0; lobby = null; playerId = 0; lastWorld = null; ResetMenu(); selfState.Reset(); screens.Survivor.Sync.Reset();
            threat.SetLevel(0); rig.SetCursor(false); stutter?.SetActive(false);
            skillFx?.Clear(); survivorFx.Reset(); if (rig != null) rig.KillerFovOffset = 0;
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
