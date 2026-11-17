using System;
using System.Collections.Generic;
using System.Globalization;
using PingTheKiller.Client.Killer;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.UI
{
    public static class LobbyCopy
    {
        // Nickname plus markers only: "Bot 3 · bot", "Player · host · you". Humans get no kind word. R1 (11/17): the own
        // row also names the own skill choice ("Player · you · Ping scanner"); other rows never show a choice.
        public static string SlotText(Slot slot, int localId)
        {
            if (slot == null || slot.PlayerId == 0) return "Empty";
            bool own = slot.PlayerId == localId;
            return slot.Nickname + (slot.IsBot != 0 ? " · bot" : string.Empty) + (slot.IsHost != 0 ? " · host" : string.Empty)
                + (own ? " · you" : string.Empty) + (own && SkillIds.TryParseChoice(slot.SkillChoice, out Role role, out byte skill) ? " · " + SkillName(role, skill) : string.Empty);
        }

        // ---- R1 (11/17) lobby skill picker (HUD_SPEC 12.4) ----

        public const string PickerTitle = "Your skill";
        public const string RandomSkill = "Random skill";

        // R1 (11/17) a2: the picker's effect line (PlayMenuCopy.Effect, the one copy source) at 18 u in a 308 u column: broken
        // by hand into the fewest lines of at most PickerLineChars characters, as even as possible, so no line ends up with
        // a lone word (`s.`, `you.`). The label does not wrap by itself.
        public const int PickerLineChars = 30;
        public static string PickerEffect(Role role, byte skillId) => Balanced(PlayMenuCopy.Effect(role, skillId), PickerLineChars);

        // `text` in the fewest lines of at most `maxChars` (a longer single word gets its own line), minimizing the longest.
        // INTEG-R (11/17): a number and its unit (`2 s;`, `12 m`) are one word here, so no line starts with a bare unit.
        public static string Balanced(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
            string[] words = UnitWords(text.Split(' '));
            int n = words.Length;
            for (int lines = 2; lines <= n; lines++)
            {
                // best[k, i]: the smallest possible longest line for words[0..i) in k lines; from[k, i]: where line k starts.
                var best = new int[lines + 1, n + 1];
                var from = new int[lines + 1, n + 1];
                for (int k = 0; k <= lines; k++) for (int i = 0; i <= n; i++) best[k, i] = int.MaxValue;
                best[0, 0] = 0;
                for (int k = 1; k <= lines; k++)
                    for (int i = 1; i <= n; i++)
                        for (int j = k - 1; j < i; j++)
                        {
                            if (best[k - 1, j] == int.MaxValue) continue;
                            int length = -1;
                            for (int w = j; w < i; w++) length += words[w].Length + 1;
                            int worst = Math.Max(best[k - 1, j], length);
                            if (worst < best[k, i]) { best[k, i] = worst; from[k, i] = j; }
                        }
                if (best[lines, n] > maxChars && lines < n) continue;
                var parts = new string[lines];
                for (int k = lines, i = n; k >= 1; k--)
                {
                    int j = from[k, i];
                    parts[k - 1] = string.Join(" ", words, j, i - j);
                    i = j;
                }
                return string.Join("\n", parts);
            }
            return text;
        }

        // Joins a unit word (`s`, `m`, `%`, with trailing punctuation) to the number before it; the joined text is unchanged.
        private static string[] UnitWords(string[] words)
        {
            var merged = new List<string>(words.Length);
            foreach (string word in words)
            {
                string bare = word.TrimEnd('.', ',', ';', ':');
                bool unit = bare == "s" || bare == "m" || bare == "%";
                if (unit && merged.Count > 0 && double.TryParse(merged[merged.Count - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    merged[merged.Count - 1] += " " + word;
                else merged.Add(word);
            }
            return merged.ToArray();
        }

        public static string SkillName(Role role, byte skillId) => role == Role.Killer ? KillerHudCopy.SkillName(skillId) : SurvivorReadout.SkillName(skillId);

        // "Killer skills · 12 s cooldown": every skill of a role shares one cooldown.
        public static string GroupTitle(Role role)
            => (role == Role.Killer ? "Killer skills" : "Survivor skills") + " · "
                + ((int)(role == Role.Killer ? GameConstants.KillerSkillCooldownSeconds : GameConstants.SurvivorSkillCooldownSeconds)).ToString(CultureInfo.InvariantCulture) + " s cooldown";

        // The chosen line under the title.
        public static string Chosen(byte choice)
            => SkillIds.TryParseChoice(choice, out Role role, out byte skill)
                ? "Chosen: " + SkillName(role, skill) + " (" + (role == Role.Killer ? "Killer" : "Survivor") + ")"
                : "Chosen: random. A skill of the other role also gives a random one.";

        // Which role groups the picker lists: the own role when the host role choice fixes it, both while it is Random.
        // The host plays the chosen role; with a Killer host every other player is a Survivor.
        public static bool ShowsRole(Role role, byte hostRoleChoice, bool localIsHost)
        {
            if (hostRoleChoice == (byte)RoleChoice.Random) return true;
            if (localIsHost) return hostRoleChoice == (byte)(role == Role.Killer ? RoleChoice.Killer : RoleChoice.Survivor);
            return hostRoleChoice != (byte)RoleChoice.Killer || role == Role.Survivor;
        }

        // Display order only (the server's slot order is unchanged): the host first, then the other humans, then bots,
        // then empty slots; server order is kept inside each group. The result always has slots.Count entries.
        public static Slot[] DisplayOrder(IReadOnlyList<Slot> slots)
        {
            int count = slots?.Count ?? 0, next = 0;
            var ordered = new Slot[count];
            for (int rank = 0; rank < 4; rank++)
                for (int i = 0; i < count; i++)
                    if (Rank(slots[i]) == rank) ordered[next++] = slots[i];
            return ordered;
        }

        private static int Rank(Slot slot)
        {
            if (slot == null || slot.PlayerId == 0) return 3;
            if (slot.IsBot != 0) return 2;
            return slot.IsHost != 0 ? 0 : 1;
        }

        // The server names a bot "Bot " + its PlayerId, so ids taken by humans leave gaps (player in slot 4: Bot 1, 2, 3, 5).
        // Returns a copy of the state (new Slot objects; the received one is left as it was) in which every bot that still has
        // that default name is renamed Bot 1..N by ascending PlayerId among those bots. Any other name (a human who left and
        // became a bot keeps theirs) stays, and the numbering skips every name it keeps. Every client computes the same names
        // from the same state. Null stays null.
        public static S2C_LobbyState ContiguousBotNames(S2C_LobbyState state)
        {
            if (state == null) return null;
            var copy = new S2C_LobbyState { IsOffline = state.IsOffline, HostRoleChoice = state.HostRoleChoice };
            if (state.Slots == null) { copy.Slots = null; return copy; }
            copy.Slots = new Slot[state.Slots.Length];
            var kept = new HashSet<string>(StringComparer.Ordinal);
            var renamed = new List<Slot>();
            for (int i = 0; i < state.Slots.Length; i++)
            {
                Slot slot = state.Slots[i];
                if (slot == null) continue;
                var clone = new Slot { PlayerId = slot.PlayerId, Nickname = slot.Nickname, IsBot = slot.IsBot, IsHost = slot.IsHost, SkillChoice = slot.SkillChoice };
                copy.Slots[i] = clone;
                if (slot.PlayerId == 0) continue;
                if (DefaultBotName(slot)) renamed.Add(clone);
                else if (slot.Nickname != null) kept.Add(slot.Nickname);
            }
            // Numbering order only (ascending PlayerId); the slot array keeps the server order.
            for (int i = 1; i < renamed.Count; i++)
                for (int j = i; j > 0 && renamed[j].PlayerId < renamed[j - 1].PlayerId; j--)
                    (renamed[j], renamed[j - 1]) = (renamed[j - 1], renamed[j]);
            int number = 0;
            foreach (Slot bot in renamed)
            {
                string name;
                do name = BotName(++number); while (kept.Contains(name));
                bot.Nickname = name;
            }
            return copy;
        }

        private static bool DefaultBotName(Slot slot) => slot.IsBot != 0 && slot.Nickname == BotName(slot.PlayerId);

        private static string BotName(int number) => "Bot " + number.ToString(CultureInfo.InvariantCulture);

        public static string Title(bool offline) => offline ? "Lobby · offline" : "Lobby";

        public static string RoleLine(byte choice, bool offline)
            => (offline ? "Your role: " : "Host role: ") + (choice == (byte)RoleChoice.Killer ? "Killer" : choice == (byte)RoleChoice.Survivor ? "Survivor" : "Random");
    }
}
