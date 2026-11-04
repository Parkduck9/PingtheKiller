using System;
using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.QA.Tests;
using PingTheKiller.Client.UI;
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
            public float ExecutionGraceLeft { get; set; }
        }

        [Test]
        public void SelfStateHolder_CountdownFloorsAtZero_AndSnapsToNewPacket()
        {
            var holder = new SelfStateHolder();
            Assert.That(holder.Available, Is.False);

            // Feed packet with cooldown 10, rescue 0.5, invulnerable 2.0, grace 1.8 at t=10.0
            holder.Feed(10f, 0.5f, 2.0f, 1.8f, 10.0);
            Assert.That(holder.Available, Is.True);
            Assert.That(holder.Received, Is.EqualTo(1));

            // Advance by 1.0 s to t=11.0
            holder.Advance(11.0);
            Assert.That(holder.SkillCooldown, Is.EqualTo(9.0f).Within(1e-3f));
            Assert.That(holder.RescueProgress, Is.EqualTo(0.5f), "RescueProgress is never extrapolated");
            Assert.That(holder.InvulnerableLeft, Is.EqualTo(1.0f).Within(1e-3f));
            Assert.That(holder.ExecutionGraceLeft, Is.EqualTo(0.8f).Within(1e-3f));

            // Advance to t=13.0 (grace and invulnerable expired)
            holder.Advance(13.0);
            Assert.That(holder.InvulnerableLeft, Is.EqualTo(0f), "Invulnerable floors at 0");
            Assert.That(holder.ExecutionGraceLeft, Is.EqualTo(0f), "Grace floors at 0");
            Assert.That(holder.SkillCooldown, Is.EqualTo(7.0f).Within(1e-3f));

            // New packet arrives at t=13.5 with fresh values -> snaps immediately
            holder.Feed(12f, 0.8f, 1.5f, 0f, 13.5);
            Assert.That(holder.SkillCooldown, Is.EqualTo(12.0f).Within(1e-3f));
            Assert.That(holder.RescueProgress, Is.EqualTo(0.8f).Within(1e-3f));
            Assert.That(holder.InvulnerableLeft, Is.EqualTo(1.5f).Within(1e-3f));
            Assert.That(holder.ExecutionGraceLeft, Is.EqualTo(0f));
            Assert.That(holder.Received, Is.EqualTo(2));
        }

        [Test]
        public void SkillReady_FiresOnceOnZeroCrossing_AndNeverWhileLaggingOrSpectating()
        {
            var holder = new SelfStateHolder();
            // Start cooldown of 2.0s
            holder.Feed(2.0f, 0, 0, 0, 0.0);
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
            Assert.That(mRescue.Reason, Does.Contain("Rescue in progress"));
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

        [Test]
        public void ExecutionGuidance_TransitionsThroughGraceStepBackAndReady()
        {
            var guidance = new ExecutionGuidance();
            Assert.That(guidance.Prompt, Is.EqualTo(ExecutionPrompt.None));

            int handle = 42;
            // Target acquired at 1.0 m with 1.6 s grace left -> starts disarmed (touch spent)
            guidance.Update(handle, 1.0f, true, 1.6f);
            Assert.That(guidance.Target, Is.EqualTo(handle));
            Assert.That(guidance.Armed, Is.False, "In-grace touch leaves killer disarmed");
            Assert.That(guidance.Prompt, Is.EqualTo(ExecutionPrompt.Grace));
            Assert.That(KillerHudCopy.PromptLine(guidance.Prompt, 1.6f), Is.EqualTo("Wait 1.6 s, step back, touch again."));

            // Grace expires (0s), but killer is still at 1.0m (not stepped back beyond 1.2m)
            guidance.Update(handle, 1.0f, true, 0f);
            Assert.That(guidance.Armed, Is.False);
            Assert.That(guidance.Prompt, Is.EqualTo(ExecutionPrompt.StepBack));
            Assert.That(KillerHudCopy.PromptLine(guidance.Prompt, 0f), Is.EqualTo("Step back 1.2 m, then touch again."));

            // Killer steps back beyond 1.2m (e.g. 1.5m) -> Armed becomes true
            guidance.Update(handle, 1.5f, true, 0f);
            Assert.That(guidance.Armed, Is.True, "Armed once out of contact range");
            Assert.That(guidance.Prompt, Is.EqualTo(ExecutionPrompt.Ready));
            Assert.That(KillerHudCopy.PromptLine(guidance.Prompt, 0f), Is.EqualTo("Touch again to eliminate."));

            // Killer moves beyond 3.0m range -> prompt hidden
            guidance.Update(handle, 3.5f, true, 0f);
            Assert.That(guidance.Prompt, Is.EqualTo(ExecutionPrompt.None));

            // Without SelfState available -> prompt hidden
            guidance.Update(handle, 1.0f, false, 0f);
            Assert.That(guidance.Prompt, Is.EqualTo(ExecutionPrompt.None));
        }

        [Test]
        public void NetClient_IsServerPacket_AcceptsAllEightS2CPackets_AndRejectsAllC2SPackets()
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

            Assert.That(s2cCount, Is.EqualTo(10), "Protocol v0.4 defines exactly 10 S2C packet types (adds S2C_SyncCheck and S2C_SyncResult)");
        }
    }
}
