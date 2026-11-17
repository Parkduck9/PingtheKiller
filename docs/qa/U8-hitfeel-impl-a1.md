# U8-hitfeel, attempt 1 (implementer: Claude Opus)

The unit ran about 2 h, against a 75 min time box. `SkillFx.cs` and `SkillFxSetup.cs` were not touched (ownership change).

**Root causes**
- **Lag:** the stutter held the world at 8 Hz by design.
- **Swipe:** the clip started at 0.45 s, skipping most of the wind-up. The 8 Hz hold then collapsed the strike into one or two frames.
- **Effects:** no capture effect was ever spawned. Only sound, banner, stutter and kick existed.

**Changes**
- **`LaggingStutter`:** now a UI-layer look:
  - full frame rate;
  - 100 ms presentation hit-stop (no `timeScale`);
  - 0.4 s glitch burst (red flash and tear bars);
  - steady veil, vignette and scanlines;
  - Reduced mode: steady look only.
- **`CaptureImpactFx`:** new, pooled, 0.6 s red burst at chest height.
  - Survivor client: on a capture of any drawn Survivor.
  - Killer client: at the tracker instance of its own capture, so masking holds.
  - Hovl has no hit prefab, so the burst is procedural.
- **Swipe:** starts at 0.30 s. Wind-up 0.55 s, strike 0.24 s (slowed), 0.12 s hold, 1.21 s in total.
- **Killer view kick:** 70 ms wind-up, lunge, small trauma shake. Follows Reduced motion.
- **Dev:** Survivor `-ptk-chase` seeks the Killer. Added capture shots and a `[perf]` log.
- **Docs:** HUD_SPEC §4.5, §4.10 and §12.5.

**Frame times** (±1 s around a capture, max)
- **Original:** not measured.
- **First pass:** spikes of 106–203 ms. Cause: a repeated 4x3 px scanline tile, about 77k UI quads.
- **Fixed** (one stretched texture):
  - Survivor: 31.7 ms after vs 39.6 ms before.
  - Killer: 41.0 ms after vs 32.5 ms before.
  - No spike follows a capture. The remaining over-33 ms frames also appear before the capture.

**Tests:** EditMode 1491/1491 (main had 1486). Changed tests:
- `StutterClock` tests replaced.
- Kick, swipe-length, import and controller tests rewritten.
- 2 QA source checks updated, plus one Settings message.
- 7 new tests: look, component, hit-stop, swipe map, pool, frame log.

**Gates**
- BuildWindows succeeded.
- `dotnet build -warnaserror`: 0 warnings, 0 errors.

**Shots** (all under `docs/qa/U8-screens/`)
- `survivor/u8-capture-{0-hit,1-burst,2-after}.png`
- `killer2/u8-killer-capture-*.png`
- `pre/` (first pass)

**Unverified**
- No swipe frame-capture probe was run. The timing was chosen from the U4j sheets and the live shots.
- No human playtest.

**Risks**
- Particles look large next to the Killer's eye.
- Strength of the flash.
- Minimal edits outside the owned files: `KillerWorldLayer`, `DevCapture`, `SurvivorFeedback`, `MotionSetup`.

## Attempt 2 (F1-F5, F7; about 55 min against a 40 min box)

- **F1:** the Killer entry is now read inline into `killerEntry` (`SeekPoint` is removed). Changed test: `RulesReadoutQaTests.TheDevLogOnlyEverPrintsTheLocalPlayersPosition`. It asserts:
  - the only coordinate read before the guard is the Killer entry;
  - `killerEntry` is used only by `Chase`;
  - no `Debug.Log` prints it.
- **F2:** the Killer client uses first-person sizes, `maxParticleSize` 0.04, and the burst is pulled 0.4 m toward the eye. Survivor sizes are unchanged.
  - Killer shots (`a2/killer2/`): the HUD is never covered.
  - The burst itself was not seen, because the captured body sat below the view at 1 m. Unverified.
- **F3:** keys come from every frame (`a2/swipe-frames.png`, `U8-runs/a2-swipe-frames.log`). The hand peaks over clip 0.90–1.03 s.
  - Timing: 0.15 s wind-up, contact at 0.27 s, 0.12 s hold, 0.4x follow-through, 1.15 s in total.
  - The burst, flash, hit-stop and impact start at contact (`a2/survivor/u8-capture-0-hit.png`).
- **F4:** flash at 20 %. Tear bars are danger and neutral.
- **F5:** the clock eases back over 0.15 s.
- **F7:** the impact log is dev-only and has no position.

**Gates:** EditMode 1491/1491, BuildWindows succeeded, dotnet 0 warnings and 0 errors.

**Frame times:** Killer max after a capture 29.5 / 26.7 / 25.8 ms, 0 spikes after any capture.
