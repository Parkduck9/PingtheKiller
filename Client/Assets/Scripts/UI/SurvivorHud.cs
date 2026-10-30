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
        public const float Width = 672, Height = 72, Margin = 24, PromptHeight = 40, Gap = 8, MeterWidth = 304;
        private static readonly Color32 Danger = new Color32(229, 72, 77, 255);
        private readonly VisualElement root, cluster, prompt, promptBar, promptTrack, promptFill, meterTrack, meterFill, timeBar, exitBar;
        private readonly VisualElement threatStrip;
        private readonly VisualElement[] bands = new VisualElement[4]; // top, bottom, left, right (HUD_SPEC 4.2 level 3)
        private readonly Label skillName, word, tag, reason, time, terminals, exit, promptText;
        private readonly SkillRing ring;
        private readonly VisualElement[] bordered;
        private readonly Texture2D[] noise = new Texture2D[5]; // strip + four bands, rebuilt only when the mask is re-rolled
        private ThreatEffect shownEffect;
        private int shownSeed;
        private float shownFraction;
        private Vector2 shownCanvas;
        private float scale = 1;

        public SurvivorHud(VisualElement parent, Font font)
        {
            root = Box(parent, "survivor-hud");
            for (int i = 0; i < bands.Length; i++) { bands[i] = Box(root, "sband"); Show(bands[i], false); }
            prompt = Box(root, "sprompt");
            VisualElement promptLine = Box(prompt, "krow");
            promptBar = Box(promptLine, "kbar");
            promptBar.AddToClassList("danger");
            promptText = Text(promptLine, string.Empty, "km");
            Show(promptBar, false);
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
            threatStrip = Box(meter, "sthreat-strip");
            threatStrip.style.height = ThreatVisual.StripHeight;
            Show(threatStrip, false);
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
            reason.EnableInClassList("sreason-wrap", m.Wrap);
            Show(meterTrack, !m.Wrap);
            meterTrack.EnableInClassList("ally-outline", m.AllyOutline);
            Fill(meterFill, m.Fill, m.Width);
        }

        // HUD_SPEC 4.2: the meter-edge strip (levels 1-2) or the screen-edge band (level 3). seed = the mask, re-rolled by
        // ThreatVisual on a level change only; the textures are rebuilt only when the seed, the level or the canvas changes.
        public void SetThreat(ThreatEffect effect, float fraction, int seed)
        {
            Vector2 canvas = new Vector2(Screen.width, Screen.height) / Mathf.Max(.01f, scale);
            bool strip = effect == ThreatEffect.MeterStrip, band = effect == ThreatEffect.ScreenEdge;
            Show(threatStrip, strip);
            foreach (VisualElement b in bands) Show(b, band);
            if (effect == ThreatEffect.None) { shownEffect = effect; return; }
            if (effect == shownEffect && seed == shownSeed && fraction == shownFraction && (!band || canvas == shownCanvas)) return;
            shownEffect = effect; shownSeed = seed; shownFraction = fraction; shownCanvas = canvas;
            if (strip)
            {
                float width = Mathf.Round(MeterWidth * fraction);
                threatStrip.style.width = width;
                Paint(threatStrip, 0, seed, width, ThreatVisual.StripHeight, ThreatVisual.StripCell, ThreatVisual.StripDensity);
                return;
            }
            float edge = ThreatVisual.BandWidth, inner = Mathf.Max(0, canvas.y - 2 * edge);
            Place(bands[0], 0, 0, canvas.x, edge);
            Place(bands[1], 0, canvas.y - edge, canvas.x, edge);
            Place(bands[2], 0, edge, edge, inner);
            Place(bands[3], canvas.x - edge, edge, edge, inner);
            for (int i = 0; i < bands.Length; i++)
                Paint(bands[i], i + 1, unchecked(seed + 7919 * (i + 1)), bands[i].style.width.value.value, bands[i].style.height.value.value, ThreatVisual.Cell, ThreatVisual.Density);
        }

        private static void Place(VisualElement element, float left, float top, float width, float height)
        {
            element.style.left = left; element.style.top = top; element.style.width = width; element.style.height = height;
        }

        // One 1-bit texture: `danger` texels or fully transparent ones, point-filtered (no blend of palette colors).
        private void Paint(VisualElement element, int slot, int seed, float width, float height, float cell, float density)
        {
            int w = Mathf.Max(1, Mathf.CeilToInt(width / cell)), h = Mathf.Max(1, Mathf.CeilToInt(height / cell));
            bool[] mask = ThreatVisual.Mask(seed, w, h, density);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = mask[i] ? Danger : new Color32(0, 0, 0, 0);
            Texture2D texture = noise[slot];
            if (texture == null || texture.width != w || texture.height != h)
            {
                Release(texture);
                texture = noise[slot] = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "threat noise" };
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            element.style.backgroundImage = new StyleBackground(texture);
            element.MarkDirtyRepaint();
        }

        private static void Release(Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Object.Destroy(item); else Object.DestroyImmediate(item);
        }

        public void Dispose()
        {
            for (int i = 0; i < noise.Length; i++) { Release(noise[i]); noise[i] = null; }
        }

        // Read-back for tests and the dev capture.
        public ThreatEffect ThreatShown => threatStrip.style.display == DisplayStyle.Flex ? ThreatEffect.MeterStrip
            : bands[0].style.display == DisplayStyle.Flex ? ThreatEffect.ScreenEdge : ThreatEffect.None;
        public float ThreatStripWidth => threatStrip.style.width.value.value;
        public float ThreatStripHeight => threatStrip.style.height.value.value;
        // (10/30, U4e Q-02) The skill name column (ClientUI.uss .sname): wide enough for the longest skill word at S 600.
        public const float SkillNameWidth = 100, SkillBlockWidth = 176, InfoBlockWidth = 160;
        public Texture2D ThreatTexture(int slot) => noise[slot];
        public bool ReasonWraps => reason.ClassListContains("sreason-wrap");
        public bool MeterBarShown => meterTrack.style.display != DisplayStyle.None;
        public bool PromptDanger => promptBar.style.display != DisplayStyle.None;

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
            Show(promptBar, p.Danger);
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
            this.scale = scale;
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
