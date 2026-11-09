# Brief Q2-edge-labels: attempt 2 fix list

- **Implementer:** Claude Sonnet subagent (user routing 2026-11-09: Sonnet for small fixes). **QA:** Claude Fable subagent.
- Same worktree, same owned/granted files as `docs/briefs/Q2-edge-labels-a1.md`, same hard rules (no commit/push/merge, no desktop input, no downloads, do not kill processes you did not start).

Attempt 1 failed QA (`docs/qa/Q2-edge-labels-qa-a1.md`). The orchestrator accepts F-01 (major), F-02 and F-04. F-03, F-05, F-06 and F-07 are accepted as-is (backlog), no change.

1. **(Major) F-01: v2 tags flush to the right or bottom safe edge are hidden.** In `V2MarkerLayer` (`Client/Assets/Scripts/UI/RulesV2/RulesV2World.cs`), `Tag()` clamps `x` to `area.xMax - w` and then rounds the origin while the reserved width stays fractional, so the reserved rect can end up to 0.5 u past the safe edge; `PlaceEdge` does the same with `Mathf.Round(rect.x)`. `TagLayout.Free` then rejects every candidate and the tag is hidden.
   - Fix: reserve whole-unit sizes (`Mathf.Ceil` of the width and height, for measured, remembered and estimated sizes alike), and compute the rounded origin so the whole reserved rect stays inside the safe area (clamp after rounding, `Floor` toward the inside on the max side). Apply this in both `Tag()` and `PlaceEdge`. Keep the label's drawn position identical to the reserved rect's origin.
   - Do not loosen `TagLayout.Free`'s bounds check.
   - The regression tests are QA's `Client/Assets/Tests/EditMode/QA/EdgeLabelsWiringQaTests.cs` (4 tests currently fail on F-01). Do not edit that file; make them pass.
2. **(Minor) F-02:** `Client/Assets/Tests/EditMode/QA/TagLayoutQaTests.cs` L311 and L428: `(LayoutPriority)rng.Next(4)` → `rng.Next(5)` so Terminal is fuzzed again. Nothing else in that file.
3. **(Minor) F-04:** `TagLayout.Compare` boxes the enums (`x.Priority.CompareTo(y.Priority)`). Compare as `((byte)x.Priority).CompareTo((byte)y.Priority)` or by subtraction. No behaviour change.

## Acceptance

- Unity EditMode all green, including the 8 tests in `EdgeLabelsWiringQaTests` (known flake: `SurvivorKillerRenderingReverifyTests.SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract`, rerun once if it alone fails). Results: `docs/qa/q2-runs/editmode-a2.xml` and `.log` (pass `-testResults`/`-logFile` as absolute paths; Unity resolves relative paths against the project folder).
- `BuildWindows` succeeds: `docs/qa/q2-runs/build-a2.log`.
- Revert any Client file whose only change is line endings, and any Unity-touched settings asset (`DOTweenSettings.asset`, `ShaderGraphSettings.asset`).
- Append `## Attempt 2` (≤ 150 words, header line: implementer = Claude Sonnet subagent) to `docs/qa/Q2-edge-labels-impl-a1.md`.
