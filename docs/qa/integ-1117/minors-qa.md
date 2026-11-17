# integ-1117 minors: QA

Reviewer: Fable. Diff on c8ea347, reviewed by reading. Unity not re-run: `editmode-minors.xml` (1552 passed, 0 failed, both new tests and the audit tests Passed) and `build-minors.log` (Success) postdate the last source edit.

**Verdict: PASS**

- **M3 PASS.** `Resolve` returns None for any non-zero `Chase`, after the Killer-view return, so the Killer path is unchanged. `Chase` is stored at all three avatar sites (update, model, capsule), own avatar included. Props fade through the existing None path.
- **M4 PASS.** Copy `Stunned`, ring Disabled, Passive kept. `hud.Stunned` is rewritten every Killer frame from `stun.Showing`, which is time-bounded, so it cannot stick. `ChasePropsLayer` is new per match.
- **M5 PASS.** `CaptureImpactFx.Killer = SkillFx.Danger` (229, 72, 77), pinned by a test.
- **M1 PASS.** The predicate is created only inside `devArgs.Active`. The delegate takes a point and returns a bool. Entry positions reach it only from `ChasePlayerShots`, inside the Survivor-only loop. The Killer client passes static shutter centres only. Nothing new is logged.

**Findings (all minor, none blocking)**

1. M1 is not proven live: no chase shot fired in the 150 s run.
2. M1 on-screen test is frustum-only. A shutter behind a wall within 12 m still passes.
3. M4 lags one frame: `SetSkill` runs before `ApplyKillerHud`. Also covers the first frame of a new match.
4. `KillerHudCopy` uses the literal `"Stunned"`, not `ChasePropCopy.Stunned`.
5. The comment in `RulesReadoutQaTests` ("exactly one other coordinate") is stale: `ChasePlayerShots` now also reads other entries' X/Z (Survivor client, never logged).
6. The M5 assertion sits inside the M4 test.
