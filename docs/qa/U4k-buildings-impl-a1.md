# U4k-buildings — implementation report, attempt 1 (Claude Opus)

**Groups (geometry-keyed, union-find over shared edges; borders 0-3 excluded):** {4,5} {6,7,8} {9} {10,11} {12} {13,14} {15} {16,17} {18,19} {20,21} {22} {23,24,25} {26,27} {28} {29,30}. There are 12 shared edges.

**Spec**
- **S1:** Each box is inset by `Setback` and extended by `Setback` across every side it shares completely. The resulting wings tile the group outline inset by 1 m, and any cell they miss becomes an extra wing (none on this map). The setup throws if a wing grown by 1 m leaves the group's union. Wings are generated `Storeys`, clipped exactly to each box's prefab by a new `MeshBuilder` clip. Heights step between 2 and 3 storeys (8.3 / 10.8 m). A face is skipped where an equal or taller wing hides it. There are no podium parapets on shared spans.
- **S2:** All 27 interior boxes get uppers. The 5 single-box groups get fitted City 03 houses: auto yaw, height 7–10.6 m, and never the same model as a neighbour. Group colours are greedy-coloured against groups closer than 10 m. Facades rotate through 3 variants. There are roof stair houses and AC units, and a billboard on every third group.
- **S3:** A continuous generated terrace (1–2 storeys) fills the strip from 0.05 m to 2.5 m outside the border walls on all four sides, with south and north spanning the corners. Skyline houses stand shoulder to shoulder, and generated filler blocks take the leftover lengths. Backdrop shadows are off.
- **S4/S5:** Unchanged except as listed under deviations.

**Tests:** `WorldArtBuildingTests`: TheGroupsOfTheCurrentMapAreTheExpectedOnes, EveryInteriorWallHasAnUpperReachingSixToElevenMetres, UpperStoreysStandBackFromTheGroupOutline… (per vertex, Euclidean), EverySharedEdgeIsClosedByUpperGeometry (5 rays per sample), TheSkylineHasNoGapOnAnySideAndClosesTheStripBehindTheBorderWalls. `WorldArtTests`: the per-box setback test is now `…FromTheOuterBoxEdges…` and skips shared sides.

**Gates:**
- EditMode: 1391/1391 passed.
- BuildWindows: Success.
- `dotnet build -warnaserror`: 0 warnings, 0 errors.
- Offline exe, Killer and Survivor, 90 s each: exit 0, no errors.
- Rerunning the setup is idempotent in content.

**Counts (walls + backdrop):**
| | Triangles | Renderers |
|---|---|---|
| Before | 1,712,674 | 69 |
| After | 1,570,550 | 127 |

Walls went from 501,684 to 289,904 triangles.

**Screens:**
- `docs/qa/U4k-screens/before/` and `after/`: overview ×2, top-down, 5 street views.
- `after/exe-*`: offline shots.

**Deviations:**
- Test asmdef now references `PingTheKiller.Editor.Setup`.
- Podium colour is per group.
- Orphan meshes `Building 9/15 storeys` deleted.

**Unverified:**
- The dev log has no fps or frame time.
- Not every street was viewed.

**Risks:**
- Rerunning the setup churns fileIDs in prefabs that nest model instances. This is pre-existing; I reverted the unchanged Obstacle and Terminal prefabs.
- The terrace hides the lower floors of the skyline houses.
