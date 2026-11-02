using System;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // State list before layout: menu = endpoint/validation/connecting, Offline; lobby = eight slots, host permissions,
    // bot count, role, offline marker; play = survivor: Signal/Silent, own status, team terminals + Exit, hacking %,
    // skill, cursor hint; killer: skill (Exit state lives on the exit in the world); result = winner + reason, own
    // outcome, lobby reopened or not; dev line (F3) = RTT online / offline marker.
    // Layout rule: every label is one line (nowrap, see ClientUI.uss); only .message wraps, at a fixed width.
    public sealed class ClientScreens : MonoBehaviour
    {
        // Sizes in USS are logical units (1080p pixels). The panel follows min(w/1920, h/1080) clamped to [MinScale,
        // MaxScale] (HUD_SPEC 0.2 and Q15): the logical canvas is 1920x1080 at 1080p and 1600x900 at 720p.
        public const float MinScale = .8f;
        public const float MaxScale = 1f;
        // HUD_SPEC 0.3: S text renders at max(14 u x scale, 12 px).
        public const float SmallText = 14, SmallFloorPixels = 12;
        private VisualElement root, menu, lobby, result, play;
        private Label message, lobbyTitle, lobbyMessage, controlsTitle, dev, exit;
        private Label resultHeadline, resultOutcome, resultPreview, resultMessage;
        private readonly Label[] slots = new Label[Protocol.LobbySlotCount];
        private readonly VisualElement[] slotRows = new VisualElement[Protocol.LobbySlotCount];
        private TextField address;
        private Button join, cancel, offline, minus, plus, start, back, resultLeave;
        private Label botCount, roleChoice;
        private readonly Button[] roleButtons = new Button[3];
        private PanelSettings panel;
        private Vector2Int scaledFor;
        public event Action<string> Join;
        public event Action Offline;
        public event Action Leave;
        public event Action<int> ChangeBots;
        public event Action<byte> ChangeRole;
        public event Action StartMatch;
        public event Action BackToLobby;
        public string Address { get => address.value; set => address.value = value; }
        public bool Playing { get; private set; }
        public bool InResult { get; private set; }
        public KillerHud Killer { get; private set; }
        public SurvivorHud Survivor { get; private set; }
        public VisualElement StutterBack { get; private set; }
        public VisualElement StutterFront { get; private set; }
        public bool DevVisible
        {
            get => dev.style.display == DisplayStyle.Flex;
            set => dev.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static float PanelScale(int width, int height)
            => Mathf.Clamp(Mathf.Min(width / 1920f, height / 1080f), MinScale, MaxScale);

        // The S size in logical units at a panel scale: 15 u at 0.8, so it renders at 12 px.
        public static float SmallSize(float scale) => Mathf.Max(SmallText, SmallFloorPixels / Mathf.Max(.01f, scale));

        private void Awake()
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("ClientTheme");
            ApplyScale();
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.sortingOrder = 10;
            root = document.rootVisualElement;
            root.AddToClassList("root");
            root.styleSheets.Add(Resources.Load<StyleSheet>("ClientUI"));
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            root.style.unityFontDefinition = FontDefinition.FromFont(font);
            // Lagging stutter layers (HUD_SPEC 4.5): the held world image, behind every screen and the HUD.
            StutterBack = Box(root, "stutter"); StutterFront = Box(root, "stutter");
            StutterBack.pickingMode = PickingMode.Ignore; StutterFront.pickingMode = PickingMode.Ignore;
            BuildMenu(); BuildLobby(); BuildResult(); BuildPlay(font);
            dev = Text(root, string.Empty, "dev"); dev.pickingMode = PickingMode.Ignore; DevVisible = false;
            root.Query<TextElement>().ForEach(element => element.style.unityFontDefinition = FontDefinition.FromFont(font));
            ShowMenu(string.Empty);
            scaledFor = default; ApplyScale();
        }
        private void ApplyScale()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size == scaledFor) return;
            scaledFor = size;
            panel.scale = PanelScale(size.x, size.y);
            root?.EnableInClassList("s-floor", SmallSize(panel.scale) > SmallText + .01f);
            Killer?.SetScale(panel.scale);
            Survivor?.SetScale(panel.scale);
        }
        private void Update() => ApplyScale();

        private static Label Text(VisualElement parent, string value, string style = null)
        {
            var label = new Label(value);
            if (style != null) label.AddToClassList(style);
            parent.Add(label); return label;
        }
        private static Button Action(VisualElement parent, string value, System.Action clicked, bool primary = false)
        {
            var button = new Button(clicked) { text = value };
            if (primary) button.AddToClassList("primary");
            parent.Add(button); return button;
        }
        private static VisualElement Box(VisualElement parent, string style)
        {
            var box = new VisualElement(); box.AddToClassList(style); parent.Add(box); return box;
        }
        private static void Show(VisualElement element, bool visible) => element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        private static void SetText(Label label, string value)
        {
            bool visible = !string.IsNullOrEmpty(value);
            Show(label, visible);
            if (visible && label.text != value) label.text = value;
        }
        private void BuildMenu()
        {
            menu = Box(root, "screen");
            Text(menu, "Ping the Killer", "title");
            Text(menu, "Enter the IP and port of the host PC to join its lobby.", "muted");
            Text(menu, "Server IP:port", "field-label");
            address = new TextField { value = "127.0.0.1:7777", maxLength = 64 };
            address.AddToClassList("address"); menu.Add(address);
            message = Text(menu, string.Empty, "message");
            var actions = Box(menu, "row");
            join = Action(actions, "Join", () => Join?.Invoke(address.value), true);
            address.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return && join.enabledSelf) { Join?.Invoke(address.value); evt.StopPropagation(); }
            });
            cancel = Action(actions, "Cancel connection", () => Leave?.Invoke());
            offline = Action(menu, "Offline", () => Offline?.Invoke());
            Text(menu, "Play against bots on this PC. No network needed.", "note");
        }
        private void BuildLobby()
        {
            lobby = Box(root, "screen");
            lobbyTitle = Text(lobby, "Lobby", "title");
            var columns = Box(lobby, "columns");
            var roster = Box(columns, "roster");
            for (int i = 0; i < slots.Length; i++)
            {
                slotRows[i] = Box(roster, "slot");
                Text(slotRows[i], (i + 1).ToString(), "slot-number");
                slots[i] = Text(slotRows[i], "Empty", "slot-name");
            }
            var controls = Box(columns, "controls");
            controlsTitle = Text(controls, "Host controls", "section");
            var botActions = Box(controls, "row");
            botCount = Text(botActions, "Bots: 0");
            minus = Action(botActions, "− bot", () => ChangeBots?.Invoke(-1));
            plus = Action(botActions, "+ bot", () => ChangeBots?.Invoke(1));
            roleChoice = Text(controls, "Host role: Random");
            roleButtons[0] = Action(controls, "Survivor", () => ChangeRole?.Invoke((byte)RoleChoice.Survivor));
            roleButtons[1] = Action(controls, "Killer", () => ChangeRole?.Invoke((byte)RoleChoice.Killer));
            roleButtons[2] = Action(controls, "Random", () => ChangeRole?.Invoke((byte)RoleChoice.Random));
            lobbyMessage = Text(lobby, string.Empty, "message");
            var actions = Box(lobby, "row");
            start = Action(actions, "Start", () => StartMatch?.Invoke(), true);
            Action(actions, "Leave", () => Leave?.Invoke());
        }
        private void BuildResult()
        {
            result = Box(root, "screen");
            Text(result, "Match result", "title");
            resultHeadline = Text(result, string.Empty, "headline");
            resultOutcome = Text(result, string.Empty, "outcome");
            resultPreview = Text(result, "Preview: synthetic result from a dev flag, not sent by a server.", "note");
            resultMessage = Text(result, string.Empty, "message");
            var actions = Box(result, "row");
            back = Action(actions, "Back to lobby", () => BackToLobby?.Invoke(), true);
            resultLeave = Action(actions, "Leave", () => Leave?.Invoke());
        }
        private void BuildPlay(Font font)
        {
            play = Box(root, "play"); play.pickingMode = PickingMode.Ignore;
            Survivor = new SurvivorHud(play, font);
            Killer = new KillerHud(play, font);
            exit = Text(play, HudCopy.ExitLabel(false), "exit");
        }
        private void Phase(VisualElement visible)
        {
            Show(menu, visible == menu);
            Show(lobby, visible == lobby);
            Show(result, visible == result);
            Show(play, visible == play);
            root.EnableInClassList("in-game", visible == play);
            Playing = visible == play;
            InResult = visible == result;
        }
        public void ShowMenu(string notice)
        {
            Phase(menu); SetConnecting(false); message.text = notice;
            join.Focus();
        }
        public void SetConnecting(bool value)
        {
            join.SetEnabled(!value); address.SetEnabled(!value); offline.SetEnabled(!value);
            Show(cancel, value);
            if (value) { message.text = "Connecting to " + address.value + "…"; cancel.Focus(); }
        }
        public void Error(string notice) => message.text = notice;
        public void ShowLobby(S2C_LobbyState state, int localId)
        {
            bool entering = lobby.style.display == DisplayStyle.None;
            Phase(lobby);
            bool isOffline = state.IsOffline != 0;
            lobbyTitle.text = LobbyCopy.Title(isOffline);
            controlsTitle.text = isOffline ? "Match setup" : "Host controls";
            int bots = 0, humans = 0; bool host = false;
            Slot[] ordered = LobbyCopy.DisplayOrder(state.Slots); // row numbers are display positions
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = i < ordered.Length ? ordered[i] : null; bool occupied = slot != null && slot.PlayerId != 0;
                slots[i].text = LobbyCopy.SlotText(slot, localId);
                slotRows[i].EnableInClassList("empty", !occupied);
                if (!occupied) continue;
                if (slot.IsBot != 0) bots++; else humans++;
                if (slot.PlayerId == localId && slot.IsHost != 0) host = true;
            }
            botCount.text = "Bots: " + bots;
            minus.SetEnabled(host && bots > Math.Max(0, 2 - humans));
            plus.SetEnabled(host && bots + humans < Protocol.MaxPlayers);
            roleChoice.text = LobbyCopy.RoleLine(state.HostRoleChoice, isOffline);
            for (int i = 0; i < roleButtons.Length; i++)
            {
                roleButtons[i].SetEnabled(host);
                roleButtons[i].EnableInClassList("chosen", state.HostRoleChoice == (i == 2 ? 255 : i));
            }
            start.SetEnabled(host && bots + humans >= 2);
            lobbyMessage.text = host ? (bots + humans >= 2 ? "Choose a role, then start the match." : "Add a bot or wait for another player.") : "Waiting for the host to start.";
            if (entering) { if (start.enabledSelf) start.Focus(); else lobby.Query<Button>().ToList().Find(button => button.enabledSelf)?.Focus(); }
        }
        // The Survivor cluster (HUD_SPEC 4.0) or the Killer cluster (6.1); ClientApp feeds the shown one every frame.
        public void ShowPlay(Role role, byte skillId)
        {
            Phase(play);
            Survivor.SetVisible(role == Role.Survivor);
            if (role == Role.Survivor) Survivor.Begin(skillId);
            Killer.SetVisible(role == Role.Killer);
            Show(exit, false);
            root.focusController?.focusedElement?.Blur();
        }
        public void SetDev(string text) { if (dev.text != text) dev.text = text; }
        // The label sits above the ring (anchor chosen by the caller), bottom-centred on the anchor point.
        public void SetExit(Camera camera, Vector3 anchor, bool open)
        {
            Vector3 screen = camera.WorldToScreenPoint(anchor);
            bool visible = screen.z > 0 && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
            Show(exit, visible);
            exit.text = HudCopy.ExitLabel(open);
            exit.style.left = screen.x / Screen.width * root.resolvedStyle.width;
            exit.style.top = (1 - screen.y / Screen.height) * root.resolvedStyle.height;
        }
        public void ShowResult(string headline, string outcome, bool preview)
        {
            Phase(result);
            resultHeadline.text = headline;
            resultOutcome.text = outcome;
            Show(resultPreview, preview);
            root.focusController?.focusedElement?.Blur();
        }
        public void SetResultLobby(bool lobbyOpen, bool isOffline)
        {
            resultMessage.text = ResultCopy.LobbyLine(lobbyOpen, isOffline);
            bool focusBack = lobbyOpen && !back.enabledSelf;
            back.SetEnabled(lobbyOpen);
            if (focusBack) back.Focus(); else if (!lobbyOpen) resultLeave.Focus();
        }
        // Dev scripting presses the real buttons, so a disabled button stays disabled. Returns whether it was enabled.
        public bool BackToLobbyEnabled => InResult && back.enabledSelf;
        public bool PressBackToLobby()
        {
            if (!BackToLobbyEnabled) return false;
            BackToLobby?.Invoke(); return true;
        }
        public bool PressStart()
        {
            if (lobby.style.display == DisplayStyle.None || !start.enabledSelf) return false;
            StartMatch?.Invoke(); return true;
        }
        private void OnDestroy() { Survivor?.Dispose(); if (panel != null) Destroy(panel); }
    }
}
