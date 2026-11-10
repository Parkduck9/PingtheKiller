# QA report: minors-1110 attempt 1 (reviewer: Fable; implementer: Claude Sonnet)

**Verdict: PASS (with one accepted deviation and 5 minors, no blocker/major).**

Worktree `C:/Unity/MultiPlayGame/PtK-work/minors-1110`, branch `claude/minors-1110`, uncommitted on top of main 20727da.
Reviewed: `git diff` (TagLayout.cs, PlayMenu.cs, 4 test files, HUD_SPEC.md) + new `TagLayoutClearanceTests.cs`, brief `docs/briefs/minors-1110-a1.md`, implementer report `docs/qa/minors-1110-impl-a1.md`.

## Gates (re-run by QA)

| Gate | Result | Evidence |
|---|---|---|
| Unity EditMode | **1475 / 1475 passed**, 0 failed, 0 skipped, 0 inconclusive (1468 on main + 7 new `TagLayoutClearanceTests`) | `docs/qa/minors-1110-runs/qa/editmode-qa.xml`, `.log` |
| BuildWindows | `Build Finished, Result: Success.` / `[U4] Windows build succeeded` | `docs/qa/minors-1110-runs/qa/build-windows-qa.log` lines 6783, 6798. Note: Unity's incremental player pipeline did not rewrite `Assembly-CSharp.dll` (identical to the implementer's 14:28 build), only `level0`/`globalgamemanagers` etc. were refreshed. |
| `dotnet build PingTheKiller.sln -warnaserror` | 0 warnings, 0 errors | shell output (Shared/Server untouched) |
| Offline Killer run, 1280x720, 7 bots, `-ptk-quit-after 200` | ran the full 200 s, exited by `[DEV] quit after 200 s`, exit code 0, 0 exceptions/errors in the log, 9 shots | `docs/qa/minors-1110-runs/qa/killer-run.log`, `docs/qa/minors-1110-screens/qa/*.png` |
| Line-ending churn after Unity runs | 18 asset/settings files changed only in CRLF; all reverted with `git checkout` after `git diff --ignore-cr-at-eol --quiet` confirmed EOL-only. The 7 implementer files + untracked files are untouched. | `git status` = implementer's set only |

Survivor run: not done (optional); F1 is pure copy and is pinned by tests.

## Spec table

### F1: play-menu capture line

| Bullet | Result | Note |
|---|---|---|
| `CaptureCount >= 2` and Normal -> `Next capture eliminates you.` | PASS | `PlayMenu.cs:36,42` |
| Constant renamed (`NextCaptureEliminates`) | PASS | |
| Count 1 stays `Captured once: ... 30 s for a rescue.` | PASS | unchanged |
| Other states / count 0 -> no line | PASS | unchanged code path |
| Tests at `PlayMenuTests.cs` ~136-139, 259 and `PlayMenuQaTests.cs` ~36-41 updated | PASS | |
| Assertion: no `PlayMenuCopy` capture line contains "twice" | PASS | both constants asserted (`PlayMenuTests.cs:141-143`) |
| HUD_SPEC section 5 line 396 updated, marked `(11/10)` | PASS | |

### F2: TagLayout clearance

| Bullet | Result | Note |
|---|---|---|
| `TagGap` = 4 u clearance from every other placed tag and every avoided visible glyph | PASS for Survivor, Edge, Terminal tags; **DEVIATION** for Overloaded/Exit (4 u -> 2 u -> 0 u fallback, see F-1) | strict for the tags the finding was about |
| Value chosen and justified (= `EdgeSpread.Gap`) | PASS | pinned by `TheGapIsTheEdgeMarkerGapAndTheStackStepFollowsIt` |
| Own glyph keeps its relation; default offsets not pushed | PASS | own glyph = plain `Overlaps` via `current`; the Lagging second tag (separate item, `head - 4`) is exactly 4 u from the body top and stays (probe E) |
| Chevron decision stated | PASS | Survivor tag avoids Edge chevrons (`Avoids`), reason given (both Survivor information) |
| Priority order kept | PASS | |
| Stacking steps kept (below, above, below again, hidden) | PASS | step = height + 4 now |
| Q2 `Slot` path kept for Overloaded/Exit | PASS (+ fallback) | |
| Bounds and blocked rects kept | PASS | blocked stays strict overlap, as before |
| Determinism (depends only on the set of items) | PASS | probe: 20 000 random layouts solved in reverse input order -> 0 content differences (index swaps occur only between items with identical content, as on main) |
| No per-frame allocation | PASS | only two scalar fields added; `List<T>` struct enumerator unchanged |
| Q2 hysteresis in `EdgePlacement.cs` untouched | PASS | file unchanged |
| `Slot`/`NextTo` candidates use the same clearance (lands exactly `TagGap` away) | PASS | `ASlottedExitTagLandsExactlyTagGapFromTheObstacle` |
| `StepGap` consistent with the clearance | PASS | `StepGap = TagGap` |
| Test 1: touching Survivor tags end >= TagGap apart or stacked | PASS | |
| Test 2: Survivor tag vs Edge chevron keeps TagGap | PASS | |
| Test 3: slotted Exit tag lands TagGap from the obstacle | PASS | |
| Test 4: fuzz/invariant over random layouts | PASS | two fuzz tests; the second allows the slotted fallback (consistent with the deviation) |
| Existing assertion changes listed | PASS | TagLayoutQaTests Step/displaced/touch tests, TagLayoutTests message, all listed |
| HUD_SPEC section 6 one-line clearance note | PASS, but incomplete | the note omits the Overloaded/Exit fallback (F-4) |

Code hunt beyond the brief (all clean): `current` is assigned before the `continue`, so it is never stale; `gap` is reset to `TagGap` after the slotted block, so it cannot leak into the next tag (probe C: a Terminal tag placed after a fallen-back Exit keeps 4 u); `Slot`'s `limit` grows with `StepGap` (108 u instead of 100 u for 23 u tags), which is the "same 4-step reach" the brief asks for; `NextTo`'s horizontal cull uses the same `gap` and `Eps` as `Apart`, so the two agree; the glyph-hiding pass is unchanged.

## Findings

### F-1 (minor, DEVIATION) Overloaded/Exit tags fall back to 2 u, then 0 u, instead of being hidden
- Where: `TagLayout.cs:115-119`.
- Brief: "Keep ... Q2's Slot path ... then they are hidden"; "A placed tag keeps a minimum clearance TagGap from every other placed tag".
- Verification of the implementer's claim: I replicated `EdgeSpreadTests.TheCrowdedRightEdgeStaysReadableAt720pAnd1080p` outside Unity (shimmed `EdgePlacement.cs` + the four TagLayout variants, scratch probe) and ran its 31 rows at 1600x900 and 1920x1080:
  - old (main): 0 hidden rows, but the Exit/Overloaded tag **touches** (0 u) in 8/31 rows;
  - new (4/2/0): 0 hidden rows, touches in **2/31** rows;
  - without the 0 u step (4/2): the Exit tag is hidden at dy = -70, -60 (900p) -> the test fails;
  - strict 4 u: hidden at dy = -80/-70/-60 (1080p) -> the test fails.
  So the deviation is indeed required to keep "everything must fit" green; the alternative is to relax that assertion.
- Realistic crowding (random right-edge probe: 2-5 in-view tags, 1-3 chevrons, 1 Exit tag, 20 000 rounds): the Exit tag used the 2 u fallback in 51 rounds, the 0 u fallback in 23, and touched a Survivor tag/chevron in 21 (0.1 %). Survivor-vs-Survivor and Survivor-vs-glyph pairs under 4 u: **0**. The original finding (two Survivor distance tags touching, a Survivor tag against a chevron) cannot recur; only an Exit/Overloaded tag may still touch, rarely.
- Judgement: acceptable as scoped (Survivor information is strict; HUD_SPEC 6.2 wants the Exit marker always shown), provided it is documented (F-4). If the orchestrator prefers the brief's letter, the fix is to delete line 118 and let the crowded test accept a hidden Exit tag in those 2-3 rows.

### F-2 (minor) The larger stacking step and the 4 u rule hide more tags
- Probe (old vs new, same random inputs, in-view Survivor bodies + distance tags, 4 000 rounds each):

| survivors | spread | hidden old | hidden new |
|---|---|---|---|
| 3 | 200 u | 4.0 % | 4.9 % |
| 5 | 200 u | 14.9 % | 17.9 % |
| 5 | 320 u | 8.0 % | 9.7 % |
| 7 | 200 u | 25.2 % | 30.0 % |
| 7 | 320 u | 14.1 % | 17.1 % |

  Relative +20-25 % hidden distance tags in crowds. In the right-edge probe, Survivor tags hidden rose from 207 to 753 per 20 000 rounds (0.3 % -> 1.1 %), mostly from the new chevron avoidance.
- Expected cost of the clearance; no action needed, recorded for the HUD_SPEC trade-off.

### F-3 (minor, latent) EdgeSpread's gap fallback (4 -> 3 -> 2 u) now conflicts with TagLayout's 4 u
- Probe D: two Edge chevrons with beside-tags spaced by the spread at 3 u or 2 u: the second tag is restacked by 27 u (old: stays). The spread accepts `MinGap` 2 u, the layout rejects it, so a full edge would flip tags by a step when the spread shrinks its gap.
- Not reachable with the current limits (7 bots + 3 alerts ~ 321 u of ~672 u on the right edge at 720p); no effect in the run. Worth a follow-up: let Edge-vs-Edge pairs accept the spread's gap, or raise `EdgeSpread.MinGap` to 4.

### F-4 (minor) Documentation does not describe the fallback
- `docs/ui/HUD_SPEC.md` section 6 note (11/10) states a flat 4 u rule; the Overloaded/Exit 2 u / 0 u fallback is only in the class comment.
- The class comment (`TagLayout.cs:35-38`) inserts the fallback sentence before the sentence that introduces the Slot path and is a 200+ character line; reads out of order.

### F-5 (minor, pre-existing visual) 4 u renders as 1-2 visible px at 720p
- `crop-killer-inview-right-x4.png`: two Edge tags ("42 m" / "42 m") placed by EdgeSpread at 4 u show a 1 px background gap (0.8 px/u, anti-aliased plate edges). A stacked TagLayout tag (`crop-killer-crowd-right-x6.png`, "40 m" / "40 m") shows 2 px. No touching, but the margin is thin at 720p. Not this unit's regression (EdgeSpread.Gap); consider 6 u or pixel snapping later.

### Info
- `PlayMenu.cs` working copy is LF; git warns "LF will be replaced by CRLF". Diff content is clean (3 hunks only).
- Stacked tags two steps from their chevron (crowd shot, bottom "40 m") are hard to attribute to a glyph; pre-existing stacking design, not changed here.

## Screenshots (QA run, 1280x720, Killer, 7 bots)

- `docs/qa/minors-1110-screens/qa/killer-crowd.png` + `crop-killer-crowd-right-x6.png`: right edge, diamond + 3 chevrons; tags "39 m" (stacked above), "37 m", "40 m", "40 m" (stacked below); every gap >= 2 px, no touching, no tag on a chevron.
- `docs/qa/minors-1110-screens/qa/killer-inview.png` + `crop-killer-inview-right-x4.png`: in-view body left, diamond + 2 chevrons right, two edge tags at the bottom; no touching.
- `killer-occluded.png`, `killer-offscreen.png`: sparse, clean.
- Implementer's `docs/qa/minors-1110-screens/killer-crowd.png` (crop `crop-impl-killer-crowd-right-x6.png`): 4-tag edge column, 4 u gaps visible.

## SLOP section 9 score (Killer markers)

| Check | Points |
|---|---:|
| No V1-V12 tells (flat plates, no glow, no animation on markers) | 20 / 20 |
| No L1-L8 tells; one spacing scale (4 u gap = edge gap = stack gap) | 13 / 15 (F-5: 4 u reads as 1-2 px at 720p) |
| Copy, fixed vocabulary (`39 m`, `Terminal 7`, `Exit 2 · No power`) | 15 / 15 |
| "What state, what action" | 14 / 15 (two-step stacked tags ambiguous, pre-existing) |
| Role HUD / no leak (only Signal Survivors shown) | 20 / 20 |
| Interactive states (none changed) | 10 / 10 |
| Contrast / readable at 1280x720 | 5 / 5 |
| **Total** | **97 / 100**, no gate failure |

## Unverified
- The exact qa-int collapse scene (38 m / 39 m side by side) was not reproduced live; coverage is by `TwoInViewSurvivorTagsWhosePreferredRectsTouchEndUpTagGapApartOrStacked`, the fuzz tests and the probe (0 Survivor pairs under 4 u in 20 000 rounds).
- Survivor-side play menu not screenshotted (F1 pinned by tests only).

## Orchestrator judgement (Claude Opus, 2026-11-10)

**PASS.** F1 and F2 meet every spec bullet. The F2 Overloaded/Exit fallback (4 → 2 → 0 u) is accepted. It keeps the Q2 crowded-edge rule "everything stays readable", and the probe shows 0 Survivor-vs-Survivor or Survivor-vs-glyph pairs under 4 u, so the original "38 m / 39 m" touch cannot recur.

- **F-4 (docs):** fixed in review. The HUD_SPEC §6 (11/10) note now names the fallback.
- **F-2 (more hidden tags):** the expected cost; no action.
- **F-5 (thin gap at 720p):** pre-existing, no action.
- **F-3 (EdgeSpread 3/2 u vs TagLayout 4 u):** recorded as a follow-up candidate. A full edge is not reachable with 7 Survivors and 3 alerts.
