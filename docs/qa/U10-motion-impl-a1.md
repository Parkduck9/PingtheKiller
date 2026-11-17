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
