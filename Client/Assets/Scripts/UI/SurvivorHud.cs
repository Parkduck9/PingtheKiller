using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI
{
    // HUD_SPEC 4.0 Survivor cluster: bottom-center, 24 u above the bottom edge, 672x72 u, `panel` fill, 1 u `line` border,
    // three blocks [8] skill 168 [8] transmit meter 304 [8] info 168 [8]. Above it the HUD_SPEC 13.1 stack, bottom-up with
    // 8 u gaps: the objective row (exits + collapse, 672x48), the prompt row or the hold row (672x40), the route panel
    // (320x80, 11/17 R2); each only while it applies. Every value comes from SurvivorReadout / V2Wire; this class only displays it.
    public sealed class SurvivorHud
    {
        public const float Width = 672, Height = 72, Margin = 24, PromptHeight = 40, Gap = 8, MeterWidth = 304;
        private static readonly Color32 Danger = new Color32(229, 72, 77, 255);
        private readonly VisualElement root, cluster, stack, prompt, promptBar, promptTrack, promptFill, meterTrack, meterFill, timeBar, result, resultBar;
        private readonly VisualElement threatStrip;
        private readonly VisualElement[] bands = new VisualElement[4]; // top, bottom, left, right (HUD_SPEC 4.2 level 3)
        private readonly Label skillName, word, tag, reason, time, terminals, promptText, resultText;
        private readonly SkillRing ring;
        private readonly VisualElement[] bordered;
        private readonly Texture2D[] noise = new Texture2D[5]; // strip + four bands, rebuilt only when the mask is re-rolled
        private ThreatEffect shownEffect;
        private int shownSeed;
        private float shownFraction;
        private Vector2 shownCanvas;
        private float scale = 1;

        public RouteTracker Route { get; } = new RouteTracker();
        public RoutePanel RoutePanel { get; }
        public HoldActionBar Hold { get; }
        public ObjectiveRow Objective { get; }
        // HUD_SPEC 5: the shared play menu in its Row layout, in the prompt row's slot of the 13.1 stack (it grows upward).
        public PlayMenu Menu { get; }
        private byte menuSkill;

        public SurvivorHud(VisualElement parent, Font font)
        {
            root = Box(parent, "survivor-hud");
            for (int i = 0; i < bands.Length; i++) { bands[i] = Box(root, "sband"); Show(bands[i], false); }
            V2Markers = new V2MarkerLayer(font);
            root.Add(V2Markers);
            stack = Box(root, "v2-survivor-stack");
            RoutePanel = new RoutePanel(Route, font);
            stack.Add(RoutePanel);
            // HUD_SPEC 14.1a: the skill result line, directly above the prompt row.
            result = Box(stack, "sresult");
            VisualElement resultLine = Box(result, "krow");
            resultBar = Box(resultLine, "kbar");
            resultText = Text(resultLine, string.Empty, "km");
            prompt = Box(stack, "sprompt");
            VisualElement promptLine = Box(prompt, "krow");
            promptBar = Box(promptLine, "kbar");
            promptBar.AddToClassList("danger");
            promptText = Text(promptLine, string.Empty, "km");
            Show(promptBar, false);
            promptTrack = Box(prompt, "sprompt-track");
            promptFill = Box(promptTrack, "sfill");
            Menu = new PlayMenu(PlayMenuLayout.Row, font);
            stack.Add(Menu);
            Hold = new HoldActionBar(Role.Survivor, font);
            stack.Add(Hold);
            Objective = new ObjectiveRow(font);
            stack.Add(Objective);

            cluster = Box(root, "scluster");
            VisualElement skill = Box(cluster, "sskill");
            ring = new SkillRing { SurvivorSkills = true };
            skill.Add(ring);
            VisualElement column = Box(skill, "sskill-text");
            skillName = Text(column, string.Empty, "ks", "kbold", "sname");
            Text(column, "[Q]", "ks", "kmuted");
            CastFlash = new SkillCastFlash(true, 16); // U9: left of the cluster, 8 u outside its border
            skill.Add(CastFlash);

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

            bordered = new[] { cluster, prompt, result, meterTrack, Hold, Objective, Menu };
            foreach (Label label in root.Query<Label>().ToList()) label.style.unityFontDefinition = FontDefinition.FromFont(font);
            Show(prompt, false);
            Show(result, false);
        }

        public VisualElement Root => root;
        public SkillRing Ring => ring;
        public SkillCastFlash CastFlash { get; }

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
        // HUD_SPEC 13.6 world layer: the Survivor's Regressing tags (drawn under the cluster and the stack).
        public V2MarkerLayer V2Markers { get; }
        public int RegressingShown { get; private set; }

        public const float TerminalPointHeight = 1.5f;

        // Every frame: a Regressing tag above each Regressing terminal whose 1.5 m point is on screen, kept out of the cluster
        // and stack keep-out; nothing off screen and no edge indicator.
        public void DrawTerminalFlags(PingTheKiller.Shared.Packets.S2C_WorldState world, World.ViewPose pose)
        {
            Vector2 screen = new Vector2(V2Markers.layout.width, V2Markers.layout.height);
            if (float.IsNaN(screen.x) || screen.x <= 0 || float.IsNaN(screen.y) || screen.y <= 0)
                screen = new Vector2(Screen.width, Screen.height) / Mathf.Max(.01f, scale);
            DrawTerminalFlags(world, pose, screen);
        }

        public void DrawTerminalFlags(PingTheKiller.Shared.Packets.S2C_WorldState world, World.ViewPose pose, Vector2 screen)
        {
            V2Markers.Begin();
            RegressingShown = 0;
            if (world?.Terminals != null)
            {
                Rect safe = EdgePlacement.SafeRect(screen, false), keepOut = EdgePlacement.KeepOut(ClusterRect(screen));
                foreach (TerminalState terminal in world.Terminals)
                {
                    TerminalFlagsView flags = V2Wire.Terminal(terminal);
                    if (!flags.Regressing) continue;
                    if (!pose.ToViewport(new Vector3(terminal.X, TerminalPointHeight, terminal.Z), out Vector2 v) || v.x < 0 || v.x > 1 || v.y < 0 || v.y > 1) continue;
                    var p = new Vector2(v.x * screen.x, (1 - v.y) * screen.y);
                    if (!safe.Contains(p)) continue;
                    V2Markers.Regressing(EdgePlacement.ClampOutside(p, keepOut, safe), flags, Role.Survivor);
                    RegressingShown++;
                }
            }
            V2Markers.End();
        }

        public void Begin(byte skillId)
        {
            V2Markers.Begin(); V2Markers.End(); RegressingShown = 0;
            menuSkill = skillId; Menu.SetOpen(false); Menu.SetNote(null);
            SetText(skillName, SurvivorReadout.SkillName(skillId));
            Show(prompt, false);
            Show(result, false);
            CastFlash.Hide();
            Hold.Hide();
            Objective.Set(null, new CollapseView { GlobalLeft = -1, PersonalLeft = -1 });
            Route.Reset();
            RoutePanel.Refresh(false, null, 0);
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

        // The meter strip keeps the mask's cells but stacks each column from the bottom (texture row 0), so it reads as a
        // ragged interference bar rising off the meter, not as a line of tiny red glyphs pressed against the cluster's top
        // border (U6c-wire B1). The density and the seed's column counts are unchanged.
        public static bool[] Columns(bool[] mask, int w, int h)
        {
            var stacked = new bool[mask.Length];
            for (int x = 0; x < w; x++)
            {
                int count = 0;
                for (int y = 0; y < h; y++) if (mask[y * w + x]) count++;
                for (int y = 0; y < count; y++) stacked[y * w + x] = true;
            }
            return stacked;
        }

        // One 1-bit texture: `danger` texels or fully transparent ones, point-filtered (no blend of palette colors).
        private void Paint(VisualElement element, int slot, int seed, float width, float height, float cell, float density)
        {
            int w = Mathf.Max(1, Mathf.CeilToInt(width / cell)), h = Mathf.Max(1, Mathf.CeilToInt(height / cell));
            bool[] mask = ThreatVisual.Mask(seed, w, h, density);
            if (slot == 0) mask = Columns(mask, w, h);
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

        // Info block: time (4.9), terminals (4.3). The v1 line 3 (`Exit …`) is gone; the objective row replaces it (13.1).
        public void SetInfo(float timeLeft, int done, int needed)
        {
            SetText(time, KillerHudCopy.Time(timeLeft));
            Show(timeBar, KillerHudCopy.TimePressure(timeLeft));
            SetText(terminals, SurvivorReadout.TerminalsLine(done, needed));
            terminals.EnableInClassList("kbold", needed > 0 && done >= needed);
        }

        // HUD_SPEC 13.1 / 13.4 / 13.5: both exits and the collapse clock, shown once an exit is Powered or Open or a clock runs.
        public void SetObjective(ExitView[] exits, in CollapseView collapse) => Objective.Set(exits, collapse);

        // HUD_SPEC 13.2 (11/17, R2): the route panel follows the tracker every frame. visible: keys captured and Normal;
        // glyph: RouteCopy.KeyboardGlyph or PadGlyph; reduced: Reduced motion (no pulse growth, no shake).
        public void SetRoute(bool visible, string glyph, bool reduced, double now)
        {
            RoutePanel.Reduced = reduced;
            RoutePanel.Refresh(visible, glyph, now);
        }

        // 4.8: cooldown = SelfState.SkillCooldown when available (null: no cooldown ring). activeFraction < 0 = none.
        public void SetSkill(byte skillId, SkillRing.State state, float? cooldown, float activeFraction, bool signalArc)
        {
            float fraction = cooldown.HasValue ? cooldown.Value / GameConstants.SurvivorSkillCooldownSeconds : 0;
            ring.Set(skillId, state, fraction, activeFraction, signalArc);
            ring.SetCount(state == SkillRing.State.Cooldown && cooldown.HasValue ? SurvivorReadout.Ceil(cooldown.Value).ToString() : null);
            skillName.EnableInClassList("kmuted", state == SkillRing.State.Disabled);
        }

        // HUD_SPEC 5: the menu takes the prompt row's place; the prompt and hold rows stay hidden while it is open (the
        // per-frame SetPrompt cannot bring them back). The meter cluster, objective row, result line and route panel stay.
        public bool MenuOpen => Menu.Open;
        public void SetMenu(bool open)
        {
            if (open) Menu.SetContent(SurvivorReadout.SkillName(menuSkill), PlayMenuCopy.SurvivorEffect(menuSkill));
            Menu.SetOpen(open);
            if (open) { Show(prompt, false); Hold.Hide(); }
        }
        public void FocusMenu() => Menu.FocusSettings();
        // Line 3 (PlayMenuCopy.CaptureNote), refreshed every frame from the own entry.
        public void SetMenuNote(string note) => Menu.SetNote(note);

        // The prompt row, or the 13.3 hold row in its place while the own exit switch hold runs.
        public void SetPrompt(PromptState p, in HoldActionView hold)
        {
            if (MenuOpen) { Show(prompt, false); Hold.Hide(); return; }
            if (hold.Kind != HoldKind.None)
            {
                Show(prompt, false);
                Hold.Set(hold, "[E]");
                return;
            }
            Hold.Hide();
            SetPrompt(p);
        }

        public void SetPrompt(PromptState p)
        {
            bool visible = !MenuOpen && !string.IsNullOrEmpty(p.Text);
            Show(prompt, visible);
            if (!visible) return;
            SetText(promptText, p.Text);
            promptText.EnableInClassList("kmuted", p.Muted);
            Show(promptBar, p.Danger);
            Show(promptTrack, p.Bar != BarFill.Empty);
            Fill(promptFill, p.Bar, p.Width);
        }

        // HUD_SPEC 14.1a result line: M `text` after a 4x18 u bar in the line's colour (none for a `muted` reason line).
        public void SetResult(FxLine line)
        {
            Show(result, line.Shown);
            if (!line.Shown) return;
            SetText(resultText, line.Text);
            resultText.EnableInClassList("kmuted", line.Tone == LineTone.Reason);
            Show(resultBar, line.Tone == LineTone.Danger || line.Tone == LineTone.Signal || line.Tone == LineTone.Ally);
            resultBar.EnableInClassList("danger", line.Tone == LineTone.Danger);
            resultBar.EnableInClassList("fill-signal", line.Tone == LineTone.Signal);
            resultBar.EnableInClassList("fill-ally", line.Tone == LineTone.Ally);
        }

        public string ResultText => result.style.display == DisplayStyle.None ? null : resultText.text;
        public bool ResultBarHas(string kind) => resultBar.style.display != DisplayStyle.None && resultBar.ClassListContains(kind);
        public bool ResultBarShown => resultBar.style.display != DisplayStyle.None;
        public bool ResultMuted => resultText.ClassListContains("kmuted");

        // Read-back for tests and the dev log.
        public string Word => word.text;
        public string Reason => reason.text;
        public string Tag => tag.style.display == DisplayStyle.None ? null : tag.text;
        public string PromptText => prompt.style.display == DisplayStyle.None ? null : promptText.text;
        public string HoldText => Hold.style.display == DisplayStyle.None ? null : Hold.Text;
        public bool ObjectiveShown => Objective.style.display != DisplayStyle.None;
        public float MeterFillPercent => meterFill.style.width.value.value;
        public float PromptFillPercent => promptFill.style.width.value.value;
        public bool MeterFillHas(string kind) => meterFill.ClassListContains(kind);
        public bool PromptFillHas(string kind) => promptFill.ClassListContains(kind);
        public string SkillCount => ring.Count;

        // The cluster plus the 13.1 stack, in panel units (for the edge markers' keep-out, HUD_SPEC 4.6).
        public Rect ClusterRect(Vector2 screen)
        {
            float height = Height;
            if (MenuOpen) height += PlayMenu.RowHeight + Gap;
            else if (prompt.style.display != DisplayStyle.None || Hold.style.display != DisplayStyle.None) height += PromptHeight + Gap;
            if (result.style.display != DisplayStyle.None) height += PromptHeight + Gap;
            if (Objective.style.display != DisplayStyle.None) height += 48 + Gap;
            if (RoutePanel.style.display != DisplayStyle.None) height += RoutePanel.Height + Gap;
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
            CastFlash.SetScale(scale);
            V2Markers.Scale = scale;
            RoutePanel.Scale = scale;
        }
    }
}
