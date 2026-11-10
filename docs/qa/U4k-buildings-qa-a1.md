# U4k-buildings — QA report, attempt 1 (Fable)

**Verdict: PASS.** No blocker or major finding. QA edited no product code and added no tests.

## Gates (independent re-run)

- EditMode: **1391 total, 1391 passed, 0 failed** (`docs/qa/U4k-runs/qa-editmode.xml`, `.log`); all 5 `WorldArtBuildingTests` and 19 `WorldArtTests` pass. (A relative `-testResults` path resolves against `-projectPath`; XML moved.)
- Setup re-run (`qa-setup-rerun.log`): same counts. Generated meshes, Building 4–30 prefabs and `WorldArt.asset` are content-identical.
- Exe not rebuilt. Offline logs: 0 errors, no fps line.
- Line-ending and ID churn from my runs reverted; worktree equals the implementer's state.

## Spec

| Item | Result | Note |
|---|---|---|
| S1 groups | PASS | Union-find over shared edges, no indices; 15 groups / 12 edges match my hand computation. Wings tile the inset outline; shared edges closed. |
| S2 uppers on 4–30 | PASS | 8.3/10.8 m wings or 9–10.6 m fitted houses; geometry-keyed; test pins the group list. |
| S3 skyline | PASS | Continuous terrace 0.05–2.5 m outside the border walls; houses abut, fillers take leftovers; shadows off. |
| S4 rules | PASS | Rule tests green. Idempotent in content; fileID churn in Backdrop, Obstacle 8/9/11/12/14, Terminal prefabs (pre-existing, disclosed). |
| S5 perf | PASS | Triangles 1.71 M → 1.57 M (−8 %), renderers 69 → 127. Backdrop 1.28 M tris is pre-existing. |

Tests: Uppers, Outer setback (per vertex, Euclidean), Shared edges (5-ray enclosure, fails on a slot), Grouping, Skyline: all PASS. Existing setback test renamed and skips shared sides: DEVIATION, acceptable.

## Findings

1. **Minor — parapet lip across equal-height roof joins.** `PlanWings` drops a parapet only when neighbours cover the face *fully*; with partial coverage (wing 8 south in {6,7,8}, wing 23 east in {23,24,25}) a 0.2 × 0.3 m ridge runs across the neighbour's same-height roof. Expected: no parapet on the covered span. Evidence: `after/top-down.png` ≈ (350–390, 740–760) and (330, 160–220). Rooftop views only.
2. **Minor — rerun dirties the tree** (CRLF→LF rewrites, fileID churn in prefabs nesting models). Content identical; pre-existing.
3. **Minor — skyline test is satisfied by the terrace alone**; notches between house tops above it are untested (cosmetic).

## Visual review

Before: flat 3 m blocks in the centre, 2 m slots over shared edges, sky between skyline houses and above the border wall. After: every block has 2–3 storeys, L/U/T groups read as single stepped buildings, the terrace closes the border strip on all sides and corners, podium roofs join without ridges. No z-fighting, floating or clipping pieces, or stretched houses seen. Coherent night city.

**Score: 86/100.** Worst spots: `after/street-edge-w.png` (90–330, 230–330) terrace reads as flat colour strips, sky notches between house tops behind; `after/top-down.png` parapet lips (above); `after/overview-ne-720.png` (1000–1130, 130–330) a 6-storey skyline house dwarfs the 1-storey terrace.

## Orchestrator judgement (Opus)

PASS. S1-S5 and all test bullets PASS; EditMode 1391/1391 re-run independently by QA; BuildWindows and the offline exe runs are the implementer's. I compared the before/after overview, the south U street and the exe Killer shot myself: the podium-only blocks and shared-edge slots are gone. The three minors (parapet lip across partially covered equal-height joins, rerun fileID churn which predates this unit, the skyline test being satisfied by the terrace alone) are accepted as follow-ups, not blockers. Not committed: editmode-*.log / qa-editmode.log (3 MB each), editmode-1.xml and the shots-* folders (the relevant shots are copied in U4k-screens/after).
