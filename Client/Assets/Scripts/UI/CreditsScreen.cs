using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // S3-credits (HUD_SPEC 1.8): the Credits and licences screen, opened from the main menu. Built like the How to play screen:
    // left-aligned, no modal, a scrolling body and a Back button. Esc, gamepad B (NavigationCancel) or Back closes it.
    public sealed class CreditsScreen
    {
        private readonly VisualElement panel;
        private readonly ScrollView scroll;
        private readonly Button back;
        private readonly Font font;
        private readonly List<string> texts = new List<string>();
        public event Action Closed;
        public bool Visible => panel.style.display == DisplayStyle.Flex;
        public VisualElement Panel => panel;
        public ScrollView Scroll => scroll;
        // Every label in order (tests and the dev log).
        public IReadOnlyList<string> Texts => texts;

        public CreditsScreen(VisualElement root, Font value)
        {
            font = value;
            panel = new VisualElement(); panel.AddToClassList("credits"); root.Add(panel);
            Label(panel, CreditsCopy.Title, "title");
            scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("cr-scroll"); panel.Add(scroll);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Label(scroll.contentContainer, CreditsCopy.CreditsHeading, "htp-h");
            foreach (string line in CreditsCopy.Lines) Label(scroll.contentContainer, line, "htp-t");
            Label(scroll.contentContainer, CreditsCopy.LicencesHeading, "htp-h");
            Label(scroll.contentContainer, CreditsCopy.InterNotice, "htp-t");
            Label(scroll.contentContainer, CreditsCopy.OflText(), "cr-ofl");
            Label(scroll.contentContainer, CreditsCopy.DoTweenNotice, "htp-t");
            back = new Button(Close) { text = CreditsCopy.Back }; back.AddToClassList("primary"); panel.Add(back);
            panel.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                e.StopPropagation(); Close();
            });
            panel.RegisterCallback<NavigationCancelEvent>(e => { e.StopPropagation(); Close(); });
            panel.style.display = DisplayStyle.None;
        }

        public void Show()
        {
            scroll.scrollOffset = Vector2.zero;
            panel.style.display = DisplayStyle.Flex;
            back.Focus();
        }

        public void Hide() => panel.style.display = DisplayStyle.None;

        public void Close()
        {
            if (!Visible) return;
            Hide();
            Closed?.Invoke();
        }

        private void Label(VisualElement parent, string text, string style)
        {
            var label = new Label(text); label.AddToClassList(style);
            label.style.unityFontDefinition = FontDefinition.FromFont(font);
            parent.Add(label);
            texts.Add(text);
        }
    }
}
