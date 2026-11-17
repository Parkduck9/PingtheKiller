# U4l-gapfill — QA report, attempt 1

- **Reviewer:** Fable (QA only; no product code edited). **Implementer:** Claude Opus.
- **Worktree:** `PtK-work/u4l-gapfill`, branch `claude/u4l-gapfill`, uncommitted on main 3675877.
- **Verdict: PASS** — no blocker, no major. Six minor findings (F1–F6) and two notes.

## Gates (run by QA)

| Gate | Result | Evidence |
|---|---|---|
| Unity EditMode | **1492/1492 passed** = the unit's 1491 + 1 temporary QA probe (since removed). `WorldArtGapTests` 5/5. | `docs/qa/U4l-runs/qa/editmode.xml`, `.log` |
| Gap scan re-run (full density, label `qa`) | 0 see-through / 252,400 rays; upper 0/2504; sky 0 gaps, 5 notches / 37,128; fill 1.000 / 1.000; overshoot 0.000. Identical to `U4l-gaps-after.md`. | `docs/qa/U4l-gaps-qa.md`, `U4l-runs/qa/scan-qa.log` |
| BuildWindows | **Success** | `U4l-runs/qa/build.log` line 6853 |
| `dotnet build PingTheKiller.sln -warnaserror` | 0 warnings, 0 errors | console |
| Live exe, Survivor, offline, 7 bots, 120 s, 1280x720 (+ `-ptk-wander 110`) | exit 0; 0 exceptions, 0 error lines; 9 shots | `U4l-runs/qa/exe-survivor.log`, `U4l-screens/qa/live/` |

After the Unity runs, 19 files (Characters, Resources, Settings, ProjectSettings) differed only in line endings (`git diff --ignore-cr-at-eol` empty). QA reverted them.

## Spec table

| Spec | Result | Note |
|---|---|---|
| S1 detector: eye points, ray fan, heights, see-through rule | PASS | Matches the brief. The three later rule changes are judged below. |
| S1 also: fill ratios, podium, uppers | PASS | Upper check is perpendicular rays only, at 4 m and 5.5 m. |
| S1 output: md + json + cluster shots + 6 sweeps | PASS | 71 views before and after, same `views.txt`. All 40 cluster shots and all 6 sweeps are obstacles; no house or skyline shot (QA added them). |
| S2.1 wall boxes solid 0–3 m | PASS | Podiums unchanged; independent scan finds no ray that exits a wall box at any incidence up to 85°. |
| S2.2 obstacles ≥ 95 % per axis, ≤ box + 0.05 m, ≤ 1.5 m | PASS | Fill 1.000, overshoot 0.000, tops 1.29–1.49 m. See F2, F5. |
| S2.3 no sky at eye level behind the border walls | PASS | 0 columns with open sky at pitch ≤ 8°; the remaining notches are roofline steps at 15–19° (shots). |
| S2.4 existing rules kept | PASS | `WorldArt.Matches` asserted in tests; existing WorldArt tests green and unchanged; no diff in `Server*`, `Shared`, `Tools`, `Client/Assets/Scripts`, `Resources`. Idempotent setup **not re-verified** (note N1). |
| S2.5 re-run: 0 hits, after shots at the same views | PASS | Reproduced. |
| S3 counts reported, increase explained | PASS | See Performance. |
| Tests: reduced-density regression tests | PASS | Not tautological (mutation runs below); sensitivity gaps in F4. |
| Report ≤ 300 words, commit message | PASS | One inaccurate statement (F1). |

## 1. Detector honesty

QA wrote an independent re-implementation of the see-through rule **without** the 60° incidence filter, with a ±85° fan (353,360 rays, same eye points), and ran it on the after catalog. Source: `docs/qa/U4l-runs/qa/U4lQaProbe.cs.txt`; output: `U4l-runs/qa/probe.txt`.

| Incidence on the entered face | Walls (> 0.25 m inside) | Obstacles (> 0.25 m inside) |
|---|---|---|
| ≤ 60.5° (what the shipped scan counts) | 0 | 0 |
| 60.5–75.5° | 0 | 11 |
| > 75.5° | 2391 | 25 |

- **60° filter: hides nothing real.** Every wall ray it drops travels exactly 0.459 m = 0.04 m recess / cos 85°, and none exits its box. The obstacle rays travel at most 1.147 m = 0.10 m core inset / cos 85°, and none exits. These are grazing paths along a 4 cm recess or the 10 cm seam, not views to the other side.
- **Sky rule "only hits beyond the border walls": no effect on this catalog.** QA counted both rules over 1690 columns: 0 gaps and 1 notch under either rule.
- **12° cap: a relabel, and it should be read as one.** `scan-after-1.log` (after the backing row was added) still reported 5 sky gaps; the final scan reports 0 gaps and 5 notches. The art did not change between them for the sky; the rule did. The shots (`U4l-screens/qa2/notch-W-*.png`, `horizon-*.png`, `qa/sky-S-54-20-yaw-10.png`) show a continuous building wall with roofs of different heights. That is a skyline, not a see-through gap. Accepted.
- **Upper rule "hit art before leaving the group union":** correct for straight-on views. It does not test diagonal views or gaps narrower than 0.5 m. No gap seen in the house shots.

## 2. Visual

Shots: `docs/qa/U4l-screens/qa/` (100 batch views at 1.7 m eye height: every obstacle from two corners and low along its long face, every fitted house at 5 m, 9 m and oblique, the border walls, the grazing worst cases) and `qa2/` (skyline). Before/after pairs compared: `sweep-1-obstacle4-W-3`, `sweep-5-obstacle0-E-2`, clusters for obstacles 3 and 6.

- No see-through found in any shot. The space under and over the cars is gone.
- Containers read as ribbed steel containers with rails and corner posts. Crate stacks read as boxes. Not grey boxes.
- The live run cannot do a guided left/right sweep without input (capture moments are fixed), so the close-range sweep is the batch renders.

## Findings

**F1 — minor — the fitted house is now mostly the core block, not the house model.**
- Repro: `probe.txt` section 2, or look at `qa/house9-*`, `house15-E-9m`, `house22-S-5m`, `house22-W-oblique`, `house28-S-9m`.
- Expected (impl report and code comment): "the house's walls hide it"; the core is seen only through notches.
- Actual: from street eye points the core is the first surface hit above the podium in 71–88 % of rays for walls 9, 15, 22 and 28 (wall 9: core 1168 rays, house model 4). Only wall 12's west face is mainly the model (26 % core). The model shows as fragments through the core: a row of small arch ornaments on wall 15, a bay and balcony on wall 22, a balcony on wall 28, thin slivers above a window on wall 22 south.
- Impact: looks like a normal generated block with some odd attachments; no see-through, no z-fighting seen. The U4k house variety is mostly lost, and the hidden model triangles are still drawn.
- Suggested: for those four, either drop the model or set the core at the model's real wall plane.

**F2 — minor — dumpster rows look rough.**
- Repro: `qa/obstacle0-corner0.png`, `obstacle9-corner1.png`, `after/cluster-obstacle6-S-1.0-44.0.png`.
- Actual: the model is stretched up to about 4 x 1 m (obstacle 6); the flat black core top (1.15 m) shows as a slab between the lids; the dark corner posts stand proud of the rounded corners.
- Expected: reads as dumpsters. It does at a glance, but it is the weakest replacement.

**F3 — minor — the dark core is visible in the seams.**
- With a 0.10 m limit instead of 0.25 m, 1001 obstacle rays (≤ 60°) travel 0.10–0.25 m before art. The player sees the near-black core between props. Solid, so not a see-through; cosmetic.

**F4 — minor — test sensitivity gaps.** Mutations against `Scan(Density.Reduced)`, which is what `WorldArtGapTests` asserts (`probe.txt` section 5):

| Mutation | Reduced scan result | Test fails? |
|---|---|---|
| Obstacle 4 emptied | 120 see-through, fill < 0 | yes |
| Core removed from dumpster props 0, 2, 6, 9, 10, 12 | 9–12 see-through each | yes |
| Core removed from crate stacks 8, 11, 14 | 0 | no |
| Upper core removed, walls 9 / 15 / 22 / 28 | 7 / 12 / 4 / 16 upper gaps | yes |
| Upper core removed, wall 12 | 0 | no |
| Backing row removed | 1 sky gap (full density: 2) | yes, by one column |

The tests are not tautological. The crate cores and wall 12's core are either unnecessary or untested, and the sky test hangs on a single column.

**F5 — minor — invisible band over the low crates.** Crates are 1.05–1.25 m; collision is 1.2 m. Over the 1.05 m and 1.1 m crates there is up to 0.15 m of collision with no art. The fill ratio does not see it because it uses the bounds' top. Hardly noticeable.

**F6 — minor — sky wording.** "Sky gaps 8 → 0" is true only with the 12° cap; 5 of the 8 remain as notches (see section 1). The impl report does list them.

**Notes**
- N1: idempotent setup was not re-run by QA (a rerun churns fileIDs in product assets, which QA must not edit), and no test asserts it.
- N2: the live run with `-ptk-wander` produced only the fixed capture moments; HUD and world look normal in `qa/live/survivor-3p.png` and `play-4s.png`.

## 3. Rules kept

- Art inside the boxes: obstacle overshoot 0.000 m; `EveryWallVisualFillsItsBoxFootprintWithinFiveCentimetres` green.
- Obstacles ≤ 1.5 m: tops 1.29–1.49 m.
- Podium fill 1.000 on all 31 walls.
- `git diff --stat`: 21 prefabs under `Art/World/Prefabs` and `WorldArtSetup.cs` only; new meshes, materials, `WorldArtGapScan.cs`, `WorldArtGapTests.cs`. Nothing in Server, Server.Core, Shared, Tools or client scripts.

## 4. Performance

| | Before (tris / renderers) | After | Change |
|---|---|---|---|
| Walls | 289,904 / 60 | 294,266 / 65 | +1.5 % / +5 |
| Backdrop | 1,280,646 / 67 | 1,301,878 / 71 | +1.7 % / +4 |
| Obstacles | 52,826 / 43 | 41,612 / 70 | −21 % / +27 (+63 %) |
| Total | 1,623,376 / 170 | 1,637,756 / 206 | +0.9 % / +21 % |

Acceptable. The +27 obstacle renderers are small static opaque meshes that share a few materials, and obstacle triangles went down. The whole scene gains 36 renderers and under 1 % triangles. No frame-time measurement was made.

## 5. SLOP §9 score

The unit changes no UI, HUD, menu or copy; the live shots show the HUD as on main.

| Check | Points |
|---|---|
| No V1–V12 tells (gates V1, V2, V4, V8, V10 clear) | 20 / 20 |
| Layout | 15 / 15 |
| Copy | 15 / 15 |
| State and action | 15 / 15 |
| Role HUDs, no leak | 20 / 20 |
| Interactive states | 10 / 10 |
| Contrast, readable at 1280x720 | 5 / 5 |
| **Total** | **100 / 100, no gate failure** (unchanged UI) |

World art, outside §9: no placeholder or grey-box look; F1 and F2 are the rough spots.

## Do not commit

- `docs/qa/U4l-runs/` entirely (8.3 MB of logs: `editmode.log` 3.3 MB, `editmode.xml` 1.2 MB, `build.log`, setup and scan logs, `shots-survivor/`, and QA's `qa/` 5 MB).
- `docs/qa/U4l-screens/qa/` (6.9 MB) and `qa2/` — QA evidence only.
- `docs/qa/U4l-gaps-qa.md` / `.json` — QA's duplicate of the after scan.
- `docs/qa/U4l-screens/before/` (5.1 MB) and `after/` (3.9 MB): keep out unless the screenshots are wanted in history; the `.md` reports reference them by name.
- Commit: the code, the regenerated `Art/World/**`, `U4l-gaps-before/after.md` + `.json`, the brief, the impl and QA reports.

## Orchestrator judgement (Claude Opus, 2026-11-17)

**PASS.** QA's independent scan, with no 60° filter and a ±85° fan, confirms 0 see-through rays, and no rule hides a real gap. The minors are recorded as follow-ups. None of them breaks the user goal (no see-through up close, art fills its boxes):
- **F1:** the house cores dominate the façades.
- **F2:** the dumpster rows look rough.
- **F5:** a 0.15 m band over the low crates has no art.

Large run and shot folders are left uncommitted, as QA advised.
