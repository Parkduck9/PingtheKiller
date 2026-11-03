# Brief U6e — attempt 1: Map layout v2.1 (denser city, DbD-style loops)

Implementer: Claude Opus subagent (quota fallback: Codex is out of credits). QA: Gemini Flash. These units run in parallel in other worktrees: U6a (rules v2), U4h (world art), U6c-ui, U7a.

Read first:
- `AGENTS.md`
- `docs/GDD-v2.md` §5
- `docs/briefs/U6b-a1.md` (the map v2 spec and its validation tests a–h)
- `docs/maps/map-v2.svg`

Do **not** commit.

## Goal

Map v2 (U6b) works, but the user and the orchestrator find it too simple:
- wide boulevards and large empty fields;
- repeated rectangles;
- almost no DbD-style **loops**, i.e. a building or block that a chased survivor can run around to break line of sight.

Re-author the layout **data** only. Everything else already derives from `FixedMap`: NavGrid, line of sight, client drawing, the world art (U4h fits art to each box at runtime) and the harness.

## Owned files

- `Server.Core/Runtime/Map/FixedMap.cs`: **layout data only**, meaning the arrays of walls, obstacles, terminals, exits, hatch points and spawns, plus any small private helper that builds them.
  - Do not change types, methods or signatures.
  - U6a edits this same file in parallel. It adds an `ExitSwitches` list (computed 4 m from each exit toward the map centre) and moves a hatch point. Keep your diff confined to the data so the merge is easy.
- `Server.Tests/Core/MapV2Tests.cs`: add tests; keep every existing assertion.
- `Server.Tests/QA/U6b/U6bQaTests.cs`: change only position-specific fixtures, and keep their intent.
- Any `Server.Tests`, `Tools/Harness*` or `Client/Assets/Tests/EditMode/**` test that hard-codes v2 coordinates. Prefer deriving positions from `FixedMap` data.
- `docs/maps/map-v21.svg`, generated with `dotnet run --project Tools/Harness -- --render-map docs/maps/map-v21.svg`.
- `docs/qa/U6e-impl-a1.md` (report).

## Layout requirements

These are in addition to U6b validation tests a–h, which must still pass.

1. **Same frame.**
   - 80 × 80 with 4 border walls.
   - 8 terminals, spread the same way as before (first-N spread).
   - 2 exits on opposite edges.
   - 5–6 hatch points, each ≥ 6 m from every exit and from the point 4 m from that exit toward the centre.
   - Killer spawn near the centre. 7 survivor spawns in 2–3 clusters, each > 20 m from the killer and with line of sight blocked.
2. **Buildings.** 18–28 building boxes.
   - Build L, T and U shapes from touching boxes.
   - Vary the sizes: at least 4 distinct footprints, and none repeated more than 3 times.
3. **Loops: at least 6.**
   - A loop is a building group with a footprint ≤ 14 × 14 m and a continuous walkable ring ≥ 2.5 m wide all around it.
   - Add a test that finds loops from the data, for example by flood-filling the ring cells of the inflated NavGrid around each building cluster.
   - Spread them: at least 2 in each half of the map (west/east and north/south).
4. **No big empty fields.**
   - Outside at most 2 plazas (each ≤ 18 × 18 m), no walkable point may be more than **8 m** from the nearest wall or obstacle box.
   - Test this on a 1 m grid.
5. **Cover.** The blocked share in validation (f) must be **50–75 %**. It was 40.9 %.
6. **Alleys.** Alleys are 2.5–4 m wide (test b). Keep 2–4 dead ends.
7. **Navigation.** Path stretch (test h) median ≤ 1.6. The NavGrid query budget is the same as in U6b.
8. **Terminals.**
   - Every terminal has a wall within 6 m.
   - At least 2 terminals sit in exposed spots (a plaza or a wide street), so hacking risk varies.
9. **Obstacles.** 14–24 obstacle boxes, used at loop corners and street mouths, the way DbD places junk piles.

## Acceptance

- `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings.
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`: green.
- `dotnet test Tools/Harness.Tests`: green.
- Unity EditMode: all green. Run from the worktree root:
  `"/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults "$PWD/docs/qa/u6e-runs/editmode.xml" -logFile docs/qa/u6e-runs/editmode.log`
  The first run imports assets for a long time.
- Harness: score 100 with `--expect-los`, using fresh servers (see `docs/PLAN.md` §5).
- `BalanceSweep`: report the numbers only; do not tune.
- Render the SVG and **look at it**.
  - To view it, serve the folder with `python -m http.server` on 127.0.0.1 and screenshot it with any tool you have, or reason about the layout from the data.
  - Describe the layout in words in the report.

## Report

`docs/qa/U6e-impl-a1.md`, ≤ 300 words. Start with the header note "quota fallback: Codex out of credits". Include:
- counts;
- the loops found, with their positions;
- cover %;
- stretch;
- the largest empty-circle radius;
- sweep numbers;
- every test you changed, as old check → new check.
