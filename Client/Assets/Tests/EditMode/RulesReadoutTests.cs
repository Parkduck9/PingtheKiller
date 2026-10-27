using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Tests
{
    // Skill names keyed by Shared SkillIds, lobby display order, the wander autopilot and the scripted ambient probe.
    public sealed class RulesReadoutTests
    {
        [Test]
        public void SkillNamesAreKeyedBySharedSkillIds()
        {
            Assert.That(HudCopy.SkillName(Role.Killer, SkillIds.PingScanner), Is.EqualTo("Ping Scanner"));
            Assert.That(HudCopy.SkillName(Role.Killer, SkillIds.Overclock), Is.EqualTo("Overclock"));
            Assert.That(HudCopy.SkillName(Role.Killer, SkillIds.PacketAfterimage), Is.EqualTo("Packet Afterimage"));
            Assert.That(HudCopy.SkillName(Role.Killer, SkillIds.FirewallBlock), Is.EqualTo("Firewall Block"));
            Assert.That(HudCopy.SkillName(Role.Survivor, SkillIds.DummyPacket), Is.EqualTo("Dummy Packet"));
            Assert.That(HudCopy.SkillName(Role.Survivor, SkillIds.StealthRelay), Is.EqualTo("Stealth Relay"));
            Assert.That(HudCopy.SkillName(Role.Survivor, SkillIds.TrafficOverload), Is.EqualTo("Traffic Overload"));
            Assert.That(HudCopy.SkillName(Role.Survivor, SkillIds.EmergencyRestore), Is.EqualTo("Emergency Restore"));
            Assert.That(HudCopy.SkillName(Role.Survivor, SkillIds.WhiteNoise), Is.EqualTo("White Noise"));
            Assert.That(HudCopy.SkillName(Role.Killer, SkillIds.KillerSkillCount), Does.StartWith("Skill "));
            Assert.That(HudCopy.SkillName(Role.Survivor, SkillIds.SurvivorSkillCount), Does.StartWith("Skill "));
        }

        [Test]
        public void OnlyTheSharedPassiveIdIsPassive()
        {
            for (byte id = 0; id < SkillIds.KillerSkillCount; id++)
                Assert.That(HudCopy.IsPassive(Role.Killer, id), Is.EqualTo(id == SkillIds.PacketAfterimage), "killer " + id);
            for (byte id = 0; id < SkillIds.SurvivorSkillCount; id++)
                Assert.That(HudCopy.IsPassive(Role.Survivor, id), Is.False, "survivor " + id);
        }

        private static Slot Bot(int id) => new Slot { PlayerId = id, Nickname = "Bot " + id, IsBot = 1 };
        private static Slot Human(int id, bool host = false) => new Slot { PlayerId = id, Nickname = "P" + id, IsHost = (byte)(host ? 1 : 0) };

        [Test]
        public void OfflineHostComesFirstThenBotsInServerOrder()
        {
            // Server.Core order offline: three bots, then the player (host), then the added bots.
            var slots = new[] { Bot(1), Bot(2), Bot(3), Human(4, true), Bot(5), Bot(6), new Slot(), new Slot() };
            Slot[] ordered = LobbyCopy.DisplayOrder(slots);
            Assert.That(ordered.Length, Is.EqualTo(8));
            Assert.That(new[] { ordered[0].PlayerId, ordered[1].PlayerId, ordered[2].PlayerId, ordered[3].PlayerId, ordered[4].PlayerId, ordered[5].PlayerId },
                Is.EqualTo(new[] { 4, 1, 2, 3, 5, 6 }));
            Assert.That(ordered[6].PlayerId, Is.EqualTo(0));
            Assert.That(ordered[7].PlayerId, Is.EqualTo(0));
            Assert.That(slots[0].PlayerId, Is.EqualTo(1), "the server's array is not reordered");
        }

        [Test]
        public void HostThenOtherHumansThenBotsThenEmpty()
        {
            var slots = new Slot[] { Bot(1), Human(2), null, Bot(3), Human(4, true), Human(5), new Slot(), Bot(6) };
            Slot[] ordered = LobbyCopy.DisplayOrder(slots);
            Assert.That(ordered[0].PlayerId, Is.EqualTo(4), "host first");
            Assert.That(ordered[1].PlayerId, Is.EqualTo(2));
            Assert.That(ordered[2].PlayerId, Is.EqualTo(5));
            Assert.That(ordered[3].PlayerId, Is.EqualTo(1));
            Assert.That(ordered[4].PlayerId, Is.EqualTo(3));
            Assert.That(ordered[5].PlayerId, Is.EqualTo(6));
            Assert.That(ordered[6] == null || ordered[6].PlayerId == 0, Is.True);
            Assert.That(ordered[7] == null || ordered[7].PlayerId == 0, Is.True);
            Assert.That(LobbyCopy.DisplayOrder(null), Is.Empty);
        }

        [Test]
        public void TheLobbyScreenShowsTheDisplayOrder()
        {
            var lobby = new S2C_LobbyState();
            lobby.Slots[0] = Bot(1);
            lobby.Slots[1] = Human(2, true);
            Assert.That(LobbyCopy.SlotText(LobbyCopy.DisplayOrder(lobby.Slots)[0], 2), Is.EqualTo("P2 · host · you"));
            Assert.That(LobbyCopy.SlotText(LobbyCopy.DisplayOrder(lobby.Slots)[1], 2), Is.EqualTo("Bot 1 · bot"));
        }

        [Test]
        public void WanderTurnsSlowlyInOpenSpace()
        {
            var map = new FixedMap();
            var wander = new Wander(0);
            float yaw = wander.Next(map, 30, 30, 1);
            Assert.That(yaw, Is.EqualTo(Wander.TurnRate).Within(.001f));
            Assert.That(wander.Next(map, 30, 30, float.NaN), Is.EqualTo(yaw).Within(.001f), "a bad dt does not turn");
        }

        [Test]
        public void WanderReversesBeforeAWall()
        {
            var map = new FixedMap();
            // Facing +z, 1.5 m from the north boundary wall (z = 60): the wall is within 2 m ahead.
            Assert.That(Wander.WallAhead(map, 30, 58.5f, 0), Is.True);
            Assert.That(Wander.WallAhead(map, 30, 30, 0), Is.False);
            var wander = new Wander(0);
            float yaw = wander.Next(map, 30, 58.5f, 0);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, 180)), Is.LessThan(1), "heading reversed");
            Assert.That(Wander.WallAhead(map, 30, 58.5f, yaw), Is.False);
            // The turn direction flipped as well.
            Assert.That(Mathf.DeltaAngle(yaw, wander.Next(map, 30, 30, 1)), Is.EqualTo(-Wander.TurnRate).Within(.01f));
        }

        [Test]
        public void WanderFindsAClearHeadingInACorner()
        {
            var map = new FixedMap();
            var wander = new Wander(45); // facing the north-east corner, 1 m from both walls
            float yaw = wander.Next(map, 59, 59, 0);
            Assert.That(Wander.WallAhead(map, 59, 59, yaw), Is.False);
        }

        [Test]
        public void AmbientIsNeverBlackAndBrightestFromAbove()
        {
            var directions = new[] { Vector3.up, Vector3.forward, Vector3.right, Vector3.back, Vector3.down };
            var results = new Color[directions.Length];
            SceneLighting.AmbientProbe().Evaluate(directions, results);
            for (int i = 0; i < results.Length; i++)
                Assert.That(results[i].r + results[i].g + results[i].b, Is.GreaterThan(.1f), "ambient towards " + directions[i]);
            Assert.That(results[0].grayscale, Is.GreaterThan(results[1].grayscale + .05f), "wall tops get more ambient than wall sides");
            for (int i = 1; i < 4; i++) Assert.That(results[i].grayscale, Is.EqualTo(results[1].grayscale).Within(.01f), "sides are even");
        }
    }
}
