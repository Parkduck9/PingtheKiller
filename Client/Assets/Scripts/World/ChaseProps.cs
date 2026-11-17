using System;
using System.Collections.Generic;
using PingTheKiller.Client.UI;
using PingTheKiller.Shared.Game;
using UnityEngine;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 15 (Q5): the pure rules of the Shutter and Rail presentation. Everything here reads the server's state (the
    // WorldState shutter bytes, PlayerState.Chase and SelfState.ActionKind) and the static WorldChaseProps boxes; nothing
    // predicts an outcome.

    // How a Shutter is drawn. Unknown wire values draw Up (the snapshot validation already rejects them).
    public enum ShutterLook : byte { Up, Down, Broken }

    public static class ChasePropLooks
    {
        public static ShutterLook Of(byte state)
            => state == (byte)ShutterState.Down ? ShutterLook.Down : state == (byte)ShutterState.Broken ? ShutterLook.Broken : ShutterLook.Up;

        // The look of shutter `id` in a snapshot's shutter list (Up when the list is missing or short).
        public static ShutterLook Of(IReadOnlyList<byte> shutters, int id)
            => shutters == null || id < 0 || id >= shutters.Count ? ShutterLook.Up : Of(shutters[id]);

        // The child group shown for each look (ChasePropShapes groups; Frame is always shown).
        public static string Group(ShutterLook look) => look == ShutterLook.Down ? ChasePropShapes.Down : look == ShutterLook.Broken ? ChasePropShapes.Broken : ChasePropShapes.Up;
    }

    // The one geometry definition of the props, shared by the editor setup (ChasePropSetup builds prefabs from it) and the
    // runtime primitive fallback. Parts are boxes in the prop's local frame: x along the prop, z across it, y up, origin at
    // the box centre on the floor. Nothing stands outside the server box in x/z except the Broken panel and the vault
    // ground marks, which lie flat (below MaxFlatHeight).
    public static class ChasePropShapes
    {
        public const string Frame = "Frame", Up = "Up", Down = "Down", Broken = "Broken", Rail = "Rail", Marker = "Marker";
        public enum Paint : byte { Steel, SteelDark, Shutter, Slat, Edge, Marker }

        public struct Part
        {
            public string Group, Name;
            public Vector3 Center, Size, Euler;
            public Paint Paint;
            public Part(string group, string name, Vector3 center, Vector3 size, Paint paint, Vector3 euler = default)
            {
                Group = group; Name = name; Center = center; Size = size; Paint = paint; Euler = euler;
            }
        }

        public const float PostWidth = .12f, PostHeight = 2.6f, HousingHeight = .28f, PanelHeight = GameConstants.ShutterHeight;
        public const float RailHeight = GameConstants.RailHeight, MaxFlatHeight = .16f;

        // Palette (WorldArtSetup's Shutter / Steel colours, night graded at build time); the vault marker is the light
        // neutral used for world readability cues (the hatch rim), never a role colour.
        public static Color Colour(Paint paint)
        {
            switch (paint)
            {
                case Paint.Steel: return new Color32(66, 70, 77, 255);
                case Paint.SteelDark: return new Color32(40, 43, 48, 255);
                case Paint.Shutter: return new Color32(92, 96, 101, 255);
                case Paint.Slat: return new Color32(58, 61, 66, 255);
                case Paint.Edge: return new Color32(150, 154, 160, 255);
                default: return new Color32(214, 212, 204, 255);
            }
        }

        // Unlit-looking paints skip the night grade (the marker must stay the lightest part of the prop).
        public static bool Graded(Paint paint) => paint != Paint.Marker;

        // Yaw that turns the local x axis onto the prop's long axis.
        public static float Yaw(ChaseProp prop) => prop.AlongX ? 0 : 90;

        public static Vector3 Center(ChaseProp prop) => new Vector3(prop.CenterX, 0, prop.CenterZ);

        public static List<Part> ShutterParts(ChaseProp prop)
        {
            float length = prop.HalfLength * 2, inner = length - 2 * PostWidth, half = length * .5f;
            float depth = Mathf.Max(.2f, prop.HalfThickness * 2);
            var parts = new List<Part>
            {
                // Frame: guide posts against the alley walls and the roll housing overhead (always shown).
                new Part(Frame, "Post L", new Vector3(-half + PostWidth * .5f, PostHeight * .5f, 0), new Vector3(PostWidth, PostHeight, depth), Paint.SteelDark),
                new Part(Frame, "Post R", new Vector3(half - PostWidth * .5f, PostHeight * .5f, 0), new Vector3(PostWidth, PostHeight, depth), Paint.SteelDark),
                new Part(Frame, "Housing", new Vector3(0, PostHeight + HousingHeight * .5f, 0), new Vector3(length, HousingHeight, depth + .04f), Paint.Steel),
                // Up: only the bottom bar of the rolled panel shows under the housing.
                new Part(Up, "Rolled lip", new Vector3(0, PostHeight - .05f, 0), new Vector3(inner, .1f, .08f), Paint.Edge),
                // Down: a slatted panel 1.2 m high, the same from both sides, light top and bottom edges.
                new Part(Down, "Panel", new Vector3(0, PanelHeight * .5f, 0), new Vector3(inner, PanelHeight, .06f), Paint.Shutter),
                new Part(Down, "Bottom bar", new Vector3(0, .05f, 0), new Vector3(inner, .1f, .1f), Paint.Edge),
                new Part(Down, "Top bar", new Vector3(0, PanelHeight - .04f, 0), new Vector3(inner, .08f, .1f), Paint.Edge),
                // Broken: the panel torn in two halves lying flat across the gap, and a torn strip left under the housing.
                new Part(Broken, "Torn half A", new Vector3(-inner * .25f, .06f, .5f), new Vector3(inner * .5f - .06f, .05f, 1.1f), Paint.Shutter, new Vector3(4, 0, 5)),
                new Part(Broken, "Torn half B", new Vector3(inner * .25f, .07f, -.45f), new Vector3(inner * .5f - .06f, .05f, 1.0f), Paint.Shutter, new Vector3(-6, 0, -4)),
                new Part(Broken, "Torn strip", new Vector3(0, PostHeight - .14f, 0), new Vector3(inner, .28f, .05f), Paint.Shutter, new Vector3(9, 0, 0)),
            };
            for (int i = 0; i < 5; i++)
                parts.Add(new Part(Down, "Slat " + i, new Vector3(0, .2f * (i + 1), 0), new Vector3(inner, .025f, .09f), Paint.Slat));
            return parts;
        }

        public static List<Part> RailParts(ChaseProp prop)
        {
            float length = prop.HalfLength * 2, half = length * .5f;
            return new List<Part>
            {
                new Part(Rail, "Post L", new Vector3(-half + .05f, RailHeight * .5f, 0), new Vector3(.08f, RailHeight, .08f), Paint.SteelDark),
                new Part(Rail, "Post R", new Vector3(half - .05f, RailHeight * .5f, 0), new Vector3(.08f, RailHeight, .08f), Paint.SteelDark),
                new Part(Rail, "Top tube", new Vector3(0, RailHeight - .035f, 0), new Vector3(length, .07f, .1f), Paint.Edge),
                new Part(Rail, "Mid tube", new Vector3(0, .55f, 0), new Vector3(length, .05f, .06f), Paint.Steel),
                // The vault point: a light band on the top tube and a flat plate on each side of the rail.
                new Part(Marker, "Vault band", new Vector3(0, RailHeight - .035f, 0), new Vector3(.4f, .085f, .115f), Paint.Marker),
                new Part(Marker, "Vault plate +", new Vector3(0, .005f, .45f), new Vector3(.5f, .01f, .25f), Paint.Marker),
                new Part(Marker, "Vault plate -", new Vector3(0, .005f, -.45f), new Vector3(.5f, .01f, .25f), Paint.Marker),
            };
        }
    }

    // HUD_SPEC 15.2: what E would do next to a Shutter or Rail. Ranges are the server's (GameConstants), measured to the
    // shutter box and to the rail's vault point (its centre); the nearest target in its own range wins.
    public enum ChasePromptKind : byte { None, Pull, VaultShutter, VaultRail, Force }

    public static class ChasePropPrompt
    {
        public static ChasePromptKind Survivor(float x, float z, IReadOnlyList<byte> shutters)
        {
            ChasePromptKind best = ChasePromptKind.None; float bestDistance = float.MaxValue;
            IReadOnlyList<ChaseProp> list = WorldChaseProps.Shutters;
            for (int i = 0; i < list.Count; i++)
            {
                ShutterLook look = ChasePropLooks.Of(shutters, i);
                float d = list[i].Distance(x, z);
                if (look == ShutterLook.Up && d <= GameConstants.ShutterPullRadius) Take(ChasePromptKind.Pull, d, ref best, ref bestDistance);
                else if (look == ShutterLook.Down && d <= GameConstants.ShutterVaultRadius) Take(ChasePromptKind.VaultShutter, d, ref best, ref bestDistance);
            }
            NearestRail(x, z, ref best, ref bestDistance);
            return best;
        }

        public static ChasePromptKind Killer(float x, float z, IReadOnlyList<byte> shutters)
        {
            ChasePromptKind best = ChasePromptKind.None; float bestDistance = float.MaxValue;
            IReadOnlyList<ChaseProp> list = WorldChaseProps.Shutters;
            for (int i = 0; i < list.Count; i++)
            {
                float d = list[i].Distance(x, z);
                if (ChasePropLooks.Of(shutters, i) == ShutterLook.Down && d <= GameConstants.ShutterBreakRadius) Take(ChasePromptKind.Force, d, ref best, ref bestDistance);
            }
            NearestRail(x, z, ref best, ref bestDistance);
            return best;
        }

        private static void NearestRail(float x, float z, ref ChasePromptKind best, ref float bestDistance)
        {
            foreach (ChaseProp rail in WorldChaseProps.Rails)
            {
                float dx = x - rail.CenterX, dz = z - rail.CenterZ, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d <= GameConstants.RailVaultRadius) Take(ChasePromptKind.VaultRail, d, ref best, ref bestDistance);
            }
        }

        private static void Take(ChasePromptKind kind, float d, ref ChasePromptKind best, ref float bestDistance)
        {
            if (d >= bestDistance) return;
            best = kind; bestDistance = d;
        }

        public static string SurvivorText(ChasePromptKind kind)
            => kind == ChasePromptKind.Pull ? ChasePropCopy.Pull : kind == ChasePromptKind.VaultShutter ? ChasePropCopy.VaultShutter
            : kind == ChasePromptKind.VaultRail ? ChasePropCopy.VaultRail : null;

        public static InteractPromptText Killer(ChasePromptKind kind)
            => kind == ChasePromptKind.Force ? new InteractPromptText(ChasePropCopy.Force, ChasePropCopy.ForceHint)
            : kind == ChasePromptKind.VaultRail ? new InteractPromptText(ChasePropCopy.KillerVaultRail, ChasePropCopy.KillerVaultHint) : default;

        // The own SelfState action as a chase action (None for the v0.4 holds and for unknown values).
        public static bool IsChaseAction(byte actionKind)
            => actionKind == (byte)ActionKind.Pull || actionKind == (byte)ActionKind.Vault || actionKind == (byte)ActionKind.Break;
    }

    public readonly struct InteractPromptText
    {
        public readonly string Text, Hint;
        public InteractPromptText(string text, string hint) { Text = text; Hint = hint; }
    }

    // HUD_SPEC 15.3: PlayerState.Chase -> the procedural pose overlay. One pose at a time; Stunned > Breaking > Vaulting >
    // Pulling (the server never sets two at once, but a stale combination must still draw one pose).
    public enum ChasePose : byte { None, Pull, Vault, Break, Stunned }

    public static class ChaseActionMap
    {
        public static ChasePose Pose(byte chase)
        {
            if ((chase & (byte)ChaseFlags.Stunned) != 0) return ChasePose.Stunned;
            if ((chase & (byte)ChaseFlags.Breaking) != 0) return ChasePose.Break;
            if ((chase & (byte)ChaseFlags.Vaulting) != 0) return ChasePose.Vault;
            if ((chase & (byte)ChaseFlags.Pulling) != 0) return ChasePose.Pull;
            return ChasePose.None;
        }

        // The server duration of a pose, or 0 for an open-ended one (Break runs while the Killer holds; Stunned is fixed).
        // A vault over a Down shutter is one within ShutterVaultRadius + 0.5 m of a Down shutter box at its start; any
        // other vault is a rail vault (Survivor 0.8 s, Killer 1.6 s).
        public static float Seconds(ChasePose pose, Role role, float x, float z, IReadOnlyList<byte> shutters)
        {
            switch (pose)
            {
                case ChasePose.Pull: return GameConstants.ShutterPullSeconds;
                case ChasePose.Stunned: return GameConstants.ShutterStunSeconds;
                case ChasePose.Break: return GameConstants.ShutterBreakSeconds;
                case ChasePose.Vault:
                    if (role == Role.Survivor && NearDownShutter(x, z, shutters)) return GameConstants.ShutterVaultSeconds;
                    return role == Role.Killer ? GameConstants.RailVaultKillerSeconds : GameConstants.RailVaultSurvivorSeconds;
                default: return 0;
            }
        }

        // The hop height that clears the prop being vaulted (feet tucked about 0.4 m).
        public static float HopHeight(float seconds) => seconds >= GameConstants.ShutterVaultSeconds - .01f && seconds < GameConstants.RailVaultKillerSeconds ? .8f : .62f;

        public static bool NearDownShutter(float x, float z, IReadOnlyList<byte> shutters)
        {
            IReadOnlyList<ChaseProp> list = WorldChaseProps.Shutters;
            for (int i = 0; i < list.Count; i++)
                if (ChasePropLooks.Of(shutters, i) == ShutterLook.Down && list[i].Distance(x, z) <= GameConstants.ShutterVaultRadius + .5f) return true;
            return false;
        }
    }

    // Per-character pose clock: the pose starts at the snapshot that first carries it; Phase runs 0..1 over Seconds.
    public sealed class ChasePoseClock
    {
        public ChasePose Pose { get; private set; }
        public double StartedAt { get; private set; } = double.NegativeInfinity;
        public float Seconds { get; private set; }

        // Returns true on a pose edge (a new pose started).
        public bool Observe(ChasePose pose, float seconds, double now)
        {
            if (pose == Pose) return false;
            Pose = pose; StartedAt = now; Seconds = seconds;
            return pose != ChasePose.None;
        }

        public float Phase(double now) => Seconds <= 0 ? 0 : Mathf.Clamp01((float)((now - StartedAt) / Seconds));

        public void Reset() { Pose = ChasePose.None; StartedAt = double.NegativeInfinity; Seconds = 0; }
    }

    // HUD_SPEC 15.4: the Killer's stun feedback timing. The server's Stunned flag starts it; the burst and the line last
    // while the flag is set, at most ShutterStunSeconds + Grace after the edge; the own Killer's screen impulse lasts
    // ImpulseSeconds and is off under Reduced motion.
    public sealed class StunFeedback
    {
        public const float ImpulseSeconds = .25f, ImpulseDegrees = 5f, Grace = .2f;
        private double startedAt = double.NegativeInfinity;
        private bool stunned;

        public bool Stunned => stunned;
        public double StartedAt => startedAt;

        // Each snapshot (or each frame with the latest flag). True on the stun edge.
        public bool Observe(bool flag, double now)
        {
            bool edge = flag && !stunned;
            if (edge) startedAt = now;
            stunned = flag;
            return edge;
        }

        public bool Showing(double now) => stunned && now - startedAt <= GameConstants.ShutterStunSeconds + Grace;

        public string Line(double now) => Showing(now) ? ChasePropCopy.Stunned : null;

        // Vertical FOV offset (degrees, negative = narrower): a quick punch in, eased out by ImpulseSeconds.
        public float FovOffset(double now, bool reduced)
        {
            if (reduced) return 0;
            double t = now - startedAt;
            if (t < 0 || t >= ImpulseSeconds) return 0;
            float u = (float)(t / ImpulseSeconds);
            float envelope = u < .15f ? u / .15f : (1 - u) * (1 - u) / (.85f * .85f);
            return -ImpulseDegrees * envelope;
        }

        public void Reset() { stunned = false; startedAt = double.NegativeInfinity; }
    }
}
