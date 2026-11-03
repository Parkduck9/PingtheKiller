# Brief U6b — attempt 1: Map v2 (80 × 80 city blocks) + bot navigation

Implementer: Codex `gpt-6.1-sol`, effort high. QA: Antigravity Gemini Flash.

Read first: `AGENTS.md`, then `docs/GDD-v2.md` §5 and §6 ("공통 길찾기"). The GDD is in Korean; the spec below is the binding English summary.

Do **not** commit. Work only in this worktree.

## Goal

Replace the 60 × 60 four-wall arena with a hand-authored 80 × 80 m city-block map, and give the existing bots path-finding so they can play it.

- This unit does **not** change game rules. Rules v2 (generators, two working exits, collapse, hatch) is the next unit, U6a.
- Keep the current rules working on the new map:
  - The single-exit rule uses `Exits[0]`.
  - Terminal counts follow the new table (spec 6).

## Owned files

**Server.Core**
- `Server.Core/Runtime/Map/**`: `FixedMap.cs`, plus new files `Obstacle`, `NavGrid` and similar.
- `Server.Core/Runtime/BotBrain.cs`, `BotViews.cs`, `SurvivorBrain.cs`, `KillerBrain.cs`, `GameServer.Bots.cs`. Steering only: replace straight-line steering with path following. Keep the decisions as they are.
- Any other Server.Core file that hard-codes the old map size or exit. List each one in the report.

**Shared**
- `Shared/Runtime/Game/GameConstants.cs`: only `TerminalsFor` and map-size constants.

**Client** (only what is needed so the new map renders and plays)
- `Client/Assets/Scripts/World/SharedMap.cs`, `WorldProps.cs`, `MapGeometry.cs`.
- Camera/rig files that hard-code a 60 m size.
- `Client/Assets/Scripts/Dev/Wander.cs`.

**Tests and harness**
- `Server.Tests/**`, `Tools/Harness/**`, `Tools/Harness.Tests/**`.
- `Client/Assets/Tests/EditMode/**`: tests that hard-code old map positions, sizes or wall counts.
- New: `Tools/Harness/MapSvg.cs` plus a `--render-map <path.svg>` flag.

**Generated and docs**
- Unity-generated `.meta` files for new files under `Server.Core/` and `Client/`.
- `docs/maps/map-v2.svg` (generated) and `docs/qa/U6b-impl-a1.md` (report).

## Spec

### 1. Size and wall model
`FixedMap.Size = 80`. There are two blocker kinds:

| Kind | Blocks movement (`CanStand`, `Move`) | Blocks line of sight (`SegmentBlocked`) | Height |
|---|---|---|---|
| `Walls` (buildings and border) | yes | yes | 3 m (`MapGeometry.WallHeight`) |
| `Obstacles` (new) | yes | **no** | 1.2 m (cars, fences, containers) |

Both are axis-aligned boxes with the same closed-box semantics as today's `Wall`. Keep `SegmentBlocked` allocation-free.

### 2. Layout (hand-authored constant data, deterministic, no randomness)
- 4 border walls enclosing [0, 80]².
- 10–14 building blocks: rectangles, each at least 6 × 6 m.
- Alleys between buildings, 2.5–4 m wide. Include 2–3 dead ends.
- 2–3 open plazas, each at least 14 × 14 m with no walls.
- 8–16 low obstacles, placed in plazas and alley mouths.
- Every walkable region must be reachable from every other (see test a).

### 3. Points of interest
- **Terminals: exactly 8 positions.** Mix them across plazas (2–3), alleys and building edges. Order the list so that the first 5 and the first 7 are also well spread. `CreateTerminals` takes the first N.
- **Exits: 2**, on opposite edges, each a 2 m-radius point at least 2 m inside the border.
  - `Exits` is `IReadOnlyList<MapPoint>`.
  - Keep `Exit` as `Exits[0]` until U6a.
- **Hatch candidates:** 4–6 points (`HatchPoints`). Data only; U6a uses them.
- **Spawns:** the killer is near the centre. The 7 survivors are in 2–3 clusters. Every survivor spawn is more than 20 m from the killer spawn and has a wall blocking line of sight to it.

### 4. Navigation
- Add `NavGrid` in `Server.Core/Runtime/Map/`: a 0.5 m grid, with walls and obstacles inflated by `PlayerRadius` + 0.05.
- 8-neighbour A* with no corner cutting, plus string-pulling/smoothing so paths do not zig-zag.
- Build the grid once per map instance and share it read-only.
- Allocation-free per query after warm-up (reuse buffers; the game loop is single-threaded).
- Each query must take at most 2 ms at the 99th percentile on this machine for the longest diagonal path. Add a test that checks this budget loosely: no more than 5 ms on average over 200 random queries.
- Bots follow paths:
  - Replan when the goal moves more than 2 m, or every 1 s, whichever comes first.
  - Unstuck: if a bot has not progressed for 1.5 s, replan from its current cell.
- Keep the fair-information rule: navigation uses only the static map.

### 5. Client
- `WorldProps` draws obstacles as 1.2 m boxes with the existing neutral material (art comes later, in U4h).
- Draw both exit rings. The second exit ring always looks "locked/muted" until U6a.
- Camera, fog, far plane, minimap and edge-marker math must use `FixedMap.Size`, not 60.

### 6. Terminal table
Change `GameConstants.TerminalsFor`:

| Survivors | Required | Placed |
|---|---|---|
| 1–2 | 3 | 5 |
| 3–5 | 4 | 7 |
| 6–7 | 5 | 8 |

Hack time stays 10 s in this unit.

### 7. Render
`dotnet run --project Tools/Harness -- --render-map docs/maps/map-v2.svg` writes a top-down SVG at 10 px/m showing:
- walls (dark), obstacles (mid grey), terminals with their index, exits, hatch points, and spawns (killer red, survivors teal);
- a 10 m grid.

### 8. Harness
- H1–H6 must still score 100 with `--expect-los`.
- Update any scenario that uses old coordinates.
- H6 line-of-sight sampling must still have a meaningful number of blocked samples. Report the visible / blocked counts.

## Map validation tests (new `Server.Tests/Core/MapV2Tests.cs`)

- **a.** Flood fill over NavGrid: one connected walkable component. All terminals, exits, hatch points and spawns are standable and inside it.
- **b.** Minimum clear gap between any two blockers that form an alley is at least 2.5 m. A gap of 0 is allowed only when two blockers touch to form one building.
- **c.** Terminals are at least 14 m apart pairwise. Each terminal is at least 10 m from each exit. The two exits are at least 60 m apart.
- **d.** Terminal 1.5 m radius and exit 2 m radius circles are fully standable.
- **e.** Spawn rules from spec 3.
- **f.** Cover: across 2,000 deterministic random standable point pairs 10–25 m apart, between 35 % and 75 % are line-of-sight blocked. Report the exact value.
- **g.** Obstacles never block line of sight. Walls do.
- **h.** NavGrid path length is at most 1.6 × the straight-line distance for the median of 200 random pairs. Report the value.

## Balance sweep

`BotBalanceTests` (trait `BalanceSweep`) still runs.
- Report the new numbers for every configuration.
- If a target fails **only** because the map grew (for example, a longer median), do not tune bot decisions. Report it; U6d re-tunes for v2 targets.
- Do not edit the target table yourself.

## Acceptance (paste summary lines in the report)

```
dotnet build PingTheKiller.sln -warnaserror
dotnet test Server.Tests --filter "Category!=BalanceSweep"
dotnet test Server.Tests --filter "Category=BalanceSweep"
dotnet test Tools/Harness.Tests
dotnet run --project Tools/Harness -- --render-map docs/maps/map-v2.svg
"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults docs/qa/unity-editmode-u6b.xml -logFile docs/qa/unity-u6b.log
```

- Build: 0 warnings.
- All non-sweep tests green.
- Unity EditMode: all green, 0 compile errors.

For the harness, start a sandbox server and a normal server on free ports, then run:

```
dotnet artifacts/bin/Harness/release/Harness.dll --host 127.0.0.1 --port P --h6-port P+1 --threshold 100 --expect-los
```

The score must be 100.

## Report

`docs/qa/U6b-impl-a1.md`, English, ≤ 300 words:
- the layout summary (counts, plaza sizes);
- validation values (f) and (h);
- NavGrid timing;
- sweep numbers;
- harness score;
- the files touched outside the owned list, and why;
- anything stubbed.
