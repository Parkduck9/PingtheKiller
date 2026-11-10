using PingTheKiller.Shared.Net;

namespace PingTheKiller.Shared.Game
{
    // v0.6 S2C_SkillRejected.Reason: why the server refused a C2S_UseSkill. 0 is invalid on the wire.
    public enum SkillRejectReason : byte
    {
        NotInMatch = 1,         // no match is running (lobby, result)
        NotYourSkill = 2,       // the id is not the caster's own skill, or out of range for the role
        NotNormal = 3,          // the caster is Lagging, Eliminated or Escaped
        Cooldown = 4,           // the server cooldown has not ended (TimeEpsilon edge included)
        NoTarget = 5,           // Ping scanner: no active Survivor; Emergency restore: no Lagging ally in radius
        NoSwitchInProgress = 6, // Firewall block: no unopened exit has switch progress
        Passive = 7,            // Packet afterimage cannot be activated
        Sandbox = 8             // the dev sandbox has no skills
    }

    internal static class SkillRejectWire
    {
        internal const byte Max = (byte)SkillRejectReason.Sandbox;

        internal static byte ReadReason(ref PacketReader r)
        {
            byte value = r.ReadByte(Max);
            if (value == 0) throw new PacketFormatException("Undefined skill reject reason.");
            return value;
        }
    }
}
