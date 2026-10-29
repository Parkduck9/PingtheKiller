using PingTheKiller.Client.Killer;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 4.0 Survivor cluster: bottom-center, 24 u above the bottom edge, 672x72 u, `panel` fill, 1 u `line` border,
    // three blocks [8] skill 168 [8] transmit meter 304 [8] info 168 [8]; the prompt row (672x40) 8 u above it, only when
    // a prompt applies. Every value comes from SurvivorReadout; this class only displays it.
    public sealed class SurvivorHud
    {
        public const float Width = 672, Height = 72, Margin = 24, PromptHeight = 40, Gap = 8;
        private readonly VisualElement root, cluster, prompt, promptTrack, promptFill, meterTrack, meterFill, timeBar, exitBar;
        private readonly Label skillName, word, tag, reason, time, terminals, exit, promptText;
        private readonly SkillRing ring;
        private readonly VisualElement[] bordered;

        public SurvivorHud(VisualElement parent, Font font)
        {
            root = Box(parent, "survivor-hud");
            prompt = Box(root, "sprompt");
            promptText = Text(prompt, string.Empty, "km");
            promptTrack = Box(prompt, "sprompt-track");
            promptFill = Box(promptTrack, "sfill");

            cluster = Box(root, "scluster");
            VisualElement skill = Box(cluster, "sskill");
            ring = new SkillRing { SurvivorSkills = true };
            skill.Add(ring);
            VisualElement column = Box(skill, "sskill-text");
            skillName = Text(column, string.Empty, "ks", "kbold", "sname");
            Text(column, "[Q]", "ks", "kmuted");

            VisualElement meter = Box(cluster, "smeter");
            VisualElement rowA = Box(meter, "srow-a");
            word = Text(rowA, string.Empty, "km", "kbold");
            tag = Text(rowA, string.Empty, "ks", "stag");
            reason = Text(meter, string.Empty, "ks", "sreason");
            meterTrack = Box(meter, "smeter-track");
            meterFill = Box(meterTrack, "sfill");

            VisualElement info = Box(cluster, "sinfo");
            VisualElement line1 = Box(info, "krow");
            timeBar = Box(line1, "kbar");
            time = Text(line1, "0:00", "km", "kbold");
            terminals = Text(info, string.Empty, "km");
            VisualElement line3 = Box(info, "krow");
            exitBar = Box(line3, "kbar");
            exit = Text(line3, string.Empty, "km");

            bordered = new[] { cluster, prompt, meterTrack };
            foreach (Label label in root.Query<Label>().ToList()) label.style.unityFontDefinition = FontDefinition.FromFont(font);
            Show(prompt, false);
        }

        public VisualElement Root => root;
        public SkillRing Ring => ring;

        private static VisualElement Box(VisualElement parent, string style)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList(style);
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
            if (label.text != (value ?? string.Empty)) label.text = value ?? string.Empty;
        }

        private static void Fill(VisualElement fill, BarFill kind, float width)
        {
            fill.EnableInClassList("fill-ally", kind == BarFill.Ally);
            fill.EnableInClassList("fill-danger", kind == BarFill.Danger);
            fill.EnableInClassList("fill-signal", kind == BarFill.Signal);
            fill.EnableInClassList("fill-text", kind == BarFill.Text);
            float percent = kind == BarFill.Empty || float.IsNaN(width) ? 0 : Mathf.Clamp01(width) * 100;
            var length = new Length(percent, LengthUnit.Percent);
            if (fill.style.width != length) fill.style.width = length;
        }

        // Match start: the skill name (HUD_SPEC 8, max 2 lines in 80 u) and the key glyph; no prompt.
        public void Begin(byte skillId)
        {
            SetText(skillName, SurvivorReadout.SkillName(skillId));
            Show(prompt, false);
        }

        public void SetMeter(MeterState m)
        {
            SetText(word, m.Word);
            word.EnableInClassList("ssignal", m.WordSignal);
            SetText(tag, m.Tag);
            Show(tag, !string.IsNullOrEmpty(m.Tag));
            tag.EnableInClassList("kmuted", m.TagMuted);
            SetText(reason, m.Reason);
            reason.EnableInClassList("kmuted", m.ReasonMuted);
            meterTrack.EnableInClassList("ally-outline", m.AllyOutline);
            Fill(meterFill, m.Fill, m.Width);
        }

        // Info block: time (4.9), terminals (4.3), exit (4.7).
        public void SetInfo(float timeLeft, int done, int needed, ExitLine line, float? lockedLeft)
        {
            SetText(time, KillerHudCopy.Time(timeLeft));
            Show(timeBar, KillerHudCopy.TimePressure(timeLeft));
            SetText(terminals, SurvivorReadout.TerminalsLine(done, needed));
            terminals.EnableInClassList("kbold", needed > 0 && done >= needed);
            SetText(exit, SurvivorReadout.ExitText(line, lockedLeft));
            exit.EnableInClassList("kmuted", line == ExitLine.Locked);
            Show(exitBar, line != ExitLine.Locked);
            exitBar.EnableInClassList("fill-ally", line == ExitLine.Open);
            exitBar.EnableInClassList("danger", line == ExitLine.LockedByKiller);
        }

        // 4.8: cooldown = SelfState.SkillCooldown when available (null: no cooldown ring). activeFraction < 0 = none.
        public void SetSkill(byte skillId, SkillRing.State state, float? cooldown, float activeFraction, bool signalArc)
        {
            float fraction = cooldown.HasValue ? cooldown.Value / GameConstants.SurvivorSkillCooldownSeconds : 0;
            ring.Set(skillId, state, fraction, activeFraction, signalArc);
            ring.SetCount(state == SkillRing.State.Cooldown && cooldown.HasValue ? SurvivorReadout.Ceil(cooldown.Value).ToString() : null);
            skillName.EnableInClassList("kmuted", state == SkillRing.State.Disabled);
        }

        public void SetPrompt(PromptState p)
        {
            bool visible = !string.IsNullOrEmpty(p.Text);
            Show(prompt, visible);
            if (!visible) return;
            SetText(promptText, p.Text);
            promptText.EnableInClassList("kmuted", p.Muted);
            Show(promptTrack, p.Bar != BarFill.Empty);
            Fill(promptFill, p.Bar, p.Width);
        }

        // Read-back for tests and the dev log.
        public string Word => word.text;
        public string Reason => reason.text;
        public string Tag => tag.style.display == DisplayStyle.None ? null : tag.text;
        public string PromptText => prompt.style.display == DisplayStyle.None ? null : promptText.text;
        public float MeterFillPercent => meterFill.style.width.value.value;
        public float PromptFillPercent => promptFill.style.width.value.value;
        public bool MeterFillHas(string kind) => meterFill.ClassListContains(kind);
        public bool PromptFillHas(string kind) => promptFill.ClassListContains(kind);
        public string SkillCount => ring.Count;

        // The cluster plus the prompt row, in panel units (for the edge markers' keep-out, HUD_SPEC 4.6).
        public Rect ClusterRect(Vector2 screen)
        {
            float height = Height + (prompt.style.display == DisplayStyle.None ? 0 : PromptHeight + Gap);
            return new Rect((screen.x - Width) * .5f, screen.y - Margin - height, Width, height);
        }

        public void SetVisible(bool visible) => Show(root, visible);

        // 1 u borders snap to whole pixels (HUD_SPEC 0.2): 1 physical px at every panel scale.
        public void SetScale(float scale)
        {
            float border = Mathf.Max(1, Mathf.Round(scale)) / scale;
            foreach (VisualElement block in bordered)
            {
                block.style.borderTopWidth = border; block.style.borderBottomWidth = border;
                block.style.borderLeftWidth = border; block.style.borderRightWidth = border;
            }
            ring.Scale = scale; ring.MarkDirtyRepaint();
        }
    }
}
