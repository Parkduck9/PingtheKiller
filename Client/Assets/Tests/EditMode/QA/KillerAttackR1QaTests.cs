using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Audio;
using PingTheKiller.Client.Killer;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Client.QA.Tests
{
    // R1 (11/17): the Killer's attack (left click / RT -> C2S_Attack), its prompt, its hit/miss readout, the lobby skill
    // picker copy and selection, and the Killer skill copy that the picker, the play menu and the HUD share.
    public sealed class KillerAttackR1QaTests
    {
        private const string Dot = "·";

        // ---- input ------------------------------------------------------------------------------------------------------

        [Test]
        public void AClickSendsAnAttackForTheKillerOnlyWithARisingSeq()
        {
            var input = new AttackInput();
            Assert.That(input.TryCreate(false, Role.Killer, out C2S_Attack none), Is.False, "no press, no packet");
            Assert.That(none, Is.Null);
            Assert.That(input.TryCreate(true, Role.Survivor, out C2S_Attack survivor), Is.False, "Survivors never send it");
            Assert.That(survivor, Is.Null);
            Assert.That(input.TryCreate(true, Role.Killer, out C2S_Attack first), Is.True);
            Assert.That(input.TryCreate(true, Role.Killer, out C2S_Attack second), Is.True);
            Assert.That(first.Seq, Is.EqualTo(1u));
            Assert.That(second.Seq, Is.EqualTo(2u), "the server drops a Seq it already saw, so every press counts up");
            input.Reset();
            Assert.That(input.TryCreate(true, Role.Killer, out C2S_Attack again), Is.True);
            Assert.That(again.Seq, Is.EqualTo(1u), "a new link starts over");
        }

        [Test]
        public void TheAttackIsBoundToTheLeftMouseButtonAndTheRightTriggerBehindTheMenuGate()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string frame = QaSource.MethodText(code, "void AttackFrame(");
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame, Does.Contain("presses && "), "the play menu, focus and cursor gate it like Q");
            Assert.That(frame, Does.Contain("Mouse.current.leftButton.wasPressedThisFrame"));
            Assert.That(frame, Does.Contain("gamepad.rightTrigger.wasPressedThisFrame"));
            Assert.That(frame, Does.Contain("attackInput.TryCreate("));
            Assert.That(code, Does.Match(@"if \(!survivor\) AttackFrame\("), "only the Killer client reads the attack");
        }

        // ---- prompt -----------------------------------------------------------------------------------------------------

        [Test]
        public void TheAttackPromptShowsWithinThreeMetresOfAVisibleBlip()
        {
            Assert.That(KillerHudCopy.Attack(2.99f, false), Is.EqualTo("Click  Attack"));
            Assert.That(KillerHudCopy.Attack(3f, false), Is.EqualTo("Click  Attack"), "3 m inclusive");
            Assert.That(KillerHudCopy.Attack(3.01f, false), Is.Null);
            Assert.That(KillerHudCopy.Attack(float.PositiveInfinity, false), Is.Null, "no blip, no prompt");
            Assert.That(KillerHudCopy.Attack(1f, true), Is.EqualTo("RT  Attack"), "gamepad-only players");
            // A kick or hatch prompt in reach keeps the slot (KillerWorldLayer only fills an empty interact prompt).
            string code = QaSource.StripComments(QaPaths.ReadScript("Killer/KillerWorldLayer.cs"));
            Assert.That(code, Does.Contain("if (string.IsNullOrEmpty(interact.Text)) interact.Text = KillerHudCopy.Attack("));
        }

        // ---- hit / miss ---------------------------------------------------------------------------------------------------

        [Test]
        public void TheSwingWatchReadsTheSwingingFlagThenAHitOrAMiss()
        {
            var watch = new KillerSwingWatch();
            Assert.That(watch.Observe(false, 100, 0), Is.EqualTo(SwingEvent.None));
            Assert.That(watch.Observe(true, 101, .05), Is.EqualTo(SwingEvent.Started), "the flag rising is the wind-up");
            Assert.That(watch.Observe(true, 102, .1), Is.EqualTo(SwingEvent.None), "held: no second start");
            Assert.That(watch.OnCapture(.25), Is.EqualTo(SwingEvent.Hit));
            Assert.That(watch.Tick(5), Is.EqualTo(SwingEvent.None), "a hit is never also a miss");
            Assert.That(watch.Observe(false, 117, 1), Is.EqualTo(SwingEvent.None), "the fall after a shown hit says nothing");
            Assert.That(watch.Observe(true, 140, 2), Is.EqualTo(SwingEvent.Started));
            Assert.That(watch.Tick(2 + KillerSwingWatch.ResultSeconds - .01), Is.EqualTo(SwingEvent.None), "still inside the window");
            Assert.That(watch.Tick(2 + KillerSwingWatch.ResultSeconds), Is.EqualTo(SwingEvent.Missed));
            Assert.That(watch.Tick(3), Is.EqualTo(SwingEvent.None), "once");
            Assert.That(watch.OnCapture(3.1), Is.EqualTo(SwingEvent.None), "a later capture is not this swing's hit");
            Assert.That(watch.Swings, Is.EqualTo(2)); Assert.That(watch.Hits, Is.EqualTo(1)); Assert.That(watch.Misses, Is.EqualTo(1));
            Assert.That(KillerSwingWatch.ResultSeconds, Is.GreaterThan(GameConstants.SwingWindUpSeconds + .2), "two snapshots of slack after the hit check");
            Assert.That(watch.Observe(false, 160, 3.2), Is.EqualTo(SwingEvent.None), "a miss-length fall after Missed: nothing more");
        }

        // a2 (F3): an eliminating hit removes the entry, so no CaptureConfirm comes. A Normal blip vanishing near the Killer
        // holds the verdict; the own Swinging flag's length in server ticks (hit 16, miss 20, interrupt <= 4) decides.
        [Test]
        public void AnEliminatingHitIsAHitFromTheFlagLengthNotAMiss()
        {
            Assert.That(KillerSwingWatch.WindUpTicks, Is.EqualTo(4));
            Assert.That(KillerSwingWatch.HitTicks, Is.EqualTo(16));
            Assert.That(KillerSwingWatch.MissTicks, Is.EqualTo(20));
            Assert.That(KillerSwingWatch.HitLength(16) && KillerSwingWatch.HitLength(17), Is.True);
            Assert.That(KillerSwingWatch.HitLength(20) || KillerSwingWatch.HitLength(19) || KillerSwingWatch.HitLength(4) || KillerSwingWatch.HitLength(2), Is.False);

            var watch = new KillerSwingWatch();
            Assert.That(watch.Observe(true, 200, 10), Is.EqualTo(SwingEvent.Started));
            watch.OnVanish(10.25); // the hit tick: the eliminated Survivor's entry is gone
            Assert.That(watch.Held, Is.True);
            Assert.That(watch.Tick(10 + KillerSwingWatch.ResultSeconds + .1), Is.EqualTo(SwingEvent.None), "no Missed while held");
            Assert.That(watch.Observe(false, 216, 10.85), Is.EqualTo(SwingEvent.Hit), "hit-length fall: the hit");
            Assert.That(watch.Hits, Is.EqualTo(1)); Assert.That(watch.Misses, Is.Zero);

            // A Survivor that only stopped moving (Silent) also vanishes; the miss-length fall says Missed.
            Assert.That(watch.Observe(true, 300, 20), Is.EqualTo(SwingEvent.Started));
            watch.OnVanish(20.1);
            Assert.That(watch.Tick(20.6), Is.EqualTo(SwingEvent.None));
            Assert.That(watch.Observe(false, 320, 21.05), Is.EqualTo(SwingEvent.Missed));
            // A stun interrupt (short flag) is not a hit either.
            Assert.That(watch.Observe(true, 400, 30), Is.EqualTo(SwingEvent.Started));
            watch.OnVanish(30.1);
            Assert.That(watch.Observe(false, 403, 30.2), Is.EqualTo(SwingEvent.Missed));
            // A held verdict whose flag never falls ends as Missed after HoldSeconds.
            Assert.That(watch.Observe(true, 500, 40), Is.EqualTo(SwingEvent.Started));
            watch.OnVanish(40.2);
            Assert.That(watch.Tick(40 + KillerSwingWatch.HoldSeconds), Is.EqualTo(SwingEvent.Missed));
            // Without a vanish, Missed comes at ResultSeconds; a hit-length fall then corrects it (an unseen Survivor).
            var late = new KillerSwingWatch();
            late.Observe(true, 600, 50);
            Assert.That(late.Tick(50 + KillerSwingWatch.ResultSeconds), Is.EqualTo(SwingEvent.Missed));
            Assert.That(late.Observe(false, 616, 50.85), Is.EqualTo(SwingEvent.LateHit));
            Assert.That(late.Hits, Is.EqualTo(1)); Assert.That(late.Misses, Is.Zero);
            // A vanish outside a pending swing holds nothing.
            late.OnVanish(60);
            Assert.That(late.Held, Is.False);
        }

        [Test]
        public void ANormalBlipVanishingWithinSwingRangeIsReportedAndNothingElse()
        {
            var tracker = new BlipTracker(new System.Random(3), new List<(float x, float z)>());
            var near = new BlipInput(2.2f, 0, BlipStatus.Normal);
            var far = new BlipInput(8f, 0, BlipStatus.Normal);
            var lagging = new BlipInput(0, 1.5f, BlipStatus.Lagging);
            tracker.Accept(10, new[] { near, far, lagging }, 0, 0);
            Assert.That(tracker.NormalVanishedNear, Is.False);
            tracker.Accept(11, new[] { far, lagging }, 0, 0);
            Assert.That(tracker.NormalVanishedNear, Is.True, "the Normal blip 2.2 m away vanished");
            Assert.That((tracker.VanishedX, tracker.VanishedZ), Is.EqualTo((2.2f, 0f)));
            tracker.Accept(12, new[] { far, lagging }, 0, 0);
            Assert.That(tracker.NormalVanishedNear, Is.False, "one snapshot only");
            tracker.Accept(13, new[] { lagging }, 0, 0);
            Assert.That(tracker.NormalVanishedNear, Is.False, "8 m away is out of swing range");
            tracker.Accept(14, new BlipInput[0], 0, 0);
            Assert.That(tracker.NormalVanishedNear, Is.False, "a Lagging blip is not a swing target");
        }

        // a2 (F2): the press plays the swing at once (once per swing); the result follows from the server.
        [Test]
        public void APressPredictsTheSwingFeedbackOncePerSwing()
        {
            var watch = new KillerSwingWatch();
            Assert.That(watch.Predict(1), Is.True, "the first press: sweep and whoosh at once");
            Assert.That(watch.Predict(1.1), Is.False, "a second click before the server's flag: nothing");
            watch.Observe(true, 10, 1.15);
            Assert.That(watch.Predict(1.6), Is.False, "the server is swinging (wind-up or recovery)");
            watch.Observe(false, 26, 2);
            Assert.That(watch.Predict(2.05), Is.True);
            string code = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            string frame = QaSource.MethodText(code, "void AttackFrame(");
            Assert.That(frame, Does.Contain("if (swingWatch.Predict(now))"));
            Assert.That(frame, Does.Contain("v2Audio.Play(V2Cue.SwingWhoosh);"));
            Assert.That(frame, Does.Contain("killerLayer.Kick?.PlaySwing("));
            Assert.That(frame.IndexOf("link.Send(attack);"), Is.LessThan(frame.IndexOf("swingWatch.Predict(now)")), "after the press is sent");
            Assert.That(frame, Does.Not.Contain("Play()"), "never the hit kick before the server's result");
            string missed = QaSource.MethodText(code, "void OnSwingMissed(");
            Assert.That(missed, Does.Contain("Fx.OnMissed(now)"));
            Assert.That(missed, Does.Not.Contain("PlaySwing").And.Not.Contain("SwingWhoosh"));
        }

        // a2 (F1): a sweep before any capture kick left the shake evaluated at an infinite time (NaN camera, black world).
        [Test]
        public void ASweepWithoutAPriorKickLeavesTheCameraFinite()
        {
            bool saved = KillerAttackKick.ReducedMotion;
            var host = new UnityEngine.GameObject("kick-sweep");
            var eye = new UnityEngine.GameObject("eye");
            try
            {
                KillerAttackKick.ReducedMotion = false;
                var camera = eye.AddComponent<UnityEngine.Camera>();
                var start = new UnityEngine.Vector3(3, 1.7f, -2);
                eye.transform.SetPositionAndRotation(start, UnityEngine.Quaternion.Euler(0, 30, 0));
                float fov = camera.fieldOfView;
                var kick = host.AddComponent<KillerAttackKick>();
                Assert.That(kick.PlaySwing(5), Is.True);
                for (double t = 5; t < 5.4; t += .02)
                {
                    kick.Apply(camera, t);
                    var p = eye.transform.position;
                    var q = eye.transform.rotation;
                    Assert.That(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsNaN(q.x) || float.IsNaN(q.w) || float.IsNaN(camera.fieldOfView), Is.False, "t=" + t);
                    Assert.That(UnityEngine.Vector3.Distance(p, start), Is.LessThan(1e-4f), "a sweep turns, never moves the eye");
                }
                Assert.That(camera.fieldOfView, Is.EqualTo(fov));
                for (double t = -1; t < 2; t += .25)
                {
                    KillerAttackKick.Shake(double.PositiveInfinity, false, out float r, out float u, out float roll);
                    Assert.That((r, u, roll), Is.EqualTo((0f, 0f, 0f)));
                    KillerAttackKick.Offset(double.NegativeInfinity, false, out float f, out float push);
                    Assert.That((f, push), Is.EqualTo((0f, 0f)));
                }
                KillerAttackKick.Shake(double.NaN, false, out float nr, out _, out _);
                Assert.That(nr, Is.EqualTo(0f));
            }
            finally { KillerAttackKick.ReducedMotion = saved; UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(eye); }
        }

        [Test]
        public void TheSwingSweepIsSmallAndAMissSaysMissedBriefly()
        {
            Assert.That(SkillFeedback.Missed, Is.EqualTo("Missed"));
            var fx = new KillerFxState();
            fx.OnMissed(10);
            Assert.That(fx.Line(10.5).Text, Is.EqualTo("Missed"));
            Assert.That(fx.Line(10.5).Tone, Is.EqualTo(LineTone.Plain), "the text tone, not the muted reason");
            Assert.That(fx.Line(10 + SkillFeedback.MissedSeconds).Text, Is.Null, "brief");
            Assert.That(SkillFeedback.MissedSeconds, Is.InRange(.6f, 1.2f));
            Assert.That(KillerAttackKick.Sweep(-.01, false), Is.EqualTo(0));
            Assert.That(KillerAttackKick.Sweep(KillerAttackKick.SweepSeconds, false), Is.EqualTo(0));
            float peak = 0;
            for (int i = 1; i < 30; i++) peak = System.Math.Max(peak, System.Math.Abs(KillerAttackKick.Sweep(i * KillerAttackKick.SweepSeconds / 30, false)));
            Assert.That(peak, Is.GreaterThan(1f).And.LessThanOrEqualTo(KillerAttackKick.SweepDegrees), "a small sweep, at most 4 degrees");
            Assert.That(KillerAttackKick.SweepDegrees, Is.LessThanOrEqualTo(4f));
            Assert.That(KillerAttackKick.Sweep(.1, true), Is.EqualTo(0), "reduced motion: none");
            // The whoosh is a procedural cue like the other v2 cues (V2SoundTests checks every cue's level and edges).
            float[] whoosh = V2CueSynth.Build(V2Cue.SwingWhoosh, KillerAudio.Rate);
            Assert.That(whoosh.Length, Is.EqualTo(V2CueSynth.Length(V2Cue.SwingWhoosh, KillerAudio.Rate)));
        }

        // ---- skill copy -----------------------------------------------------------------------------------------------

        [Test]
        public void TheKillerSkillCopyHasOneSourceWithTheR1Numbers()
        {
            Assert.That(PlayMenuCopy.KillerEffect(SkillIds.PingScanner), Is.EqualTo("Shows the nearest Survivor for 3 s."));
            Assert.That(PlayMenuCopy.KillerEffect(SkillIds.Overclock), Is.EqualTo("1.5× speed for 3 s, then 0.3 s slowed."));
            Assert.That(PlayMenuCopy.KillerEffect(SkillIds.PacketTrace), Is.EqualTo("Survivors within 12 m of you turn Signal for 2.5 s."));
            Assert.That(PlayMenuCopy.KillerEffect(SkillIds.FirewallBlock), Does.Contain("5 s").And.Contain("terminal for 8 s"));
            for (byte id = 0; id < SkillIds.KillerSkillCount; id++)
            {
                Assert.That(KillerHudCopy.SkillEffect(id), Is.EqualTo(PlayMenuCopy.KillerEffect(id)), "the HUD intro reads the same source");
                Assert.That(PlayMenuCopy.Effect(Role.Killer, id), Is.EqualTo(PlayMenuCopy.KillerEffect(id)));
                Assert.That(PlayMenuCopy.KillerSkillLine(id), Is.Not.Empty);
            }
            for (byte id = 0; id < SkillIds.SurvivorSkillCount; id++)
                Assert.That(PlayMenuCopy.Effect(Role.Survivor, id), Is.EqualTo(PlayMenuCopy.SurvivorEffect(id)));
            Assert.That(KillerHudCopy.TerminalTag(2, true), Is.EqualTo("Terminal 3 " + Dot + " Locked"), "the Killer's locked terminal marker");
            Assert.That(KillerHudCopy.TerminalTag(2, false), Is.EqualTo("Terminal 3"));
        }

        // ---- lobby picker -------------------------------------------------------------------------------------------------

        [Test]
        public void ThePickerListsTheOwnRoleOrBothWhileTheRoleIsRandom()
        {
            const byte random = (byte)RoleChoice.Random, killer = (byte)RoleChoice.Killer, survivor = (byte)RoleChoice.Survivor;
            foreach (bool host in new[] { true, false })
            {
                Assert.That(LobbyCopy.ShowsRole(Role.Killer, random, host), Is.True);
                Assert.That(LobbyCopy.ShowsRole(Role.Survivor, random, host), Is.True);
            }
            Assert.That(LobbyCopy.ShowsRole(Role.Killer, killer, true), Is.True, "a Killer host");
            Assert.That(LobbyCopy.ShowsRole(Role.Survivor, killer, true), Is.False);
            Assert.That(LobbyCopy.ShowsRole(Role.Killer, survivor, true), Is.False, "a Survivor host");
            Assert.That(LobbyCopy.ShowsRole(Role.Survivor, survivor, true), Is.True);
            Assert.That(LobbyCopy.ShowsRole(Role.Killer, killer, false), Is.False, "with a Killer host everyone else survives");
            Assert.That(LobbyCopy.ShowsRole(Role.Survivor, killer, false), Is.True);
            Assert.That(LobbyCopy.ShowsRole(Role.Killer, survivor, false), Is.True, "with a Survivor host a guest may be the Killer");
        }

        [Test]
        public void PickerEffectLinesAreBrokenEvenlyWithNoLoneWord()
        {
            // a2 (F5): the shared effect copy, broken by hand at 18 u in the 308 u column (the label never wraps itself).
            foreach (Role role in new[] { Role.Killer, Role.Survivor })
            {
                int count = role == Role.Killer ? SkillIds.KillerSkillCount : SkillIds.SurvivorSkillCount;
                for (byte id = 0; id < count; id++)
                {
                    string text = LobbyCopy.PickerEffect(role, id);
                    Assert.That(text.Replace("\n", " "), Is.EqualTo(PlayMenuCopy.Effect(role, id)), "the one copy source, unchanged");
                    string[] lines = text.Split('\n');
                    Assert.That(lines.Length, Is.InRange(1, 3), text);
                    foreach (string line in lines)
                    {
                        Assert.That(line.Length, Is.LessThanOrEqualTo(LobbyCopy.PickerLineChars), text);
                        if (lines.Length > 1) Assert.That(line.Split(' ').Length, Is.GreaterThanOrEqualTo(3), "no orphan: " + text);
                    }
                }
            }
            Assert.That(LobbyCopy.Balanced("Survivors within 12 m of you turn Signal for 2.5 s.", 30),
                Is.EqualTo("Survivors within 12 m of\nyou turn Signal for 2.5 s."));
        }

        [Test]
        public void ThePickerCopyNamesTheChoiceTheGroupCooldownAndTheOwnRow()
        {
            Assert.That(LobbyCopy.GroupTitle(Role.Killer), Is.EqualTo("Killer skills " + Dot + " 12 s cooldown"));
            Assert.That(LobbyCopy.GroupTitle(Role.Survivor), Is.EqualTo("Survivor skills " + Dot + " 15 s cooldown"));
            Assert.That(LobbyCopy.Chosen(SkillIds.NoChoice), Does.StartWith("Chosen: random"));
            Assert.That(LobbyCopy.Chosen(SkillIds.Choice(Role.Killer, SkillIds.PacketTrace)), Is.EqualTo("Chosen: Packet trace (Killer)"));
            Assert.That(LobbyCopy.Chosen(SkillIds.Choice(Role.Survivor, SkillIds.WhiteNoise)), Is.EqualTo("Chosen: White noise (Survivor)"));
            var own = new Slot { PlayerId = 2, Nickname = "Ana", SkillChoice = SkillIds.Choice(Role.Killer, SkillIds.Overclock) };
            Assert.That(LobbyCopy.SlotText(own, 2), Is.EqualTo("Ana " + Dot + " you " + Dot + " Overclock"));
            Assert.That(LobbyCopy.SlotText(own, 3), Is.EqualTo("Ana"), "another player's choice is never shown");
            own.SkillChoice = SkillIds.NoChoice;
            Assert.That(LobbyCopy.SlotText(own, 2), Is.EqualTo("Ana " + Dot + " you"));
            var state = new S2C_LobbyState { Slots = new[] { new Slot { PlayerId = 1, Nickname = "Bot 1", IsBot = 1, SkillChoice = SkillIds.NoChoice },
                new Slot { PlayerId = 2, Nickname = "Ana", SkillChoice = SkillIds.Choice(Role.Survivor, 1) } } };
            Assert.That(LobbyCopy.ContiguousBotNames(state).Slots[1].SkillChoice, Is.EqualTo(SkillIds.Choice(Role.Survivor, 1)), "the copy keeps the choice");
        }

        [Test]
        public void EveryPickerButtonSendsItsRoleQualifiedChoice()
        {
            string code = QaSource.StripComments(QaPaths.ReadScript("UI/ClientScreens.cs"));
            string build = QaSource.MethodText(code, "void BuildSkillPicker(");
            Assert.That(build, Does.Contain("byte choice = SkillIds.Choice(role, id);"));
            Assert.That(build, Does.Contain("ChooseSkill?.Invoke(choice)"));
            Assert.That(build, Does.Contain("ChooseSkill?.Invoke(SkillIds.NoChoice)"), "Random clears the choice");
            Assert.That(build, Does.Contain("LobbyCopy.PickerEffect(role, id)"), "the effect line beside each name");
            string app = QaSource.StripComments(QaPaths.ReadScript("ClientApp.cs"));
            Assert.That(app, Does.Contain("screens.ChooseSkill += choice => link?.Send(new C2S_ChooseSkill { SkillId = choice });"));
        }
    }
}
