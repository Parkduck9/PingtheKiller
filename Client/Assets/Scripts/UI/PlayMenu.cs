using System;
using System.Collections.Generic;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // Stacked: the Killer's 320x256 block (HUD_SPEC 6.6). Row: the Survivor's 672x152 block (HUD_SPEC 5).
    public enum PlayMenuLayout : byte { Stacked, Row }

    // Survivor play menu copy (HUD_SPEC 5, 8). The buttons reuse the Killer menu's words and the Settings title.
    public static class PlayMenuCopy
    {
        // HUD_SPEC 8 Survivor effect lines.
        public static string SurvivorEffect(byte skillId)
        {
            switch (skillId)
            {
                case SkillIds.DummyPacket: return "A fake Signal where you stand, 3 s.";
                case SkillIds.StealthRelay: return "You and Survivors within 5 m stay Silent for 2 s, even while moving.";
                case SkillIds.TrafficOverload: return "You are Signal for 2 s; the Killer moves at half speed for 2 s.";
                case SkillIds.EmergencyRestore: return "Instantly rescues a Lagging Survivor next to you.";
                case SkillIds.WhiteNoise: return "Five fake Signals across the map, 1.5 s.";
                default: return string.Empty;
            }
        }

        // GDD v2 4: the first capture leaves 60 s for a rescue, the second 30 s, the third eliminates. The v1 sentence
        // ("Captured once: the next capture eliminates you.") is only true after two captures now.
        public const string CapturedOnce = "Captured once: the next capture leaves 30 s for a rescue.";
        public const string CapturedTwice = "Captured twice: the next capture eliminates you.";

        // Line 3 (S muted): only while Normal, from the own entry's CaptureCount; null = no line.
        public static string CaptureNote(PlayerStatus status, int captureCount)
        {
            if (status != PlayerStatus.Normal || captureCount <= 0) return null;
            return captureCount >= GameConstants.EliminatingCaptureCount - 1 ? CapturedTwice : CapturedOnce;
        }
    }

    // The play-menu input gate (HUD_SPEC 5, 6.6): while the menu is open there is no mouse look, no WASD, E, Q or Space.
    public sealed class PlayMenuGate
    {
        private int closedFrame = int.MinValue;
        public bool Open { get; private set; }

        // Returns whether the state changed. frame = the frame the change happens on (Time.frameCount).
        public bool Set(bool open, int frame)
        {
            if (Open == open) return false;
            Open = open;
            if (!open) closedFrame = frame;
            return true;
        }

        public void Reset() { Open = false; closedFrame = int.MinValue; }

        // Look, held keys (WASD, E) and presses all need a focused window, a captured cursor and a closed menu.
        public bool Active(bool focused, bool cursorReleased) => focused && !cursorReleased && !Open;

        // Press edges (Space, Q): a Submit on Resume (Space, Enter, gamepad A) may close the menu earlier in the same frame,
        // and that key must not also press the sync check or the skill.
        public bool Presses(bool active, int frame) => active && frame != closedFrame;

        // Opening: one zero MoveInput, then the Interact release if a press was sent. The match keeps running.
        public static void Stop(MovementInput movement, InteractInput interact, float yaw, Action<IPacket> send)
        {
            send(movement.Final(yaw));
            if (interact.TryRelease(out C2S_Interact release)) send(release);
        }
    }

    // One play menu block: skill name (M 600), effect (S), an optional muted line (Row only), then Resume (primary),
    // Settings and Leave match. Stacked keeps the Killer's kmenu / kbutton classes (HUD_SPEC 6.6); Row lays the three
    // 160x40 buttons out in one row with an 8 u gap (HUD_SPEC 5). Focus order: Resume -> Settings -> Leave match.
    public sealed class PlayMenu : VisualElement
    {
        public const float StackedWidth = 320, StackedHeight = 256, StackedButtonWidth = 296;
        public const float RowWidth = 672, RowHeight = 152, RowButtonWidth = 160;
        public const float ButtonHeight = 40, ButtonGap = 8;
        private readonly Label skill, effect, note;
        private readonly Button resume, settings, leave;
        private readonly Button[] buttons;
        public PlayMenuLayout Layout { get; }
        public event Action Resume;
        public event Action OpenSettings;
        public event Action LeaveMatch;

        public PlayMenu(PlayMenuLayout layout, Font font)
        {
            Layout = layout;
            pickingMode = PickingMode.Ignore;
            bool row = layout == PlayMenuLayout.Row;
            AddToClassList(row ? "smenu" : "kblock");
            if (!row) AddToClassList("kmenu");
            skill = Text(this, "km", "kbold", "kgap");
            effect = row ? Text(this, "ks", "kgap") : Text(this, "ks", "kwrap", "kmenu-effect");
            if (row)
            {
                note = Text(this, "ks", "kmuted", "smenu-note");
                Show(note, false);
            }
            VisualElement actions = this;
            if (row)
            {
                actions = new VisualElement { pickingMode = PickingMode.Ignore };
                actions.AddToClassList("smenu-actions");
                Add(actions);
            }
            resume = MakeButton(actions, KillerHudCopy.Resume, () => Resume?.Invoke(), row);
            resume.AddToClassList("primary");
            settings = MakeButton(actions, SettingsCopy.Title, () => OpenSettings?.Invoke(), row);
            leave = MakeButton(actions, KillerHudCopy.LeaveMatch, OnLeave, row);
            leave.AddToClassList("klast");
            buttons = new[] { resume, settings, leave };
            RegisterCallback<NavigationMoveEvent>(Navigate);
            if (font != null)
            {
                foreach (Label label in this.Query<Label>().ToList()) label.style.unityFontDefinition = FontDefinition.FromFont(font);
                foreach (Button button in buttons) button.style.unityFontDefinition = FontDefinition.FromFont(font);
            }
            Show(this, false);
        }

        private static Label Text(VisualElement parent, params string[] styles)
        {
            var label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            foreach (string style in styles) label.AddToClassList(style);
            parent.Add(label); return label;
        }

        private static Button MakeButton(VisualElement parent, string text, Action clicked, bool row)
        {
            var button = new Button(clicked) { text = text };
            button.AddToClassList("kbutton");
            if (row) button.AddToClassList("smenu-button");
            parent.Add(button); return button;
        }

        private static void Show(VisualElement element, bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) element.style.display = display;
        }

        private static void SetText(Label label, string value)
        {
            if (label.text != (value ?? string.Empty)) label.text = value ?? string.Empty;
        }

        public bool Open => style.display == DisplayStyle.Flex;
        public IReadOnlyList<Button> Buttons => buttons;
        public string SkillText => skill.text;
        public string EffectText => effect.text;
        public string NoteText => note == null || note.style.display == DisplayStyle.None ? null : note.text;

        public void SetContent(string skillName, string effectLine)
        {
            SetText(skill, skillName);
            SetText(effect, effectLine);
        }

        // Row only: the S muted line 3; null or empty hides it.
        public void SetNote(string value)
        {
            if (note == null) return;
            Show(note, !string.IsNullOrEmpty(value));
            SetText(note, value);
        }

        // Opening focuses Resume; closing drops the focus from whichever menu button holds it, so a hidden button never
        // takes a later Submit.
        public void SetOpen(bool open)
        {
            Show(this, open);
            if (open) { resume.Focus(); return; }
            Focusable focused = focusController?.focusedElement;
            foreach (Button button in buttons) if (focused == button) button.Blur();
            resume.Blur();
        }

        // Back from Settings: focus returns to the button that opened it.
        public void FocusSettings() { if (Open) settings.Focus(); }

        // Dev scripting presses the real Leave button path; only while the menu is open.
        public bool PressLeave()
        {
            if (!Open || !leave.enabledSelf) return false;
            OnLeave(); return true;
        }

        private void OnLeave() => LeaveMatch?.Invoke();

        // Arrows and Tab move through Resume -> Settings -> Leave match; arrows stop at the ends, Tab wraps. Focus never
        // leaves the menu while it is open (the HUD has no other focusable element).
        private void Navigate(NavigationMoveEvent evt)
        {
            int index = Array.IndexOf(buttons, evt.target as Button);
            if (index < 0) return;
            int next = index;
            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Left: next = Math.Max(0, index - 1); break;
                case NavigationMoveEvent.Direction.Down:
                case NavigationMoveEvent.Direction.Right: next = Math.Min(buttons.Length - 1, index + 1); break;
                case NavigationMoveEvent.Direction.Next: next = (index + 1) % buttons.Length; break;
                case NavigationMoveEvent.Direction.Previous: next = (index + buttons.Length - 1) % buttons.Length; break;
                default: return;
            }
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
            buttons[next].Focus();
        }
    }
}
