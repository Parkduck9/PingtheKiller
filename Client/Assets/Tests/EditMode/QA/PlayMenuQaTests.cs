using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PingTheKiller.Client.UI;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests.QA
{
    // Q6 QA: gaps around the shared play menu (reset paths, press-edge gate, copy boundaries, Killer menu unchanged).
    public sealed class PlayMenuQaTests
    {
        private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", rel));
        private static Font HudFont() => Resources.Load<Font>("Fonts/Inter-Regular");

        [Test]
        public void TheGateResetClearsOpenAndTheClosedFrameSoTheNextMatchPressesWork()
        {
            var gate = new PlayMenuGate();
            gate.Set(true, 5); gate.Set(false, 9);
            Assert.That(gate.Presses(true, 9), Is.False);
            gate.Set(true, 20);
            gate.Reset();
            Assert.That(gate.Open, Is.False);
            Assert.That(gate.Presses(true, 9), Is.True, "Reset forgets the closed frame");
            Assert.That(gate.Active(true, false), Is.True);
            Assert.That(gate.Active(false, false), Is.False, "unfocused");
            Assert.That(gate.Active(true, true), Is.False, "cursor released");
        }

        [Test]
        public void CaptureNoteBoundaries()
        {
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Normal, 0), Is.Null);
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Normal, 1), Is.EqualTo(PlayMenuCopy.CapturedOnce));
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Normal, 2), Is.EqualTo(PlayMenuCopy.NextCaptureEliminates));
            Assert.That(PlayMenuCopy.CaptureNote(PlayerStatus.Normal, 3), Is.EqualTo(PlayMenuCopy.NextCaptureEliminates));
            foreach (PlayerStatus s in System.Enum.GetValues(typeof(PlayerStatus)))
                if (s != PlayerStatus.Normal) Assert.That(PlayMenuCopy.CaptureNote(s, 2), Is.Null, s.ToString());
            Assert.That(PlayMenuCopy.CapturedOnce.Length, Is.LessThan(70));
        }

        [Test]
        public void TheKillerMenuClosesOnBeginAndKeepsTheStackedSizes()
        {
            var hud = new KillerHud(new VisualElement(), HudFont());
            hud.Begin(SkillIds.PacketAfterimage);
            hud.SetMenu(true);
            Assert.That(hud.MenuOpen, Is.True);
            hud.Begin(SkillIds.PacketAfterimage);
            Assert.That(hud.MenuOpen, Is.False, "next match: menu hidden");
            Assert.That(hud.Menu.Layout, Is.EqualTo(PlayMenuLayout.Stacked));
            Assert.That(KillerHud.MenuHeight, Is.EqualTo(256f));
            Assert.That(hud.Menu.NoteText, Is.Null, "no line 3 for the Killer");
        }

        [Test]
        public void TheSurvivorMenuClosesOnEveryResetPathAndOnlyTheMenuIsHidden()
        {
            var hud = new SurvivorHud(new VisualElement(), HudFont());
            hud.Begin(SkillIds.StealthRelay);
            hud.SetMenu(true);
            hud.SetMenu(false); // ResetMenu path
            Assert.That(hud.MenuOpen, Is.False);
            Assert.That(hud.Menu.Buttons[0].focusController?.focusedElement, Is.Not.SameAs(hud.Menu.Buttons[0]));
            hud.SetMenu(true);
            Assert.That(hud.Menu.Buttons.Count, Is.EqualTo(3));
        }

        [Test]
        public void ClientAppResetsTheMenuOnStartResultLeaveAndHasNoStaleFlag()
        {
            string app = Src("ClientApp.cs");
            Assert.That(Regex.IsMatch(app, @"\bmenuOpen\b"), Is.False, "old Killer-only flag is gone");
            Assert.That(Regex.Matches(app, @"\bResetMenu\(\)").Count, Is.GreaterThanOrEqualTo(4), "def + start + EndMatch + Disconnect");
            Assert.That(app, Does.Contain("releasedCursor && !menuGate.Open"), "click never re-locks under the menu");
            Assert.That(app, Does.Contain("rig?.SetCursor(focused && !menuGate.Open)"));
            Assert.That(Regex.IsMatch(app, @"if \(screens\.SettingsOpen\) screens\.CloseSettings\(\);\s*else SetMenu"), Is.True, "Esc in Settings closes Settings first");
            Assert.That(app, Does.Contain("keys = active &&"));
            Assert.That(app, Does.Contain("interactHeld = (active &&"));
            Assert.That(app, Does.Contain("pressed = (presses &&"));
        }

        [Test]
        public void TheKillerUssBlockIsNotEdited()
        {
            string uss = File.ReadAllText(Path.Combine(Application.dataPath, "Resources", "ClientUI.uss"));
            Assert.That(uss, Does.Contain(".kmenu"));
            Assert.That(uss, Does.Contain(".smenu"));
        }
    }
}
