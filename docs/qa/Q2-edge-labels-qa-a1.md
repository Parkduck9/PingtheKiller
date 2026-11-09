# Q2-edge-labels — QA report, attempt 1

- **Implementer:** Claude Opus subagent (quota fallback: Codex out of credits).
- **QA:** Claude Fable subagent (user routing change 2026-11-09; Codex and Gemini Flash not used).
- **Worktree:** `C:\Unity\MultiPlayGame\PingtheKiller\.claude\worktrees\objective-borg-dc2fe1`, uncommitted diff on `claude/objective-borg-dc2fe1` (base `53f4ef9`).
- **Reviewed:** `git diff` of `EdgePlacement.cs`, `TagLayout.cs`, `KillerWorldLayer.cs`, `MarkerLayer.cs`, `RulesV2World.cs`, `TagLayoutTests.cs`; new `EdgeSpreadTests.cs`; implementer report `Q2-edge-labels-impl-a1.md`; screenshots `q2-runs/shots-a1-*`.
- **QA additions (tests only, no product code touched):** `Client/Assets/Tests/EditMode/QA/EdgeLabelsWiringQaTests.cs` (+ .meta).

## Verdict: FAIL

The spread and the shared layout pass are correct and well tested in isolation, but the real wiring hides the Overloaded alert tag whenever it sits flush against the right (or bottom) safe edge — the exact situation of the original 1080p bug evidence (F-01, major). One product line is the cause; everything else in the frame checks out (see `ApartFromTheF01Rounding…` test).

## Per-spec-bullet table

| # | Spec bullet | Result | Evidence |
|---|---|---|---|
| 1 | Edge spreading: pure, allocation-free `EdgeSpread`; one edge per marker, deterministic corner rule; order by ideal + stable key (never input order); displacement-minimising 1D packing (PAVA) clamped to safe-rect side minus keep-out; 6 u hysteresis; whole-unit outputs; ≤ 1 u in → ≤ 1 u out; gap 4→3→2 then drop Overloaded tags first, farthest first; glyphs never dropped | **PASS** | `EdgeSpread.Order/Pack/TryPack/DropOneTag` (EdgePlacement.cs L219-396). `EdgeSpreadTests` 11/11 green: N=5/7/10 on all four edges with ≥ 4 u gaps; 25 shuffles identical; 120-frame drift, ≤ 1 u moves, no flips inside hysteresis; overfull edge shrinks then drops alerts farthest-first, Survivor tags kept. QA `EdgeSpreadSolveAllocatesNoGcMemoryAfterWarmUp`: 0 GC.Alloc samples on the third frame (32 markers on three edges). Keys cannot collide: blip handles start at 1 (`BlipTracker.nextHandle = 1`), alerts use `-1 - Id`. |
| 2 | One layout pass for all Killer tags; v2 tags measured (one-frame hidden rule kept); Overloaded priority documented; spread not undone; every visible tag inside the 24 u safe rect and out of the keep-out | **FAIL (F-01)** | Architecture is right: `MarkerLayer.External/ExternalResult`, `V2MarkerLayer.LayoutInput/ApplyLayout`, `markers.Layout(safe, keepOut)`, `LayoutPriority.Overloaded = 2` with comment, `KeepGlyph` for Exits, slotted nearest-free placement for Overloaded/Exit tags; `TagLayoutTests`/`EdgeSpreadTests` mixed-layer cases pass and shift stays 0 for spread tags. But `V2MarkerLayer.PlaceEdge` (and `Tag()`) round `x` after clamping to `safe.xMax - w` while keeping the fractional width: the reserved rect ends `xMax = 1576.2 > 1576`, `TagLayout.Free` rejects every candidate, and the alert tag is hidden. Reproduced through the real objects: `EdgeLabelsWiringQaTests` 4 strict tests fail with `reserved v2 rect 0 … (x:1361.00, width:215.20) (xMax 1576.2)` at 1600×900 (same arithmetic at 1920×1080: 1681 + 215.2). |
| 3 | Zero GC allocation per frame in the new paths | **PASS** (note F-04) | EdgeSpread: 0 samples (above). TagLayout: with equal item counts, pre-Q2 priorities and Q2 Overloaded/Exit items both produce 18 GC.Alloc samples, so the slot path adds nothing. The 18 are pre-existing: `TagLayout.Compare` calls `x.Priority.CompareTo(y.Priority)`, which boxes both enum operands on every comparison (2 × 9 comparisons for 10 sorted items). `KillerWorldLayer.DrawEdges` uses the `List<T>` struct enumerator; `EdgeMark` is a struct; no LINQ/closures. |
| 4 | Survivor HUD unchanged; `RulesV2Preview` compiles and works | **PASS** | `SurvivorHud` calls only `V2Markers.Begin/Regressing/End` (SurvivorHud.cs L152-168); `Regressing` keeps its signature and the Survivor path never calls `LayoutInput/ApplyLayout/PlaceEdge`. The only change on that path: an unmeasured (hidden) label is now positioned from the estimated size instead of a 0×0 rect, invisible either way. `RulesV2WiringTests` 28/28 and `SurvivorFeedbackQaTests` green. `RulesV2Preview` uses the unchanged `Overloaded(at, onScreen, pointing, text)`, `Exit`, `Regressing` overloads (compiled in the EditMode run). |
| 5 | SLOP: no new style, same tag styles and copy, existing spacing scale | **PASS** | Only `BesideGap = 4`, `BelowGap = 12`, step gap 2 u used; tags reuse `tag`/`v2-tag` classes; copy unchanged (`KillerHudCopy.Distance`, `TerminalFlagCopy.Overloaded`). Screenshots show the same tag look as before. |

Acceptance tests from the brief: N ≥ 5 per edge — present and asserts gap ≥ 4 u, safe rect, keep-out, no drop, layout shift 0 (PASS). Stable ordering — 25 shuffles with exact ties (PASS). No jitter — 120 frames, ≤ 0.5 u/frame, asserts ≤ 1 u moves and no order flip within hysteresis (PASS). Crowded 1280×720 / 1920×1080 with real `KillerHud` keep-out, 3 alerts + Exit + chevron + diamond swept over 31 offsets each (PASS, but **weak**: it feeds `spread.Tag(i)` straight into `TagLayout`, bypassing `V2MarkerLayer.PlaceEdge`'s rounding, which is why F-01 slipped through). Mixed layers — PASS. Existing `EdgePlacementTests` 9, `TagLayoutTests` 9, `QA/EdgePlacementQaTests` 20, `QA/TagLayoutQaTests` 32, `QA/KillerHudQaTests` 35: all green.

## Findings

### F-01 — major — Overloaded/Exit/Regressing tags flush to the right or bottom safe edge are hidden by the layout
- **Where:** `Client/Assets/Scripts/UI/RulesV2/RulesV2World.cs` `PlaceEdge` L110 (`new Rect(Mathf.Round(rect.x), Mathf.Round(rect.y), slot.Rect.width, slot.Rect.height)`) and `Tag` L181-184 (`x = Clamp(x, …, area.xMax - w)` then `Mathf.Round(x)`), consumed by `TagLayout.Free` L167 (strict bounds check) via `LayoutInput`.
- **Repro (deterministic, EditMode):** `EdgeLabelsWiringQaTests.OneOffScreenOverloadedAlertAndTwoSurvivorChevronsOnTheRightEdgeNeverOverlap_RealWiring` — one off-screen Overloaded terminal to the Killer's right at 1600×900: reserved rect `x:1361, w:215.2` → `xMax 1576.2 > 1576`. In play: any alert whose measured label width has a fractional part below .5 (so `Round` goes up) at the right edge, or a height fraction below .5 at the bottom edge. Because the metres tick, the text and width change and the tag flickers between shown and hidden while the Killer moves.
- **Expected:** the alert tag is shown at its spread position (spec 2, HUD_SPEC 13.6 "Tag below it").
- **Actual:** `ExternalResult(i).TagVisible == false` → `ApplyLayout` hides the label. Square and pointer still draw. Same mechanism hides on-screen Overloaded, Exit and Killer Regressing tags that `Tag()` clamps to the right/bottom `SafeArea` edge (before Q2 those were simply drawn 0.2 u past the inset).
- **Evidence:** `docs/qa/q2-runs/qa-editmode-a1.xml`, `qa-editmode-a1-r3-wiring.xml` (4 failures, identical message). The companion test `ApartFromTheF01RoundingTheCrowdedRightEdgeIsCleanAtBothResolutions_RealWiring` passes at both resolutions with 1 and 3 alerts and 2–3 chevrons: spread, chevron tags, keep-out, overlaps, same-edge gaps and the moved v2 square are all right, so the fix is the rounding only (e.g. reserve `Ceil` sizes and clamp the rounded origin to `area.xMax - w` / `yMax - h`, or round the rect inward).

### F-02 — minor — lost fuzz coverage for `Terminal` after the renumbering
- `Client/Assets/Tests/EditMode/QA/TagLayoutQaTests.cs` L311 and L428: `(LayoutPriority)rng.Next(4)` now yields Survivor/Edge/Overloaded/Exit and never Terminal (= 4). The implementer flagged it. Covered now by `EdgeLabelsWiringQaTests.RandomLayoutsOverAllFivePrioritiesKeepTheInvariants` (300 rounds, all five priorities, KeepGlyph, 2.6k Terminal items asserted). The two old lines should become `rng.Next(5)` in a QA follow-up; left untouched here to keep this attempt's diff to one new file.

### F-03 — minor — a chevron tag can land away from its chevron
- `docs/qa/q2-runs/shots-a1-1080-away/killer-crowd.png` right edge (zoom in the QA scratch): two chevrons at y ≈ 543/572, on-screen diamond tags `40 m`/`42 m` and `Terminal 6` in the same column; `TagLayout` stacks the chevron tags to `45 m` above and `29 m` below, so which tag belongs to which chevron is no longer obvious. No overlap, nothing hidden (so no spec failure); pre-existing 3-step stacking, more visible now that the Beside tag starts only 4 u from the chevron. Acceptable for this unit; worth a note for the HUD polish backlog.

### F-04 — minor (pre-existing) — `TagLayout.Compare` boxes two enums per comparison
- `TagLayout.cs` L179 `x.Priority.CompareTo(y.Priority)`: `Enum.CompareTo(object)` boxes both operands; measured 18 GC.Alloc samples for 10 pre-sorted items, 110 for 20 unsorted. Not introduced by Q2 (U4c code) and the Q2 slot path adds none, but it is the only per-frame allocation left on the Killer tag path. One-line fix: compare `(byte)x.Priority` to `(byte)y.Priority`.

### F-05 — minor (behaviour change, by design) — the Terminal square and name tag hide under an on-screen Overloaded square
- `TagLayout.Solve` L86-91 now hides a Terminal glyph (priority 4) that overlaps the Overloaded square (priority 2) of the same terminal, together with its `Terminal n` tag; `EdgeSpreadTests.TheOverloadedPriorityIsBetweenEdgeAndExitAndItsGlyphIsNeverHidden` asserts it. The alert text carries the terminal name, so no information is lost. HUD_SPEC 13.6 does not say either way; recording it so the orchestrator can accept or reject.

### F-06 — minor — one-frame wrong reservation when a pooled v2 label changes to a different tag's text
- `RulesV2World.cs` `Tag` L174-176: when label *i* is reused for another text (e.g. an alert appears before the Regressing tags, shifting every later label), `sizes[i]` still holds the previous text's size, so the reserved rect is wrong for that hidden frame and the neighbours re-layout the next frame. Cosmetic one-frame pop; the comment's claim holds only for the same label's metres ticking.

### F-07 — info — layout bounds are now the safe rect
- `KillerWorldLayer.Render` L172 `markers.Layout(safe, keepOut)` (was the full screen). Survivor distance tags and Terminal tags within 24 u of a screen edge that previously showed now stack or hide. This is what spec 2 asks for; noted because it affects tags outside this unit's scope.

### Corner case — info
- `EdgeSpread.SideOf` sends corners to Left/Right; a Right-edge item at the top corner and a Top-edge item at the right corner are never spread against each other. Their tags are still separated by `TagLayout`; two glyphs (chevron vs alert square) can still overlap at a corner, as before Q2. Not exercised by any test; low frequency.

## EditMode runs

| Run | File | Total | Passed | Failed | Notes |
|---|---|---|---|---|---|
| Implementer r2 | `q2-runs/editmode-a1-r2.xml` | 1398 | 1398 | 0 | before QA tests |
| QA full | `q2-runs/qa-editmode-a1.xml` (+ `.log`) | 1405 | 1400 | 5 | 1398 pre-existing + implementer tests all green, flake `SurvivorKillerRenderingReverifyTests…` passed first time; 5 failures all in the new QA file (4 × F-01, 1 × GC test measuring unequal item counts, rewritten) |
| QA filtered r2 | `q2-runs/qa-editmode-a1-r2-wiring.xml` | 7 | 2 | 5 | F-01 message made explicit |
| QA filtered r3 (final) | `q2-runs/qa-editmode-a1-r3-wiring.xml` | 8 | 4 | 4 | 4 × F-01; `ApartFromTheF01…`, both GC tests and the 5-priority fuzz pass |

Final state: 1398 pre-existing + 12 implementer tests green; of the 8 QA tests, 4 fail on F-01 by design (they are the regression tests for the fix). Results written by Unity relative to the project path were moved to `docs/qa/q2-runs/`; `Client/docs` removed. `Client/Assets/Resources/DOTweenSettings.asset` (line endings only) reverted; no other Unity-touched file.

## Visual review (SLOP §9)

Screenshots inspected: `shots-a1-720-nearest/killer-crowd.png` (three left-edge chevrons `45 m / 44 m / 42 m` beside their chevrons at a 27 u pitch = 23 u tag + 4 u gap, right-edge `43 m`, diamonds `26 m`/`44 m` apart), `shots-a1-1080-chase/killer-crowd.png` (three `55 m` top-edge chevrons spread horizontally, tags below, ~8 u apart; `50 m` further right), `shots-a1-720-away/killer-crowd.png` (left edge ×3, right edge `41 m`/`49 m` with exactly 4 u between them and `Terminal 4` stacked below), `shots-a1-1080-away/killer-crowd.png` (F-03 case, no overlaps), `shots-a1-720-nearest/killer-offscreen.png`. Compared with `U6c-wire-screens/5-killer-overloaded-exitmarker-720.png` and `a2-5-killer-overloaded-1080.png`: those overlaps involved the Overloaded tag over on-screen diamond tags (720) and over right-edge `42 m` chevron tags (1080). No run reproduced an Overloaded alert, so the fix for both is verified only in EditMode — and for the 1080 case F-01 means the alert tag would currently vanish instead of overlap.

| Check | Points | Score |
|---|---:|---:|
| No V1–V12 visual tells (gates V1, V2, V4, V8, V10) | 20 | 20 — no glow, gradients, animation; same keylines and tag fills |
| No L1–L8 layout tells; one spacing scale | 15 | 15 — 4/12 u gaps, 2 u stacking step; no overlaps in any shot |
| Copy follows §4 and fixed vocabulary | 15 | 15 — unchanged strings |
| Every element answers "what state, what action" (gate) | 15 | 12 — F-03 tag-to-chevron ambiguity; gate passes for what is drawn |
| Role-specific HUDs, no information leak (gate) | 20 | 20 — Survivor HUD untouched; Killer gains no new information |
| Interactive states / keyboard & gamepad (gate) | 10 | 10 — no interactive element changed (n/a) |
| Contrast ≥ 4.5:1, readable at 1280×720 | 5 | 5 — tags legible in all 720p shots |
| **Total** | **100** | **97 / 100, no gate failure** (for the rendered frames; the F-01 defect is a functional failure tracked above, not a slop tell) |

Coverage of the Overloaded alert path: EditMode only. `EdgeSpreadTests.TheCrowdedRightEdgeStaysReadableAt720pAnd1080p` covers spread + `TagLayout` with estimated sizes; `EdgeLabelsWiringQaTests` covers the real `V2MarkerLayer`/`MarkerLayer`/`EdgeSpread`/`TagLayout` objects in the exact `KillerWorldLayer.Render` call order, with these replacements: 3D projection replaced by screen directions into `EdgePlacement.Place`; `TerminalFlagsView` built by hand instead of `V2Wire.Terminal`; labels never attached to a panel, so every v2 tag takes the unmeasured branch (estimated size, hidden for the frame) — the measured-size branch, the `SafeArea` clamp of `Tag()` and the actual painting are not exercised by any test. No dev flag or product code was added.

## Recommendation for attempt 2

Fix F-01 in `V2MarkerLayer.PlaceEdge` and `Tag()` (round inward or clamp after rounding; or reserve `Ceil(w)`/`Ceil(h)` and clamp with those), re-run `EdgeLabelsWiringQaTests` (all 8 must pass) plus the full EditMode suite; optionally fold F-04 (one line) and F-02 (`rng.Next(5)`) in. Same approach, no design change needed.
