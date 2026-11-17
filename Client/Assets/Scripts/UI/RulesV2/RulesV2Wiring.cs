using System.Collections.Generic;
using System.Globalization;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.UI.RulesV2
{
    // HUD_SPEC 13.2 (11/17, R2): the sync check and its tracker were retired; the route panel (RoutePanel.cs) reads
    // S2C_SelfState's route through RouteTracker.

    // Packet -> view model copies for the rules v2 HUD (HUD_SPEC 13). Pure.
    public static class V2Wire
    {
        // WorldState exits in wire order (at most two). lockedLeft: the Killer's own FirewallBlock seconds left, applied to
        // a Locked exit; negative when unknown (Survivors use the Killer-bound effect timing).
        public static ExitView[] Exits(S2C_WorldState world, float lockedLeft = -1)
        {
            int count = world?.Exits == null ? 0 : Mathf.Min(2, world.Exits.Count);
            var exits = new ExitView[count];
            for (int i = 0; i < count; i++)
            {
                ExitState wire = world.Exits[i];
                exits[i] = ExitView.FromWire(wire.Id, wire.Flags, wire.SwitchProgress);
                if (exits[i].Locked && lockedLeft > 0) exits[i].LockedLeft = lockedLeft;
            }
            return exits;
        }

        // Firewall block has a target: an unopened Exit with switch progress (GDD v2 3, U6a R4).
        public static bool SwitchInProgress(S2C_WorldState world)
        {
            if (world?.Exits == null) return false;
            for (int i = 0; i < world.Exits.Count && i < 2; i++)
            {
                ExitView exit = ExitView.FromWire(world.Exits[i].Id, world.Exits[i].Flags, world.Exits[i].SwitchProgress);
                if (!exit.Open && exit.SwitchProgress01 > 0) return true;
            }
            return false;
        }

        public static TerminalFlagsView Terminal(in TerminalState terminal) => TerminalFlagsView.FromWire(terminal.Id, terminal.Progress, terminal.Flags);

        // Personal = SelfState.CollapseLeft (Survivor), global = WorldState.CollapseLeft; NaN reads as not running.
        public static CollapseView Collapse(float globalLeft, float personalLeft, bool lagging)
            => new CollapseView { GlobalLeft = Clock(globalLeft), PersonalLeft = Clock(personalLeft), Lagging = lagging };

        // The Killer has no personal clock: its SelfState.CollapseLeft is the global one, shown only when the world has none.
        public static CollapseView KillerCollapse(float worldLeft, float selfLeft)
            => new CollapseView { GlobalLeft = Clock(worldLeft) >= 0 ? Clock(worldLeft) : Clock(selfLeft), PersonalLeft = -1 };

        public static HoldActionView Hold(ISelfState self)
        {
            if (self == null || !self.Available) return default;
            var kind = (HoldKind)(byte)self.ActionKind;
            return kind == HoldKind.None ? default : new HoldActionView { Kind = kind, Progress01 = self.ActionProgress };
        }

        // Rows in lobby slot order; result ids missing from the lobby follow in ascending order. Names are lobby nicknames.
        public static ResultV2View Result(S2C_GameResult result, S2C_LobbyState lobby, int ownId, Role ownRole)
        {
            var order = new List<int>();
            if (lobby?.Slots != null)
                foreach (Slot slot in lobby.Slots)
                    if (slot != null && slot.PlayerId != 0 && !order.Contains(slot.PlayerId)) order.Add(slot.PlayerId);
            var extra = new List<int>();
            if (result.EscapedIds != null) foreach (int id in result.EscapedIds) if (!order.Contains(id) && !extra.Contains(id)) extra.Add(id);
            if (result.EliminatedIds != null) foreach (int id in result.EliminatedIds) if (!order.Contains(id) && !extra.Contains(id)) extra.Add(id);
            extra.Sort();
            order.AddRange(extra);
            return ResultV2View.FromIds(result.WinnerRole, result.Reason, result.EscapedIds, result.EliminatedIds, order, id => Nickname(lobby, id), ownId, ownRole);
        }

        public static string Nickname(S2C_LobbyState lobby, int id)
        {
            if (lobby?.Slots != null)
                foreach (Slot slot in lobby.Slots)
                    if (slot != null && slot.PlayerId == id && !string.IsNullOrEmpty(slot.Nickname)) return slot.Nickname;
            return null;
        }

        private static float Clock(float value) => float.IsNaN(value) || value < 0 ? -1 : value;
    }

    // HUD_SPEC 13.3 interact prompts (before a hold starts): the exit switch and the hatch for Survivors, the kick and the
    // hatch close for the Killer. Text null = no prompt.
    // HUD_SPEC 13.4: the exits of one WorldState as view models, built once per received snapshot into a reused buffer, so
    // the readers (objective row, prompts, Killer markers, Firewall target) allocate nothing per frame. The Locked seconds
    // (the own FirewallBlock knowledge) are applied in place on every call.
    public sealed class ExitCache
    {
        private static readonly ExitView[] None = new ExitView[0];
        private S2C_WorldState source;
        private ExitView[] views = None;
        private readonly float[] wireLocked = new float[2];
        public bool SwitchInProgress { get; private set; }
        public ExitView[] Views => views;

        public ExitView[] For(S2C_WorldState world, float lockedLeft = -1)
        {
            if (!ReferenceEquals(world, source))
            {
                source = world;
                int count = world?.Exits == null ? 0 : Mathf.Min(2, world.Exits.Count);
                if (views.Length != count) views = count == 0 ? None : new ExitView[count];
                SwitchInProgress = false;
                for (int i = 0; i < count; i++)
                {
                    ExitState wire = world.Exits[i];
                    views[i] = ExitView.FromWire(wire.Id, wire.Flags, wire.SwitchProgress);
                    wireLocked[i] = views[i].LockedLeft;
                    if (!views[i].Open && views[i].SwitchProgress01 > 0) SwitchInProgress = true;
                }
            }
            for (int i = 0; i < views.Length; i++) views[i].LockedLeft = views[i].Locked && lockedLeft > 0 ? lockedLeft : wireLocked[i];
            return views;
        }

        public void Reset() { source = null; views = None; SwitchInProgress = false; }
    }

    // Per-frame text without per-frame strings: each slot keeps the last key (the displayed value) and its text; the text
    // is rebuilt only when the key changes.
    public sealed class TextMemo
    {
        private readonly long[] keys;
        private readonly string[] texts;

        public TextMemo(int slots)
        {
            keys = new long[slots];
            texts = new string[slots];
        }

        public bool Hit(int slot, long key, out string text)
        {
            text = texts[slot];
            return text != null && keys[slot] == key;
        }

        public string Put(int slot, long key, string text)
        {
            keys[slot] = key; texts[slot] = text;
            return text;
        }
    }

    public struct InteractPrompt
    {
        public string Text, Hint;
        public bool Muted;
    }

    public static class InteractCopy
    {
        public const string HatchEscape = "Press [E] to escape through the Emergency port";
        public const string HatchClose = "Hold [E] to close the Emergency port";
        public const string HatchCloseHint = "Stand still · starts the shutdown";
        public const string KickHint = "Stand still · hacked Terminals only";

        // Called every frame while in reach: memoized per exit / terminal, so an unchanged prompt is the same string.
        private static readonly TextMemo Memo = new TextMemo(4 + 16);

        public static string SwitchOpen(in ExitView exit)
        {
            int slot = exit.Id & 1;
            long key = ExitCopy.Key(exit);
            if (Memo.Hit(slot, key, out string text)) return text;
            return Memo.Put(slot, key, "Hold [E] to open " + ExitCopy.Name(exit.Id) + " · " + HoldCopy.Percent(exit.SwitchProgress01).ToString(CultureInfo.InvariantCulture) + "% — you will be Signal");
        }

        public static string SwitchLocked(in ExitView exit)
        {
            int slot = 2 + (exit.Id & 1);
            long key = ExitCopy.Key(exit);
            if (Memo.Hit(slot, key, out string text)) return text;
            return Memo.Put(slot, key, ExitCopy.Name(exit.Id) + " is " + ExitCopy.State(exit) + " by the Killer");
        }

        public static string Kick(byte terminalId)
        {
            int slot = 4 + (terminalId & 15);
            if (Memo.Hit(slot, terminalId, out string text)) return text;
            return Memo.Put(slot, terminalId, "Hold [E] to jam " + TerminalFlagCopy.Name(terminalId));
        }

        // Survivor, Normal: the hatch first (it ends the match for this Survivor at once), then the nearest switch in reach.
        public static InteractPrompt Survivor(float x, float z, ExitView[] exits, IReadOnlyList<MapPoint> switches, bool hatchVisible, float hatchX, float hatchZ)
        {
            var prompt = new InteractPrompt();
            if (hatchVisible && Distance(x, z, hatchX, hatchZ) <= GameConstants.HatchRadius) { prompt.Text = HatchEscape; return prompt; }
            int nearest = NearestSwitch(x, z, exits, switches);
            if (nearest < 0) return prompt;
            ExitView exit = exits[nearest];
            if (exit.Locked) { prompt.Text = SwitchLocked(exit); prompt.Muted = true; }
            else prompt.Text = SwitchOpen(exit);
            return prompt;
        }

        // Killer: the hatch close first, then the kick of the nearest unfinished terminal in reach.
        public static InteractPrompt Killer(float x, float z, IReadOnlyList<TerminalState> terminals, bool hatchVisible, float hatchX, float hatchZ)
        {
            var prompt = new InteractPrompt();
            if (hatchVisible && Distance(x, z, hatchX, hatchZ) <= GameConstants.HatchRadius)
            {
                prompt.Text = HatchClose; prompt.Hint = HatchCloseHint; return prompt;
            }
            int best = -1; float bestDistance = float.MaxValue;
            if (terminals != null)
                for (int i = 0; i < terminals.Count; i++)
                {
                    if (terminals[i].Progress >= 100) continue;
                    float d = Distance(x, z, terminals[i].X, terminals[i].Z);
                    if (d <= GameConstants.TerminalRadius && d < bestDistance) { bestDistance = d; best = i; }
                }
            if (best < 0) return prompt;
            prompt.Text = Kick(terminals[best].Id); prompt.Hint = KickHint;
            return prompt;
        }

        // Index into exits of the nearest powered, unopened exit whose switch is within ExitSwitchRadius, or -1. switches[i]
        // is the switch of the exit with Id i (FixedMap.ExitSwitches).
        public static int NearestSwitch(float x, float z, ExitView[] exits, IReadOnlyList<MapPoint> switches)
        {
            int best = -1; float bestDistance = float.MaxValue;
            if (exits == null || switches == null) return best;
            for (int i = 0; i < exits.Length; i++)
            {
                if (!exits[i].Powered || exits[i].Open || exits[i].Id >= switches.Count) continue;
                MapPoint at = switches[exits[i].Id];
                float d = Distance(x, z, at.X, at.Z);
                if (d <= GameConstants.ExitSwitchRadius && d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        private static float Distance(float ax, float az, float bx, float bz) { float dx = ax - bx, dz = az - bz; return Mathf.Sqrt(dx * dx + dz * dz); }
    }
}
