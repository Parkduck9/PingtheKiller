using System;
using NUnit.Framework;
using PingTheKiller.Client.Net;
using PingTheKiller.Client.UI;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.Tests
{
    // Q3 (HUD_SPEC 14.3 "Server reason"): an S2C_SkillRejected for the own pending press shows its reason at once and
    // disarms the 0.5 s watch; the watch stays only as a fallback.
    public sealed class SkillRejectFeedbackTests
    {
        private const int Own = 3, KillerId = 1;

        private static SurvivorFrame Frame() => new SurvivorFrame
        {
            OwnId = Own, Own = new OwnEntry { Status = PlayerStatus.Normal, X = 40, Z = 40 }, Mates = new Mate[0], Names = id => "Me"
        };

        private static readonly SkillRejectReason[] Reasons = (SkillRejectReason[])Enum.GetValues(typeof(SkillRejectReason));

        // ---- Copy ----

        [Test]
        public void CopyTableIsPinned()
        {
            // Survivor (14.1a, no bar).
            Assert.That(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.DummyPacket, SkillRejectReason.Cooldown, PlayerStatus.Normal), Is.EqualTo("Skill still on cooldown."));
            Assert.That(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.EmergencyRestore, SkillRejectReason.NoTarget, PlayerStatus.Normal),
                Is.EqualTo("Emergency restore needs a Lagging Survivor next to you.").And.EqualTo(SurvivorReadout.RestoreNeedsTarget));
            Assert.That(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.DummyPacket, SkillRejectReason.NotNormal, PlayerStatus.Lagging),
                Is.EqualTo("Skills are off while Lagging.").And.EqualTo(SurvivorReadout.SkillsOffLagging));
            Assert.That(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.DummyPacket, SkillRejectReason.NotNormal, PlayerStatus.Eliminated), Is.Null);
            Assert.That(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.DummyPacket, SkillRejectReason.NotNormal, PlayerStatus.Escaped), Is.Null);
            foreach (SkillRejectReason generic in new[] { SkillRejectReason.NotInMatch, SkillRejectReason.NotYourSkill, SkillRejectReason.Sandbox })
            {
                Assert.That(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.WhiteNoise, generic, PlayerStatus.Normal), Is.EqualTo("Skill not used — the server did not accept it."));
                Assert.That(SkillFeedback.RejectionLine(Role.Killer, SkillIds.Overclock, generic, PlayerStatus.Normal), Is.EqualTo("Not used · server did not accept"));
            }
            // Killer (6.1c, muted).
            Assert.That(SkillFeedback.RejectionLine(Role.Killer, SkillIds.Overclock, SkillRejectReason.Cooldown, PlayerStatus.Normal), Is.EqualTo("Not used · still on cooldown"));
            Assert.That(SkillFeedback.RejectionLine(Role.Killer, SkillIds.PingScanner, SkillRejectReason.NoTarget, PlayerStatus.Normal), Is.EqualTo("Not used · no Survivor to scan"));
            Assert.That(SkillFeedback.RejectionLine(Role.Killer, SkillIds.FirewallBlock, SkillRejectReason.NoSwitchInProgress, PlayerStatus.Normal),
                Is.EqualTo("Not used · nothing to lock").And.EqualTo(SkillFeedback.NoSwitchReason));
            // R1 (11/17): no skill is passive; a Passive reason (never sent now) reads as the generic refusal.
            Assert.That(SkillFeedback.RejectionLine(Role.Killer, SkillIds.PacketTrace, SkillRejectReason.Passive, PlayerStatus.Normal),
                Is.EqualTo(SkillFeedback.KillerRefused));
            // A reason that does not fit the role, or an unknown value, falls back to the generic line.
            Assert.That(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.DummyPacket, SkillRejectReason.NoSwitchInProgress, PlayerStatus.Normal), Is.EqualTo(SkillFeedback.SurvivorRefused));
            Assert.That(SkillFeedback.RejectionLine(Role.Killer, SkillIds.Overclock, SkillRejectReason.NotNormal, PlayerStatus.Normal), Is.EqualTo(SkillFeedback.KillerRefused));
            Assert.That(SkillFeedback.RejectionLine(Role.Killer, SkillIds.Overclock, (SkillRejectReason)99, PlayerStatus.Normal), Is.EqualTo(SkillFeedback.KillerRefused));
        }

        [Test]
        public void NoLineSaysErrorOrFailAndKillerLinesFitTheBlock()
        {
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
                for (byte skill = 0; skill < SkillIds.SurvivorSkillCount; skill++)
                    foreach (SkillRejectReason reason in Reasons)
                        foreach (PlayerStatus status in new[] { PlayerStatus.Normal, PlayerStatus.Lagging, PlayerStatus.Eliminated })
                        {
                            string line = SkillFeedback.RejectionLine(role, skill, reason, status);
                            if (line == null) continue;
                            Assert.That(line.ToLowerInvariant(), Does.Not.Contain("error").And.Not.Contain("fail"), line);
                            if (role == Role.Killer) Assert.That(line.Length, Is.LessThanOrEqualTo(34), line);
                        }
        }

        // Pre-checks and server reasons for the same case show the same text (PressReason vs RejectionLine).
        [Test]
        public void LocalPreChecksAndServerReasonsAgree()
        {
            Assert.That(SkillFeedback.PressReason(Role.Survivor, SkillIds.EmergencyRestore, PlayerStatus.Normal, 0, false, out _),
                Is.EqualTo(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.EmergencyRestore, SkillRejectReason.NoTarget, PlayerStatus.Normal)));
            Assert.That(SkillFeedback.PressReason(Role.Survivor, SkillIds.DummyPacket, PlayerStatus.Lagging, 0, true, out _),
                Is.EqualTo(SkillFeedback.RejectionLine(Role.Survivor, SkillIds.DummyPacket, SkillRejectReason.NotNormal, PlayerStatus.Lagging)));
        }

        // ---- Survivor ----

        [Test]
        public void SurvivorRejectionShowsTheReasonAtOnceAndDisarmsTheWatch()
        {
            var state = new SurvivorFxState();
            state.Press.OnPress(10, true);
            string shown = state.OnRejected(SkillIds.DummyPacket, SkillIds.DummyPacket, SkillRejectReason.Cooldown, PlayerStatus.Normal, null, 10.08);
            Assert.That(shown, Is.EqualTo(SkillFeedback.SurvivorOnCooldown));
            FxLine line = state.Line.Current(10.1);
            Assert.That(line.Text, Is.EqualTo("Skill still on cooldown."));
            Assert.That(line.Tone, Is.EqualTo(LineTone.Reason), "same styling as 14.3: no bar, muted");
            Assert.That(line.Seconds, Is.EqualTo(SkillFeedback.ReasonSeconds));
            Assert.That(state.Press.Armed, Is.False);
            Assert.That(state.Press.Tick(11, 0f), Is.False, "the 0.5 s fallback no longer fires");
            Assert.That(state.Line.Current(10.1).Text, Is.EqualTo("Skill still on cooldown."));
        }

        [Test]
        public void SurvivorEffectAfterARejectionReplacesTheLine()
        {
            var state = new SurvivorFxState();
            state.Press.OnPress(10, true);
            state.OnRejected(SkillIds.DummyPacket, SkillIds.DummyPacket, SkillRejectReason.NotYourSkill, PlayerStatus.Normal, null, 10.05);
            Assert.That(state.Line.Current(10.06).Text, Is.EqualTo(SkillFeedback.SurvivorRefused));
            state.OnEffect(Frame(), false, Own, SkillIds.DummyPacket, 40, 40, 3, 10.1);
            Assert.That(state.Line.Current(10.2).Text, Does.StartWith("Dummy packet:"));
        }

        [Test]
        public void SurvivorRejectionAfterAnOwnEffectIsIgnored()
        {
            var state = new SurvivorFxState();
            state.Press.OnPress(10, true);
            state.OnEffect(Frame(), false, Own, SkillIds.DummyPacket, 40, 40, 3, 10.05);
            Assert.That(state.OnRejected(SkillIds.DummyPacket, SkillIds.DummyPacket, SkillRejectReason.Cooldown, PlayerStatus.Normal, null, 10.1), Is.Null);
            Assert.That(state.Line.Current(10.2).Text, Does.StartWith("Dummy packet:"));
        }

        [Test]
        public void SurvivorRejectionWithoutAPendingPressOrForAnotherIdIsIgnored()
        {
            var state = new SurvivorFxState();
            // A press the client already explained (cooldown shown, or a local reason) never armed the watch.
            state.Press.OnPress(10, false);
            Assert.That(state.OnRejected(SkillIds.DummyPacket, SkillIds.DummyPacket, SkillRejectReason.Cooldown, PlayerStatus.Normal, null, 10.05), Is.Null);
            Assert.That(state.Line.Current(10.1).Shown, Is.False);
            // Another id does not answer the own press: the watch stays armed and the fallback still fires.
            state.Press.OnPress(20, true);
            Assert.That(state.OnRejected(SkillIds.DummyPacket, SkillIds.WhiteNoise, SkillRejectReason.Cooldown, PlayerStatus.Normal, null, 20.05), Is.Null);
            Assert.That(state.Press.Armed, Is.True);
            Assert.That(state.Press.Tick(20.6, 0f), Is.True, "fallback when no rejection for the own press arrives");
        }

        [Test]
        public void SurvivorNotNormalShowsLaggingOnlyWhenThePromptRowDoesNot()
        {
            var state = new SurvivorFxState();
            state.Press.OnPress(10, true);
            Assert.That(state.OnRejected(SkillIds.DummyPacket, SkillIds.DummyPacket, SkillRejectReason.NotNormal, PlayerStatus.Lagging, SurvivorReadout.SkillsOffLagging, 10.05), Is.Null);
            Assert.That(state.Press.Armed, Is.False, "answered, even without a line");
            state.Press.OnPress(20, true);
            Assert.That(state.OnRejected(SkillIds.DummyPacket, SkillIds.DummyPacket, SkillRejectReason.NotNormal, PlayerStatus.Lagging, null, 20.05), Is.EqualTo(SurvivorReadout.SkillsOffLagging));
            state.Press.OnPress(30, true);
            Assert.That(state.OnRejected(SkillIds.DummyPacket, SkillIds.DummyPacket, SkillRejectReason.NotNormal, PlayerStatus.Eliminated, null, 30.05), Is.Null);
        }

        // ---- Killer ----

        [Test]
        public void KillerRejectionShowsTheMutedReasonAtOnceAndDisarmsTheWatch()
        {
            var state = new KillerFxState();
            state.Press.OnPress(10, true);
            Assert.That(state.OnRejected(SkillIds.PingScanner, SkillIds.PingScanner, SkillRejectReason.NoTarget, 10.05), Is.EqualTo(SkillFeedback.KillerNoScanTarget));
            FxLine line = state.Line(10.1);
            Assert.That(line.Text, Is.EqualTo("Not used · no Survivor to scan"));
            Assert.That(line.Tone, Is.EqualTo(LineTone.Reason));
            Assert.That(state.Press.Armed, Is.False);
            Assert.That(state.Press.Tick(11, 0f), Is.False);
            Assert.That(state.Line(12.1).Text, Is.Null, "reason lines show 2 s");
        }

        [Test]
        public void KillerEffectAfterARejectionAndRejectionAfterAnEffect()
        {
            var state = new KillerFxState();
            state.Press.OnPress(10, true);
            state.OnRejected(SkillIds.Overclock, SkillIds.Overclock, SkillRejectReason.Cooldown, 10.05);
            Assert.That(state.Line(10.06).Text, Is.EqualTo(SkillFeedback.KillerOnCooldown));
            state.OnEffect(KillerId, KillerId, SkillIds.Overclock, 0, 0, 3, 10.1);
            Assert.That(state.Line(10.2).Text, Does.StartWith("Overclock:"), "an own effect replaces the reason");
            // A rejection after the own effect of the press is ignored.
            state.Press.OnPress(20, true);
            state.OnEffect(KillerId, KillerId, SkillIds.Overclock, 0, 0, 3, 20.05);
            Assert.That(state.OnRejected(SkillIds.Overclock, SkillIds.Overclock, SkillRejectReason.Cooldown, 20.1), Is.Null);
            Assert.That(state.Line(20.2).Text, Does.StartWith("Overclock:"));
        }

        [Test]
        public void TheClientAcceptsTheNewServerPacket()
        {
            Assert.That(NetClient.IsServerPacket(PacketId.S2C_SkillRejected), Is.True);
            Assert.That((ushort)PacketId.S2C_SkillRejected, Is.EqualTo((ushort)0x0016));
        }
    }
}
