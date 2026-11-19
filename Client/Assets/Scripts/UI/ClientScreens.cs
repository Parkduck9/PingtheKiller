using System;
using PingTheKiller.Client.Settings;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // State list before layout: menu = endpoint/validation/connecting, Offline; lobby = eight slots, host permissions,
    // bot count, role, offline marker; play = survivor: Signal/Silent, own status, terminals, the rules v2 stack (route
    // panel, hold row, both exits + collapse), skill, cursor hint; killer: skill, hold and collapse blocks (exit states
    // live on the exits in the world); result = the rules v2 result (HUD_SPEC 13.7) + lobby reopened or not; dev line
    // (F3) = RTT online / offline marker.
    // Layout rule: every label is one line (nowrap, see ClientUI.uss); only .message wraps, at a fixed width.
    public sealed class ClientScreens : MonoBehaviour
    {
        // Sizes in USS are logical units (1080p pixels). The panel follows min(w/1920, h/1080) clamped to [MinScale,
        // MaxScale] (HUD_SPEC 0.2 and Q15): the logical canvas is 1920x1080 at 1080p and 1600x900 at 720p.
        public const float MinScale = .8f;
        public const float MaxScale = 1f;
        // HUD_SPEC 0.3: S text renders at max(14 u x scale, 12 px).
        public const float SmallText = 14, SmallFloorPixels = 12;
        private VisualElement root, menu, lobby, play;
        private ResultV2Panel result;
        private Label message, lobbyTitle, lobbyMessage, controlsTitle, dev;
        private Label resultPreview, resultMessage;
        private readonly Label[] slots = new Label[Protocol.LobbySlotCount];
        private readonly VisualElement[] slotRows = new VisualElement[Protocol.LobbySlotCount];
        private TextField address;
        private Button join, cancel, offline, minus, plus, start, back, resultLeave;
        private Label botCount, roleChoice;
        private readonly Button[] roleButtons = new Button[3];
        // R1 (11/17) lobby skill picker: every Killer skill, every Survivor skill (index = role offset + id), then Random.
        private VisualElement skills;
        private Label skillChosen;
        private readonly VisualElement[] skillGroups = new VisualElement[2];
        private readonly Button[] skillButtons = new Button[SkillIds.KillerSkillCount + SkillIds.SurvivorSkillCount];
        private Button randomSkill;
        private SettingsScreen settingsScreen;
        private HowToPlayScreen howTo;
        private CreditsScreen credits;
        private Button openSettings, openHowTo, openCredits;
        private PanelSettings panel;
        private Vector2Int scaledFor;
        public event Action<string> Join;
        public event Action Offline;
        public event Action Leave;
        public event Action<int> ChangeBots;
        public event Action<byte> ChangeRole;
        // R1 (11/17): the picked lobby skill choice (SkillIds.Choice, or SkillIds.NoChoice for random).
        public event Action<byte> ChooseSkill;
        public event Action StartMatch;
        public event Action BackToLobby;
        public string Address { get => address.value; set => address.value = value; }
        public bool Playing { get; private set; }
        public bool InResult { get; private set; }
        // U7a: the player's options, loaded from PlayerPrefs; the Settings screen edits them.
        public GameSettings Options { get; private set; }
        public bool SettingsOpen => settingsScreen != null && settingsScreen.Visible;
        public bool HowToOpen => howTo != null && howTo.Visible;
        public HowToPlayScreen HowTo => howTo;
        public bool CreditsOpen => credits != null && credits.Visible;
        public CreditsScreen Credits => credits;
        public Button CreditsButton => openCredits;
        // 11/17 R3 (HUD_SPEC 3): the match intro card, both roles, bottom-center over the match.
        public IntroCard Intro { get; private set; }
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
            root.styleSheets.Add(Resources.Load<StyleSheet>("RulesV2"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("HelpUI"));
            Font font = Resources.Load<Font>("Fonts/Inter-Regular");
            root.style.unityFontDefinition = FontDefinition.FromFont(font);
            // Lagging stutter layers (HUD_SPEC 4.5): the held world image, behind every screen and the HUD.
            StutterBack = Box(root, "stutter"); StutterFront = Box(root, "stutter");
            StutterBack.pickingMode = PickingMode.Ignore; StutterFront.pickingMode = PickingMode.Ignore;
            BuildMenu(); BuildLobby(); BuildResult(font); BuildPlay(font);
            Options = GameSettings.Load(new PlayerPrefsStore());
            settingsScreen = new SettingsScreen(root, Options, () => Playing);
            settingsScreen.Closed += SettingsClosed;
            howTo = new HowToPlayScreen(root, font);
            howTo.Closed += HowToClosed;
            credits = new CreditsScreen(root, font);
            credits.Closed += CreditsClosed;
            Killer.OpenSettings += ShowSettings;
            Survivor.Menu.OpenSettings += ShowSettings;
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
            Intro?.SetScale(panel.scale);
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
            openHowTo = Action(menu, HelpCopy.HowToPlay, ShowHowTo);
            openHowTo.AddToClassList("set-open");
            openSettings = Action(menu, SettingsCopy.Title, ShowSettings);
            openSettings.AddToClassList("set-open");
            openCredits = Action(menu, CreditsCopy.Button, ShowCredits);
            openCredits.AddToClassList("set-open");
        }
        private void BuildLobby()
        {
            lobby = Box(root, "screen");
            lobby.AddToClassList("lobby-wide");
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
            controls.AddToClassList("with-skills");
            controlsTitle = Text(controls, "Host controls", "section");
            var botActions = Box(controls, "row");
            botCount = Text(botActions, "Bots: 0");
            minus = Action(botActions, "− bot", () => ChangeBots?.Invoke(-1));
            plus = Action(botActions, "+ bot", () => ChangeBots?.Invoke(1));
            roleChoice = Text(controls, "Host role: Random");
            roleButtons[0] = Action(controls, "Survivor", () => ChangeRole?.Invoke((byte)RoleChoice.Survivor));
            roleButtons[1] = Action(controls, "Killer", () => ChangeRole?.Invoke((byte)RoleChoice.Killer));
            roleButtons[2] = Action(controls, "Random", () => ChangeRole?.Invoke((byte)RoleChoice.Random));
            BuildSkillPicker(columns);
            lobbyMessage = Text(lobby, string.Empty, "message");
            var actions = Box(lobby, "row");
            start = Action(actions, "Start", () => StartMatch?.Invoke(), true);
            Action(actions, "Leave", () => Leave?.Invoke());
        }
        // R1 (11/17) HUD_SPEC 12.4: the third lobby column. One row per skill: the name button, its effect line beside it.
        private void BuildSkillPicker(VisualElement columns)
        {
            skills = Box(columns, "skills");
            Text(skills, LobbyCopy.PickerTitle, "section");
            skillChosen = Text(skills, string.Empty, "note");
            skillChosen.AddToClassList("pick-chosen");
            int index = 0;
            for (int g = 0; g < 2; g++)
            {
                Role role = g == 0 ? Role.Killer : Role.Survivor;
                skillGroups[g] = Box(skills, "pick-group");
                Text(skillGroups[g], LobbyCopy.GroupTitle(role), "pick-title");
                int count = role == Role.Killer ? SkillIds.KillerSkillCount : SkillIds.SurvivorSkillCount;
                for (byte id = 0; id < count; id++)
                {
                    VisualElement row = Box(skillGroups[g], "pick-row");
                    byte choice = SkillIds.Choice(role, id);
                    skillButtons[index] = Action(row, LobbyCopy.SkillName(role, id), () => ChooseSkill?.Invoke(choice));
                    skillButtons[index].AddToClassList("pick");
                    Text(row, LobbyCopy.PickerEffect(role, id), "pick-effect");
                    index++;
                }
            }
            randomSkill = Action(skills, LobbyCopy.RandomSkill, () => ChooseSkill?.Invoke(SkillIds.NoChoice));
            randomSkill.AddToClassList("pick");
        }

        // The picker for the own slot's choice and the host role choice.
        private void SetSkillPicker(byte choice, byte hostRoleChoice, bool host)
        {
            skillChosen.text = LobbyCopy.Chosen(choice);
            Show(skillGroups[0], LobbyCopy.ShowsRole(Role.Killer, hostRoleChoice, host));
            Show(skillGroups[1], LobbyCopy.ShowsRole(Role.Survivor, hostRoleChoice, host));
            int index = 0;
            for (int g = 0; g < 2; g++)
            {
                Role role = g == 0 ? Role.Killer : Role.Survivor;
                int count = role == Role.Killer ? SkillIds.KillerSkillCount : SkillIds.SurvivorSkillCount;
                for (byte id = 0; id < count; id++) skillButtons[index++].EnableInClassList("chosen", choice == SkillIds.Choice(role, id));
            }
            randomSkill.EnableInClassList("chosen", choice == SkillIds.NoChoice);
        }

        // Dev and tests: the picker as shown (group visibility, the chosen line) and a scripted pick.
        public bool SkillGroupShown(Role role) => skillGroups[role == Role.Killer ? 0 : 1].style.display != DisplayStyle.None;
        public string SkillChosenText => skillChosen.text;
        public void PickSkill(byte choice) => ChooseSkill?.Invoke(choice);

        // HUD_SPEC 13.7: the rules v2 result body; 7.5 Back to lobby and 7.6 Leave go into its Actions.
        private void BuildResult(Font font)
        {
            result = new ResultV2Panel(font);
            root.Add(result);
            resultMessage = Text(result.Notes, string.Empty);
            resultPreview = Text(result.Notes, ResultCopy.PreviewNote);
            back = Action(result.Actions, "Back to lobby", () => BackToLobby?.Invoke(), true);
            resultLeave = Action(result.Actions, "Leave", () => Leave?.Invoke());
            resultLeave.AddToClassList("v2-secondary");
        }
        public ResultV2Panel Result => result;
        private void BuildPlay(Font font)
        {
            play = Box(root, "play"); play.pickingMode = PickingMode.Ignore;
            Survivor = new SurvivorHud(play, font);
            Killer = new KillerHud(play, font);
            Intro = new IntroCard(font); // after both HUDs: drawn over them
            play.Add(Intro);
        }
        private void Phase(VisualElement visible)
        {
            settingsScreen?.Hide();
            howTo?.Hide();
            credits?.Hide();
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
            join.SetEnabled(!value); address.SetEnabled(!value); offline.SetEnabled(!value); openSettings.SetEnabled(!value); openHowTo.SetEnabled(!value); openCredits.SetEnabled(!value);
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
            int bots = 0, humans = 0; bool host = false; byte ownChoice = SkillIds.NoChoice;
            Slot[] ordered = LobbyCopy.DisplayOrder(state.Slots); // row numbers are display positions
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = i < ordered.Length ? ordered[i] : null; bool occupied = slot != null && slot.PlayerId != 0;
                slots[i].text = LobbyCopy.SlotText(slot, localId);
                slotRows[i].EnableInClassList("empty", !occupied);
                if (!occupied) continue;
                if (slot.IsBot != 0) bots++; else humans++;
                if (slot.PlayerId == localId && slot.IsHost != 0) host = true;
                if (slot.PlayerId == localId) ownChoice = slot.SkillChoice;
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
            SetSkillPicker(ownChoice, state.HostRoleChoice, host);
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
            root.focusController?.focusedElement?.Blur();
        }
        // Opened from the main menu (it replaces the menu) or from either role's play menu (it sits over the match).
        public void ShowSettings()
        {
            if (SettingsOpen || CreditsOpen) return;
            if (!Playing) Show(menu, false);
            settingsScreen.Show();
        }
        public void CloseSettings() => settingsScreen.Close();
        // 11/17 R3 (HUD_SPEC 1.7): the How to play screen replaces the main menu; Back or Esc returns to it.
        public void ShowHowTo() => ShowHowTo(Role.Survivor);
        public void ShowHowTo(Role page)
        {
            if (HowToOpen || CreditsOpen || Playing) return;
            Show(menu, false);
            howTo.Show(page);
        }
        public void CloseHowTo() => howTo.Close();
        // S3-credits (HUD_SPEC 1.8): the Credits and licences screen replaces the main menu; Back, Esc or B returns to it.
        public void ShowCredits()
        {
            if (CreditsOpen || HowToOpen || SettingsOpen || Playing) return;
            Show(menu, false);
            credits.Show();
        }
        public void CloseCredits() => credits.Close();
        private void CreditsClosed() { Show(menu, true); openCredits.Focus(); }
        private void HowToClosed() { Show(menu, true); openHowTo.Focus(); }
        private void SettingsClosed()
        {
            if (Playing) { OpenPlayMenu(Killer, Survivor)?.FocusSettings(); return; }
            Show(menu, true); openSettings.Focus();
        }
        // The play menu of the role in the match (HUD_SPEC 5 / 6.6), or null while neither is open.
        public static PlayMenu OpenPlayMenu(KillerHud killer, SurvivorHud survivor)
            => killer != null && killer.MenuOpen ? killer.Menu : survivor != null && survivor.MenuOpen ? survivor.Menu : null;
        public void SetDev(string text) { if (dev.text != text) dev.text = text; }
        public void ShowResult(ResultV2View view, bool preview)
        {
            Phase(result);
            result.Set(view);
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
        // Dev scripting (-ptk-menu-leave): the open play menu's Leave match button path, and whether the main menu shows.
        public bool PressLeaveMatch()
        {
            PlayMenu open = OpenPlayMenu(Killer, Survivor);
            return open != null && open.PressLeave();
        }
        public bool MainMenuShown => menu.style.display == DisplayStyle.Flex && !SettingsOpen;
        public bool PressStart()
        {
            if (lobby.style.display == DisplayStyle.None || !start.enabledSelf) return false;
            StartMatch?.Invoke(); return true;
        }
        private void OnDestroy() { Survivor?.Dispose(); if (panel != null) Destroy(panel); }
    }
}
