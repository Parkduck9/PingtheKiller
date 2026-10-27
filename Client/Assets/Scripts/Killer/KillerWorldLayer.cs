using System.Collections.Generic;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Killer
{
    // The Killer's world layer and HUD feed (HUD_SPEC 6.1-6.6). KillerScene turns the anonymous stream into instances;
    // this component draws them (the shared Survivor character with a `signal` outline in view, through-wall diamonds,
    // edge chevrons), the Terminal and Exit markers, feeds the cluster, plays the cues and the positional sound.
    public sealed class KillerWorldLayer : MonoBehaviour
    {
        public const float IntroSeconds = 3, TagGap = 12, ChevronTagOffset = 30, DistanceTagBeyond = 20, TerminalTagOnlyWithin = 8;
        public const float TerminalMarkerHeight = 1.5f, ExitMarkerHeight = 2.5f, HeadHeight = 1.95f, LaggingTilt = 25, StutterHz = 8;
        private static readonly Color Neutral = new Color32(128, 130, 134, 255);
        private sealed class Character
        {
            public GameObject Root, Pose;
            public Transform Hull;
        }
        private readonly Dictionary<int, Character> characters = new Dictionary<int, Character>();
        private readonly List<int> gone = new List<int>();
        private readonly List<Material> materials = new List<Material>();
        private readonly System.Random rng = new System.Random(); // client-local; never seeded from wire data
        private readonly KillerCueRules cueRules = new KillerCueRules();
        private readonly ExecutionGuidance guidance = new ExecutionGuidance();
        private KillerScene scene;
        private BlipSoundModel sound;
        private System.Func<int, bool> isVoiced;
        private KillerAudio audioOut;
        private KillerHud hud;
        private MarkerLayer markers;
        private ISelfState self;
        private Material body, visor, outline;
        private S2C_WorldState last;
        private int playerId;
        private byte skillId;
        private double startedAt, activeStart = double.NegativeInfinity, activeEnd = double.NegativeInfinity;
        private double overclockEnd = double.NegativeInfinity, slowedEnd = double.NegativeInfinity, lockedEnd = double.NegativeInfinity;
        private bool lockKnown;
        public int InView { get; private set; }
        public int Occluded { get; private set; }
        public int Edges { get; private set; }

        public void Initialize(int ownId, byte skill, KillerHud killerHud, ISelfState selfState, double now)
        {
            playerId = ownId; skillId = skill; hud = killerHud; markers = hud.Markers; self = selfState; startedAt = now;
            sound = new BlipSoundModel(new System.Random(rng.Next()));
            isVoiced = sound.IsVoiced;
            audioOut = gameObject.AddComponent<KillerAudio>();
            body = Lit(Neutral); visor = Lit(CharacterLook.Visor);
            outline = CharacterLook.Outline(CharacterLook.Signal);
            if (outline != null) materials.Add(outline);
            else Debug.LogWarning("[killer] outline shader missing; in-view Survivors are drawn without the signal outline");
            hud.Begin(skillId);
            cueRules.Begin(now);
        }

        private Material Lit(Color color)
        {
            var template = Resources.Load<Material>("WorldLit");
            var material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
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
            cueRules.OnWorld(state.TimeLeft, state.ExitOpen != 0);
            cueRules.OnSignalCount(scene.Blips.Count, now);
            if (self.Available) cueRules.OnCooldown(self.SkillCooldown);
            int target = scene.NearestLagging(out float distance);
            guidance.Update(target, distance, self.Available, self.ExecutionGraceLeft);
        }

        // Own effects (CasterId = own id) drive the active arc, the Overclock line and the Exit lock seconds; the
        // Killer-bound TrafficOverload drives the slowed line. Only these fields are read.
        public void OnEffect(S2C_SkillEffect effect, double now)
        {
            float duration = float.IsNaN(effect.Duration) || effect.Duration < 0 ? 0 : Mathf.Min(effect.Duration, 60);
            if (effect.CasterId == playerId)
            {
                cueRules.OnOwnEffect();
                activeStart = now; activeEnd = now + duration;
                if (effect.SkillId == SkillIds.Overclock) overclockEnd = now + duration;
                if (effect.SkillId == SkillIds.FirewallBlock) { lockedEnd = now + duration; lockKnown = true; }
            }
            else if (effect.SkillId == SkillIds.TrafficOverload) slowedEnd = now + duration;
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
            UpdateHud(now, menuOpen);
            Rect safe = EdgePlacement.SafeRect(screen, false), keepOut = EdgePlacement.KeepOut(hud.ClusterRect(screen.y));
            markers.Clear(MarkerLayer.Static);
            markers.Clear(MarkerLayer.Signals);
            DrawStatic(pose, screen, safe, keepOut, now);
            InView = Occluded = Edges = 0;
            if (scene != null)
            {
                IReadOnlyList<BlipView> views = scene.Frame(pose, now, isVoiced);
                sound.Update(dt, scene.Blips, pose.Eye.x, pose.Eye.z, scene.IsOccluded);
                audioOut.UpdateWorld(sound, views, dt);
                DrawBlips(views, pose, screen, safe, keepOut, now);
            }
            // One layout pass over both channels: no tag overlaps another, nothing covers a Survivor marker.
            markers.Layout(new Rect(0, 0, screen.x, screen.y), keepOut);
            markers.Commit(MarkerLayer.Static);
            markers.Commit(MarkerLayer.Signals);
        }

        private void UpdateHud(double now, bool menuOpen)
        {
            hud.SetIntro(now - startedAt < IntroSeconds && !menuOpen);
            bool exitOpen = last != null && last.ExitOpen != 0;
            if (last != null) hud.SetInfo(last.TimeLeft, HudDone(last), last.TerminalsRequired);
            float active = now < activeEnd && activeEnd > activeStart ? (float)((activeEnd - now) / (activeEnd - activeStart)) : -1;
            hud.SetSkill(skillId, exitOpen, self, active);
            if (now < slowedEnd) hud.SetEffect(KillerHudCopy.Slowed((float)(slowedEnd - now)), true);
            else if (now < overclockEnd) hud.SetEffect(KillerHudCopy.Overclock((float)(overclockEnd - now)), false);
            else if (now < overclockEnd + SkillIds.OverclockRecoverySeconds) hud.SetEffect(KillerHudCopy.Recovering((float)(overclockEnd + SkillIds.OverclockRecoverySeconds - now)), false);
            else hud.SetEffect(null, false);
            float grace = self.Available ? self.ExecutionGraceLeft : 0;
            hud.SetPrompt(menuOpen ? ExecutionPrompt.None : guidance.Prompt, grace);
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

        // Terminal and Exit markers (drawn under the Signal markers).
        private void DrawStatic(ViewPose pose, Vector2 screen, Rect safe, Rect keepOut, double now)
        {
            if (last == null) return;
            foreach (TerminalState terminal in last.Terminals)
            {
                if (terminal.Progress == 100) continue; // done: no marker
                var at = new Vector3(terminal.X, TerminalMarkerHeight, terminal.Z);
                if (!Project(pose, at, screen, out Vector2 p)) continue;
                bool blocked = MapGeometry.SegmentBlocked(SharedMap.Map, pose.Eye.x, pose.Eye.z, terminal.X, terminal.Z);
                float distance = new Vector2(terminal.X - pose.Eye.x, terminal.Z - pose.Eye.z).magnitude;
                p = EdgePlacement.ClampOutside(p, keepOut, safe);
                bool tagOnly = !blocked && distance <= TerminalTagOnlyWithin;
                if (!tagOnly) markers.Square(MarkerLayer.Static, p);
                markers.Tag(MarkerLayer.Static, tagOnly ? p : p + new Vector2(0, TagGap), KillerHudCopy.Terminal(terminal.Id), TagStyle.Muted, tagOnly ? TagPivot.Center : TagPivot.Below, LayoutPriority.Terminal, !tagOnly);
            }
            var exitAt = new Vector3(SharedMap.Map.Exit.X, ExitMarkerHeight, SharedMap.Map.Exit.Z);
            if (Project(pose, exitAt, screen, out Vector2 e))
            {
                e = EdgePlacement.ClampOutside(e, keepOut, safe);
                ExitMark state = last.ExitOpen == 0 ? ExitMark.Locked : last.ExitLocked != 0 ? ExitMark.LockedByKiller : ExitMark.Open;
                markers.Exit(MarkerLayer.Static, e, state);
                string tag = state == ExitMark.Locked ? KillerHudCopy.ExitLocked : state == ExitMark.Open ? KillerHudCopy.ExitOpen
                    : KillerHudCopy.ExitLockedFor(lockKnown && now < lockedEnd ? (float)(lockedEnd - now) : (float?)null);
                markers.Tag(MarkerLayer.Static, e + new Vector2(0, TagGap), tag, state == ExitMark.Locked ? TagStyle.Muted : TagStyle.Text, TagPivot.Below, LayoutPriority.Exit, true);
            }
        }

        private void DrawBlips(IReadOnlyList<BlipView> views, ViewPose pose, Vector2 screen, Rect safe, Rect keepOut, double now)
        {
            float pixel = Mathf.Max(1, Mathf.Round(markers.Scale)); // 1 u outline in physical px
            foreach (BlipView view in views)
            {
                bool lagging = view.Status == BlipStatus.Lagging;
                Character character = Get(view.Handle);
                bool drawn = view.Class == BlipClass.InView;
                if (character.Root.activeSelf != drawn) character.Root.SetActive(drawn);
                if (drawn)
                {
                    Pose(character, view, pose, now, pixel, lagging);
                    bool hasHead = Project(pose, view.Position + Vector3.up * HeadHeight, screen, out Vector2 head);
                    bool hasFeet = Project(pose, view.Position, screen, out Vector2 feet);
                    if (!view.OutOfFrustum) InView++;
                    // The body's screen rectangle (capsule 0.8 m wide, 1.95 m to the visor top): no tag or terminal mark covers it.
                    if (hasHead && hasFeet)
                    {
                        float half = Mathf.Max(4, (feet.y - head.y) * .5f * CharacterLook.BodyScale.x / HeadHeight);
                        markers.Body(MarkerLayer.Signals, Rect.MinMaxRect(feet.x - half, head.y, feet.x + half, feet.y));
                    }
                    else markers.Body(MarkerLayer.Signals, default);
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
                    markers.Chevron(MarkerLayer.Signals, c, pointing, lagging);
                    markers.Tag(MarkerLayer.Signals, EdgePlacement.TagAnchor(c, screen, ChevronTagOffset), lagging ? KillerHudCopy.LaggingDistance(view.Distance) : KillerHudCopy.Distance(view.Distance), TagStyle.Text, TagPivot.Center, LayoutPriority.Edge, true);
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
            foreach (int handle in gone) { Destroy(characters[handle].Root); characters.Remove(handle); }
        }

        // HUD_SPEC 6.3 rule 5: facing and animation from matched motion only. Walk = a small bob; Lagging = tilted pose
        // with the animation (and position) stepped at 8 Hz.
        private void Pose(Character character, BlipView view, ViewPose pose, double now, float pixel, bool lagging)
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
            float bob = view.Walking ? Mathf.Abs(Mathf.Sin((float)(t * 2 * System.Math.PI * 1.6))) * .05f : 0;
            character.Pose.transform.localPosition = new Vector3(0, bob, 0);
            character.Pose.transform.localRotation = Quaternion.Euler(lagging ? LaggingTilt : 0, 0, 0);
            if (character.Hull != null)
            {
                float depth = Mathf.Max(ViewPose.Near, pose.ToCamera(position + Vector3.up * CharacterLook.Height * .5f).z);
                character.Hull.localScale = Vector3.Scale(CharacterLook.BodyScale, CharacterLook.HullScale(pose.PixelSize(depth, Screen.height) * pixel));
            }
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
            var root = new GameObject("Signal instance");
            root.transform.SetParent(transform, false);
            var pose = new GameObject("Pose");
            pose.transform.SetParent(root.transform, false);
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
            Destroy(item.GetComponent<Collider>()); // positions are server-owned; nothing collides on the client
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
            foreach (Material material in materials) if (material != null) Destroy(material);
        }
    }
}
