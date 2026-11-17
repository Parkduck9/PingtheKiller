using System;
using System.Collections.Generic;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.UI.RulesV2
{
    // HUD_SPEC 13.2 packet routing (11/17, R2), pure. The own route from S2C_SelfState {RouteId, RouteLength, RouteDirs,
    // RouteIndex}; the server judges every key. The client knows the route, so it predicts each key at once (the pulse, the
    // burst flash and the error do not wait for the round trip) and reconciles with the next SelfState:
    // - same RouteId, within RollbackSeconds of the last key: the predicted index never falls behind the server's (keys
    //   still in flight);
    // - same RouteId, later than that (attempt 2, F2): the server's state wins: its index is adopted and a predicted error
    //   or completion is undone, because the key was dropped (not hacking yet, rate limit); the 1 Hz SelfState refresh
    //   guarantees such a packet arrives;
    // - a new RouteId: a fresh route (after an error, on another terminal, or after the gap);
    // - RouteId 0: no route (the gap after a completed route, the hack stopped, or the terminal is done).
    // After a predicted error or completion nothing more is sent for that route until the server answers.
    public sealed class RouteTracker
    {
        public const float PulseSeconds = .12f, DoneSeconds = .6f, ErrorSeconds = 1.5f, ShakeSeconds = .3f;
        // A SelfState of the same route this long after the last key reflects every key sent (round trip under 0.25 s).
        public const double RollbackSeconds = .3;
        private double lastKeyAt = double.NegativeInfinity;
        private readonly Queue<double> sent = new Queue<double>();
        private byte id, dirs, length;
        private int predicted;
        private bool lockedOut, waitingNext, firstRouteDone;
        private double pulseAt = double.NegativeInfinity, doneAt = double.NegativeInfinity, errorAt = double.NegativeInfinity;
        public double GapStartedAt { get; private set; } = double.NegativeInfinity;

        public byte RouteId => id;
        public byte Directions => dirs;
        public int Length => length;
        public int Index => predicted;
        // A route the player can answer now (the cells are drawn from it).
        public bool Present => id != 0 && !lockedOut && !waitingNext && predicted < length;
        // Any route the server holds for this player, answerable or not (keys are captured while one exists).
        public bool Held => id != 0;
        // The first route of the match shows the "Press the arrows in order" hint until it ends.
        public bool Hint => !firstRouteDone;
        public int KeysSent { get; private set; }
        public int Completed { get; private set; }
        public int Errors { get; private set; }

        // One per accepted key press (true: right): the panel pulse, the tick sound and the holo pulse.
        public event Action<bool> Key;
        public event Action RouteDone;
        public event Action RouteFailed;

        public void OnSelf(byte routeId, byte routeLength, byte routeDirs, byte routeIndex, double now)
        {
            if (routeId == 0)
            {
                if (id != 0) GapStartedAt = now;
                id = 0; predicted = 0; lockedOut = waitingNext = false;
                return;
            }
            byte len = (byte)Math.Min(routeLength, HackRoute.MaxLength);
            if (routeId != id)
            {
                id = routeId; dirs = routeDirs; length = len;
                predicted = Math.Min((int)routeIndex, len); lockedOut = waitingNext = false;
                return;
            }
            dirs = routeDirs;
            length = len;
            int server = Math.Min((int)routeIndex, len);
            if (now - lastKeyAt >= RollbackSeconds)
            {
                // F2: the server never saw (or dropped) the predicted key: follow it and make the route answerable again.
                if (lockedOut) { errorAt = double.NegativeInfinity; Errors = Math.Max(0, Errors - 1); }
                if (waitingNext && server < len) { doneAt = double.NegativeInfinity; Completed = Math.Max(0, Completed - 1); }
                predicted = server;
                lockedOut = waitingNext = false;
            }
            else if (server > predicted) predicted = server;
        }

        // True when the route id changed.
        public bool OnSelf(S2C_SelfState self, double now)
        {
            byte before = id;
            OnSelf(self.RouteId, self.RouteLength, self.RouteDirs, self.RouteIndex, now);
            return before != id;
        }

        // A route key (WASD, arrows, d-pad, stick flick or the dev autopilot). True with the packet to send.
        public bool TryPress(RouteDirection direction, double now, out C2S_HackInput input)
        {
            input = null;
            if (!Present) return false;
            while (sent.Count > 0 && now - sent.Peek() >= 1) sent.Dequeue();
            if (sent.Count >= GameConstants.RouteInputsPerSecond) return false;
            sent.Enqueue(now);
            input = new C2S_HackInput { RouteId = id, Direction = (byte)direction };
            KeysSent++;
            lastKeyAt = now;
            bool right = direction == HackRoute.DirectionAt(dirs, predicted);
            pulseAt = now;
            if (right)
            {
                predicted++;
                Key?.Invoke(true);
                if (predicted >= length)
                {
                    waitingNext = true; doneAt = now; Completed++; firstRouteDone = true;
                    RouteDone?.Invoke();
                }
            }
            else
            {
                lockedOut = true; errorAt = now; Errors++; firstRouteDone = true;
                Key?.Invoke(false);
                RouteFailed?.Invoke();
            }
            return true;
        }

        public RouteDirection Expected => HackRoute.DirectionAt(dirs, Math.Min(predicted, HackRoute.MaxLength - 1));
        public float Pulse(double now) => Since(now, pulseAt, PulseSeconds);
        public bool DoneFlash(double now) => now - doneAt < DoneSeconds;
        public bool ErrorShown(double now) => now - errorAt < ErrorSeconds;
        public float Shake(double now) => Since(now, errorAt, ShakeSeconds);
        // 0..1 of the gap before the next route, estimated locally (the server counts it only while hacking).
        public float GapFraction(double now) => id != 0 ? 1 : Mathf.Clamp01((float)((now - GapStartedAt) / GameConstants.RouteGapSeconds));

        private static float Since(double now, double at, float seconds)
        {
            double d = now - at;
            return d < 0 || d >= seconds ? 0 : 1 - (float)(d / seconds);
        }

        // Own status left Normal or the link went: the route is gone; the first-route hint memory stays for the match.
        public void Clear()
        {
            id = 0; predicted = 0; lockedOut = waitingNext = false; sent.Clear();
        }

        // MatchStart, GameResult, disconnect.
        public void Reset()
        {
            Clear();
            firstRouteDone = false; KeysSent = Completed = Errors = 0;
            pulseAt = doneAt = errorAt = GapStartedAt = lastKeyAt = double.NegativeInfinity;
        }
    }

    // HUD_SPEC 13.2 route keys, pure: one direction per frame from the press edges. W / Up arrow / d-pad up = Up, S / Down
    // = Down, A / Left = Left, D / Right = Right. A left-stick flick counts once: it fires when the stick leaves the
    // FlickOn radius along its dominant axis and re-arms only after returning inside FlickOff.
    public sealed class RouteKeys
    {
        public const float FlickOn = .6f, FlickOff = .3f;
        private bool armed = true;

        public static RouteDirection? FromPresses(bool up, bool down, bool left, bool right)
        {
            if (up) return RouteDirection.Up;
            if (down) return RouteDirection.Down;
            if (left) return RouteDirection.Left;
            if (right) return RouteDirection.Right;
            return null;
        }

        public RouteDirection? Flick(Vector2 stick)
        {
            float m = stick.magnitude;
            if (m < FlickOff) { armed = true; return null; }
            if (!armed || m < FlickOn) return null;
            armed = false;
            if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y)) return stick.x > 0 ? RouteDirection.Right : RouteDirection.Left;
            return stick.y > 0 ? RouteDirection.Up : RouteDirection.Down;
        }

        public void Reset() => armed = true;

        // Movement stops while E is held and the own entry is Hacking, including the gap between routes (QA a2 N1);
        // only a present route answers the keys (RouteTracker.TryPress). Anything else (released E, an exit switch,
        // a vault, a gated terminal) leaves W A S D to movement at once (attempt 2, F1). routeHeld is kept for callers.
        public static bool Capturing(bool interactHeld, bool ownHacking, bool routeHeld) => interactHeld && ownHacking;
    }

    // HUD_SPEC 13.2 (11/17, R2): the route panel, in the old sync lock bar's spot and budget (320x80 u, `panel` fill, 1 u
    // `line` edge). Four 56x36 u cells at y 8, 16 u apart (x 24, 96, 168, 240), each holding a drawn arrow (no glyph font):
    // done cells `muted` fill with a `bg` arrow, the current cell a 2 u `text` edge with a `text` arrow, later cells a 1 u
    // `line` edge with a `muted` arrow. A right key pulses the cell it completed (+3 u, 0.12 s). An error shakes the panel
    // (±4 u, 0.3 s) and draws its edge 2 u `danger` (the Killer was alerted) while the error line shows. Between routes the
    // cells are empty and a 2 u `muted` gap bar fills under them. Reduced motion: no pulse growth and no shake.
    // Label row y 52-80 u: the hint, the error line, the burst (`+3.4 %`, M 600 `text`) or the key glyphs.
    public sealed class RoutePanel : VisualElement
    {
        public const float Width = 320, Height = 80, CellWidth = 56, CellHeight = 36, CellTop = 8, CellGap = 16, CellLeft = 24;
        public const float PulseGrow = 3, ShakeReach = 4, ArrowSize = 12;
        private readonly Label label;
        private readonly RouteTracker tracker;
        private double now;
        public float Scale { get; set; } = 1;
        public bool Reduced { get; set; }
        public bool Shown { get; private set; }
        public float ShakeOffset { get; private set; }
        public string Glyph { get; private set; } = RouteCopy.KeyboardGlyph;
        public RouteTracker Tracker => tracker;
        public string LabelText => label.text;

        public RoutePanel(RouteTracker value, Font font)
        {
            tracker = value;
            pickingMode = PickingMode.Ignore;
            AddToClassList("v2-route");
            label = V2Ui.Text(this, RouteCopy.Waiting(Glyph), font, "v2-route-label");
            generateVisualContent += Draw;
            style.display = DisplayStyle.None;
        }

        public static float CellX(int index) => CellLeft + index * (CellWidth + CellGap);

        // visible: the keys are captured (RouteKeys.Capturing) and the own status is Normal. glyph: RouteCopy glyph text.
        public void Refresh(bool visible, string glyph, double time)
        {
            now = time;
            Glyph = glyph ?? RouteCopy.KeyboardGlyph;
            Shown = visible;
            V2Ui.Show(this, visible);
            if (!visible) return;
            V2Ui.SetText(label, RouteCopy.Label(tracker, Glyph, now));
            label.EnableInClassList("v2-route-done", tracker.DoneFlash(now) && !tracker.ErrorShown(now));
            float shake = Reduced ? 0 : tracker.Shake(now);
            ShakeOffset = shake > 0 ? Mathf.Sin(shake * 40f) * ShakeReach * shake : 0;
            style.translate = new Translate(ShakeOffset, 0);
            MarkDirtyRepaint();
        }

        private static void Block(Painter2D p, float x0, float x1, float y0, float y1, Color color)
        {
            if (x1 <= x0) return;
            p.fillColor = color;
            p.BeginPath(); p.MoveTo(new Vector2(x0, y0)); p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x0, y1)); p.ClosePath(); p.Fill();
        }

        private static void Frame(Painter2D p, float x0, float y0, float x1, float y1, float width, Color color)
        {
            p.lineWidth = width; p.strokeColor = color; p.lineCap = LineCap.Butt; p.lineJoin = LineJoin.Miter;
            float h = width * .5f;
            p.BeginPath(); p.MoveTo(new Vector2(x0 + h, y0 + h)); p.LineTo(new Vector2(x1 - h, y0 + h)); p.LineTo(new Vector2(x1 - h, y1 - h));
            p.LineTo(new Vector2(x0 + h, y1 - h)); p.ClosePath(); p.Stroke();
        }

        // A solid arrow (triangle head and stem) centred at c, pointing the route direction (screen space, y down).
        public static void Arrow(Painter2D p, Vector2 c, RouteDirection d, float size, Color color)
        {
            Vector2 f = d == RouteDirection.Up ? Vector2.down : d == RouteDirection.Down ? Vector2.up : d == RouteDirection.Left ? Vector2.left : Vector2.right;
            Vector2 s = new Vector2(-f.y, f.x);
            Vector2 tip = c + f * size, back = c + f * (size * .1f), tail = c - f * size;
            p.fillColor = color;
            p.BeginPath(); p.MoveTo(tip); p.LineTo(back + s * size * .8f); p.LineTo(back - s * size * .8f); p.ClosePath(); p.Fill();
            float stem = size * .28f;
            p.BeginPath(); p.MoveTo(back + s * stem); p.LineTo(tail + s * stem); p.LineTo(tail - s * stem); p.LineTo(back - s * stem); p.ClosePath(); p.Fill();
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!Shown) return;
            Painter2D p = context.painter2D;
            float hair = Palette.Stroke(1, Scale);
            bool error = tracker.ErrorShown(now);
            Block(p, 0, Width, 0, Height, Palette.Panel);
            Frame(p, 0, 0, Width, Height, error ? Palette.Stroke(2, Scale) : hair, error ? Palette.Danger : Palette.Line);
            if (!tracker.Present)
            {
                // Between routes: empty cells and the gap bar.
                for (int i = 0; i < HackRoute.MaxLength; i++) Frame(p, CellX(i), CellTop, CellX(i) + CellWidth, CellTop + CellHeight, hair, Palette.Line);
                float bar = Palette.Stroke(2, Scale), y = CellTop + CellHeight + 4, right = CellX(HackRoute.MaxLength - 1) + CellWidth;
                Block(p, CellX(0), right, y, y + bar, Palette.Line);
                Block(p, CellX(0), CellX(0) + (right - CellX(0)) * tracker.GapFraction(now), y, y + bar, Palette.Muted);
                return;
            }
            float pulse = Reduced ? 0 : tracker.Pulse(now) * PulseGrow;
            for (int i = 0; i < tracker.Length; i++)
            {
                float x0 = CellX(i), x1 = x0 + CellWidth, y0 = CellTop, y1 = CellTop + CellHeight;
                var c = new Vector2((x0 + x1) * .5f, (y0 + y1) * .5f);
                RouteDirection d = HackRoute.DirectionAt(tracker.Directions, i);
                if (i < tracker.Index)
                {
                    float g = i == tracker.Index - 1 ? pulse : 0;
                    Block(p, x0 - g, x1 + g, y0 - g, y1 + g, Palette.Muted);
                    Arrow(p, c, d, ArrowSize, Palette.Bg);
                }
                else if (i == tracker.Index)
                {
                    Frame(p, x0, y0, x1, y1, Palette.Stroke(2, Scale), Palette.Text);
                    Arrow(p, c, d, ArrowSize, Palette.Text);
                }
                else
                {
                    Frame(p, x0, y0, x1, y1, hair, Palette.Line);
                    Arrow(p, c, d, ArrowSize, Palette.Muted);
                }
            }
        }
    }

    internal static class V2Ui
    {
        public static Label Text(VisualElement parent, string value, Font font, params string[] classes)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            if (font != null) label.style.unityFontDefinition = FontDefinition.FromFont(font);
            label.AddToClassList("v2-text");
            foreach (string c in classes) label.AddToClassList(c);
            parent.Add(label);
            return label;
        }

        public static VisualElement Box(VisualElement parent, params string[] classes)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (string c in classes) box.AddToClassList(c);
            parent.Add(box);
            return box;
        }

        public static void Show(VisualElement element, bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) element.style.display = display;
        }

        public static void SetText(Label label, string value)
        {
            if (label.text != value) label.text = value;
        }

        public static void Fill(VisualElement fill, float fraction)
            => fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
    }
}
