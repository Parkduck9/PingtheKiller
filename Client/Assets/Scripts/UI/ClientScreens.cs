using System;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // State list before layout: menu = endpoint/validation/connecting; lobby = eight slots,
    // host permissions/bot count/role; play = recipient's Signal state or Exit lock, plus RTT.
    // Layout rule: every label is one line (nowrap, see ClientUI.uss); only .message wraps, at a fixed width.
    public sealed class ClientScreens : MonoBehaviour
    {
        // Sizes in USS are 1080p pixels. Below 1080p the panel scales down, but not under MinScale,
        // so 14 px text stays at least 11 px at 1280x720 (the layout still fits at 1280x720).
        public const float MinScale = .8f;
        private VisualElement root, menu, lobby, play, hud;
        private Label message, lobbyMessage, debug, signal, exit;
        private readonly Label[] slots = new Label[Protocol.LobbySlotCount];
        private TextField address;
        private Button join, cancel, minus, plus, start;
        private Label botCount, roleChoice;
        private readonly Button[] roleButtons = new Button[3];
        private PanelSettings panel;
        private Vector2Int scaledFor;
        private double shownRtt;
        private int shownHint;
        public event Action<string> Join;
        public event Action Leave;
        public event Action<int> ChangeBots;
        public event Action<byte> ChangeRole;
        public event Action StartMatch;
        public string Address { get => address.value; set => address.value = value; }
        public bool Playing { get; private set; }

        public static float PanelScale(int width, int height)
            => Mathf.Clamp(Mathf.Min(width / 1920f, height / 1080f), MinScale, 1);

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
            BuildMenu(); BuildLobby(); BuildPlay();
            root.Query<TextElement>().ForEach(element => element.style.unityFontDefinition = FontDefinition.FromFont(font));
            ShowMenu(string.Empty);
        }
        private void ApplyScale()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size == scaledFor) return;
            scaledFor = size;
            panel.scale = PanelScale(size.x, size.y);
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
        private void BuildMenu()
        {
            menu = Box(root, "screen");
            Text(menu, "Ping the Killer", "title");
            Text(menu, "Run the server on this PC: dotnet run --project Server", "muted");
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
            var offline = Action(menu, "Offline", () => { }); offline.SetEnabled(false);
            Text(menu, "Offline mode arrives in U4b", "note");
        }
        private void BuildLobby()
        {
            lobby = Box(root, "screen");
            Text(lobby, "Lobby", "title");
            var columns = Box(lobby, "columns");
            var roster = Box(columns, "roster");
            for (int i = 0; i < slots.Length; i++) slots[i] = Text(roster, (i + 1) + "   Empty", "slot");
            var controls = Box(columns, "controls");
            Text(controls, "Host controls", "section");
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
        private void BuildPlay()
        {
            play = Box(root, "play"); play.pickingMode = PickingMode.Ignore;
            hud = Box(play, "hud"); hud.pickingMode = PickingMode.Ignore;
            signal = Text(hud, string.Empty, "signal");
            debug = Text(hud, string.Empty, "debug");
            exit = Text(play, "Exit · Locked", "exit");
        }
        private void Phase(VisualElement visible)
        {
            menu.style.display = visible == menu ? DisplayStyle.Flex : DisplayStyle.None;
            lobby.style.display = visible == lobby ? DisplayStyle.Flex : DisplayStyle.None;
            play.style.display = visible == play ? DisplayStyle.Flex : DisplayStyle.None;
            root.EnableInClassList("in-game", visible == play);
            Playing = visible == play;
        }
        public void ShowMenu(string notice)
        {
            Phase(menu); SetConnecting(false); message.text = notice;
            join.Focus();
        }
        public void SetConnecting(bool value)
        {
            join.SetEnabled(!value); address.SetEnabled(!value);
            cancel.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            if (value) { message.text = "Connecting to " + address.value + "…"; cancel.Focus(); }
        }
        public void Error(string notice) => message.text = notice;
        public void ShowLobby(S2C_LobbyState state, int localId)
        {
            bool entering = lobby.style.display == DisplayStyle.None;
            Phase(lobby);
            int bots = 0, humans = 0; bool host = false;
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = state.Slots[i]; bool occupied = slot.PlayerId != 0;
                slots[i].text = (i + 1) + "   " + (occupied ? slot.Nickname + (slot.IsBot != 0 ? " · bot" : " · player")
                    + (slot.IsHost != 0 ? " · host" : "") + (slot.PlayerId == localId ? " · you" : "") : "Empty");
                slots[i].EnableInClassList("empty", !occupied);
                if (!occupied) continue;
                if (slot.IsBot != 0) bots++; else humans++;
                if (slot.PlayerId == localId && slot.IsHost != 0) host = true;
            }
            botCount.text = "Bots: " + bots;
            minus.SetEnabled(host && bots > Math.Max(0, 2 - humans));
            plus.SetEnabled(host && bots + humans < 8);
            roleChoice.text = "Host role: " + (state.HostRoleChoice == 255 ? "Random" : ((Role)state.HostRoleChoice).ToString());
            for (int i = 0; i < roleButtons.Length; i++)
            {
                roleButtons[i].SetEnabled(host);
                roleButtons[i].EnableInClassList("chosen", state.HostRoleChoice == (i == 2 ? 255 : i));
            }
            start.SetEnabled(host && bots + humans >= 2);
            lobbyMessage.text = host ? (bots + humans >= 2 ? "Choose a role, then start the match." : "Add a bot or wait for another player.") : "Waiting for the host to start.";
            if (entering) { if (start.enabledSelf) start.Focus(); else lobby.Query<Button>().ToList().Find(button => button.enabledSelf)?.Focus(); }
        }
        public void ShowPlay(Role role)
        {
            Phase(play);
            hud.EnableInClassList("survivor-hud", role == Role.Survivor);
            hud.EnableInClassList("killer-hud", role == Role.Killer);
            signal.style.display = role == Role.Survivor ? DisplayStyle.Flex : DisplayStyle.None;
            exit.style.display = DisplayStyle.None;
            root.focusController?.focusedElement?.Blur();
        }
        public void SetSignal(bool transmitting)
        {
            signal.text = transmitting ? "Signal" : "Silent";
            signal.EnableInClassList("transmitting", transmitting);
        }
        // The killer's cursor is never locked, so only the survivor gets a cursor hint.
        public void SetDebug(double rtt, bool survivor, bool releasedCursor)
        {
            double rounded = rtt < 0 ? -1 : Math.Round(rtt);
            int hint = survivor ? (releasedCursor ? 2 : 1) : 0;
            if (rounded == shownRtt && hint == shownHint && debug.text.Length > 0) return;
            shownRtt = rounded; shownHint = hint;
            debug.text = (rounded < 0 ? "RTT: waiting" : "RTT: " + rounded + " ms")
                + (hint == 2 ? " · Click to resume mouse look" : hint == 1 ? " · Esc: release cursor" : string.Empty);
        }
        public void SetExit(Camera camera, bool open)
        {
            Vector3 screen = camera.WorldToScreenPoint(World.MapSource.Exit);
            bool visible = screen.z > 0 && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
            exit.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            exit.text = open ? "Exit" : "Exit · Locked";
            exit.style.left = screen.x / Screen.width * root.resolvedStyle.width;
            exit.style.top = (1 - screen.y / Screen.height) * root.resolvedStyle.height;
        }
        private void OnDestroy() { if (panel != null) Destroy(panel); }
    }
}
