using System.Collections.Generic;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Killer
{
    // The Killer's world layer and HUD feed (HUD_SPEC 6.1-6.6). KillerScene turns the anonymous stream into instances;
    // this component draws them (the shared Survivor character with a `signal` outline in view, through-wall diamonds,
    // edge chevrons), the Terminal markers, the rules v2 world layer (both Exits, Regressing tags, the Overloaded alert;
    // HUD_SPEC 13.4, 13.6), feeds the cluster (prompt / hold block, collapse block), plays the cues and the positional sound.
    public sealed class KillerWorldLayer : MonoBehaviour
    {
        public const float IntroSeconds = 3, TagGap = 12, DistanceTagBeyond = 20, TerminalTagOnlyWithin = 8;
        public const float TerminalMarkerHeight = 1.5f, ExitMarkerHeight = 2.5f, HeadHeight = 1.95f, LaggingTilt = 25, StutterHz = 8;
        private static readonly Color Neutral = new Color32(128, 130, 134, 255);
        private sealed class Character
        {
            public GameObject Root, Pose;
            public Transform Hull;
            public CharacterInstance Art;
            public CharacterMotion Motion; // U4j: Status + matched speed only (HUD_SPEC 6.3)
        }
        private readonly Dictionary<int, Character> characters = new Dictionary<int, Character>();
        private readonly Stack<Character> pool = new Stack<Character>();
        private CharacterArt art;
        private readonly List<int> gone = new List<int>();
        private readonly List<Material> materials = new List<Material>();
        private readonly System.Random rng = new System.Random(); // client-local; never seeded from wire data
        private readonly KillerCueRules cueRules = new KillerCueRules();
        private KillerScene scene;
        private BlipSoundModel sound;
        private System.Func<int, bool> isVoiced;
        private KillerAudio audioOut;
        private KillerHud hud;
        private MarkerLayer markers;
        private V2MarkerLayer v2;
        private readonly ExitCache exitCache = new ExitCache();
        // Q2: the frame's off-screen edge markers (Survivor chevrons, off-screen Overloaded alerts), spread along their edge
        // before they are drawn. V2Tag >= 0: an Overloaded alert (its v2 tag index); otherwise a chevron.
        private struct EdgeMark { public int Spread, V2Tag; public Vector2 Pointing; public bool Lagging; public string Text; }
        private readonly EdgeSpread spread = new EdgeSpread();
        private readonly List<EdgeMark> edgeMarks = new List<EdgeMark>();
        private ISelfState self;
        private Material body, visor, outline;
        private S2C_WorldState last;
        private int playerId;
        private byte skillId;
        private double startedAt, activeStart = double.NegativeInfinity, activeEnd = double.NegativeInfinity;
        private double lockedEnd = double.NegativeInfinity;
        private bool lockKnown;
        private KillerAttackKick kick; // U4j: first-person attack feedback on the own capture
        // R1 (11/17): the same component plays the swing sweep on the local press (ClientApp, from KillerSwingWatch).
        public KillerAttackKick Kick => kick;
        public int InView { get; private set; }
        public int Occluded { get; private set; }
        public int Edges { get; private set; }

        public void Initialize(int ownId, byte skill, KillerHud killerHud, ISelfState selfState, double now)
        {
            playerId = ownId; skillId = skill; hud = killerHud; markers = hud.Markers; v2 = hud.V2Markers; self = selfState; startedAt = now;
            sound = new BlipSoundModel(new System.Random(rng.Next()));
            isVoiced = sound.IsVoiced;
            audioOut = gameObject.AddComponent<KillerAudio>();
            body = Lit(Neutral); visor = Lit(CharacterLook.Visor);
            art = CharacterArt.Load();
            if (art != null && art.NeutralMaterial != null) body = art.NeutralMaterial;
            outline = CharacterLook.Outline(CharacterLook.Signal);
            if (outline != null) materials.Add(outline);
            else Debug.LogWarning("[killer] outline shader missing; in-view Survivors are drawn without the signal outline");
            hud.Begin(skillId);
            cueRules.Begin(now);
            if (kick == null) kick = gameObject.AddComponent<KillerAttackKick>();
        }

        private Material Lit(Color color)
        {
            var material = WorldMaterials.NewMaterial(false, ToonShading.Find()); // toon, or URP Lit when the shader is missing (U4i)
            material.color = color;
            materials.Add(material);
            return material;
        }

        public void Accept(S2C_WorldState state, double now)
        {
            if (scene == null) scene = new KillerScene(new System.Random(rng.Next()), KillerScene.TerminalPoints(state.Terminals));
            scene.Accept(state, now);
            last = state;
            foreach (BlipEvent item in scene.Events) cueRules.OnTrackerEvent(item.Kind);
            foreach (BlipEvent item in scene.Events) if (item.Kind == BlipEventKind.CaptureConfirm) { kick.Play(); Captures++; captureHandle = item.Handle; }
            VanishedNear = scene.NormalVanishedNear;
            if (VanishedNear) VanishedPoint = scene.VanishedPoint;
            cueRules.OnWorld(state.TimeLeft, WorldExits.AnyOpen(state));
            cueRules.OnSignalCount(scene.Blips.Count, now);
        }

        // R1 (11/17) a2: the last snapshot dropped a Normal blip near the Killer (KillerSwingWatch.OnVanish), and where.
        public bool VanishedNear { get; private set; }
        public Vector3 VanishedPoint { get; private set; }

        // R1 (11/17) a2: the server's verdict says the own swing hit, but no CaptureConfirm came (an eliminating hit removed
        // the entry): the same first-person kick as a capture.
        public void PlayUnseenHit() => kick?.Play();

        // Captures confirmed by the tracker this match (dev staging of the knocked-down shot).
        public int Captures { get; private set; }
        private int captureHandle = -1;

        // U8: the ground position of the tracker instance of the last CaptureConfirm (the capture impact effect). Only an
        // instance the tracker built from received entries, so a masked (Silent) Survivor never has one.
        public bool CapturePoint(out Vector3 position)
        {
            position = default;
            if (scene == null || captureHandle < 0) return false;
            foreach (BlipView view in scene.Views) if (view.Handle == captureHandle) { position = view.Position; return true; }
            return false;
        }

        // The own cooldown reached 0 (the SelfState clock's ready edge, once per cooldown).
        public void OnSkillReady() => cueRules.OnSkillReady();

        // Instances tracked in the last snapshot (dev log and the crowd shot; counted by the tracker, never from entries).
        public int Tracked => scene == null ? 0 : scene.Blips.Count;
        // The interact prompt or hold label shown this frame (dev log); null when none.
        public string Prompt => hud.Hold.style.display == UnityEngine.UIElements.DisplayStyle.None ? hud.PromptText : hud.Hold.Text;
        // Overloaded alerts drawn this frame (on screen or at the edge).
        public int Overloaded { get; private set; }

        // HUD_SPEC 14.0 Killer rule: own effects (CasterId = own id) drive the active arc, the 6.1c line and the Exit lock
        // seconds; the Killer-bound TrafficOverload (CasterId 0) drives the slowed line. Anything else is ignored. The
        // returned record (Viewer None when ignored) is played by SkillFx; its cue replaces skill_use (14.1 Sound).
        public KillerFxState Fx { get; } = new KillerFxState();

        public FxRecord LastEffect { get; private set; }

        public void OnEffect(S2C_SkillEffect effect, double now)
        {
            LastEffect = Fx.OnEffect(playerId, effect.CasterId, effect.SkillId, effect.X, effect.Z, effect.Duration, now);
            if (effect.CasterId == playerId && LastEffect.Viewer == FxViewer.Caster)
            {
                float duration = SkillFeedback.Clamp(effect.Duration);
                activeStart = now; activeEnd = now + duration;
                if (effect.SkillId == SkillIds.FirewallBlock) { lockedEnd = now + duration; lockKnown = true; }
            }
        }

        // R1 (11/17): gamepad-only players read "RT  Attack" (ClientApp sets it from the connected devices).
        public bool PadPrompt { get; set; }

        // R1 (11/17): the horizontal distance to the nearest Normal tracker instance (any received blip), or +inf.
        public float NearestNormalBlip(Vector3 eye)
        {
            float best = float.PositiveInfinity;
            if (scene == null) return best;
            foreach (BlipView view in scene.Views)
            {
                if (view.Status != BlipStatus.Normal) continue;
                float d = new Vector2(view.Position.x - eye.x, view.Position.z - eye.z).magnitude;
                if (d < best) best = d;
            }
            return best;
        }

        // The nearest rendered instance, or the nearest one behind a wall (dev killer-look only).
        public bool NearestBlip(Vector3 from, bool occludedOnly, out Vector3 position)
        {
            position = default;
            if (scene == null) return false;
            float best = float.PositiveInfinity;
            foreach (BlipView view in scene.Views)
            {
                if (occludedOnly && !view.Occluded) continue;
                float d = (view.Position - from).sqrMagnitude;
                if (d < best) { best = d; position = view.Position; }
            }
            return !float.IsPositiveInfinity(best);
        }

        public void Render(ViewPose pose, double now, float dt, bool menuOpen)
        {
            foreach (KillerCue cue in cueRules.Pending) audioOut.Play(cue);
            cueRules.Pending.Clear();
            Vector2 screen = new Vector2(markers.layout.width, markers.layout.height);
            if (float.IsNaN(screen.x) || screen.x <= 0) screen = new Vector2(Screen.width / Mathf.Max(.01f, markers.Scale), Screen.height / Mathf.Max(.01f, markers.Scale));
            UpdateHud(now, menuOpen, pose.Eye);
            Rect safe = EdgePlacement.SafeRect(screen, false), keepOut = EdgePlacement.KeepOut(hud.ClusterRect(screen.y));
            markers.Clear(MarkerLayer.Static);
            markers.Clear(MarkerLayer.Signals);
            v2.Begin();
            spread.Begin(safe, keepOut);
            edgeMarks.Clear();
            DrawStatic(pose, screen, safe, keepOut, now);
            InView = Occluded = Edges = 0;
            if (scene != null)
            {
                IReadOnlyList<BlipView> views = scene.Frame(pose, now, isVoiced);
                sound.Update(dt, scene.Blips, pose.Eye.x, pose.Eye.z, scene.IsOccluded);
                audioOut.UpdateWorld(sound, views, dt);
                DrawBlips(views, pose, screen, safe, keepOut, now);
            }
            DrawEdges();
            // One layout pass over both channels and the rules v2 tags: no tag overlaps another, nothing covers a Survivor
            // marker, every tag inside the safe rectangle and out of the cluster keep-out.
            for (int i = 0; i < v2.LayoutCount; i++)
            {
                v2.LayoutInput(i, out LayoutPriority priority, out Rect glyph, out Rect tag, out bool keepGlyph);
                markers.External(priority, glyph, tag, keepGlyph);
            }
            markers.Layout(safe, keepOut);
            for (int i = 0; i < v2.LayoutCount; i++)
            {
                LayoutItem result = markers.ExternalResult(i);
                v2.ApplyLayout(i, result.TagVisible, result.TagShift);
            }
            markers.Commit(MarkerLayer.Static);
            markers.Commit(MarkerLayer.Signals);
            v2.End();
        }

        // Q2: spreads the frame's edge markers along their edges, then draws them at the spread points.
        private void DrawEdges()
        {
            spread.Solve();
            foreach (EdgeMark mark in edgeMarks)
            {
                Vector2 at = spread.Glyph(mark.Spread);
                if (mark.V2Tag >= 0) { v2.PlaceEdge(mark.V2Tag, at, spread.Tag(mark.Spread), spread.TagShown(mark.Spread)); continue; }
                markers.Chevron(MarkerLayer.Signals, at, mark.Pointing, mark.Lagging);
                if (spread.TagShown(mark.Spread))
                    markers.Tag(MarkerLayer.Signals, spread.Tag(mark.Spread).center, mark.Text, TagStyle.Text, TagPivot.Center, LayoutPriority.Edge, true);
            }
        }

        private void UpdateHud(double now, bool menuOpen, Vector3 eye)
        {
            // R3 (11/17): the 7 s intro card (UI/Help/IntroCard, HUD_SPEC 3) replaces this 3 s block; the block stays in KillerHud for tests.
            hud.SetIntro(false);
            if (last != null) hud.SetInfo(last.TimeLeft, HudDone(last), last.TerminalsRequired);
            float active = now < activeEnd && activeEnd > activeStart ? (float)((activeEnd - now) / (activeEnd - activeStart)) : -1;
            exitCache.For(last);
            hud.SetSkill(skillId, self, active);
            FxLine line = Fx.Line(now);
            hud.SetEffect(line.Text, line.Tone == LineTone.Danger, line.Tone == LineTone.Reason);
            InteractPrompt interact = last == null ? default
                : InteractCopy.Killer(eye.x, eye.z, last.Terminals, last.HatchVisible == 1, last.HatchX, last.HatchZ);
            // R1 (11/17) 6.1b: with nothing to hold in reach, a visible blip within 3 m shows the attack prompt.
            if (string.IsNullOrEmpty(interact.Text)) interact.Text = KillerHudCopy.Attack(NearestNormalBlip(eye), PadPrompt);
            hud.SetPrompt(interact, V2Wire.Hold(self));
            hud.SetCollapse(V2Wire.KillerCollapse(last != null ? last.CollapseLeft : -1, self.Available ? self.CollapseLeft : -1));
        }


        private static int HudDone(S2C_WorldState state)
        {
            int done = 0;
            foreach (TerminalState terminal in state.Terminals) if (terminal.Progress == 100) done++; // 0/100 from U2c; 100 = done
            return done;
        }

        private static bool Project(ViewPose pose, Vector3 world, Vector2 screen, out Vector2 point)
        {
            point = default;
            if (!pose.ToViewport(world, out Vector2 v) || v.x < 0 || v.x > 1 || v.y < 0 || v.y > 1) return false;
            point = new Vector2(v.x * screen.x, (1 - v.y) * screen.y);
            return true;
        }

        // Terminal markers (drawn under the Signal markers), then the rules v2 layer: a Regressing tag replaces a terminal's
        // tag, the Overloaded alert (HUD_SPEC 13.6) and both Exit markers (13.4; off-screen exits get no indicator).
        private void DrawStatic(ViewPose pose, Vector2 screen, Rect safe, Rect keepOut, double now)
        {
            Overloaded = 0;
            if (last == null) return;
            foreach (TerminalState terminal in last.Terminals)
            {
                TerminalFlagsView flags = V2Wire.Terminal(terminal);
                var at = new Vector3(terminal.X, TerminalMarkerHeight, terminal.Z);
                float distance = new Vector2(terminal.X - pose.Eye.x, terminal.Z - pose.Eye.z).magnitude;
                bool projected = Project(pose, at, screen, out Vector2 p);
                if (TerminalFlagCopy.ShowOverloaded(flags, Role.Killer))
                {
                    Overloaded++;
                    string alert = v2.OverloadedTag(flags, distance);
                    if (projected && safe.Contains(p)) v2.Overloaded(EdgePlacement.ClampOutside(p, keepOut, safe), true, Vector2.zero, alert);
                    else
                    {
                        Vector2 edge = EdgePlacement.Place(screen, EdgePlacement.Direction(pose.ToCamera(at)), safe, keepOut, false, out Vector2 pointing);
                        int tag = v2.OverloadedEdge(edge, pointing, alert);
                        int item = spread.Add(edge, new Vector2(V2MarkerLayer.OverloadedHalf, V2MarkerLayer.OverloadedHalf), v2.TagSize(tag),
                            EdgeTagMode.Below, LayoutPriority.Overloaded, -1 - terminal.Id, distance);
                        edgeMarks.Add(new EdgeMark { Spread = item, V2Tag = tag });
                    }
                }
                if (terminal.Progress == 100) continue; // done: no marker
                if (!projected) continue;
                bool blocked = MapGeometry.SegmentBlocked(SharedMap.Map, pose.Eye.x, pose.Eye.z, terminal.X, terminal.Z);
                p = EdgePlacement.ClampOutside(p, keepOut, safe);
                bool tagOnly = !blocked && distance <= TerminalTagOnlyWithin;
                if (!tagOnly) markers.Square(MarkerLayer.Static, p);
                if (flags.Regressing) { v2.Regressing(tagOnly ? p + new Vector2(0, TagGap) : p, flags, Role.Killer); continue; }
                // R1 (11/17): a terminal under the own Firewall block reads "Terminal N · Locked" in `text`.
                markers.Tag(MarkerLayer.Static, tagOnly ? p : p + new Vector2(0, TagGap), flags.Locked ? KillerHudCopy.TerminalTag(terminal.Id, true) : KillerHudCopy.Terminal(terminal.Id), flags.Locked ? TagStyle.Text : TagStyle.Muted, tagOnly ? TagPivot.Center : TagPivot.Below, LayoutPriority.Terminal, !tagOnly);
            }
            float lockedLeft = lockKnown && now < lockedEnd ? (float)(lockedEnd - now) : -1;
            ExitView[] exits = exitCache.For(last, lockedLeft);
            for (int i = 0; i < exits.Length; i++)
            {
                ExitState wire = last.Exits[i];
                if (!Project(pose, new Vector3(wire.X, ExitMarkerHeight, wire.Z), screen, out Vector2 e)) continue;
                v2.Exit(EdgePlacement.ClampOutside(e, keepOut, safe), exits[i]);
            }
        }

        private void DrawBlips(IReadOnlyList<BlipView> views, ViewPose pose, Vector2 screen, Rect safe, Rect keepOut, double now)
        {
            float pixel = Mathf.Max(1, Mathf.Round(markers.Scale)); // 1 u outline in physical px
            foreach (BlipView view in views)
            {
                bool lagging = view.Status == BlipStatus.Lagging;
                Character character = Get(view.Handle);
                // U4j: the motion tracks every matched instance (also undrawn) from its Status and matched speed only.
                character.Motion?.Observe(now, (byte)view.Status, 0, 0, character.Art != null ? character.Art.Speed : 0);
                bool drawn = view.ModelVisible;
                if (character.Root.activeSelf != drawn) character.Root.SetActive(drawn);
                if (drawn)
                {
                    Vector3 ground = Pose(character, view, pose, now, pixel, lagging);
                    // U4d F-02: the head point follows the drawn pose (stepped position, 25 deg Lagging tilt).
                    Vector3 headPoint = HeadPoint(ground, view.FacingYaw, lagging);
                    // U4j: a knocked-down / crawling body carries its Lagging tag just above it (head bone + 0.25 m, at least 0.7 m).
                    if (character.Motion != null && character.Motion.TagHeight(HeadHeight, out float top)) headPoint = ground + Vector3.up * top;
                    bool hasHead = Project(pose, headPoint, screen, out Vector2 head);
                    bool hasFeet = Project(pose, ground, screen, out Vector2 feet);
                    if (!view.OutOfFrustum) InView++;
                    // The body's screen rectangle (capsule 0.8 m wide, 1.95 m to the visor top): no tag or terminal mark covers it.
                    // U4d F-01: also when the feet or the head project outside the screen (execution range), clamped to it.
                    markers.Body(MarkerLayer.Signals, BodyRect(pose, ground, headPoint, screen, out Rect bodyRect) ? bodyRect : default);
                    if (view.Distance > DistanceTagBeyond && hasFeet)
                        markers.Tag(MarkerLayer.Signals, feet + new Vector2(0, 4), KillerHudCopy.Distance(view.Distance), TagStyle.Text, TagPivot.Below, LayoutPriority.Survivor, true);
                    if (lagging && hasHead)
                        markers.Tag(MarkerLayer.Signals, head - new Vector2(0, 4), KillerHudCopy.Lagging, TagStyle.Lagging, TagPivot.Above, LayoutPriority.Survivor, true);
                }
                else if (view.Class == BlipClass.Occluded && !view.OutOfFrustum && Project(pose, view.Position + Vector3.up * KillerScene.MarkerHeight, screen, out Vector2 p))
                {
                    Occluded++;
                    p = EdgePlacement.ClampOutside(p, keepOut, safe);
                    markers.Diamond(MarkerLayer.Signals, p, lagging);
                    markers.Tag(MarkerLayer.Signals, p + new Vector2(0, lagging ? 16 : TagGap), lagging ? KillerHudCopy.LaggingDistance(view.Distance) : KillerHudCopy.Distance(view.Distance), TagStyle.Text, TagPivot.Below, LayoutPriority.Survivor, true);
                }
                // An edge indicator: off-screen, or (F-06) a held In view / Occluded instance whose marker has left the frustum.
                if (view.Edge)
                {
                    Edges++;
                    Vector3 cameraSpace = pose.ToCamera(view.Position + Vector3.up * KillerScene.MarkerHeight);
                    Vector2 c = EdgePlacement.Place(screen, EdgePlacement.Direction(cameraSpace), safe, keepOut, false, out Vector2 pointing);
                    // Q2: drawn by DrawEdges after the spread; the tag sits beside the chevron, on the screen-centre side.
                    string text = lagging ? KillerHudCopy.LaggingDistance(view.Distance) : KillerHudCopy.Distance(view.Distance);
                    float half = MarkerLayer.ChevronHalf(lagging);
                    int item = spread.Add(c, new Vector2(half, half), MarkerLayer.TagSize(text, TagStyle.Text), EdgeTagMode.Beside, LayoutPriority.Edge, view.Handle, view.Distance);
                    edgeMarks.Add(new EdgeMark { Spread = item, V2Tag = -1, Pointing = pointing, Lagging = lagging, Text = text });
                }
            }
            // Despawned instances vanish at once (no fade, HUD_SPEC 6.3 rule 2).
            gone.Clear();
            foreach (var entry in characters)
            {
                bool alive = false;
                foreach (BlipView view in views) if (view.Handle == entry.Key) { alive = true; break; }
                if (!alive) gone.Add(entry.Key);
            }
            foreach (int handle in gone)
            {
                Character removed = characters[handle];
                removed.Root.SetActive(false);
                // Bound the spare pool; a White noise burst can contain more than eight blips.
                if (pool.Count < 8) pool.Push(removed); else { removed.Art?.Dispose(); Destroy(removed.Root); }
                characters.Remove(handle);
            }
        }

        // The top of the drawn character: HeadHeight above the ground point, tilted forward by LaggingTilt (about the ground
        // pivot, toward the facing) in the Lagging pose, exactly as Pose rotates the model.
        public static Vector3 HeadPoint(Vector3 ground, float facingYaw, bool lagging)
            => ground + Quaternion.Euler(0, facingYaw, 0) * (Quaternion.Euler(lagging ? LaggingTilt : 0, 0, 0) * (Vector3.up * HeadHeight));

        // The character's screen rectangle in panel units (y down): head to feet, 0.41 x its height wide, from the unclamped
        // projection and then clamped to the screen, so a Survivor whose feet (or head) leave the view still blocks tags.
        // False when neither point is in front of the camera or the clamped rectangle is empty.
        public static bool BodyRect(ViewPose pose, Vector3 ground, Vector3 headPoint, Vector2 screen, out Rect rect)
        {
            rect = default;
            bool hasHead = pose.ToViewport(headPoint, out Vector2 h), hasFeet = pose.ToViewport(ground, out Vector2 f);
            if (!hasHead && !hasFeet) return false;
            Vector2 head = hasHead ? new Vector2(h.x * screen.x, (1 - h.y) * screen.y) : default;
            Vector2 feet = hasFeet ? new Vector2(f.x * screen.x, (1 - f.y) * screen.y) : default;
            if (!hasHead) head = new Vector2(feet.x, 0);         // behind the near plane above: reaches the top edge
            if (!hasFeet) feet = new Vector2(head.x, screen.y);  // behind the near plane below: reaches the bottom edge
            float top = Mathf.Min(head.y, feet.y), bottom = Mathf.Max(head.y, feet.y);
            float half = Mathf.Max(4, (bottom - top) * .5f * CharacterLook.BodyScale.x / HeadHeight);
            float xMin = Mathf.Max(0, Mathf.Min(head.x, feet.x) - half), xMax = Mathf.Min(screen.x, Mathf.Max(head.x, feet.x) + half);
            top = Mathf.Max(0, top); bottom = Mathf.Min(screen.y, bottom);
            if (xMax <= xMin || bottom <= top) return false;
            rect = Rect.MinMaxRect(xMin, top, xMax, bottom);
            return true;
        }

        // HUD_SPEC 6.3 rule 5: facing and animation from matched motion only. Walk = a small bob; Lagging = tilted pose
        // with the animation (and position) stepped at 8 Hz. Returns the ground point the model was drawn at.
        private Vector3 Pose(Character character, BlipView view, ViewPose pose, double now, float pixel, bool lagging)
        {
            Vector3 position = view.Position;
            double t = now;
            if (lagging)
            {
                t = System.Math.Floor(now * StutterHz) / StutterHz;
                Blip blip = FindBlip(view.Handle);
                if (blip != null) position = scene.Tweened(blip, t);
            }
            character.Root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, view.FacingYaw, 0));
            if (character.Art != null)
            {
                character.Pose.transform.localRotation = Quaternion.Euler(lagging ? LaggingTilt : 0, 0, 0);
                // U4j: the knock-down / crawl clips replace the Lagging lean (a crawling body tilted 25 deg sinks its head).
                if (character.Motion != null && character.Motion.Drives) character.Pose.transform.localRotation = Quaternion.identity;
                character.Motion?.Drive(now);
                character.Art.Animate(view.Position, now, lagging, true, view.Walking);
                character.Motion?.Ground();
                return position;
            }
            float bob = view.Walking ? Mathf.Abs(Mathf.Sin((float)(t * 2 * System.Math.PI * 1.6))) * .05f : 0;
            character.Pose.transform.localPosition = new Vector3(0, bob, 0);
            character.Pose.transform.localRotation = Quaternion.Euler(lagging ? LaggingTilt : 0, 0, 0);
            if (character.Hull != null)
            {
                character.Hull.localScale = CharacterLook.BodyScale;
            }
            return position;
        }

        private Blip FindBlip(int handle)
        {
            IReadOnlyList<Blip> blips = scene.Blips;
            for (int i = 0; i < blips.Count; i++) if (blips[i].Handle == handle) return blips[i];
            return null;
        }

        // The shared Survivor model: one neutral material for every instance, no per-player look (HUD_SPEC N1).
        private Character Get(int handle)
        {
            if (characters.TryGetValue(handle, out Character found)) return found;
            if (pool.Count > 0)
            {
                Character reused = pool.Pop();
                reused.Root.SetActive(true);
                reused.Art?.ResetMotion();
                reused.Motion?.Reset();
                reused.Root.SetActive(false);
                characters.Add(handle, reused);
                return reused;
            }
            var root = new GameObject("Signal instance");
            root.transform.SetParent(transform, false);
            var pose = new GameObject("Pose");
            pose.transform.SetParent(root.transform, false);
            if (art != null && art.SurvivorPrefab != null)
            {
                GameObject model = Instantiate(art.SurvivorPrefab, pose.transform);
                CharacterMotion motion = CharacterMotion.Attach(model, MotionView.Killer); // U4j: before CharacterInstance binds it
                var instance = new CharacterInstance(model, true);
                foreach (Renderer renderer in instance.Renderers)
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++) slots[i] = body;
                    renderer.sharedMaterials = slots;
                    if (outline == null || !(renderer is SkinnedMeshRenderer skinned)) continue;
                    var hullObject = new GameObject("Signal outline");
                    hullObject.transform.SetParent(renderer.transform, false);
                    var hullRenderer = hullObject.AddComponent<SkinnedMeshRenderer>();
                    hullRenderer.sharedMesh = skinned.sharedMesh;
                    hullRenderer.bones = skinned.bones;
                    hullRenderer.rootBone = skinned.rootBone;
                    hullRenderer.localBounds = skinned.localBounds;
                    hullRenderer.updateWhenOffscreen = true;
                    hullRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    hullRenderer.receiveShadows = false;
                    for (int i = 0; i < slots.Length; i++) slots[i] = outline;
                    hullRenderer.sharedMaterials = slots;
                }
                root.SetActive(false);
                var imported = new Character { Root = root, Pose = pose, Art = instance };
                imported.Motion = motion;
                characters.Add(handle, imported);
                return imported;
            }
            Part(pose.transform, PrimitiveType.Capsule, Vector3.up * CharacterLook.Height * .5f, CharacterLook.BodyScale, body);
            Part(pose.transform, PrimitiveType.Cube, CharacterLook.VisorOffset, CharacterLook.VisorScale, visor);
            Transform hull = outline != null ? Part(pose.transform, PrimitiveType.Capsule, Vector3.up * CharacterLook.Height * .5f, CharacterLook.BodyScale, outline) : null;
            root.SetActive(false);
            var character = new Character { Root = root, Pose = pose, Hull = hull };
            characters.Add(handle, character);
            return character;
        }

        private static Transform Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            CharacterLook.Release(item.GetComponent<Collider>()); // positions are server-owned
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            var renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return item.transform;
        }

        private void OnDestroy()
        {
            audioOut?.Silence();
            if (markers != null) { markers.Clear(MarkerLayer.Static); markers.Commit(MarkerLayer.Static); markers.Clear(MarkerLayer.Signals); markers.Commit(MarkerLayer.Signals); }
            if (v2 != null) { v2.Begin(); v2.End(); }
            foreach (Material material in materials) if (material != null) Destroy(material);
            foreach (Character character in characters.Values) character.Motion?.Dispose();
            foreach (Character character in pool) character.Motion?.Dispose();
            if (kick != null) { kick.Restore(); Destroy(kick); kick = null; } // U4j: camera back to the rig's placement
        }
    }
}
