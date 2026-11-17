# U10-motion — implementation report, attempt 1 (Claude Opus)

**S1 root cause.** The pose was never missing. A live-like EditMode replay (walk in, stop, Hacking) reaches `ButtonPushing` at speed ≈ 0 for both the own Survivor and a mate. These were ruled out:
- the `Hacking` flag;
- the speed threshold (the server applies no slide);
- the controller state;
- the Avatar mask.

The real cause is legibility. U4j's `MotionReach` IK drops the one-hand press to the 0.72 m terminal top, which is hip height, so from the camera behind the character it reads as standing idle (`s1-before-own-hack-main.png`).

**Motions.** All are IK on the base pass (`InteractionRig`). Talk01 was not used.
- **Hack:** ButtonPushing plus both hands typing on a 1.18 m panel, on alternating 0.17 s beats.
- **Restore:** the hips drop 0.36 m and the body leans 30° over the Lagging ally, hands down.
- **Own switch hold:** the right palm on the switch at 1.15 m, leaning in. It is own-only, because other Survivors' holds are not exposed per player.
- **Jam:** the Killer model (Survivor view) does one two-hand slam when a terminal within 2 m turns Regressing.

**Props** (`HoloPropFx`, built once per character): a translucent `ally` key-grid panel with a beat pulse and a packet stream, an `ally` link line, and a `danger` glitch burst. These are panels, lines and squares, with no rings (U9). The materials are new, in `Art/Holo/Resources`.

**Safety.** The props are driven by received `Hacking`/`Rescuing`, the own `ActionKind` and `Regressing`. The Killer view shows none.

**Locomotion.** At 4.0 m/s the feet used to move at 1.68 m/s (the walk). Now walk is 0.5–1.6 m/s and run is at 4.0 m/s. The Killer runs at 4.6 and 6.9 m/s on the same tree. Rates come from the measured clip speeds (walk 1.715, run 3.401, injured walk 1.092). Injured movement blends from the limp into the run. Sprint is unused because no Survivor speed exceeds 4.0 m/s.

**Tests.** `InteractionMotionTests` (15 tests): the mapping, the S1 regression, the pool, the colours, the Killer view, the jam, and no foot sliding.

**Gates.**
- EditMode 1501/1501 passed.
- BuildWindows succeeded.
- `dotnet build -warnaserror`: 0 warnings, 0 errors.

**Shots** are in `docs/qa/U10-screens/`.

**Unverified.** No live switch-hold or jam shot (covered by the jam EditMode test only). `CharacterViews.cs`, `WorldView.cs` and `ClientApp.cs` got small hooks outside the owned files.

**Risks.**
- From directly behind, the torso hides most of the panel.
- Merge with U8 in `MotionSetup` and `CharacterMotion`.

## Attempt 2 (Claude Sonnet)

- **Own hack readability.** Panel raised from 1.18 to 1.4 m, widened from 0.62 to 0.9 m (0.5 m tall, 0.5 m forward, tilt 28 degrees). Hand spread widened from 0.15 to 0.22 m, so the hands stay on the panel. From the default camera behind, the panel now shows above and past both shoulders (`U10-screens/a2/hacking.png`; before: `U10-qa-screens/compare-before-after-behind.png`).
- **Restore while moving.** `InteractionMap.Resolve` keeps Restore up to 1.5 m/s (`RestoreMoveSpeed`); Hack and Switch still need speed < 0.3 m/s. The rig tracks the rescuer's ground speed; while moving it frees the feet and hip drop and keeps the lean and the link line. The explicit QA diagnostic was not changed.
- **Dead code.** Removed `InteractionMotion.JamWeight` and `JamTracker.Reset`. The Killer's unused panel is still built, but never shown.
- **Gates.** EditMode 1504 passed, 0 failed, 2 explicit skipped (1506 total; +3 Resolve assertions in an existing test). BuildWindows OK. Live run done, `U10-screens/a2/`, log `U10-runs/live-a2.log`. The moving-rescuer pose was not seen live.
