quota fallback: Codex out of credits

# U7b-skillfx implementation, attempt 1 (Claude Opus subagent)

**Files**
- New: `UI/SkillFeedback.cs` (pure rules, line, press watch, Killer/Survivor state), `World/SkillFx.cs` (pools), `World/SkillFxArt.cs`, `Editor/Setup/SkillFxSetup.cs`, generated `Resources/SkillFxArt.asset`, `Art/SkillFx/` (marker copy at `_Emission` 0.8, URP Unlit ring), `Tests/EditMode/SkillFeedbackTests.cs`.
- Changed: `ClientApp.cs`, `KillerWorldLayer.cs`, `KillerHudCopy.cs`, `KillerHud.cs`, `SurvivorHud.cs`, `SurvivorReadout.cs` (`Mate.Stealthed`), `SettingsScreen.cs`, `RoleCamera.cs`, `DevArgs.cs`/`DevCapture.cs` (`-ptk-want-skill`, `skillfx-*` event shots), `ClientUI.uss`, `HUD_SPEC.md`.
- Existing tests: two Overclock/Recovering copy assertions (`KillerHudTests`, `QA/KillerHudQaTests`), per §14.2.2.

**Per-skill status:** all 9 rows of §14.2 are built as written. Two small changes are recorded in §14: the Traffic overload one-shot uses the effect's `X, Z`, and a Disabled reason that the prompt row already shows is not repeated.

**Tests:** EditMode 1382/1382 passed, 0 compile errors (baseline 1356, +26 new).

**Build:** `BuildScript.BuildWindows` succeeded.

**Shots** (`docs/qa/U7b-skillfx-screens/`, live offline matches at 720p and 1080p; I opened all of them):
- Killer caster: k0, k1.
- Survivor caster: s0, s1, s2, s4.
- Ping-scanned target: 1080p only. Three extra 720p Survivor runs got no Killer bot with Ping scanner.
- Ally views: Dummy, White noise, Relay, Restore.
- Reduced motion: White noise as a still ring.
- Bloom check: no near-white pixels in the world area of the Hovl shots, so no ring fallback was needed.

**SLOP self-score:** 88. Glow appears only on in-world events (≤ 1 s, no bloom), lines use palette meanings, and there is no glow on UI. One deduction: the Hovl halo is soft.

**Gaps**
- None of the clips has been auditioned (🔶).
- No Firewall block shots: it needs a switch in progress.
- One relay shot catches the ring mid-growth, because the capture delay is counted in frames.
- The server sends no rejection reason (§14.3).

## Attempt 2

- **F1:** `FxSound` now carries the effect point (`X, Z`). All 6 positional records were audited: Dummy, Overload, Restore, White noise and Relay at the effect point, Firewall at its switch. `SkillFeedback.SoundPoint`/`VisualPoint` are the only anchoring path: `SkillFx.Play` calls them, and `ClientApp.PlayFx` passes only the own position. New tests: `AnUnaffectedRelayAllyHearsTheCueAtTheRelayPoint` (an ally 25 m away gets the relay point and the cue is muted beyond 20 m), `EveryPositionalCueCarriesTheEffectPoint`, `SkillFxPlacesThePositionalVoiceAtTheEffectPoint`.
- **F2:** two pooled skill-tier voices. A new cue starts on the idle voice while the cut one fades over 30 ms. Nothing is allocated per frame or per cue, because rolloff curves are cached per range. Test: `ANewSkillCueFadesThePreviousOneOver30Ms`.
- **F3:** not staged. Two more 720p runs (scan4, scan5) never drew a Killer bot with Ping scanner, because the bot skill is random.
- EditMode 1386/1386 (`docs/qa/u7b-runs/editmode-a2.xml`); BuildWindows succeeded (`build-a2.log`); 19 re-serialised files reverted.
