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
            // Up and Down (arrows, d-pad, stick) scroll by about three lines, PageUp and PageDown by a page; Back keeps focus.
            panel.RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (e.direction != NavigationMoveEvent.Direction.Up && e.direction != NavigationMoveEvent.Direction.Down) return;
                ScrollBy(e.direction == NavigationMoveEvent.Direction.Down ? LineStep : -LineStep);
                e.StopPropagation(); e.PreventDefault();
            });
            panel.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.PageDown && e.keyCode != KeyCode.PageUp) return;
                ScrollBy((e.keyCode == KeyCode.PageDown ? 1 : -1) * Mathf.Max(LineStep, scroll.contentViewport.layout.height - LineStep));
                e.StopPropagation();
            });
            panel.RegisterCallback<NavigationCancelEvent>(e => { e.StopPropagation(); Close(); });
            panel.style.display = DisplayStyle.None;
        }

        public const float LineStep = 72f; // about three lines of the 14 u body
        // Scrolls the body by delta logical units, clamped to the content (the mouse wheel and the bar still work).
        public void ScrollBy(float delta)
        {
            float max = Mathf.Max(0, scroll.contentContainer.layout.height - scroll.contentViewport.layout.height);
            scroll.scrollOffset = new Vector2(0, Mathf.Clamp(scroll.scrollOffset.y + delta, 0, max));
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
