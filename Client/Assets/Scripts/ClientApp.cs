using System;
using System.Collections.Generic;
using PingTheKiller.Client.Dev;
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
    public sealed class ClientApp : MonoBehaviour
    {
        private IGameLink link;
        private string endpointText = string.Empty;
        private ClientScreens screens;
        private Camera view;
        private RoleCamera rig;
        private WorldView world;
        private ThreatAudio threat;
        private DevCapture dev;
        private int playerId, botCount;
        private MovementInput movement = new MovementInput();
        private readonly InteractInput interact = new InteractInput();
        private readonly HashSet<byte> loggedEffects = new HashSet<byte>();
        private Role role;
        private byte skillId;
        private PlayerStatus localStatus;
        private bool playing, exitOpen, releasedCursor;
        private double joinDeadline;
        private S2C_LobbyState lobby;
        private S2C_WorldState lastWorld;
        private string statusLine, teamLine, hackingLine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<ClientApp>() == null) new GameObject("Ping the Killer").AddComponent<ClientApp>();
        }
        private void Awake()
        {
            Application.runInBackground = true;
            var cameraObject = new GameObject("Role camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            view = cameraObject.GetComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color32(16, 17, 19, 255);
            view.GetUniversalAdditionalCameraData();
            rig = new RoleCamera(view);
            var lightObject = new GameObject("Map light", typeof(Light));
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(55, -30, 0);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
            RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
            if (EventSystem.current == null)
            {
                var events = new GameObject("UI input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
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
            DevArgs devArgs = DevArgs.Parse(Environment.GetCommandLineArgs());
            if (devArgs.Active)
            {
                dev = gameObject.AddComponent<DevCapture>(); dev.Initialize(devArgs);
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
                    statusLine = teamLine = hackingLine = null;
                    playing = true; movement.ResetKeys(); interact.Reset(); releasedCursor = false;
                    var map = new GameObject("Match world"); map.transform.SetParent(transform, false);
                    world = map.AddComponent<WorldView>(); world.Initialize(playerId, role);
                    rig.Begin(role); screens.ShowPlay(role, HudCopy.SkillLine(role, skillId)); screens.SetSignal(false);
                    rig.Place(Vector3.zero);
                    dev?.OnMatchStart();
                    break;
                case S2C_WorldState state:
                    if (!playing || world == null) break;
                    world.Accept(state, received.Time);
                    lastWorld = state;
                    exitOpen = state.ExitOpen != 0 && state.ExitLocked == 0;
                    threat.SetLevel(role == Role.Survivor ? state.ThreatLevel : (byte)0);
                    foreach (PlayerState player in state.Players)
                    {
                        if (player.Id != playerId) continue;
                        localStatus = (PlayerStatus)player.Status;
                        screens.SetSignal((player.Flags & (byte)PlayerFlags.Signal) != 0);
                        statusLine = HudCopy.StatusLine(localStatus, player.StatusTimer);
                        hackingLine = null;
                        if ((player.Flags & (byte)PlayerFlags.Hacking) != 0)
                        {
                            int nearest = HudCopy.NearestTerminal(state.Terminals, player.X, player.Z);
                            if (nearest >= 0) hackingLine = HudCopy.HackingLine(state.Terminals[nearest].Progress);
                        }
                    }
                    teamLine = HudCopy.TerminalsLine(state.Terminals, state.TerminalsRequired, exitOpen);
                    break;
                case S2C_SkillEffect effect:
                    // No visuals yet; accepted and ignored, logged once per skill.
                    if (loggedEffects.Add(effect.SkillId)) Debug.Log("[net] SkillEffect skill=" + effect.SkillId + " ignored (no visuals yet)");
                    break;
                case S2C_GameResult result:
                    EndMatch(result, false);
                    break;
            }
        }
        private void EndMatch(S2C_GameResult result, bool preview)
        {
            bool wasPlaying = playing;
            playing = false; joinDeadline = 0;
            rig.SetCursor(false); threat.SetLevel(0);
            if (wasPlaying && link != null && link.IsConnected) link.Send(movement.Final(rig.Yaw));
            if (interact.TryRelease(out C2S_Interact release)) link?.Send(release);
            DestroyWorld();
            screens.ShowResult(ResultCopy.Headline(result, lobby), ResultCopy.Outcome(result, playerId, role, localStatus), preview);
            screens.SetResultLobby(false, link != null && link.IsOffline);
        }
        // Dev only (-ptk-preview-result): a synthetic result handled locally, never sent to any server.
        private void Preview(S2C_GameResult result)
        {
            if (result.Reason == (byte)GameEndReason.SurvivorEscaped && result.EscapedIds.Count == 0)
            {
                if (role == Role.Survivor) result.EscapedIds.Add(playerId);
                if (lastWorld != null)
                    foreach (PlayerState player in lastWorld.Players)
                        if (player.Role == (byte)Role.Survivor && player.Id != playerId && result.EscapedIds.Count < 2) result.EscapedIds.Add(player.Id);
            }
            Debug.Log("[DEV] preview result reason=" + (GameEndReason)result.Reason + " (local only)");
            EndMatch(result, true);
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
            // Only the survivor locks the cursor, so only the survivor can release it. The killer keeps moving on Esc.
            if (survivor && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                releasedCursor = true; rig.SetCursor(false);
            }
            if (survivor && releasedCursor && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                releasedCursor = false; rig.SetCursor(true);
            }
            bool active = Application.isFocused && !releasedCursor;
            rig.Look(active);
            // Opposing keys still count as held. MovementInput owns the 30 Hz rate and the release zeros.
            bool keys = active && keyboard != null && (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed);
            bool scripted = dev != null && dev.HoldForward; // dev capture: scripted W through the same 30 Hz path
            bool held = keys || scripted;
            float right = keys ? (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0) : 0;
            float forward = scripted ? 1 : keys ? (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0) : 0;
            if (movement.TryCreate(held, right, forward, rig.Yaw, Time.unscaledDeltaTime, out C2S_MoveInput input)) link.Send(input);
            // Hold E (survivors): one press edge and one release edge; losing focus or the cursor counts as release.
            if (survivor && interact.TryCreate(active && keyboard != null && keyboard.eKey.isPressed, out C2S_Interact use)) link.Send(use);
            if (active && SkillInput.TryCreate(keyboard != null && keyboard.qKey.wasPressedThisFrame, role, skillId, localStatus, out C2S_UseSkill skill))
                link.Send(skill);
            world.Render(LinkClock.Now);
            // TODO: client-side prediction for the local survivor camera (it renders 100 ms in the past).
            if (world.LocalPosition(LinkClock.Now, out Vector3 position) || role == Role.Killer) rig.Place(position);
            if (survivor) screens.SetSurvivorHud(statusLine, teamLine, hackingLine, releasedCursor);
            // Killer: above the ring on the map (screen-up is +z). Survivor: above the ring in the world.
            Vector3 anchor = survivor ? SharedMap.Exit + Vector3.up * 2.6f : SharedMap.Exit + Vector3.forward * (GameConstants.ExitRadius + .6f);
            screens.SetExit(view, anchor, exitOpen);
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!playing) return;
            releasedCursor = !focused;
            rig?.SetCursor(focused);
            // Same-frame release zero; Update keeps calling MovementInput, which sends the backup zero.
            if (!focused && movement.TryCreate(false, 0, 0, rig.Yaw, 0, out C2S_MoveInput input)) link?.Send(input);
            if (!focused && interact.TryRelease(out C2S_Interact release)) link?.Send(release);
        }
        private void DestroyWorld()
        {
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
            playing = false; joinDeadline = 0; lobby = null; playerId = 0; lastWorld = null;
            threat.SetLevel(0); rig.SetCursor(false);
            DestroyWorld();
            screens.ShowMenu(notice);
        }
        private void OnDestroy() { link?.Dispose(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
