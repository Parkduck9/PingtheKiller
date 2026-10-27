using PingTheKiller.Client.UI;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.World
{
    // Hold-E hacking/rescue (survivors only): Active = 1 once on press, Active = 0 once on release. Never repeats while
    // held. Release also covers focus loss, cursor release, match end and disconnect: the caller passes held = false.
    public sealed class InteractInput
    {
        public bool Held { get; private set; }

        public bool TryCreate(bool held, out C2S_Interact packet)
        {
            packet = null;
            if (held == Held) return false;
            Held = held;
            packet = new C2S_Interact { Active = (byte)(held ? 1 : 0) };
            return true;
        }

        // Forced release (match end, leave): a zero is due only if a press was sent.
        public bool TryRelease(out C2S_Interact packet) => TryCreate(false, out packet);

        // New match or new link: forget the key without sending.
        public void Reset() => Held = false;
    }

    public static class SkillInput
    {
        // Q on press only. Passive skills are never sent; a player that is not Normal cannot use a skill (GDD 3.3).
        public static bool TryCreate(bool pressed, Role role, byte skillId, PlayerStatus status, out C2S_UseSkill packet)
        {
            packet = null;
            if (!pressed || HudCopy.IsPassive(role, skillId) || status != PlayerStatus.Normal) return false;
            packet = new C2S_UseSkill { SkillId = skillId };
            return true;
        }
    }
}
