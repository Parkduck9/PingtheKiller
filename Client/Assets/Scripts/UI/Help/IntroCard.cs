using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 3 (11/17 R3): when the card shows, what it says and for how long. Pure; ClientApp feeds the clock and the keys.
    // It shows from the match start for HelpCopy.IntroSeconds and never shows under the play menu. Esc, Enter, Space or a mouse
    // click ends it at once (INTEG-R, R3 QA minor 4); movement, E and Q do not, so the first steps never hide it by accident.
    // It never blocks input and has no animation, so Reduced motion needs no variant.
    public sealed class IntroCardState
    {
        private Role role;
        private byte skill;
        private int terminals;
        private double startedAt = double.NegativeInfinity;
        private bool dismissed = true;

        public void Begin(Role value, byte skillId, double now)
        {
            role = value; skill = skillId; terminals = 0; startedAt = now; dismissed = false;
        }

        public void End() { dismissed = true; }

        // TerminalsRequired from a WorldState (0 until the first one arrives).
        public void SetTerminals(int required) { if (required > 0) terminals = required; }

        public void Dismiss() { dismissed = true; }

        // The inputs that end the card: Esc, Enter (either), Space or a mouse button press. WASD, arrows, E and Q never do.
        // (11/18, M1) A gamepad A or B press ends it too (`pad`).
        public static bool DismissInput(bool escape, bool enter, bool space, bool click, bool pad = false) => escape || enter || space || click || pad;

        // INTEG-R minors: Esc while the card shows dismisses it and nothing else (the menu does not open that press, so the card
        // can never come back when the menu closes). Returns true when the press was consumed.
        public bool ConsumeEscape(double now, bool menuOpen, bool escape)
        {
            if (!escape || !Visible(now, menuOpen)) return false;
            Dismiss(); return true;
        }

        // (11/18, M1) Esc or the gamepad Start while the card shows: the same, so Start never opens the menu with the card
        // waiting behind it.
        public bool ConsumeBack(double now, bool menuOpen, bool escape, bool start) => ConsumeEscape(now, menuOpen, escape || start);

        public bool Visible(double now, bool menuOpen) => !dismissed && !menuOpen && now - startedAt < HelpCopy.IntroSeconds;

        public Role Role => role;
        public string Goal => HelpCopy.IntroGoal(role, terminals);
        public string SkillLine => HelpCopy.IntroSkillLine(role, skill);
        public string Effect => HelpCopy.SkillEffect(role, skill);
        public string Controls => HelpCopy.Controls(role);
    }

    // The 672 u match intro card, bottom-center. Survivor: in the prompt row's slot, directly above the cluster (its top edge
    // stays below the character's feet, HUD_SPEC 4.10: 900 - 104 - 134 = 662 u at 720p, feet at most 622 u). Killer: bottom-center,
    // clear of the bottom-left cluster (no body in view). Lines: goal (M 600), skill + key + cooldown (M), effect (S), controls (S muted).
    public sealed class IntroCard : VisualElement
    {
        public const float Width = 672, SurvivorBottom = 104, KillerBottom = 24;
        private readonly Label goal, skill, effect, controls;

        public IntroCard(Font font)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("introcard");
            goal = Line("km", "kbold");
            skill = Line("km");
            effect = Line("ks", "ic-wrap");
            controls = Line("ks", "ic-wrap", "kmuted");
            skill.style.marginTop = 4; controls.style.marginTop = 4;
            foreach (Label label in this.Query<Label>().ToList()) label.style.unityFontDefinition = FontDefinition.FromFont(font);
            style.display = DisplayStyle.None;
        }

        private Label Line(params string[] styles)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            foreach (string name in styles) label.AddToClassList(name);
            Add(label); return label;
        }

        public bool Shown => style.display != DisplayStyle.None;
        public string GoalText => goal.text;
        public string SkillText => skill.text;
        public string EffectText => effect.text;
        public string ControlsText => controls.text;

        public void Set(IntroCardState state, bool visible)
        {
            if (visible)
            {
                EnableInClassList("ic-killer", state.Role == Role.Killer);
                EnableInClassList("ic-survivor", state.Role != Role.Killer);
                SetText(goal, state.Goal); SetText(skill, state.SkillLine); SetText(effect, state.Effect); SetText(controls, state.Controls);
            }
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (style.display != display) style.display = display;
        }

        // 1 u border snapped to whole pixels (HUD_SPEC 0.2).
        public void SetScale(float scale)
        {
            float border = Mathf.Max(1, Mathf.Round(scale)) / scale;
            style.borderTopWidth = border; style.borderBottomWidth = border; style.borderLeftWidth = border; style.borderRightWidth = border;
        }

        private static void SetText(Label label, string value) { if (label.text != value) label.text = value; }
    }
}
