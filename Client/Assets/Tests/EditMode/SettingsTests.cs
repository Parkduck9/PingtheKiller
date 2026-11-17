using System.Collections.Generic;
using NUnit.Framework;
using PingTheKiller.Client.Dev;
using PingTheKiller.Client.Settings;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.Tests
{
    // U7a: player options. FOV is the horizontal angle at 16:9 (Hor+), stored under ptk.settings.*; out-of-range stored
    // values fall back to the default; sensitivity and Invert Y scale the mouse delta; the rigs take the FOV.
    public sealed class SettingsTests
    {
        private sealed class MemoryStore : ISettingsStore
        {
            public readonly Dictionary<string, float> Values = new Dictionary<string, float>();
            public int Flushes;
            public bool TryGet(string key, out float value) => Values.TryGetValue(key, out value);
            public void Set(string key, float value) => Values[key] = value;
            public void Flush() => Flushes++;
        }

        [Test]
        public void TheDefaultsAreTheCurrentRigsAndFullVolume()
        {
            var settings = new GameSettings();
            Assert.That(settings.SurvivorVerticalFov, Is.EqualTo(SurvivorOrbitRig.VerticalFov).Within(.01f), "the Survivor camera is unchanged");
            Assert.That(settings.KillerVerticalFov, Is.EqualTo(KillerViewRig.VerticalFov).Within(.01f), "the Killer camera is unchanged");
            Assert.That(settings.SurvivorFov, Is.InRange(GameSettings.SurvivorFovMin, GameSettings.SurvivorFovMax));
            Assert.That(settings.KillerFov, Is.EqualTo(90f));
            Assert.That(settings.Sensitivity, Is.EqualTo(1f));
            Assert.That(settings.InvertY, Is.False);
            Assert.That(settings.ReducedMotion, Is.False);
            Assert.That(settings.Volume, Is.EqualTo(100));
            Assert.That(settings.Volume01, Is.EqualTo(1f));
        }

        [Test]
        public void TheSettersClampToTheirRangesAndIgnoreNonNumbers()
        {
            var settings = new GameSettings { SurvivorFov = 10, KillerFov = 400, Sensitivity = 99, Volume = 900 };
            Assert.That(settings.SurvivorFov, Is.EqualTo(60f));
            Assert.That(settings.KillerFov, Is.EqualTo(100f));
            Assert.That(settings.Sensitivity, Is.EqualTo(3f));
            Assert.That(settings.Volume, Is.EqualTo(100));
            settings.SurvivorFov = 500; settings.KillerFov = 1; settings.Sensitivity = 0; settings.Volume = -5;
            Assert.That(settings.SurvivorFov, Is.EqualTo(90f));
            Assert.That(settings.KillerFov, Is.EqualTo(60f));
            Assert.That(settings.Sensitivity, Is.EqualTo(.25f));
            Assert.That(settings.Volume, Is.EqualTo(0));
            settings.SurvivorFov = float.NaN; settings.KillerFov = float.PositiveInfinity; settings.Sensitivity = float.NaN;
            Assert.That(settings.SurvivorFov, Is.EqualTo(GameSettings.SurvivorFovDefault));
            Assert.That(settings.KillerFov, Is.EqualTo(GameSettings.KillerFovDefault));
            Assert.That(settings.Sensitivity, Is.EqualTo(GameSettings.SensitivityDefault));
        }

        [Test]
        public void AStoredValueOutOfRangeOrNotANumberLoadsAsThatSettingsDefaultOnly()
        {
            var store = new MemoryStore();
            store.Values[GameSettings.SurvivorFovKey] = 200;
            store.Values[GameSettings.KillerFovKey] = 75;
            store.Values[GameSettings.SensitivityKey] = float.NaN;
            store.Values[GameSettings.VolumeKey] = -1;
            store.Values[GameSettings.InvertYKey] = 1;
            store.Values[GameSettings.ReducedMotionKey] = 7;
            GameSettings settings = GameSettings.Load(store);
            Assert.That(settings.SurvivorFov, Is.EqualTo(GameSettings.SurvivorFovDefault), "200 is above 90");
            Assert.That(settings.KillerFov, Is.EqualTo(75f), "a valid value next to a bad one is kept");
            Assert.That(settings.Sensitivity, Is.EqualTo(1f), "NaN");
            Assert.That(settings.Volume, Is.EqualTo(100), "-1");
            Assert.That(settings.InvertY, Is.True);
            Assert.That(settings.ReducedMotion, Is.False, "7 is not a flag");
            Assert.That(GameSettings.Load(null).Volume, Is.EqualTo(100));
            Assert.That(GameSettings.Load(new MemoryStore()).KillerFov, Is.EqualTo(90f), "nothing stored: defaults");
        }

        [Test]
        public void SavingThenLoadingGivesTheSameValuesUnderThePtkSettingsPrefix()
        {
            var store = new MemoryStore();
            var settings = new GameSettings(store)
            {
                SurvivorFov = 85, KillerFov = 70, Sensitivity = 1.75f, InvertY = true, ReducedMotion = true, Volume = 35
            };
            settings.Save();
            Assert.That(store.Flushes, Is.EqualTo(1));
            Assert.That(store.Values.Count, Is.EqualTo(6));
            foreach (string key in store.Values.Keys) Assert.That(key, Does.StartWith("ptk.settings."));
            GameSettings loaded = GameSettings.Load(store);
            Assert.That(loaded.SurvivorFov, Is.EqualTo(85f));
            Assert.That(loaded.KillerFov, Is.EqualTo(70f));
            Assert.That(loaded.Sensitivity, Is.EqualTo(1.75f));
            Assert.That(loaded.InvertY, Is.True);
            Assert.That(loaded.ReducedMotion, Is.True);
            Assert.That(loaded.Volume, Is.EqualTo(35));
        }

        [Test]
        public void ThePlayerPrefsStoreReportsAMissingKeyAsMissing()
        {
            Assert.That(new PlayerPrefsStore().TryGet("ptk.settings.test-never-written", out _), Is.False);
        }

        [Test]
        public void ChangedFiresOnlyForARealChangeAndResetRestoresTheDefaults()
        {
            var settings = new GameSettings();
            int changes = 0;
            settings.Changed += () => changes++;
            settings.Volume = 100; settings.InvertY = false; settings.Sensitivity = 1;
            Assert.That(changes, Is.EqualTo(0), "same values");
            settings.Volume = 50; settings.InvertY = true; settings.KillerFov = 80;
            Assert.That(changes, Is.EqualTo(3));
            settings.ResetToDefaults();
            Assert.That(changes, Is.EqualTo(4));
            Assert.That(settings.Volume, Is.EqualTo(100));
            Assert.That(settings.InvertY, Is.False);
            Assert.That(settings.KillerFov, Is.EqualTo(90f));
        }

        [Test]
        public void SensitivityScalesBothAxesAndInvertYFlipsOnlyThePitch()
        {
            var settings = new GameSettings();
            Vector2 plain = settings.LookDelta(new Vector2(10, 10), RoleCamera.Sensitivity);
            Assert.That(plain.x, Is.EqualTo(1.2f).Within(1e-4f));
            Assert.That(plain.y, Is.EqualTo(1.2f).Within(1e-4f));
            settings.Sensitivity = 2;
            Vector2 twice = settings.LookDelta(new Vector2(10, 10), RoleCamera.Sensitivity);
            Assert.That(twice.x, Is.EqualTo(plain.x * 2).Within(1e-4f));
            Assert.That(twice.y, Is.EqualTo(plain.y * 2).Within(1e-4f));
            settings.InvertY = true;
            Vector2 inverted = settings.LookDelta(new Vector2(10, 10), RoleCamera.Sensitivity);
            Assert.That(inverted.x, Is.EqualTo(twice.x));
            Assert.That(inverted.y, Is.EqualTo(-twice.y));
        }

        private static GameObject NewCamera(out RoleCamera rig, out Camera camera)
        {
            var go = new GameObject("settings-test-camera", typeof(Camera));
            camera = go.GetComponent<Camera>();
            rig = new RoleCamera(camera);
            return go;
        }

        private static void Release(GameObject go)
        {
            Object.DestroyImmediate(go);
            UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true;
        }

        [Test]
        public void TheCameraAppliesSensitivityToTheYaw()
        {
            GameObject go = NewCamera(out RoleCamera rig, out _);
            try
            {
                rig.Begin(Role.Survivor);
                rig.Options = new GameSettings { Sensitivity = 2 };
                rig.Look(new Vector2(10, 0));
                Assert.That(rig.Yaw, Is.EqualTo(2.4f).Within(1e-3f), "10 counts x 0.12 x 2");
                rig.Options.InvertY = true;
                rig.Look(new Vector2(10, 0));
                Assert.That(rig.Yaw, Is.EqualTo(4.8f).Within(1e-3f), "Invert Y never touches the yaw");
            }
            finally { Release(go); }
        }

        [Test]
        public void TheKillerPitchFollowsSensitivityAndInvertY()
        {
            GameObject go = NewCamera(out RoleCamera rig, out _);
            try
            {
                rig.Options = new GameSettings { Sensitivity = 2 };
                rig.Begin(Role.Killer);
                rig.Look(new Vector2(0, 10));
                rig.Place(new Vector3(40, 0, 40), .02f);
                Assert.That(rig.Pose.Pitch, Is.EqualTo(-2.4f).Within(1e-3f), "mouse up looks up (negative pitch) at 2x");
                rig.Options.InvertY = true;
                rig.Begin(Role.Killer);
                rig.Look(new Vector2(0, 10));
                rig.Place(new Vector3(40, 0, 40), .02f);
                Assert.That(rig.Pose.Pitch, Is.EqualTo(2.4f).Within(1e-3f), "inverted: mouse up looks down");
            }
            finally { Release(go); }
        }

        [Test]
        public void TheSurvivorOrbitPitchFollowsTheMouseAndInvertY()
        {
            GameObject go = NewCamera(out RoleCamera rig, out _);
            try
            {
                rig.Begin(Role.Survivor);
                rig.Look(new Vector2(0, 10));
                rig.Place(new Vector3(40, 0, 40), .02f);
                Assert.That(rig.Pose.Pitch, Is.EqualTo(SurvivorOrbitRig.DefaultPitch - 1.2f).Within(1e-3f));
                rig.Options.InvertY = true;
                rig.Begin(Role.Survivor);
                rig.Look(new Vector2(0, 10));
                rig.Place(new Vector3(40, 0, 40), .02f);
                Assert.That(rig.Pose.Pitch, Is.EqualTo(SurvivorOrbitRig.DefaultPitch + 1.2f).Within(1e-3f));
            }
            finally { Release(go); }
        }

        [Test]
        public void EachRigTakesItsOwnFovFromTheSettingsAtOnce()
        {
            GameObject go = NewCamera(out RoleCamera rig, out Camera camera);
            try
            {
                rig.Options = new GameSettings { SurvivorFov = 60, KillerFov = 100 };
                rig.Begin(Role.Survivor);
                rig.Place(new Vector3(40, 0, 40), .02f);
                Assert.That(camera.fieldOfView, Is.EqualTo(GameSettings.VerticalFromHorizontal(60, 16f / 9)).Within(1e-3f));
                Assert.That(rig.Pose.VerticalFov, Is.EqualTo(camera.fieldOfView));
                rig.Begin(Role.Killer);
                rig.Place(new Vector3(40, 0, 40), .02f);
                Assert.That(camera.fieldOfView, Is.EqualTo(GameSettings.VerticalFromHorizontal(100, 16f / 9)).Within(1e-3f));
                rig.Options.KillerFov = 70;
                rig.Place(new Vector3(40, 0, 40), .02f);
                Assert.That(camera.fieldOfView, Is.EqualTo(GameSettings.VerticalFromHorizontal(70, 16f / 9)).Within(1e-3f), "a change applies on the next frame");
                Assert.That(KillerViewRig.VerticalFov, Is.EqualTo(58.72f).Within(.01f), "the spec constant itself is not touched");
                Assert.That(new KillerViewRig().Fov, Is.EqualTo(KillerViewRig.VerticalFov));
                Assert.That(new SurvivorOrbitRig().Fov, Is.EqualTo(SurvivorOrbitRig.VerticalFov));
            }
            finally { Release(go); }
        }

        [Test]
        public void ReducedMotionIsOnForTheSettingOrTheDevFlag()
        {
            var go = new GameObject("settings-test-stutter");
            var cameraObject = new GameObject("settings-test-view", typeof(Camera));
            try
            {
                var stutter = go.AddComponent<LaggingStutter>();
                Assert.That(stutter.Reduced, Is.False);
                stutter.SetReducedOption(true);
                Assert.That(stutter.Reduced, Is.True, "the player's setting");
                stutter.SetReducedOption(false);
                Assert.That(stutter.Reduced, Is.False, "turning it off again restores the capture burst");
                stutter.Initialize(cameraObject.GetComponent<Camera>(), new VisualElement(), new VisualElement(), true);
                Assert.That(stutter.Reduced, Is.True, "-ptk-reduce-stutter forces it on");
                stutter.SetReducedOption(false);
                Assert.That(stutter.Reduced, Is.True, "the flag still wins over the setting");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void TheScreenShowsTheStoredValuesAndSavesWhenItCloses()
        {
            var store = new MemoryStore();
            var settings = new GameSettings(store) { KillerFov = 75, Sensitivity = 1.5f, InvertY = true, Volume = 40 };
            var screen = new SettingsScreen(new VisualElement(), settings, () => false);
            int closed = 0;
            screen.Closed += () => closed++;
            Assert.That(screen.Visible, Is.False);
            screen.Show();
            Assert.That(screen.Visible, Is.True);
            Assert.That(LabelText(screen.Panel, "set-value"), Is.EqualTo(new[] { "79°", "75°", "1.50×", "40 %" }));
            Assert.That(ButtonTexts(screen.Panel, "set-toggle"), Is.EqualTo(new[] { "On", "Off" }));
            settings.ReducedMotion = true;
            Assert.That(ButtonTexts(screen.Panel, "set-toggle"), Is.EqualTo(new[] { "On", "On" }), "the screen follows the settings");
            settings.Volume = 90;
            Assert.That(LabelText(screen.Panel, "set-value")[3], Is.EqualTo("90 %"));
            screen.Close();
            screen.Close();
            Assert.That(closed, Is.EqualTo(1), "closing twice only closes once");
            Assert.That(screen.Visible, Is.False);
            Assert.That(store.Flushes, Is.EqualTo(1));
            Assert.That(GameSettings.Load(store).Volume, Is.EqualTo(90));
        }

        [Test]
        public void TheScreenHasEverySettingAndTheCopyIsPlain()
        {
            var screen = new SettingsScreen(new VisualElement(), new GameSettings(), () => false);
            Assert.That(LabelText(screen.Panel, "set-label"), Is.EqualTo(new[]
            {
                "Survivor field of view", "Killer field of view", "Mouse sensitivity", "Invert Y", "Reduced motion", "Master volume"
            }));
            var buttons = new List<string>();
            foreach (Button button in screen.Panel.Query<Button>().ToList()) buttons.Add(button.text);
            Assert.That(buttons, Does.Contain("Done"));
            Assert.That(buttons, Does.Contain("Reset to defaults"));
            Assert.That(SettingsCopy.Degrees(79.6f), Is.EqualTo("80°"));
            Assert.That(SettingsCopy.Multiplier(.25f), Is.EqualTo("0.25×"));
            Assert.That(SettingsCopy.Percent(0), Is.EqualTo("0 %"));
        }

        [Test]
        public void TheSettingsFlagIsADevCaptureFlag()
        {
            DevArgs args = DevArgs.Parse(new[] { "-ptk-settings" });
            Assert.That(args.Settings, Is.True);
            Assert.That(args.Active, Is.True);
            Assert.That(args.Errors, Is.Empty);
            Assert.That(DevArgs.Parse(new string[0]).Settings, Is.False);
        }

        // The screen on a real runtime panel, driven with the events the keyboard (arrows, Enter) and the mouse produce.
        [Test]
        public void KeyboardNavigationAndTheMouseDriveEveryControl()
        {
            var go = new GameObject("settings-test-document");
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            try
            {
                var document = go.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                VisualElement root = document.rootVisualElement;
                Assert.That(root, Is.Not.Null);
                var settings = new GameSettings(new MemoryStore());
                var screen = new SettingsScreen(root, settings, () => false);
                int closed = 0;
                screen.Closed += () => closed++;
                screen.Show();
                List<Slider> sliders = screen.Panel.Query<Slider>().ToList();
                Assert.That(sliders.Count, Is.EqualTo(4));
                Assert.That(root.panel.focusController.focusedElement, Is.SameAs(sliders[0]), "the first control has focus");

                // Arrows change the focused slider.
                float before = settings.KillerFov;
                sliders[1].Focus();
                for (int i = 0; i < 5; i++) Send(sliders[1], NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Right));
                Assert.That(settings.KillerFov, Is.GreaterThan(before), "Right raises the Killer FOV");
                Send(sliders[1], NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Left));
                Assert.That(settings.KillerFov, Is.LessThan(before + 5).And.GreaterThanOrEqualTo(before), "Left lowers it again");

                // Enter (Submit) presses the focused button. Tab itself is UI Toolkit's own focus ring: every control is a
                // plain focusable Slider or Button with no custom tab index.
                Button invert = screen.Panel.Query<Button>(className: "set-toggle").ToList()[0];
                invert.Focus();
                Send(invert, NavigationSubmitEvent.GetPooled());
                Assert.That(settings.InvertY, Is.True, "Enter flips the toggle");
                var done = screen.Panel.Query<Button>().ToList().Find(b => b.text == "Done");
                done.Focus();
                Send(done, NavigationSubmitEvent.GetPooled());
                Assert.That(closed, Is.EqualTo(1), "Enter on Done closes the screen");

                // Mouse: dragging or clicking a slider ends in a value change on the same path.
                screen.Show();
                sliders[2].value = 2.5f;
                Assert.That(settings.Sensitivity, Is.EqualTo(2.5f).Within(1e-4f));
                sliders[3].value = 42;
                Assert.That(settings.Volume, Is.EqualTo(40), "volume snaps to 5 %");
                Send(screen.Panel.Query<Button>().ToList().Find(b => b.text == "Reset to defaults"), NavigationSubmitEvent.GetPooled());
                Assert.That(settings.Volume, Is.EqualTo(100));
                Assert.That(sliders[3].value, Is.EqualTo(100f), "the sliders follow a reset");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(panelSettings); }
        }

        private static void Send(VisualElement target, EventBase evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }

        private static string[] LabelText(VisualElement root, string style)
        {
            var found = new List<string>();
            foreach (Label label in root.Query<Label>(className: style).ToList()) found.Add(label.text);
            return found.ToArray();
        }

        private static string[] ButtonTexts(VisualElement root, string style)
        {
            var found = new List<string>();
            foreach (Button button in root.Query<Button>(className: style).ToList()) found.Add(button.text);
            return found.ToArray();
        }
    }
}
