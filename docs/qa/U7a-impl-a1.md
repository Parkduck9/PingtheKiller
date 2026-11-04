# U7a implementation report, attempt 1

quota fallback: Codex out of credits. Implementer: Claude Sonnet. Not committed.

**Changed**
- New: `Settings/GameSettings.cs` (load, clamp, save, look delta), `UI/SettingsScreen.cs`, `Tests/EditMode/SettingsTests.cs` (16 tests).
- Edited: `ClientScreens.cs`, `ClientApp.cs`, `KillerHud.cs`, `RoleCamera.cs`, `SurvivorOrbitRig.cs`, `KillerViewRig.cs`, `LaggingStutter.cs`, `DevArgs.cs`, `DevCapture.cs` (`-ptk-settings`), `ClientUI.uss`, `HUD_SPEC.md` (new section 12, section 6.6).
- Existing assertions changed (both in `KillerHudQaTests.cs`, because the Killer play menu gains a Settings button): menu buttons are `Resume, Settings, Leave match`; menu slot 208 u becomes 256 u.

**Verified**
- EditMode: 1236 total, 1236 passed, 0 failed, 0 `error CS`.
- `BuildScript.BuildWindows` succeeded.
- Screenshots in `docs/qa/U7a-screens/{720,1080}`: menu, settings-menu, play-menu, settings-play. I opened them. SLOP section 9: no gate failure, flat palette, 4 px spacing, left-aligned panel, plain copy, focus and hover states.
- Arrow keys, Enter and mouse value changes are tested on a real runtime panel.

**Spec points not met or changed**
- FOV ranges 60-90 / 60-100 exclude today's vertical 50 and 58.7. I read them as horizontal angles at 16:9 (defaults 79.2 and 90), so defaults are unchanged.
- The Survivor play menu (HUD_SPEC section 5) is not built, so a Survivor cannot open Settings in a match. Only the Killer play menu and the main menu have the button.
- Tab order is not automated (UI Toolkit's own focus ring); not checked by hand.
- An external SendKeys attempt typed into other desktop windows; I stopped it and deleted its screenshots.

**Risks**
- `KillerHud.cs` edit may conflict with U6c-ui.
- Unity re-serialised unrelated assets (Art, ProjectSettings, URP); do not commit those.
