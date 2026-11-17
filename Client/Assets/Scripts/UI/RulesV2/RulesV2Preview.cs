using System;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI.RulesV2
{
    // Dev only (-ptk-preview-v2-hud <scenario>): the rules v2 widgets on their own panel over the running match, fed with
    // fixed fake view-model data, for screenshots. Nothing here reads or sends packets. The positions are the ones
    // HUD_SPEC 13 gives for the wired HUD; terminal and exit markers are projected from the static map with the live camera.
    public sealed class RulesV2Preview : MonoBehaviour
    {
        public static readonly string[] Scenarios = { "route-panel", "exit-switch-hold", "kick-terminal", "collapse-urgent", "result-v2", "result-v2-killer" };
        // HUD_SPEC 6.1: the Killer cluster in normal play (info + skill) ends 176 u above the bottom edge.
        private const float KillerClusterTop = 24 + 72 + 8 + 72;
        private string scenario;
        private Role role;
        private Action<string> shot;
        private PanelSettings panel;
        private VisualElement root;
        private Font font;
        private RoutePanel ring;
        private readonly RouteTracker route = new RouteTracker();
        private HoldActionBar hold;
        private ObjectiveRow objective;
        private VisualElement killerStack;
        private CollapseTimer killerCollapse;
        private V2MarkerLayer markers;
        private ResultV2Panel result;
        private float started = -1;
        private int step;
        private int routeStage = -1;
        private float keyAt;
        public bool Done { get; private set; }

        public static bool Known(string value) => Array.IndexOf(Scenarios, value) >= 0;

        // The role a scenario is staged for (the run's -ptk-role should match).
        public static Role RoleOf(string value) => value == "kick-terminal" || value == "result-v2-killer" ? Role.Killer : Role.Survivor;

        public void Begin(string value, Role matchRole, Action<string> takeShot)
        {
            scenario = value; role = value == "collapse-urgent" ? matchRole : RoleOf(value); shot = takeShot;
            if (role != matchRole) Debug.LogWarning("[DEV] preview " + value + " is staged for the " + role + "; this match is " + matchRole);
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("ClientTheme");
            panel.scale = ClientScreens.PanelScale(Screen.width, Screen.height);
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.sortingOrder = 20;
            root = document.rootVisualElement;
            root.AddToClassList("root"); root.AddToClassList("in-game");
            root.pickingMode = PickingMode.Ignore;
            root.styleSheets.Add(Resources.Load<StyleSheet>("ClientUI"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("RulesV2"));
            font = Resources.Load<Font>("Fonts/Inter-Regular");
            root.style.unityFontDefinition = FontDefinition.FromFont(font);
            Build();
            started = Time.realtimeSinceStartup;
            Debug.Log("[DEV] preview v2 hud " + scenario + " (" + role + ")");
        }

        private void Build()
        {
            VisualElement overlay = V2Ui.Box(root, "v2-overlay");
            markers = new V2MarkerLayer(font);
            overlay.Add(markers);
            if (scenario.StartsWith("result-v2"))
            {
                result = new ResultV2Panel(font);
                overlay.Add(result);
                var back = new Button { text = "Back to lobby" }; back.AddToClassList("primary");
                var leave = new Button { text = "Leave" }; leave.AddToClassList("v2-secondary");
                foreach (Button b in new[] { back, leave }) { b.style.unityFontDefinition = FontDefinition.FromFont(font); result.Actions.Add(b); }
                back.Focus();
                return;
            }
            if (role == Role.Survivor)
            {
                VisualElement stack = V2Ui.Box(overlay, "v2-survivor-stack");
                ring = new RoutePanel(route, font);
                stack.Add(ring);
                hold = new HoldActionBar(Role.Survivor, font);
                stack.Add(hold);
                objective = new ObjectiveRow(font);
                stack.Add(objective);
            }
            else
            {
                killerStack = V2Ui.Box(overlay, "v2-killer-stack");
                killerStack.style.bottom = KillerClusterTop;
                hold = new HoldActionBar(Role.Killer, font);
                killerStack.Add(hold);
                killerCollapse = new CollapseTimer(Role.Killer, font);
                killerStack.Add(killerCollapse);
            }
        }

        private void Update()
        {
            if (started < 0) return;
            float scale = ClientScreens.PanelScale(Screen.width, Screen.height);
            if (!Mathf.Approximately(panel.scale, scale)) panel.scale = scale;
            root.EnableInClassList("s-floor", ClientScreens.SmallSize(scale) > ClientScreens.SmallText + .01f);
            markers.Scale = scale; if (ring != null) ring.Scale = scale;
            float t = Time.realtimeSinceStartup - started;
            markers.Begin();
            switch (scenario)
            {
                case "route-panel": Route(t); break;
                case "exit-switch-hold": ExitSwitch(t); break;
                case "kick-terminal": Kick(t, scale); break;
                case "collapse-urgent": Collapse(t, scale); break;
                default: Result(t); break;
            }
            markers.End();
        }

        private void Shoot(string name)
        {
            step++;
            shot?.Invoke(name);
        }

        // HUD_SPEC 13.2 (11/17, R2), stand-in server: route 1 (Up Right Right Down) is answered right at 0.3 s per key and
        // shot after two keys, then on its +8 % flash; route 2 (Left Up Down Right) takes a wrong second key and is shot on the
        // error line. Nothing is sent anywhere.
        private void Route(float t)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            hold.Set("Hacking Terminal 2 · 47%", .47f);
            byte first = Pack(RouteDirection.Up, RouteDirection.Right, RouteDirection.Right, RouteDirection.Down);
            byte second = Pack(RouteDirection.Left, RouteDirection.Up, RouteDirection.Down, RouteDirection.Right);
            if (routeStage < 0 && t >= .3f) { route.OnSelf(1, 4, first, 0, now); routeStage = 0; keyAt = t; }
            if (routeStage == 3 && t >= keyAt + .9f) { route.OnSelf(0, 0, 0, 0, now); route.OnSelf(2, 4, second, 0, now); routeStage = 4; keyAt = t; }
            if (routeStage >= 0 && route.Present && t >= keyAt + .3f)
            {
                keyAt = t;
                RouteDirection key = route.RouteId == 2 && route.Index == 1 ? RouteDirection.Right : route.Expected;
                route.TryPress(key, now, out _);
            }
            ring.Refresh(true, RouteCopy.KeyboardGlyph, now);
            if (step == 0 && route.RouteId == 1 && route.Index == 2) { Shoot("v2-route-panel-mid.png"); routeStage = 1; }
            else if (step == 1 && route.DoneFlash(now)) { Shoot("v2-route-panel-done.png"); routeStage = 3; keyAt = t; }
            else if (step == 2 && route.ErrorShown(now) && route.Shake(now) > .3f) { Shoot("v2-route-panel-error.png"); Debug.Log("[DEV] preview route error \"" + ring.LabelText + "\""); }
            else if (step == 3 && t > keyAt + 2) Done = true;
        }

        private static byte Pack(params RouteDirection[] dirs)
        {
            byte packed = 0;
            for (int i = 0; i < dirs.Length; i++) packed = HackRoute.WithDirection(packed, i, dirs[i]);
            return packed;
        }

        private void ExitSwitch(float t)
        {
            float progress = Mathf.Clamp01(.5f + t / GameConstants.ExitSwitchSeconds);
            hold.Set(new HoldActionView { Kind = HoldKind.ExitSwitch, Progress01 = progress }, "[E]");
            var exits = new[]
            {
                new ExitView { Id = 0, Powered = true, SwitchProgress01 = progress, LockedLeft = -1 },
                new ExitView { Id = 1, Powered = true, LockedLeft = -1 }
            };
            objective.Set(exits, new CollapseView { GlobalLeft = -1, PersonalLeft = -1 });
            ring.Refresh(false, RouteCopy.KeyboardGlyph, Time.realtimeSinceStartupAsDouble);
            if (step == 0 && t >= 1.8f) Shoot("v2-exit-switch-hold.png");
            else if (step == 1 && t > 3) Done = true;
        }

        private void Kick(float t, float scale)
        {
            float progress = Mathf.Clamp01((t - .2f) / 2f);
            hold.Set(new HoldActionView { Kind = HoldKind.KickTerminal, Progress01 = progress }, "[E]");
            killerCollapse.Set(new CollapseView { GlobalLeft = -1, PersonalLeft = -1 });
            var exits = new[] { new ExitView { Id = 0, LockedLeft = -1 }, new ExitView { Id = 1, LockedLeft = -1 } };
            KillerWorld(scale, exits, true);
            if (step == 0 && progress >= .55f) Shoot("v2-kick-terminal.png");
            else if (step == 1 && t > 3) Done = true;
        }

        private void Collapse(float t, float scale)
        {
            var exits = new[]
            {
                new ExitView { Id = 0, Powered = true, Open = true, SwitchProgress01 = 1, LockedLeft = -1 },
                new ExitView { Id = 1, Powered = true, Locked = true, SwitchProgress01 = .4f, LockedLeft = 4 - t }
            };
            var collapse = new CollapseView { GlobalLeft = Mathf.Max(0, 24 - t), PersonalLeft = Mathf.Max(0, 14 - 2 * t), Lagging = true };
            if (role == Role.Survivor)
            {
                hold.Hide();
                objective.Set(exits, collapse);
                ring.Refresh(false, RouteCopy.KeyboardGlyph, Time.realtimeSinceStartupAsDouble);
            }
            else
            {
                hold.Hide();
                killerCollapse.Set(collapse);
                KillerWorld(scale, exits, false);
            }
            if (step == 0 && t >= 1.5f) Shoot("v2-collapse-urgent.png");
            else if (step == 1 && t > 3) Done = true;
        }

        private void Result(float t)
        {
            if (step == 0 && t < .1f)
            {
                byte[] order = { 1, 2, 3, 4, 5, 6, 7 };
                string[] names = { null, "Mina", "Jae", "Rio", "Hana", "Theo", "Sora", "Ben" };
                bool killer = scenario == "result-v2-killer";
                byte[] escaped = killer ? new byte[] { 2, 4, 6 } : new byte[] { 1, 3, 5, 7 };
                byte[] eliminated = killer ? new byte[] { 1, 3, 5, 7 } : new byte[] { 2, 4, 6 };
                ResultV2View view = ResultV2View.FromIds((byte)(killer ? Role.Killer : Role.Survivor),
                    (byte)(killer ? ResultReasonV2.AllResolved : ResultReasonV2.Collapse), escaped, eliminated, order, id => names[id],
                    killer ? (byte)0 : (byte)1, killer ? Role.Killer : Role.Survivor);
                result.Set(view);
                if (!ResultV2Copy.Consistent(view)) Debug.LogWarning("[DEV] preview result winner does not match the majority rule");
            }
            if (step == 0 && t >= 1) Shoot(scenario == "result-v2-killer" ? "v2-result-3of7-killer.png" : "v2-result-4of7.png");
            else if (step == 1 && t > 2) Done = true;
        }

        // Killer world layer: both exit markers (2.5 m), and in the kick scenario the nearest on-screen terminal Regressing
        // and the nearest other terminal Overloaded (square on screen, edge chevron off screen).
        private void KillerWorld(float scale, ExitView[] exits, bool terminals)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            var screen = new Vector2(Screen.width / scale, Screen.height / scale);
            Rect safe = EdgePlacement.SafeRect(screen, false);
            float top = killerStack.worldBound.height > 0 ? killerStack.worldBound.yMin : screen.y - KillerClusterTop;
            Rect keepOut = EdgePlacement.KeepOut(Rect.MinMaxRect(24, top, 24 + 320, screen.y - 24));
            for (int i = 0; i < exits.Length && i < SharedMap.Map.Exits.Count; i++)
            {
                Vector3 at = new Vector3(SharedMap.Map.Exits[i].X, KillerWorldLayer.ExitMarkerHeight, SharedMap.Map.Exits[i].Z);
                if (Project(cam, at, scale, screen, safe, out Vector2 p, out _)) markers.Exit(EdgePlacement.ClampOutside(p, keepOut, safe), exits[i]);
            }
            if (!terminals) return;
            int regressing = -1, overloaded = -1;
            float best = float.MaxValue, other = float.MaxValue;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < SharedMap.Map.TerminalPositions.Count; i++)
            {
                Vector3 at = Terminal(i);
                float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(eye.x, eye.z));
                if (Project(cam, at, scale, screen, safe, out _, out _) && d < best) { best = d; regressing = i; }
            }
            for (int i = 0; i < SharedMap.Map.TerminalPositions.Count; i++)
            {
                if (i == regressing) continue;
                Vector3 at = Terminal(i);
                float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(eye.x, eye.z));
                if (d < other) { other = d; overloaded = i; }
            }
            if (regressing >= 0 && Project(cam, Terminal(regressing), scale, screen, safe, out Vector2 rp, out _))
                markers.Regressing(EdgePlacement.ClampOutside(rp, keepOut, safe), new TerminalFlagsView { Id = (byte)regressing, Regressing = true }, Role.Killer);
            if (overloaded < 0) return;
            var flags = new TerminalFlagsView { Id = (byte)overloaded, Overloaded = true };
            string tag = TerminalFlagCopy.Overloaded(flags, other);
            if (Project(cam, Terminal(overloaded), scale, screen, safe, out Vector2 op, out _))
                markers.Overloaded(EdgePlacement.ClampOutside(op, keepOut, safe), true, Vector2.zero, tag);
            else
            {
                Vector3 local = cam.transform.InverseTransformPoint(Terminal(overloaded));
                Vector2 edge = EdgePlacement.Place(screen, EdgePlacement.Direction(local), safe, keepOut, false, out Vector2 pointing);
                markers.Overloaded(edge, false, pointing, tag);
            }
        }

        private static Vector3 Terminal(int i)
            => new Vector3(SharedMap.Map.TerminalPositions[i].X, KillerWorldLayer.TerminalMarkerHeight, SharedMap.Map.TerminalPositions[i].Z);

        // Panel position (y down) of a world point; false when behind the camera or outside the safe rectangle.
        private static bool Project(Camera cam, Vector3 world, float scale, Vector2 screen, Rect safe, out Vector2 point, out Vector3 local)
        {
            local = cam.transform.InverseTransformPoint(world);
            Vector3 s = cam.WorldToScreenPoint(world);
            point = new Vector2(s.x / scale, screen.y - s.y / scale);
            return local.z > .1f && safe.Contains(point);
        }

        private void OnDestroy()
        {
            if (panel != null) Destroy(panel);
        }
    }
}
