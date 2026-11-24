# U6e implementation report, attempt 1 (status: done after ruling follow-up)

quota fallback: Codex out of credits. Implementers: Claude Opus subagents (layout, then fixes and retargeting).

**Layout:** SW exit at the end of the open 12 m west road (orchestrator ruling). Three bands of L, T and U blocks and single-box loops, with two 4 m dead ends and two plazas. Only the east street and north edge street give long sightlines.

| Item | Value |
|---|---|
| Boxes / obstacles / plazas | 27 / 15 / 2 (16×16) |
| Footprints | 19 distinct, max 3 repeats |
| Loops | 12 (W5 E7 S6 N6): (12,8)-(20,21), (43,4)-(52,12), (57,4)-(71,18), (12,24)-(18,37), (21,24)-(35,37), (42,16)-(53,30), (24,40)-(37,54), (47,40)-(56,54), (4,61)-(13,72), (38,58)-(52,72), (56,59)-(63,70), (67,59)-(76,71) |
| Cover / stretch / largest empty radius | 57.3 % / 1.083 / 6.0 m |
| Sweep win %, median s (core; QA) | 1v1 5, 208; 3, 182 · 1v3 5, 275; 13, 299 · 1v7 33, 377; 45, 341 |

**Phantoms:** White Noise points start 0–4 m along NavGrid routes between landmarks (cached), not straight segments. Terminal points: 1–2 → 0–2 per cast. KS p=0.996. New test `WhiteNoiseRouteTests`.

**Verified:** build 0 warnings; Server.Tests 846/846 ×3; EditMode 1220/1220; harness 100/100 ×3 (`--expect-los`, fresh servers); Harness.Tests 635/635 (after the follow-up).

**Was blocked (resolved below):** `U3dH6ExpectLosNeedsBlockedAndAbsentTests` (2). The mutation marks every blocked world Eliminated. On this map the killer's approach to A is occluded, so H6 sees A "Eliminated" before capture. No contract-correct mutation gives zero blocked ticks here.

**Alloc check:** flaky on main 3e51e24, 1 of 8 full runs (6576 B). It already counted per thread, so it now takes the minimum of 3 windows.

**Changed checks (old → new):**
- Walls 10–14 → 18–28; obstacles 8–16 → 14–24; cover .35–.75 → .50–.75.
- `KillerViewTests` phantom seeds: added 170 (4 seeds cast only 9 skills).
- Fixtures moved from wall (12,12)-(24,36) to (12,24)-(18,37) (or Walls[12]/[16]) in: LineOfSight, U3dGeometry, Planner, LobbyServer, ScenarioServer, KillerRoom, CameraRig, SurvivorFeedback, Wander. Earlier server fixtures: see `git diff`.

**Ruling follow-up:** the H6 `--expect-los` gate is private to the harness and the test project has no internals access, so both `U3dH6ExpectLosNeedsBlockedAndAbsentTests` keep the full run. The new `ScenarioServer.OpenRoad` puts the killer, A and the terminals in the wall-free rectangle at the exit corner (bounds derived from FixedMap), and the mutation now hits only B's blocked worlds. Harness.Tests: 635/635.
