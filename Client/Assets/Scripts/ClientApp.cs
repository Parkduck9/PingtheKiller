using System;
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
        private NetClient net;
        private ClientScreens screens;
        private Camera view;
        private RoleCamera rig;
        private WorldView world;
        private ThreatAudio threat;
        private DevCapture dev;
        private int playerId, botCount;
        private MovementInput movement = new MovementInput();
        private Role role;
        private bool playing, exitOpen, releasedCursor;
        private double joinDeadline;
        private S2C_LobbyState lobby;

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
            screens.Leave += () => Disconnect("Disconnected. Join a server to play.");
            screens.ChangeBots += delta => { if (IsHost()) net.Send(new C2S_SetBots { Count = (byte)Mathf.Clamp(botCount + delta, 0, 7) }); };
            screens.ChangeRole += choice => { if (IsHost()) net.Send(new C2S_SetRole { RoleChoice = choice }); };
            screens.StartMatch += () => { if (IsHost()) net.Send(new C2S_StartMatch()); };
            DevArgs devArgs = DevArgs.Parse(Environment.GetCommandLineArgs());
            if (devArgs.Active)
            {
                dev = gameObject.AddComponent<DevCapture>(); dev.Initialize(devArgs);
                dev.Join += value => { screens.Address = value; Join(value); };
                dev.HostSend += packet => { if (IsHost()) net.Send(packet); };
            }
        }
        private bool IsHost()
        {
            if (lobby == null || net == null || !net.IsConnected || playing) return false;
            foreach (Slot slot in lobby.Slots) if (slot.PlayerId == playerId && slot.IsHost != 0) return true;
            return false;
        }
        private void Join(string value)
        {
            if (!EndpointParser.TryParse(value, out var endpoint, out string error)) { screens.Error(error); return; }
            net?.Dispose();
            net = new NetClient();
            movement = new MovementInput(); // Seq lives as long as the connection.
            net.PacketReceived += Receive;
            net.Disconnected += Disconnect;
            screens.SetConnecting(true);
            joinDeadline = NetClient.Now + 10;
            net.Connect(endpoint);
        }
        private void Receive(NetClient.Received received)
        {
            switch (received.Packet)
            {
                case S2C_JoinResult joined:
                    if (joined.Result != 0) { Disconnect("The server rejected the join (code " + joined.Result + "). Check available slots and rejoin."); return; }
                    playerId = joined.PlayerId;
                    break;
                case S2C_LobbyState state:
                    joinDeadline = 0;
                    lobby = state; botCount = 0;
                    foreach (Slot slot in state.Slots) if (slot.PlayerId != 0 && slot.IsBot != 0) botCount++;
                    if (!playing) screens.ShowLobby(state, playerId);
                    dev?.OnLobby(state, IsHost());
                    break;
                case S2C_MatchStart match:
                    joinDeadline = 0;
                    if (world != null) { world.gameObject.SetActive(false); Destroy(world.gameObject); }
                    role = (Role)match.Role;
                    playing = true; movement.ResetKeys(); releasedCursor = false;
                    var map = new GameObject("Match world"); map.transform.SetParent(transform, false);
                    world = map.AddComponent<WorldView>(); world.Initialize(playerId, role);
                    rig.Begin(role); screens.ShowPlay(role); screens.SetSignal(false);
                    rig.Place(Vector3.zero);
                    dev?.OnMatchStart();
                    break;
                case S2C_WorldState state:
                    if (!playing || world == null) break;
                    world.Accept(state, received.Time);
                    exitOpen = state.ExitOpen != 0 && state.ExitLocked == 0;
                    threat.SetLevel(role == Role.Survivor ? state.ThreatLevel : (byte)0);
                    foreach (PlayerState player in state.Players)
                        if (player.Id == playerId) screens.SetSignal((player.Flags & (byte)PlayerFlags.Signal) != 0);
                    break;
            }
        }
        private void Update()
        {
            net?.Pump();
            if (joinDeadline != 0 && NetClient.Now > joinDeadline) { Disconnect("The server did not send a lobby. Check that the server uses protocol v0.2, then rejoin."); return; }
            if (!playing || world == null || net == null) return;
            Keyboard keyboard = Keyboard.current;
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
            if (movement.TryCreate(held, right, forward, rig.Yaw, Time.unscaledDeltaTime, out C2S_MoveInput input)) net.Send(input);
            world.Render(NetClient.Now);
            // TODO(U4b): client-side prediction for the local survivor camera
            if (world.LocalPosition(NetClient.Now, out Vector3 position) || role == Role.Killer) rig.Place(position);
            screens.SetDebug(net.RttMilliseconds, survivor, releasedCursor);
            screens.SetExit(view, exitOpen);
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!playing) return;
            releasedCursor = !focused;
            rig?.SetCursor(focused);
            // Same-frame release zero; Update keeps calling MovementInput, which sends the backup zero.
            if (!focused && movement.TryCreate(false, 0, 0, rig.Yaw, 0, out C2S_MoveInput input)) net?.Send(input);
        }
        private void Disconnect(string notice)
        {
            NetClient previous = net; net = null;
            if (previous != null) { previous.PacketReceived -= Receive; previous.Disconnected -= Disconnect; previous.Dispose(); }
            playing = false; joinDeadline = 0; lobby = null; playerId = 0;
            threat.SetLevel(0); rig.SetCursor(false);
            if (world != null) { world.gameObject.SetActive(false); Destroy(world.gameObject); world = null; }
            screens.ShowMenu(notice);
        }
        private void OnDestroy() { net?.Dispose(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
