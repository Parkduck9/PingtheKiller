# U6b implementation

Changed Map/*, BotBrain, GameConstants, client world/camera, tests/harness, and map-v2.svg.
80×80m; 4 borders, 12 buildings, 12 obstacles, two 16×16m plazas, 2 dead ends, 8 terminals, 2 exits, 5 hatch points, 3 survivor clusters.

Cover: 818/2000 (40.90%). Median stretch: 1.0911. Navigation(Release): mean 0.1978ms, diagonal p99 1.9110ms, 0B/query.

Sweep: win%/median[p25,p75]s/rescue%; all timeouts 0%.

| Configuration | Seeds 1–40 | Seeds 1001–1040 |
|---|---|---|
| 1v7 | 40/311[240,387]/37.5 | 47.5/360[274,417]/35 |
| 1v3 | 17.5/239[170,321]/27.5 | 15/237[157,334]/20 |
| 1v1 | 22.5/213[153,320]/0 | 20/271[134,357]/0 |
| Idle+7 | 100/222[215,228]/5 | 100/214[209,225]/2.5 |

1v3 win targets fail; legacy difficulty buckets and targets remain unchanged for U6d.

Acceptance (sandbox SDK/NuGet overrides):

- `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings.
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`: 829/829.
- `dotnet test Server.Tests --filter "Category=BalanceSweep"`: 6/8.
- `dotnet test Tools/Harness.Tests`: 635/635, eight shards plus focused reruns; one jitter retry.
- `dotnet run --project Tools/Harness -- --render-map docs/maps/map-v2.svg`: written.
- `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults docs/qa/unity-editmode-u6b.xml -logFile docs/qa/unity-u6b.log`: blocked by licensing/cache permissions. Client/tests compile; managed geometry 7/7. EditMode unverified.

TCP harness: 100/100, `--expect-los`; H6 visible/blocked=2365/870.
Outside ownership: Server.Core/Runtime/GameServer.Phantoms.cs shares steering to preserve indistinguishability; docs/qa/unity-u6b.log records verification; artifacts/*,Client/Library/* hold validation scratch/cache.
Stubs: none. No commit.

## Attempt 2 (Claude Sonnet, quota fallback: Codex out of credits)

Verdict on the 26 EditMode failures: all were stale fixtures (positions, sizes or wall counts of the 60 m map). No product bug. The three suspected symptoms were fixture positions now inside a wall: SphereCast 0 (target at 22.5,23 is inside Walls[4]), Wander reversing 180 degrees every frame ((30,30) is inside Walls[5], so a wall is always ahead), Killer classes always Occluded (Killer and survivors placed inside building Walls[4], so every ray starts in a wall). Product fixes: none needed (MapGeometry/SharedMap/WorldProps/RoleCamera/Wander unchanged in attempt 2). No Server.Core or Shared edits.

Test changes (Client/Assets/Tests/EditMode/**; no assertion deleted or loosened; tolerances unchanged):
- QA/QaSupport.cs: new internal `KillerRoom` helper. The legacy Killer-view scene (Killer 10,23 looking +x, wall x 18..20 eight metres ahead) is laid on Walls[11] (x 56..66, z 24..34): Killer at (48,29); legacy x<19 -> +38, x>=19 -> +46, z+6; wall faces 18/20 map to building faces 56/66.
- MapQaTests.TheClientDrawsTheServersFixedMapWallForWall: wall count 8 -> 16 (4 borders + 12 buildings), still equal to server wall for wall; added obstacle count 12 and obstacle-for-obstacle comparison.
- MapQaTests.ExitTerminalsSizeAndCentreAreTheServersValues: size 60 -> 80, Exit (58,30) -> Exits[0] from FixedMap plus Exits.Count 2 and ExitAt(1); centre 30 -> 40; 6 hard-coded terminals -> 8, compared with FixedMap.TerminalPositions.
- MapQaTests (new): TheWorldViewAlsoDrawsTheObstaclesAndBothExits; TheKillerRoomFixtureHasTheLegacyWallBetweenTheKillerAndTheSurvivorsBehindIt (checks KillerRoom keeps the legacy relations: standable, in front clear, behind blocked, clear from just past the wall).
- CameraRigQaTests (3 failing tests): wall 18..20/z 14..32 -> Walls[4] (x 12..24); target 22.5,23 -> east face + 2.5 m (x 26.5,z 24), same expected 2.3 m / 2.3/cos20 / dither lengths; "beside the wall's end" z 33 -> Walls[10] end + 1 m. Added a companion assertion that z = end - 1 is shortened (2.3), so the open result is the wall's end and not a missing wall.
- ClassificationHysteresisQaTests (6 failing): Pose/SceneWith go through KillerRoom (Killer 48,29; survivor 15,21 -> 53,27; wall-ray survivor 26,22 -> 72,28 behind Walls[11], clear pose x=22 -> 68).
- EdgePlacementQaTests.InViewAndOccludedInstancesNeverTakeAnEdgeSlotAndFewerThanSixAllShow, KillerAnonymityQaTests (Killer position, Pose, Truth()), KillerFrustumExitQaTests (Killer position, survivors): same legacy numbers via KillerRoom.
- WanderQaTests: (30,30) -> plaza middle (48,20) (clear in every direction); north wall tests (30,59)/(30,56) -> (50,79)/(50,76) using FixedMap.Size; inner-wall case 16.5,20 yaw 90 -> 1.5 m east of Walls[10] facing west (derived from the wall); corners 58.8 -> Size-1.2; same checks. New test TheFixturePointsAreClear... guards the fixture points.
- KillerSceneTests (2 failing + others using the scene): local MapX/MapZ copy of the KillerRoom mapping, Pose, Killer entry, survivors, KillerX/Z asserts moved to it; new TheSceneHasTheWallBetween... guard test.
- SurvivorFeedbackTests.TheFakeKiller...: "wall x 18..20 from x=15" -> Walls[4] west face from 3 m away (x=9); `X <= 18` -> `X <= wall.MinX`.
- QA/Reverify SurvivorKillerRenderingReverifyTests...U2eContract: "10 snapshots, Killer entry required" -> at least 10 snapshots and continue (max 1200) until a Killer entry was checked, because map v2 spawns are out of sight by design (took ~31 s here; risk: slow, depends on the Killer bot reaching sight within 60 s).

Results: EditMode full run 1192 total, 1192 passed, 0 failed, 0 compile errors (1188 + 4 new tests); `docs/qa/u6b-runs/editmode2.xml`/`.log`. `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings, 0 errors. Unity also rewrote DOTweenSettings.asset / ShaderGraphSettings.asset (already modified before this attempt).
