using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // Written from docs/briefs/U4b-a1.md section 4 "Input": hold E = Interact(1) once on press and Interact(0) once on release
    // (also focus loss, cursor release, match end, disconnect), survivors only; Q = UseSkill with the MatchStart SkillId on press,
    // never for a passive skill and never while not Normal.
    public sealed class ActionInputQaTests
    {
        // Feeds a per-frame key state and records every packet that would be sent.
        private static List<byte> Drive(InteractInput input, params bool[] frames)
        {
            var sent = new List<byte>();
            foreach (bool held in frames)
            {
                C2S_Interact packet;
                if (input.TryCreate(held, out packet)) sent.Add(packet.Active);
            }
            return sent;
        }

        private static bool Frames(int count, bool value, out bool[] frames)
        {
            frames = new bool[count];
            for (int i = 0; i < count; i++) frames[i] = value;
            return true;
        }

        // ---- hold E -----------------------------------------------------------------------------------------------------

        [Test]
        public void APressSendsExactlyOneActiveOne()
        {
            var input = new InteractInput();
            List<byte> sent = Drive(input, false, false, true);
            Assert.That(sent, Is.EqualTo(new List<byte> { 1 }));
            Assert.That(input.Held, Is.True);
        }

        [Test]
        public void HoldingForAThousandFramesSendsNoRepeats()
        {
            var input = new InteractInput();
            bool[] held;
            Frames(1000, true, out held);
            List<byte> sent = Drive(input, held);
            Assert.That(sent, Is.EqualTo(new List<byte> { 1 }), "one press packet and nothing more while held");
        }

        [Test]
        public void ReleaseSendsExactlyOneActiveZeroAndThenStaysSilent()
        {
            var input = new InteractInput();
            bool[] hold, idle;
            Frames(30, true, out hold);
            Frames(100, false, out idle);
            var frames = new List<bool>(hold);
            frames.AddRange(idle);
            List<byte> sent = Drive(input, frames.ToArray());
            Assert.That(sent, Is.EqualTo(new List<byte> { 1, 0 }));
            Assert.That(input.Held, Is.False);
        }

        [Test]
        public void ReleasingWithoutEverPressingSendsNothing()
        {
            var input = new InteractInput();
            Assert.That(Drive(input, false, false, false), Is.Empty);
            C2S_Interact packet;
            Assert.That(input.TryRelease(out packet), Is.False);
            Assert.That(packet, Is.Null);
        }

        [Test]
        public void EachPressReleaseCycleSendsOneOneAndOneZero()
        {
            var input = new InteractInput();
            List<byte> sent = Drive(input, true, true, false, true, false, false, true, true, true, false);
            Assert.That(sent, Is.EqualTo(new List<byte> { 1, 0, 1, 0, 1, 0 }));
        }

        // Focus loss, cursor release, match end and disconnect all reach the pure class as TryCreate(false) or TryRelease.
        [Test]
        public void AForcedReleaseWhileHeldSendsOneZeroAndASecondOneSendsNothing()
        {
            var input = new InteractInput();
            C2S_Interact packet;
            Assert.That(input.TryCreate(true, out packet), Is.True);
            Assert.That(packet.Active, Is.EqualTo(1));
            Assert.That(input.TryRelease(out packet), Is.True, "focus loss / cursor release / match end / disconnect after a press");
            Assert.That(packet.Active, Is.EqualTo(0));
            Assert.That(input.TryRelease(out packet), Is.False, "the second forced release is a no-op");
            Assert.That(input.TryCreate(false, out packet), Is.False, "and the next idle frame too");
            Assert.That(input.Held, Is.False);
        }

        [Test]
        public void AForcedReleaseWithoutAPressSendsNothing()
        {
            var input = new InteractInput();
            C2S_Interact packet;
            Assert.That(input.TryRelease(out packet), Is.False, "no Active=0 unless Active=1 was sent");
            Assert.That(packet, Is.Null);
        }

        [Test]
        public void LosingFocusWhileHeldSendsAZeroEvenThoughTheKeyIsStillDown()
        {
            // ClientApp passes (focused && !cursorReleased && E down): a lost focus is held=false although E is physically down.
            var input = new InteractInput();
            List<byte> sent = Drive(input, true, true, false, false, false);
            Assert.That(sent, Is.EqualTo(new List<byte> { 1, 0 }));
            // Focus returns with E still down: that is a fresh press and may send Active=1 again, exactly once.
            sent = Drive(input, true, true, true);
            Assert.That(sent, Is.EqualTo(new List<byte> { 1 }));
        }

        [Test]
        public void ResetForgetsTheKeyWithoutSendingAndAFollowingPressSendsOne()
        {
            var input = new InteractInput();
            Drive(input, true);
            input.Reset();
            Assert.That(input.Held, Is.False);
            C2S_Interact packet;
            Assert.That(input.TryRelease(out packet), Is.False, "Reset sent nothing and owes nothing");
            Assert.That(Drive(input, true, true), Is.EqualTo(new List<byte> { 1 }));
        }

        [Test]
        public void EveryPacketIsAValidInteractWithAnActiveFlagOfZeroOrOne()
        {
            var input = new InteractInput();
            C2S_Interact packet;
            input.TryCreate(true, out packet);
            Assert.That(packet.Id, Is.EqualTo(PacketId.C2S_Interact));
            input.TryCreate(false, out packet);
            Assert.That(packet.Active, Is.EqualTo(0));
        }

        // ---- survivors only (the pure class has no role, so the guard is checked where it lives: ClientApp) -------------

        [Test]
        public void ClientAppOnlyEverFeedsInteractInputForSurvivors()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(Regex.IsMatch(source, @"survivor\s*&&\s*interact\.TryCreate\("), Is.True,
                "hold-E is gated on the survivor role");
            Assert.That(source, Does.Contain("bool survivor = role == Role.Survivor;"));
            Assert.That(source, Does.Contain("active && keyboard != null && keyboard.eKey.isPressed"),
                "E counts only while the window is focused and the cursor is captured");
        }

        // The brief asks for "a final Interact 0 only if Active=1 was sent" and "Killer -> no Interact". EndMatch sends an
        // unconditional Interact { Active = 0 } (U4b-a1 3.3 asks for one too), so a killer emits one at match end. See finding F-02.
        [Test]
        public void MatchEndSendsInteractZeroOnlyWhenAPressIsOutstanding()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string end = QaSource.MethodText(source, "private void EndMatch(");
            Assert.That(end, Is.Not.Null, "ClientApp.EndMatch");
            Assert.That(Regex.IsMatch(end, @"new\s+C2S_Interact\s*\{\s*Active\s*=\s*0\s*\}"), Is.False,
                "EndMatch must use InteractInput.TryRelease (a zero only after a sent press), not an unconditional Interact packet");
            Assert.That(end, Does.Contain("interact.TryRelease("));
        }

        [Test]
        public void DisconnectAndNewLinksReleaseThroughInteractInput()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string release = QaSource.MethodText(source, "private void Release()");
            Assert.That(release, Does.Contain("interact.TryRelease("), "the E release goes out before the old link is disposed");
            string focus = QaSource.MethodText(source, "private void OnApplicationFocus(");
            Assert.That(focus, Does.Contain("interact.TryRelease("), "focus loss releases E");
        }

        // ---- Q: UseSkill -------------------------------------------------------------------------------------------------

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        [TestCase((byte)3)]
        [TestCase((byte)4)]
        public void QOnPressSendsTheMatchStartSkillIdForEverySurvivorSkill(byte skill)
        {
            C2S_UseSkill packet;
            Assert.That(SkillInput.TryCreate(true, Role.Survivor, skill, PlayerStatus.Normal, out packet), Is.True);
            Assert.That(packet.SkillId, Is.EqualTo(skill));
        }

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)3)]
        public void QOnPressSendsTheActiveKillerSkills(byte skill)
        {
            C2S_UseSkill packet;
            Assert.That(SkillInput.TryCreate(true, Role.Killer, skill, PlayerStatus.Normal, out packet), Is.True);
            Assert.That(packet.SkillId, Is.EqualTo(skill));
        }

        [Test]
        public void ThePassiveKillerSkillIsNeverSent()
        {
            C2S_UseSkill packet;
            Assert.That(SkillInput.TryCreate(true, Role.Killer, 2, PlayerStatus.Normal, out packet), Is.False, "Packet Afterimage is passive");
            Assert.That(packet, Is.Null);
        }

        [Test]
        public void SurvivorSkillTwoIsNotPassive()
        {
            C2S_UseSkill packet;
            Assert.That(SkillInput.TryCreate(true, Role.Survivor, 2, PlayerStatus.Normal, out packet), Is.True, "Traffic Overload is an active survivor skill");
            Assert.That(packet.SkillId, Is.EqualTo(2));
        }

        [Test]
        public void NothingIsSentWithoutAPress()
        {
            foreach (Role role in new[] { Role.Survivor, Role.Killer })
                for (byte skill = 0; skill <= 4; skill++)
                {
                    C2S_UseSkill packet;
                    Assert.That(SkillInput.TryCreate(false, role, skill, PlayerStatus.Normal, out packet), Is.False);
                    Assert.That(packet, Is.Null);
                }
        }

        [Test]
        public void NothingIsSentWhileNotNormal()
        {
            foreach (PlayerStatus status in new[] { PlayerStatus.Lagging, PlayerStatus.Escaped, PlayerStatus.Eliminated })
                foreach (Role role in new[] { Role.Survivor, Role.Killer })
                    for (byte skill = 0; skill <= 4; skill++)
                    {
                        C2S_UseSkill packet;
                        Assert.That(SkillInput.TryCreate(true, role, skill, status, out packet), Is.False, role + " " + skill + " " + status);
                        Assert.That(packet, Is.Null);
                    }
        }

        [Test]
        public void AnUnknownStatusByteIsNotNormalAndSendsNothing()
        {
            C2S_UseSkill packet;
            Assert.That(SkillInput.TryCreate(true, Role.Survivor, 0, (PlayerStatus)200, out packet), Is.False);
        }

        [Test]
        public void ClientAppFiresSkillsOnThePressEdgeUsingTheMatchStartSkillIdAndTheLocalStatus()
        {
            string source = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(source, Does.Contain("keyboard.qKey.wasPressedThisFrame"), "Q is edge-triggered, never isPressed");
            Assert.That(source, Does.Not.Contain("qKey.isPressed"));
            Assert.That(source, Does.Contain("SkillInput.TryCreate("));
            Assert.That(source, Does.Contain("skillId = match.SkillId"), "the id comes from MatchStart");
            Assert.That(Regex.IsMatch(source, @"SkillInput\.TryCreate\([^;]*role,\s*skillId,\s*localStatus"), Is.True,
                "role, MatchStart skill and the server-reported local status are what the rule sees");
            Assert.That(Regex.IsMatch(source, @"localStatus\s*=\s*\(PlayerStatus\)player\.Status"), Is.True, "status comes from WorldState, not a guess");
        }
    }
}
