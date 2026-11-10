# Q2-edge-labels — QA report, attempt 2

- **Implementer (attempt 2):** Claude Sonnet subagent. **QA:** Claude Fable subagent.
- **Worktree:** `C:\Unity\MultiPlayGame\PingtheKiller\.claude\worktrees\objective-borg-dc2fe1`, uncommitted diff on `claude/objective-borg-dc2fe1` (base `53f4ef9`).
- **Reviewed:** `git diff` of `RulesV2World.cs` (`WholeInside`, `Ceil` sizes in `Tag()`/`PlaceEdge`), `TagLayout.Compare`, `QA/TagLayoutQaTests.cs`; implementer `## Attempt 2`; `q2-runs/editmode-a2.xml`, `build-a2.log`.
- **QA additions (tests only):** `Client/Assets/Tests/EditMode/QA/EdgeLabelsPlacementQaTests.cs` (+ .meta), 3 tests.

## Verdict: PASS

| Finding | Status | Evidence |
|---|---|---|
| F-01 (major) hidden edge tags | **Fixed** | `Tag()` reserves `Ceil(w)`/`Ceil(h)` for measured, remembered and estimated sizes; `WholeInside` rounds the origin then clamps to `[Ceil(min), Floor(max - size)]` on both axes; `PlaceEdge` uses the same helper with the slot's reserved size; `label.left/top = placed.x/y`. `TagLayout.Free` untouched. `EdgeLabelsWiringQaTests` 8/8 green (were 4 failing). |
| F-02 (minor) Terminal fuzz | **Fixed** | L311/L428 `rng.Next(5)`; nothing else in the file. |
| F-04 (minor) enum boxing | **Fixed** | `((byte)x.Priority).CompareTo((byte)y.Priority)`; sort order unchanged (all `TagLayout*` tests green). |

Checked for regressions from the fix: unsized layer (`SafeArea` = 0..`float.MaxValue`): `Floor(MaxValue - w)` stays `MaxValue`, places are finite whole units, a corner anchor clamps to (0,0) (new tests). Sizes are never ≤ 0 (`MarkerLayer.TagSize` ≥ 8×23, remembered sizes only when > 0). The reserved rect exceeds the drawn label by < 1 u, so neighbours sit at most 1 u farther apart, never overlap. Survivor `Regressing` path and Exit (Above pivot) go through the same code and place correctly. `WholeInside` can only invert its clamp when the safe area is within 2 u of the tag size (impossible at ≥ 1232 u wide).

### F-08 — minor (pre-existing from attempt 1, not in the fix list) — slotted shift rounded only on the label
`ApplyLayout` sets `top = Round(slot.Rect.y + shift)`; `TagLayout.Slot` shifts can end in .5 (obstacles from MarkerLayer tags centred on whole anchors with height 23). The drawn label can sit 0.5 u off the layout-checked rect (0.5 u past the safe edge or into the 2 u step gap). Cosmetic; backlog with F-03/F-05/F-06.

## EditMode runs

| Run | File | Total | Passed | Failed |
|---|---|---|---|---|
| Implementer a2 | `q2-runs/editmode-a2.xml` | 1406 | 1406 | 0 |
| QA full | `q2-runs/qa-editmode-a2.xml` (+ `.log`) | 1406 | 1406 | 0 (flake did not occur) |
| QA new class r1 | `q2-runs/qa-editmode-a2-placement.xml` | 3 | 2 | 1 (test bug: centre assert on a tag clamped to the unsized origin; fixed) |
| QA new class r2 (final) | `q2-runs/qa-editmode-a2-placement-r2.xml` | 3 | 3 | 0 |

BuildWindows: `build-a2.log` L6845 "Windows build succeeded". `DOTweenSettings.asset` (line endings only, Unity-touched) reverted; no other non-code Client change.

## SLOP §9
Carried over from attempt 1: 97 / 100, no gate failure (no visual change in attempt 2; the only visible difference is tags flush to the right/bottom edge now showing). The measured-size branch in a live panel remains unverified by screenshot.
