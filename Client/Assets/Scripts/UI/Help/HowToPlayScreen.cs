using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 1.7 (11/17 R3): the How to play screen, opened from the main menu under Offline. Two pages, Survivor and Killer,
    // each with the goal, the controls, the status ladder (Normal, Lagging, Eliminated and how a rescue works), Signal and
    // Silent in one line, and every skill of the role with its effect. Left-aligned like Settings (no modal), Esc or Back
    // returns to the menu. All copy is HelpCopy.Page; skill names and effects are the existing tables'.
    public sealed class HowToPlayScreen
    {
        private readonly VisualElement panel, body;
        private readonly Button survivorTab, killerTab, back;
        private readonly Font font;
        private readonly List<string> texts = new List<string>();
        public Role Page { get; private set; } = Role.Survivor;
        public event Action Closed;
        public bool Visible => panel.style.display == DisplayStyle.Flex;
        public VisualElement Panel => panel;
        // Every label of the current page, in order (tests and the dev log).
        public IReadOnlyList<string> Texts => texts;

        public HowToPlayScreen(VisualElement root, Font value)
        {
            font = value;
            panel = new VisualElement(); panel.AddToClassList("howto"); root.Add(panel);
            Label(panel, HelpCopy.HowToPlay, "title");
            var tabs = new VisualElement(); tabs.AddToClassList("row"); panel.Add(tabs);
            survivorTab = new Button(() => SetPage(Role.Survivor)) { text = HelpCopy.SurvivorTab }; tabs.Add(survivorTab);
            killerTab = new Button(() => SetPage(Role.Killer)) { text = HelpCopy.KillerTab }; tabs.Add(killerTab);
            body = new VisualElement(); body.AddToClassList("htp-body"); panel.Add(body);
            back = new Button(Close) { text = HelpCopy.Back }; back.AddToClassList("primary"); panel.Add(back);
            panel.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                e.StopPropagation(); Close();
            });
            panel.style.display = DisplayStyle.None;
            SetPage(Role.Survivor);
        }

        public void Show(Role page)
        {
            SetPage(page);
            panel.style.display = DisplayStyle.Flex;
            (page == Role.Killer ? killerTab : survivorTab).Focus();
        }

        public void Hide() => panel.style.display = DisplayStyle.None;

        public void Close()
        {
            if (!Visible) return;
            Hide();
            Closed?.Invoke();
        }

        public void SetPage(Role role)
        {
            Page = role == Role.Killer ? Role.Killer : Role.Survivor;
            survivorTab.EnableInClassList("chosen", Page == Role.Survivor);
            killerTab.EnableInClassList("chosen", Page == Role.Killer);
            body.Clear(); texts.Clear();
            HelpPage page = HelpCopy.Page(Page);
            Section("How to win", new[] { page.Goal });
            Section("Controls", new[] { page.Controls });
            Section("Status", page.Ladder);
            Section("Signal and Silent", new[] { page.Visibility });
            Skills(page.Skills);
        }

        private void Section(string heading, string[] lines)
        {
            Label(body, heading, "htp-h");
            foreach (string line in lines) Label(body, line, "htp-t");
        }

        private void Skills(HelpSkill[] skills)
        {
            Label(body, "Skills", "htp-h");
            foreach (HelpSkill skill in skills)
            {
                var row = new VisualElement(); row.AddToClassList("htp-skill"); body.Add(row);
                Label(row, skill.Name, "htp-sn");
                Label(row, skill.Effect, "htp-se");
                texts.Add(skill.Name + " " + skill.Effect);
            }
        }

        private Label Label(VisualElement parent, string text, string style)
        {
            var label = new Label(text); label.AddToClassList(style);
            label.style.unityFontDefinition = FontDefinition.FromFont(font);
            parent.Add(label);
            if (parent == body || parent == panel) texts.Add(text);
            return label;
        }
    }
}
