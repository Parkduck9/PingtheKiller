# Q5 QA, attempt 1 (Fable) — shutters and vault rails

**Verdict: FAIL** at HEAD 7937f8c (EditMode gate). With the catalog regenerated every gate passes; F2 is then the open question.

On the orchestrator's instruction I ran `ChasePropSetup.Run` once here. Only `Client/Assets/Resources/ChaseProps.asset` changed (3 rail boxes); it is left uncommitted. The second build, the live runs and the editor shots use the regenerated art.

## Gates
- `dotnet build -warnaserror`: 0 warnings, 0 errors
- Server.Tests 1115/1115; BalanceSweep 18/18
- Harness 3×, fresh servers: 100/100 each (`Q5-qa-runs/harness-{1,2,3}.log`)
- EditMode at HEAD: **1503/1505** (`editmode.xml`); regenerated: 1505/1505 (`editmode-regen.xml`)
- BuildWindows: success at HEAD and regenerated

## Spec
**Q5a:** protocol v0.7 PASS · pull 1.5 m / 0.3 s PASS · stun 0.6 m zone, 2.0 s, no move/capture/interact/skill PASS · vaults 1.0 / 0.8 / 1.6 s, mirrored landing PASS · break 2.0 s, moving or releasing cancels PASS · fresh press only PASS · capture interrupts PASS · forced Signal + 1 s PASS · priority PASS · bot fairness PASS · bot use FAIL (F2).
**Masking** PASS: anonymous Killer-bound entries carry `Chase = 0`; one shutter list for all; Silent entries omitted; the Killer client draws no poses; cues are 3D linear, silent past 30 m / 14 m.
**Q5b:** runtime placement from Shared PASS (prefab roots at origin, no coordinates in tests) · catalog FAIL (F1) · prompts and copy PASS by code and tests, not seen live · poses procedural, no clip (declared deviation) · stun feedback unverified (F4) · sounds PASS · HUD_SPEC §15 PASS · SLOP §9: about 88/100 from code and editor shots, no gate failure.

## Findings
- **F1 major.** `ChaseProps.asset` holds the pre-move boxes of rails 0, 4 and 7. Repro: EditMode at HEAD. Expected `art.Matches()` true; actual false: `TheCatalogMatchesTheSharedBoxes…` and `TheViewShowsExactlyOneGroup…(True)` fail and the player silently draws primitives. Nothing else needed changing.
- **F2 major.** Bots almost never use the props. Four offline 1v7 runs (2×270 s, 2×590 s): one pull, one stun, one break, no rail vault. In `live-killer-2.log` the chasing Killer eliminated 7 of 7 in 479 s without a single pull or vault. Evidence: `live-survivor-2.log` lines 913-925; the other logs have no `shutter` or `rail` line.
- **F3 minor.** Broken is nearly indistinguishable from Up at 6 m; rails are thin and dark beside lighter, non-vaultable fences (`Q5-qa-screens/editor/editor-survivor-shutter-broken-*.png`, `editor-survivor-rail-*.png`).
- **F4 minor.** No stun burst is visible in `editor-survivor-killer-stunned-*.png` although the log says `burst True`.
- **F5 minor.** DevCapture has no shutter or rail event shots, so no live shot shows a prompt, pose, Down or Broken. Live shots show Up frames only: grounded, wall to wall, not covering markers (`live-killer/killer-occluded.png`).

## Fix list
1. Commit the regenerated `ChaseProps.asset`.
2. Raise bot use (`PullTriggerDistance`, `RailChasedDistance`, lookahead), add a bounded-time use test, re-run BalanceSweep.
3. Lighten the Broken halves and rail tubes; check the burst in a live Survivor view.
4. Add DevCapture shots on chase events and near-prop prompts.
