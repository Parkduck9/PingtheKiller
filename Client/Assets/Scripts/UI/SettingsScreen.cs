using System;
using System.Globalization;
using PingTheKiller.Client.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    public static class SettingsCopy
    {
        public const string Title = "Settings";
        public const string SurvivorFov = "Survivor field of view", KillerFov = "Killer field of view";
        public const string Sensitivity = "Mouse sensitivity", InvertY = "Invert Y", ReducedMotion = "Reduced motion", Volume = "Master volume";
        public const string FovNote = "Horizontal angle at 16:9. Applies at once.";
        public const string ReducedNote = "Lagging shows every frame instead of holding the picture at 8 Hz. Skill effects show as still rings.";
        public const string Reset = "Reset to defaults", Done = "Done";
        public static string Degrees(float value) => Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + "°";
        public static string Multiplier(float value) => value.ToString("0.00", CultureInfo.InvariantCulture) + "×";
        public static string Percent(int value) => value + " %";
        public static string OnOff(bool on) => on ? "On" : "Off";
    }

    // U7a settings: a left-aligned panel (no modal in the middle of the play view), opened from the main menu or either
    // role's play menu (HUD_SPEC 5, 6.6); closing it during a match returns the focus to that menu. Every control writes
    // to GameSettings at once; Done saves. Esc closes it outside a match (ClientApp handles Esc during a match, because
    // there it also drives the play menu).
    public sealed class SettingsScreen
    {
        private readonly VisualElement panel;
        private readonly GameSettings settings;
        private readonly Slider survivorFov, killerFov, sensitivity, volume;
        private readonly Label survivorFovValue, killerFovValue, sensitivityValue, volumeValue;
        private readonly Button invert, reduced, reset, done;
        private bool syncing;
        public event Action Closed;
        public bool Visible => panel.style.display == DisplayStyle.Flex;
        public VisualElement Panel => panel;

        public SettingsScreen(VisualElement root, GameSettings settings, Func<bool> inMatch)
        {
            this.settings = settings;
            panel = new VisualElement(); panel.AddToClassList("settings"); root.Add(panel);
            Label(panel, SettingsCopy.Title, "title");
            survivorFov = Row(SettingsCopy.SurvivorFov, GameSettings.SurvivorFovMin, GameSettings.SurvivorFovMax, 1, out survivorFovValue);
            killerFov = Row(SettingsCopy.KillerFov, GameSettings.KillerFovMin, GameSettings.KillerFovMax, 1, out killerFovValue);
            Label(panel, SettingsCopy.FovNote, "set-note");
            sensitivity = Row(SettingsCopy.Sensitivity, GameSettings.SensitivityMin, GameSettings.SensitivityMax, .05f, out sensitivityValue);
            invert = Toggle(SettingsCopy.InvertY, () => settings.InvertY = !settings.InvertY);
            reduced = Toggle(SettingsCopy.ReducedMotion, () => settings.ReducedMotion = !settings.ReducedMotion);
            Label(panel, SettingsCopy.ReducedNote, "set-note");
            volume = Row(SettingsCopy.Volume, GameSettings.VolumeMin, GameSettings.VolumeMax, 5, out volumeValue);
            var actions = new VisualElement(); actions.AddToClassList("set-actions"); panel.Add(actions);
            done = new Button(Close) { text = SettingsCopy.Done }; done.AddToClassList("primary"); actions.Add(done);
            reset = new Button(() => settings.ResetToDefaults()) { text = SettingsCopy.Reset }; actions.Add(reset);

            survivorFov.RegisterValueChangedCallback(e => { if (!syncing) settings.SurvivorFov = Mathf.Round(e.newValue); });
            killerFov.RegisterValueChangedCallback(e => { if (!syncing) settings.KillerFov = Mathf.Round(e.newValue); });
            sensitivity.RegisterValueChangedCallback(e => { if (!syncing) settings.Sensitivity = Mathf.Round(e.newValue * 20) / 20; });
            volume.RegisterValueChangedCallback(e => { if (!syncing) settings.Volume = Mathf.RoundToInt(e.newValue / 5) * 5; });
            settings.Changed += Refresh;
            panel.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape || inMatch()) return;
                e.StopPropagation(); Close();
            });
            panel.style.display = DisplayStyle.None;
            Refresh();
        }

        public void Show()
        {
            Refresh();
            panel.style.display = DisplayStyle.Flex;
            survivorFov.Focus();
        }

        public void Hide() => panel.style.display = DisplayStyle.None;

        // Done, or Esc outside a match: keep the values, write them, go back to where the screen was opened from.
        public void Close()
        {
            if (!Visible) return;
            settings.Save();
            Hide();
            Closed?.Invoke();
        }

        private void Refresh()
        {
            syncing = true;
            survivorFov.SetValueWithoutNotify(settings.SurvivorFov);
            killerFov.SetValueWithoutNotify(settings.KillerFov);
            sensitivity.SetValueWithoutNotify(settings.Sensitivity);
            volume.SetValueWithoutNotify(settings.Volume);
            syncing = false;
            survivorFovValue.text = SettingsCopy.Degrees(settings.SurvivorFov);
            killerFovValue.text = SettingsCopy.Degrees(settings.KillerFov);
            sensitivityValue.text = SettingsCopy.Multiplier(settings.Sensitivity);
            volumeValue.text = SettingsCopy.Percent(settings.Volume);
            invert.text = SettingsCopy.OnOff(settings.InvertY); invert.EnableInClassList("chosen", settings.InvertY);
            reduced.text = SettingsCopy.OnOff(settings.ReducedMotion); reduced.EnableInClassList("chosen", settings.ReducedMotion);
        }

        private static Label Label(VisualElement parent, string text, string style)
        {
            var label = new Label(text); label.AddToClassList(style); parent.Add(label); return label;
        }

        private Slider Row(string name, float min, float max, float step, out Label value)
        {
            var row = new VisualElement(); row.AddToClassList("set-row"); panel.Add(row);
            Label(row, name, "set-label");
            var slider = new Slider(min, max) { pageSize = step };
            slider.AddToClassList("set-slider"); row.Add(slider);
            value = Label(row, string.Empty, "set-value");
            return slider;
        }

        private Button Toggle(string name, Action flip)
        {
            var row = new VisualElement(); row.AddToClassList("set-row"); panel.Add(row);
            Label(row, name, "set-label");
            var button = new Button(flip); button.AddToClassList("set-toggle"); row.Add(button);
            return button;
        }
    }
}
