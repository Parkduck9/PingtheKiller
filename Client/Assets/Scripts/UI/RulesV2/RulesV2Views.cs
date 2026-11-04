using System.Collections.Generic;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.UI.RulesV2
{
    // View models for the rules v2 HUD (HUD_SPEC 13). They mirror protocol v0.4 (U6a) field for field, so the widgets
    // do not depend on packet classes; the wiring copies the packet values in. Numeric enum values equal the wire values.

    // S2C_SyncResult.Result uses PingTheKiller.Shared.Game.SyncResult directly (one shared definition, AGENTS.md 1).

    // S2C_SelfState.ActionKind.
    public enum HoldKind : byte { None = 0, ExitSwitch = 1, KickTerminal = 2, CloseHatch = 3 }

    // S2C_GameResult.Reason in protocol v0.4.
    public enum ResultReasonV2 : byte { AllResolved = 0, AllSurvivorsDown = 1, TimeUp = 2, Collapse = 3 }

    // S2C_SyncCheck {Duration, ZoneStart, ZoneSize, GreatSize} + the local time since it arrived + S2C_SyncResult.
    // Fractions are of one needle sweep (0 = 12 o'clock, 1 = a full clockwise turn). The great part is the first
    // GreatSize of the zone.
    public struct SyncCheckView
    {
        public uint CheckId;
        public float Duration, ZoneStart, ZoneSize, GreatSize;
        public float Elapsed;
        public SyncResult? Result;
    }

    // S2C_WorldState ExitState {Flags: Powered=1, Open=2, Locked=4; SwitchProgress 0..100 / 100}. Number = Id + 1.
    public struct ExitView
    {
        public byte Id;
        public bool Powered, Open, Locked;
        public float SwitchProgress01;
        // The Killer's own FirewallBlock seconds left (S2C_SkillEffect.Duration); negative when unknown.
        public float LockedLeft;

        public static ExitView FromWire(byte id, byte flags, byte switchProgress)
            => new ExitView
            {
                Id = id, Powered = (flags & 1) != 0, Open = (flags & 2) != 0, Locked = (flags & 4) != 0,
                SwitchProgress01 = switchProgress / 100f, LockedLeft = -1
            };
    }

    // S2C_SelfState {ActionKind, ActionProgress}.
    public struct HoldActionView
    {
        public HoldKind Kind;
        public float Progress01;
    }

    // S2C_WorldState.CollapseLeft (global) and S2C_SelfState.CollapseLeft (personal); -1 when not running.
    // Lagging = own PlayerState.Status == Lagging (the reason the personal clock runs at 2x). Killer: PersonalLeft = -1.
    public struct CollapseView
    {
        public float GlobalLeft, PersonalLeft;
        public bool Lagging;
        public bool Running => GlobalLeft >= 0 || PersonalLeft >= 0;
    }

    // TerminalState.Flags {Regressing=1, Overloaded=2}. Number = Id + 1.
    public struct TerminalFlagsView
    {
        public byte Id;
        public bool Regressing, Overloaded;
        // TerminalState.Progress / 100 (the Killer view receives 0 or 1 only).
        public float Progress01;

        public static TerminalFlagsView FromWire(byte id, byte progress, byte flags)
            => new TerminalFlagsView { Id = id, Progress01 = progress / 100f, Regressing = (flags & 1) != 0, Overloaded = (flags & 2) != 0 };
    }

    public struct ResultRow
    {
        public int PlayerId;
        public string Name;
        public bool Escaped;
        public bool Self;
    }

    // S2C_GameResult {WinnerRole, Reason, EscapedIds, EliminatedIds} + lobby names + own id and role.
    public sealed class ResultV2View
    {
        public Role Winner;
        public ResultReasonV2 Reason;
        public Role OwnRole;
        public readonly List<ResultRow> Rows = new List<ResultRow>();

        public int Escaped
        {
            get { int n = 0; foreach (ResultRow row in Rows) if (row.Escaped) n++; return n; }
        }

        public int Starting => Rows.Count;

        // Rows in the given slot order (escaped and eliminated ids together); ids in neither list are skipped.
        public static ResultV2View FromIds(byte winnerRole, byte reason, IReadOnlyList<byte> escapedIds, IReadOnlyList<byte> eliminatedIds,
            IReadOnlyList<byte> slotOrder, System.Func<byte, string> name, byte ownId, Role ownRole)
            => FromIds(winnerRole, reason, Widen(escapedIds), Widen(eliminatedIds), Widen(slotOrder),
                name == null ? (System.Func<int, string>)null : id => name((byte)id), ownId, ownRole);

        // S2C_GameResult carries int player ids; the same rule with the wire's id type.
        public static ResultV2View FromIds(byte winnerRole, byte reason, IReadOnlyList<int> escapedIds, IReadOnlyList<int> eliminatedIds,
            IReadOnlyList<int> slotOrder, System.Func<int, string> name, int ownId, Role ownRole)
        {
            var view = new ResultV2View
            {
                Winner = winnerRole == (byte)Role.Killer ? Role.Killer : Role.Survivor,
                Reason = reason <= (byte)ResultReasonV2.Collapse ? (ResultReasonV2)reason : ResultReasonV2.AllResolved,
                OwnRole = ownRole
            };
            if (slotOrder == null) return view;
            foreach (int id in slotOrder)
            {
                bool escaped = Contains(escapedIds, id);
                if (!escaped && !Contains(eliminatedIds, id)) continue;
                view.Rows.Add(new ResultRow { PlayerId = id, Name = name?.Invoke(id), Escaped = escaped, Self = id == ownId });
            }
            return view;
        }

        private static List<int> Widen(IReadOnlyList<byte> ids)
        {
            if (ids == null) return null;
            var list = new List<int>(ids.Count);
            foreach (byte id in ids) list.Add(id);
            return list;
        }

        private static bool Contains(IReadOnlyList<int> ids, int id)
        {
            if (ids == null) return false;
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true;
            return false;
        }
    }
}
