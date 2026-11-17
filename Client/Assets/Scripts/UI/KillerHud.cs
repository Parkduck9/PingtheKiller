using System;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 6.1 bottom-left cluster (320 u wide, margin 24 u), stacked from the top: intro (3 s) or play menu,
    // interact prompt or the hold block (13.3), the collapse block (13.5), info, effect line, skill block. Under it, the
    // world marker layer (6.2) and the rules v2 world layer (13.4, 13.6). Every block has a `panel`
    // fill and a 1 u `line` border; corner radius 0. The HUD itself has no focusable element (only the play menu does).
    public sealed class KillerHud
    {
        public const float Width = 320, Margin = 24;
        private readonly VisualElement root, cluster, intro, prompt, info, effect, skill, timeBar, effectBar;
        private readonly Label introSkill, introEffect, promptTitle, promptLine, time, terminals, oneMore, effectText, skillName, skillLine, skillState, skillKey;
        // R1 (11/17): the skill block grew from 72 u to SkillHeight for the one-line effect under the name (6.1d).
        public const float SkillHeight = 88;
        private readonly SkillRing ring;
        private readonly VisualElement[] blocks;
        public MarkerLayer Markers { get; }
        // HUD_SPEC 13.4 / 13.6: exit markers, Regressing tags and the Overloaded alert.
        public V2MarkerLayer V2Markers { get; }
        public HoldActionBar Hold { get; }
        public CollapseTimer Collapse { get; }
        // HUD_SPEC 6.6: the shared play menu in its Stacked layout (skill + effect + Resume, Settings and Leave match).
        public PlayMenu Menu { get; }
        public SkillCastFlash CastFlash { get; }
        public event Action Resume { add => Menu.Resume += value; remove => Menu.Resume -= value; }
        public event Action OpenSettings { add => Menu.OpenSettings += value; remove => Menu.OpenSettings -= value; }
        public event Action LeaveMatch { add => Menu.LeaveMatch += value; remove => Menu.LeaveMatch -= value; }
        public const float MenuHeight = PlayMenu.StackedHeight;
        public bool MenuOpen => Menu.Open;

        public KillerHud(VisualElement parent, Font font)
        {
            root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList("killer-hud");
            parent.Add(root);
            Markers = new MarkerLayer(font);
            root.Add(Markers);
            V2Markers = new V2MarkerLayer(font);
            root.Add(V2Markers);
            cluster = Box(root, "killer-cluster");

            intro = Box(cluster, "kblock", "kintro");
            Text(intro, KillerHudCopy.IntroTitle, "kl", "kbold");
            introSkill = Text(intro, string.Empty, "km", "kgap");
            introEffect = Text(intro, string.Empty, "ks", "kwrap", "kgap");
            Text(intro, KillerHudCopy.IntroRules, "ks", "kwrap", "kmuted");

            Menu = new PlayMenu(PlayMenuLayout.Stacked, font);
            cluster.Add(Menu);

            prompt = Box(cluster, "kblock", "kprompt");
            promptTitle = Text(prompt, string.Empty, "ks", "kbold");
            promptLine = Text(prompt, string.Empty, "ks", "kmuted");
            Hold = new HoldActionBar(Role.Killer, font);
            cluster.Add(Hold);
            Collapse = new CollapseTimer(Role.Killer, font);
            cluster.Add(Collapse);

            info = Box(cluster, "kblock", "kinfo");
            VisualElement timeRow = Box(info, "krow");
            timeBar = Box(timeRow, "kbar");
            time = Text(timeRow, "0:00", "km", "kbold");
            terminals = Text(info, string.Empty, "km");
            oneMore = Text(info, KillerHudCopy.OneMore, "ks", "kline");

            effect = Box(cluster, "kblock", "keffect");
            effectBar = Box(effect, "kbar"); effectBar.AddToClassList("danger");
            effectText = Text(effect, string.Empty, "ks");

            skill = Box(cluster, "kblock", "kskill");
            skill.AddToClassList("klast");
            ring = new SkillRing();
            skill.Add(ring);
            VisualElement column = Box(skill, "kskill-text");
            skillName = Text(column, string.Empty, "km", "kbold");
            skillLine = Text(column, string.Empty, "ks", "kclip");
            skillState = Text(column, string.Empty, "ks", "kmuted");
            skillKey = Text(column, "[Q]", "ks", "kmuted");
            CastFlash = new SkillCastFlash(false, 8); // U9: right of the bottom-left cluster
            skill.Add(CastFlash);

            blocks = new[] { intro, Menu, prompt, info, effect, skill, Hold, Collapse };
            foreach (Label label in root.Query<Label>().ToList()) label.style.unityFontDefinition = FontDefinition.FromFont(font);
            foreach (Button button in root.Query<Button>().ToList()) button.style.unityFontDefinition = FontDefinition.FromFont(font);
            Show(root, false);
        }

        private static VisualElement Box(VisualElement parent, params string[] styles)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (string style in styles) box.AddToClassList(style);
            parent.Add(box); return box;
        }

        private static Label Text(VisualElement parent, string value, params string[] styles)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            foreach (string style in styles) label.AddToClassList(style);
            parent.Add(label); return label;
        }

        private static void Show(VisualElement element, bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) element.style.display = display;
        }

        private static void SetText(Label label, string value)
        {
            bool visible = !string.IsNullOrEmpty(value);
            Show(label, visible);
            if (visible && label.text != value) label.text = value;
        }

        public void SetVisible(bool visible) => Show(root, visible);

        // Match start: intro on, menu off, copy for the skill.
        public void Begin(byte skillId)
        {
            introSkill.text = KillerHudCopy.SkillName(skillId);
            introEffect.text = KillerHudCopy.SkillEffect(skillId);
            Menu.SetContent(introSkill.text, introEffect.text);
            skillName.text = KillerHudCopy.SkillName(skillId);
            skillLine.text = PlayMenuCopy.KillerSkillLine(skillId);
            Show(skillKey, true);
            // R3: the IntroCard replaces the old 3 s block, so it starts hidden.
            Show(intro, false); Menu.SetOpen(false); Show(prompt, false); Show(effect, false); Hold.Hide(); CastFlash.Hide();
            Collapse.Set(new CollapseView { GlobalLeft = -1, PersonalLeft = -1 });
            Show(root, true);
        }

        public void SetIntro(bool visible) => Show(intro, visible && !MenuOpen);

        // HUD_SPEC 6.6: the menu replaces the intro and prompt area; Resume takes focus.
        public void FocusMenu() => Menu.FocusSettings();
        public void SetMenu(bool open)
        {
            Menu.SetOpen(open);
            if (open) { Show(intro, false); Show(prompt, false); Hold.Hide(); }
        }

        public void SetInfo(float timeLeft, int done, int needed)
        {
            string value = KillerHudCopy.Time(timeLeft);
            if (time.text != value) time.text = value;
            Show(timeBar, KillerHudCopy.TimePressure(timeLeft));
            SetText(terminals, KillerHudCopy.Terminals(done, needed));
            // Line 3 keeps its slot (the block is a fixed 72 u); only its text toggles.
            oneMore.style.visibility = KillerHudCopy.OneMoreLine(done, needed) != null ? Visibility.Visible : Visibility.Hidden;
        }

        // muted: a HUD_SPEC 14.3 reason line (no bar, `muted` text).
        public void SetEffect(string text, bool danger, bool muted = false)
        {
            Show(effect, !string.IsNullOrEmpty(text));
            if (!string.IsNullOrEmpty(text) && effectText.text != text) effectText.text = text;
            Show(effectBar, danger);
            effectText.EnableInClassList("kmuted", muted && !danger);
        }

        public string EffectText => effect.style.display == DisplayStyle.None ? null : effectText.text;
        public bool EffectMuted => effectText.ClassListContains("kmuted");
        public bool EffectDanger => effectBar.style.display != DisplayStyle.None;

        // HUD_SPEC 13.3: the hold block takes the prompt slot while a kick or a hatch close runs; otherwise the interact
        // prompt (kick, hatch close) shows when in reach. Both hide while the play menu is open.
        public void SetPrompt(InteractPrompt interact, in HoldActionView hold)
        {
            bool holding = hold.Kind != HoldKind.None && !MenuOpen;
            if (holding) Hold.Set(hold, "[E]"); else Hold.Hide();
            bool visible = !holding && !MenuOpen && !string.IsNullOrEmpty(interact.Text);
            Show(prompt, visible);
            if (!visible) return;
            if (promptTitle.text != interact.Text) promptTitle.text = interact.Text;
            SetText(promptLine, interact.Hint);
        }

        public string PromptText => prompt.style.display == DisplayStyle.None ? null : promptTitle.text;
        // R3 (HUD_SPEC 6.1p): true when neither the prompt nor the hold block shows, so a first-time hint may use the slot.
        public bool PromptFree => !MenuOpen && prompt.style.display == DisplayStyle.None && Hold.style.display == DisplayStyle.None;

        // HUD_SPEC 13.5: the global collapse clock (hidden while it does not run).
        public void SetCollapse(in CollapseView view) => Collapse.Set(view);

        // 6.1d. self = the S2C_SelfState clock; null or not yet received this match: no cooldown ring, Ready only.
        // M4: stunned (the own Killer entry's Chase Stunned flag, set by ChasePropsLayer.ApplyKillerHud) shows the block unavailable.
        public bool Stunned { get; set; }

        // R1 (11/17): no Killer skill is passive or waits for a switch any more.
        public static SkillRing.State RingState(float? cooldown, bool stunned)
            => stunned ? SkillRing.State.Disabled
                : cooldown.HasValue && cooldown.Value > 0 ? SkillRing.State.Cooldown : SkillRing.State.Ready;

        public string SkillLineText => skillLine.text;

        public void SetSkill(byte skillId, ISelfState self, float activeFraction)
        {
            float? cooldown = self != null && self.Available ? self.SkillCooldown : (float?)null;
            string state = KillerHudCopy.SkillState(skillId, cooldown, Stunned);
            if (skillState.text != state) skillState.text = state;
            SkillRing.State ringState = RingState(cooldown, Stunned);
            float fraction = cooldown.HasValue ? cooldown.Value / GameConstants.KillerSkillCooldownSeconds : 0;
            ring.Set(skillId, ringState, fraction, activeFraction, false);
        }

        // The cluster rectangle in panel units, for the edge-indicator keep-out. Computed from the shown blocks and their
        // fixed HUD_SPEC 6.1 heights (layout lags one frame behind a block being shown or hidden).
        public Rect ClusterRect(float screenHeight)
        {
            float height = 72 + 8 + SkillHeight; // info + skill
            if (Visible(intro)) height += 208 + 8;
            else if (Visible(Menu)) height += MenuHeight + 8;
            if (Visible(prompt)) height += 56 + 8;
            if (Visible(Hold)) height += 72 + 8;
            if (Visible(Collapse)) height += 32 + 8;
            if (Visible(effect)) height += 32 + 8;
            return new Rect(Margin, screenHeight - Margin - height, Width, height);
        }

        private static bool Visible(VisualElement element) => element.style.display != DisplayStyle.None;

        // 1 u borders snap to whole pixels (HUD_SPEC 0.2): 1 physical px at every panel scale.
        public void SetScale(float scale)
        {
            float border = Mathf.Max(1, Mathf.Round(scale)) / scale;
            foreach (VisualElement block in blocks)
            {
                block.style.borderTopWidth = border; block.style.borderBottomWidth = border;
                block.style.borderLeftWidth = border; block.style.borderRightWidth = border;
            }
            Markers.Scale = scale; ring.Scale = scale; V2Markers.Scale = scale; CastFlash.SetScale(scale);
            Markers.MarkDirtyRepaint(); ring.MarkDirtyRepaint();
        }
    }
}
