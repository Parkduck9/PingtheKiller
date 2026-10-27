using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a2.md section 3 (lobby display order) and docs/briefs/U4b-qa-a2.md item 2:
    // host first, then other humans, then bots, empty slots last; server order kept inside each group; display only;
    // row numbers are display positions 1 to 8.
    [Category("U4bQaA2")]
    public sealed class LobbyOrderQaTests
    {
        private static Slot Bot(int id) => new Slot { PlayerId = id, Nickname = "Bot " + id, IsBot = 1 };
        private static Slot Human(int id) => new Slot { PlayerId = id, Nickname = "Human " + id };
        private static Slot Host(int id) => new Slot { PlayerId = id, Nickname = "Host " + id, IsHost = 1 };
        private static Slot Empty() => new Slot();

        private static int[] Ids(Slot[] slots)
        {
            var ids = new int[slots.Length];
            for (int i = 0; i < slots.Length; i++) ids[i] = slots[i] == null ? 0 : slots[i].PlayerId;
            return ids;
        }

        private static bool IsEmpty(Slot slot) => slot == null || slot.PlayerId == 0;

        // ---- the basic rule -----------------------------------------------------------------------------------------------

        [Test]
        public void TheHostIsFirstThenOtherHumansThenBotsThenEmptySlots()
        {
            Slot[] order = LobbyCopy.DisplayOrder(new[] { Empty(), Bot(1), Human(2), Empty(), Host(3), Bot(4), Human(5), Empty() });
            Assert.That(Ids(order), Is.EqualTo(new[] { 3, 2, 5, 1, 4, 0, 0, 0 }));
        }

        [Test]
        public void OfflineTheLocalPlayerIsTheHostAndSitsInRowOneWhereverTheServerPutIt()
        {
            // Server.Core offline: bots are created first and the human joins afterwards (QA a1 F-03).
            for (int hostIndex = 0; hostIndex < 7; hostIndex++)
            {
                var slots = new Slot[8];
                int bot = 1;
                for (int i = 0; i < 8; i++) slots[i] = i == hostIndex ? Host(99) : i < 7 ? Bot(bot++) : Empty();
                Slot[] order = LobbyCopy.DisplayOrder(slots);
                Assert.That(order[0].PlayerId, Is.EqualTo(99), "host at server index " + hostIndex);
                Assert.That(LobbyCopy.SlotText(order[0], 99), Is.EqualTo("Host 99 · host · you"));
            }
        }

        [Test]
        public void OnlineAsANonHostTheHostIsFirstThenYouAndTheOtherHumansThenBots()
        {
            const int me = 3;
            // Server order: a bot, the host, another human, me, a bot.
            Slot[] order = LobbyCopy.DisplayOrder(new[] { Bot(1), Host(2), Human(5), Human(me), Bot(4), Empty(), Empty(), Empty() });
            Assert.That(order[0].PlayerId, Is.EqualTo(2), "the host is first even for a non-host viewer");
            Assert.That(order[1].PlayerId, Is.EqualTo(5), "other humans keep their server order");
            Assert.That(order[2].PlayerId, Is.EqualTo(me));
            Assert.That(order[3].PlayerId, Is.EqualTo(1));
            Assert.That(order[4].PlayerId, Is.EqualTo(4));
            Assert.That(LobbyCopy.SlotText(order[0], me), Is.EqualTo("Host 2 · host"));
            Assert.That(LobbyCopy.SlotText(order[2], me), Is.EqualTo("Human 3 · you"));
            Assert.That(LobbyCopy.SlotText(order[3], me), Is.EqualTo("Bot 1 · bot"));
        }

        [Test]
        public void OnlineAsANonHostWithNoOtherHumanTheOrderIsHostYouBots()
        {
            Slot[] order = LobbyCopy.DisplayOrder(new[] { Bot(1), Bot(2), Human(4), Host(3), Bot(5), Empty(), Empty(), Empty() });
            Assert.That(Ids(order), Is.EqualTo(new[] { 3, 4, 1, 2, 5, 0, 0, 0 }));
        }

        // ---- stability inside each group ----------------------------------------------------------------------------------

        [Test]
        public void OrderInsideEachGroupIsTheServerOrderAndNotAnIdSort()
        {
            // Ids are deliberately not ascending in any group.
            Slot[] input = { Bot(8), Human(7), Bot(2), Host(6), Human(3), Bot(5), Human(4), Empty() };
            Slot[] order = LobbyCopy.DisplayOrder(input);
            Assert.That(Ids(order), Is.EqualTo(new[] { 6, 7, 3, 4, 8, 2, 5, 0 }));
        }

        [Test]
        public void EmptySlotsKeepTheirRelativeOrderAndNullsCountAsEmpty()
        {
            Slot first = Empty(), second = Empty();
            Slot[] order = LobbyCopy.DisplayOrder(new[] { first, Bot(1), null, Host(2), second, Human(3), null, Bot(4) });
            Assert.That(Ids(order), Is.EqualTo(new[] { 2, 3, 1, 4, 0, 0, 0, 0 }));
            Assert.That(order[4], Is.SameAs(first));
            Assert.That(order[5], Is.Null, "the first null keeps its place");
            Assert.That(order[6], Is.SameAs(second));
            Assert.That(order[7], Is.Null);
        }

        [Test]
        public void ASlotWithPlayerIdZeroIsEmptyWhateverItsFlags()
        {
            // SlotText calls PlayerId 0 "Empty"; the order must agree, or a blank row would sit above real players.
            var strayHost = new Slot { PlayerId = 0, IsHost = 1 };
            var strayBot = new Slot { PlayerId = 0, IsBot = 1, Nickname = "ghost" };
            Slot[] order = LobbyCopy.DisplayOrder(new[] { strayHost, strayBot, Bot(1), Human(2) });
            Assert.That(order[0].PlayerId, Is.EqualTo(2));
            Assert.That(order[1].PlayerId, Is.EqualTo(1));
            Assert.That(order[2], Is.SameAs(strayHost));
            Assert.That(order[3], Is.SameAs(strayBot));
        }

        [Test]
        public void ADisplayOrderIsIdempotent()
        {
            Slot[] input = { Bot(1), Human(2), Empty(), Host(3), Bot(4), Human(5), Empty(), Bot(6) };
            Slot[] once = LobbyCopy.DisplayOrder(input);
            Slot[] twice = LobbyCopy.DisplayOrder(once);
            for (int i = 0; i < once.Length; i++) Assert.That(twice[i], Is.SameAs(once[i]), "row " + i);
        }

        // ---- display only: nothing in the server data changes --------------------------------------------------------------

        [Test]
        public void TheInputIsNeverReorderedOrModified()
        {
            Slot[] input = { Bot(1), Bot(2), Bot(3), Host(4), Bot(5), Empty(), Empty(), Empty() };
            Slot[] copy = (Slot[])input.Clone();
            string[] nicknames = new string[input.Length];
            for (int i = 0; i < input.Length; i++) nicknames[i] = input[i].Nickname;
            Slot[] order = LobbyCopy.DisplayOrder(input);
            Assert.That(order, Is.Not.SameAs(input), "a new array is returned");
            for (int i = 0; i < input.Length; i++)
            {
                Assert.That(input[i], Is.SameAs(copy[i]), "input slot " + i + " was reordered");
                Assert.That(input[i].Nickname, Is.EqualTo(nicknames[i]));
            }
        }

        [Test]
        public void TheLobbyPacketItselfKeepsTheServerOrder()
        {
            var lobby = new S2C_LobbyState();
            lobby.Slots[0] = Bot(1);
            lobby.Slots[1] = Bot(2);
            lobby.Slots[2] = Host(3);
            Slot[] before = (Slot[])lobby.Slots.Clone();
            LobbyCopy.DisplayOrder(lobby.Slots);
            for (int i = 0; i < before.Length; i++) Assert.That(lobby.Slots[i], Is.SameAs(before[i]));
        }

        // ---- shape of the result ------------------------------------------------------------------------------------------

        [Test]
        public void TheResultAlwaysHasAsManyEntriesAsTheInputAndIsAPermutation()
        {
            foreach (int count in new[] { 0, 1, 2, 3, 7, 8, 9 })
            {
                var input = new Slot[count];
                for (int i = 0; i < count; i++) input[i] = i % 3 == 0 ? Bot(i + 1) : i % 3 == 1 ? (Slot)Empty() : Human(i + 1);
                Slot[] order = null;
                Assert.DoesNotThrow(() => order = LobbyCopy.DisplayOrder(input), "count " + count);
                Assert.That(order.Length, Is.EqualTo(count));
                var seen = new HashSet<Slot>();
                foreach (Slot slot in order) Assert.That(seen.Add(slot), Is.True, "an entry appears twice (count " + count + ")");
                foreach (Slot slot in input) Assert.That(seen.Contains(slot), Is.True, "an entry was dropped (count " + count + ")");
            }
        }

        [Test]
        public void ANullOrEmptyListGivesAnEmptyArray()
        {
            Assert.That(LobbyCopy.DisplayOrder(null), Is.Not.Null.And.Empty);
            Assert.That(LobbyCopy.DisplayOrder(new Slot[0]), Is.Empty);
            Assert.That(LobbyCopy.DisplayOrder(new List<Slot>()), Is.Empty, "any IReadOnlyList works, not only arrays");
        }

        [Test]
        public void ARandomLobbyAlwaysSatisfiesTheOrderingRule()
        {
            var random = new Random(20261027);
            for (int round = 0; round < 500; round++)
            {
                var input = new Slot[Protocol.LobbySlotCount];
                int id = 1;
                bool hostPlaced = false;
                for (int i = 0; i < input.Length; i++)
                {
                    switch (random.Next(4))
                    {
                        case 0: input[i] = Empty(); break;
                        case 1: input[i] = Bot(id++); break;
                        case 2: input[i] = Human(id++); break;
                        default: input[i] = hostPlaced ? Human(id++) : Host(id++); hostPlaced = true; break;
                    }
                }
                Slot[] order = LobbyCopy.DisplayOrder(input);
                Assert.That(order.Length, Is.EqualTo(input.Length));
                int lastRank = -1, lastIndex = -1;
                Slot lastSlot = null;
                foreach (Slot slot in order)
                {
                    int rank = IsEmpty(slot) ? 3 : slot.IsBot != 0 ? 2 : slot.IsHost != 0 ? 0 : 1;
                    Assert.That(rank, Is.GreaterThanOrEqualTo(lastRank), "round " + round + ": host, humans, bots, empty");
                    int index = Array.IndexOf(input, slot);
                    if (rank == lastRank && lastSlot != null) Assert.That(index, Is.GreaterThan(lastIndex), "round " + round + ": stable inside a group");
                    lastRank = rank; lastIndex = index; lastSlot = slot;
                }
            }
        }

        // ---- row numbers are display positions ----------------------------------------------------------------------------

        [Test]
        public void TheRowNumbersAreFixedLabelsOneToEightCreatedByPositionNotByPlayerId()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("UI/ClientScreens.cs"));
            Assert.That(code, Does.Match(@"Text\(\s*slotRows\[i\]\s*,\s*\(i \+ 1\)\.ToString\(\)\s*,\s*""slot-number""\s*\)"),
                "row i carries the label i + 1, created once per row");
            Assert.That(Protocol.LobbySlotCount, Is.EqualTo(8));
            string show = QaSource.MethodText(code, "void ShowLobby(");
            Assert.That(show, Is.Not.Null);
            Assert.That(show, Does.Not.Contain("slot-number"), "ShowLobby never rewrites a row number");
            Assert.That(show, Does.Not.Contain("PlayerId.ToString"), "no player id is shown as a row number");
        }

        [Test]
        public void TheLobbyScreenFillsRowsFromTheDisplayOrderOnly()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("UI/ClientScreens.cs"));
            string show = QaSource.MethodText(code, "void ShowLobby(");
            Assert.That(show, Does.Contain("LobbyCopy.DisplayOrder(state.Slots)"));
            Assert.That(show, Does.Not.Contain("state.Slots["), "rows must not index the server order directly");
            Assert.That(show, Does.Match(@"LobbyCopy\.SlotText\(\s*slot\s*,\s*localId\s*\)"));
            // Counts and the host check read the same displayed slots, so they stay in step with the rows.
            Assert.That(show, Does.Contain("slot.IsBot"));
            Assert.That(show, Does.Contain("slot.PlayerId == localId && slot.IsHost"));
        }

        [Test]
        public void TheOrderIsNotAnIdSortAndNeverTouchesTheServerSlotArray()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("UI/LobbyCopy.cs"));
            Assert.That(code, Does.Not.Contain("OrderBy"));
            Assert.That(code, Does.Not.Contain(".Sort("), "a non-stable sort would break the server order inside a group");
            Assert.That(code, Does.Not.Contain("Array.Reverse"));
        }

        [Test]
        public void ARowReadsNicknamePlusMarkersAfterReordering()
        {
            Slot[] order = LobbyCopy.DisplayOrder(new[] { Bot(1), Human(2), Host(3), Empty() });
            Assert.That(LobbyCopy.SlotText(order[0], 2), Is.EqualTo("Host 3 · host"));
            Assert.That(LobbyCopy.SlotText(order[1], 2), Is.EqualTo("Human 2 · you"));
            Assert.That(LobbyCopy.SlotText(order[2], 2), Is.EqualTo("Bot 1 · bot"));
            Assert.That(LobbyCopy.SlotText(order[3], 2), Is.EqualTo("Empty"));
        }
    }
}
