using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.QA.Tests;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.UI.RulesV2;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests.Reverify
{
    // Re-verification Attack 4: SelfState readouts and local countdown (Area C of Reverify-flash.md).
    // - Cooldown ring and skill_ready, including no skill_ready while Lagging or spectating;
    // - Rescue and Protected;
    // - Killer execution guidance;
    // - NetClient S2C whitelist.
    [TestFixture]
    public sealed class SelfStateReadoutReverifyTests
    {
        private sealed class TestSelfState : ISelfState
        {
            public bool Available { get; set; } = true;
            public float SkillCooldown { get; set; }
            public float RescueProgress { get; set; }
            public float InvulnerableLeft { get; set; }
            public float CollapseLeft { get; set; } = -1;
            public ActionKind ActionKind { get; set; }
            public float ActionProgress { get; set; }
        }

        [Test]
        public void SelfStateHolder_CountdownFloorsAtZero_AndSnapsToNewPacket()
        {
            var holder = new SelfStateHolder();
            Assert.That(holder.Available, Is.False);

            // Feed packet with cooldown 10, rescue 0.5, invulnerable 2.0, collapse 1.8 at t=10.0
            holder.Feed(10f, 0.5f, 2.0f, 1.8f, 0, 0, 10.0);
            Assert.That(holder.Available, Is.True);
            Assert.That(holder.Received, Is.EqualTo(1));

            // Advance by 1.0 s to t=11.0
            holder.Advance(11.0);
            Assert.That(holder.SkillCooldown, Is.EqualTo(9.0f).Within(1e-3f));
            Assert.That(holder.RescueProgress, Is.EqualTo(0.5f), "RescueProgress is never extrapolated");
            Assert.That(holder.InvulnerableLeft, Is.EqualTo(1.0f).Within(1e-3f));
            Assert.That(holder.CollapseLeft, Is.EqualTo(0.8f).Within(1e-3f));

            // Advance to t=13.0 (collapse and invulnerable expired)
            holder.Advance(13.0);
            Assert.That(holder.InvulnerableLeft, Is.EqualTo(0f), "Invulnerable floors at 0");
            Assert.That(holder.CollapseLeft, Is.EqualTo(0f), "The collapse clock floors at 0");
            Assert.That(holder.SkillCooldown, Is.EqualTo(7.0f).Within(1e-3f));

            // New packet arrives at t=13.5 with fresh values -> snaps immediately
            holder.Feed(12f, 0.8f, 1.5f, 0f, 0, 0, 13.5);
            Assert.That(holder.SkillCooldown, Is.EqualTo(12.0f).Within(1e-3f));
            Assert.That(holder.RescueProgress, Is.EqualTo(0.8f).Within(1e-3f));
            Assert.That(holder.InvulnerableLeft, Is.EqualTo(1.5f).Within(1e-3f));
            Assert.That(holder.CollapseLeft, Is.EqualTo(0f));
            Assert.That(holder.Received, Is.EqualTo(2));
        }

        [Test]
        public void SkillReady_FiresOnceOnZeroCrossing_AndNeverWhileLaggingOrSpectating()
        {
            var holder = new SelfStateHolder();
            // Start cooldown of 2.0s
            holder.Feed(2.0f, 0, 0, 0, 0, 0, 0.0);
            Assert.That(holder.TakeSkillReady(), Is.False);

            // Advance to 1.9s (still cooling down)
            holder.Advance(1.9);
            Assert.That(holder.TakeSkillReady(), Is.False);

            // Advance to 2.0s (crosses zero)
            holder.Advance(2.0);
            Assert.That(holder.TakeSkillReady(), Is.True, "Ready edge fires when cooldown reaches 0");

            // Calling again must return false (fires once per cycle)
            Assert.That(holder.TakeSkillReady(), Is.False);

            // Verify status gate: SkillReadyAudible is true ONLY for Normal
            Assert.That(CaptureWatch.SkillReadyAudible(PlayerStatus.Normal), Is.True, "Normal survivor hears skill_ready");
            Assert.That(CaptureWatch.SkillReadyAudible(PlayerStatus.Lagging), Is.False, "Lagging survivor is silent");
            Assert.That(CaptureWatch.SkillReadyAudible(PlayerStatus.Eliminated), Is.False, "Eliminated spectator is silent");
            Assert.That(CaptureWatch.SkillReadyAudible(PlayerStatus.Escaped), Is.False, "Escaped survivor is silent");
        }

        [Test]
        public void SurvivorReadout_RescueAndProtected_RenderCorrectly()
        {
            var own = new OwnEntry { Status = PlayerStatus.Lagging, StatusTimer = 45f };
            var self = new TestSelfState { RescueProgress = 0.65f };

            // Row 3: Lagging survivor being rescued
            MeterState mRescue = SurvivorReadout.Meter(own, self, 0, 0);
            Assert.That(mRescue.Word, Is.EqualTo("Lagging"));
            Assert.That(mRescue.Reason, Does.Contain("Being restored"));
            Assert.That(mRescue.Fill, Is.EqualTo(BarFill.Ally));
            Assert.That(mRescue.Width, Is.EqualTo(0.65f).Within(1e-3f));

            // Protected tag: post-rescue immunity
            var ownNormal = new OwnEntry { Status = PlayerStatus.Normal, CaptureCount = 1 };
            var selfProt = new TestSelfState { InvulnerableLeft = 1.4f };
            MeterState mProt = SurvivorReadout.Meter(ownNormal, selfProt, 0, 0);
            Assert.That(mProt.Tag, Is.EqualTo("Protected 1.4 s"), "Protected shows one decimal tabular text");
            Assert.That(mProt.TagMuted, Is.False);

            // When InvulnerableLeft reaches 0, tag falls back to 'Captured once'
            selfProt.InvulnerableLeft = 0f;
            MeterState mFallback = SurvivorReadout.Meter(ownNormal, selfProt, 0, 0);
            Assert.That(mFallback.Tag, Is.EqualTo("Captured once"));
            Assert.That(mFallback.TagMuted, Is.True);
        }

        // GDD v2 has no execution (U6c-wire K1). This test checked the v1 execution guidance; it now checks the Killer's
        // v2 prompt slot: nothing for a Lagging Survivor, the kick near an unfinished terminal, the hatch close first.
        [Test]
        public void KillerPromptSlot_OffersKickAndHatchClose_AndNothingForALaggingTouch()
        {
            var terminals = new System.Collections.Generic.List<TerminalState>
            {
                new TerminalState { Id = 2, X = 10, Z = 10, Progress = 0 },
                new TerminalState { Id = 3, X = 20, Z = 20, Progress = 100 }
            };
            InteractPrompt none = InteractCopy.Killer(40, 40, terminals, false, 0, 0);
            Assert.That(none.Text, Is.Null, "no terminal or hatch in reach: no prompt (a Lagging Survivor never adds one)");
            InteractPrompt kick = InteractCopy.Killer(10.8f, 10, terminals, false, 0, 0);
            Assert.That(kick.Text, Is.EqualTo("Hold [E] to jam Terminal 3"));
            Assert.That(kick.Hint, Does.StartWith("Stand still"));
            Assert.That(InteractCopy.Killer(20, 20.5f, terminals, false, 0, 0).Text, Is.Null, "a done terminal cannot be kicked");
            InteractPrompt hatch = InteractCopy.Killer(10.8f, 10, terminals, true, 11, 10);
            Assert.That(hatch.Text, Is.EqualTo("Hold [E] to close the Emergency port"), "the hatch close comes first");
        }

        [Test]
        public void NetClient_IsServerPacket_AcceptsAllS2CPackets_AndRejectsAllC2SPackets()
        {
            var allPacketIds = (PacketId[])Enum.GetValues(typeof(PacketId));
            int s2cCount = 0;

            foreach (PacketId id in allPacketIds)
            {
                string name = id.ToString();
                if (name.StartsWith("S2C_", StringComparison.Ordinal))
                {
                    s2cCount++;
                    Assert.That(NetClient.IsServerPacket(id), Is.True, $"{name} must be accepted by NetClient");
                }
                else if (name.StartsWith("C2S_", StringComparison.Ordinal))
                {
                    Assert.That(NetClient.IsServerPacket(id), Is.False, $"{name} must be rejected from incoming stream");
                }
            }

            Assert.That(s2cCount, Is.EqualTo(10), "Protocol v0.6 defines 12 S2C packet types (adds S2C_SkillRejected); 11/17 (R2) retires S2C_SyncCheck and S2C_SyncResult");
        }
    }
}
