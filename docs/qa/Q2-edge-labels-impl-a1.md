# Q2-edge-labels — implementation report, attempt 1

Implementer: Claude Opus subagent (quota fallback: Codex is out of credits).

**Files.** `Killer/EdgePlacement.cs` (new `EdgeSpread`, `EdgeSide`, `EdgeTagMode`), `Killer/TagLayout.cs` (`Overloaded = 2` priority, Exit 3, Terminal 4; `KeepGlyph`; nearest-free-slot search for Overloaded/Exit tags only). Scope extension: `KillerWorldLayer.cs` (collect edge markers → spread → draw; feed v2 tags into the layout; layout bounds = safe rect), `MarkerLayer.cs` (`External`/`ExternalResult`, `ChevronHalf`), `RulesV2World.cs` (`OverloadedEdge`, `PlaceEdge`, `LayoutInput`, `ApplyLayout`, reserved tag sizes). Tests: new `EdgeSpreadTests.cs`, +1 in `TagLayoutTests.cs`.

**Design.** Per edge: order by ideal coordinate (key tie-break, 6 u hysteresis), isotonic regression (PAVA) in gap-removed coordinates, clamp to the segment, round. Overfull: gap 4→3→2, then drop Overloaded tags farthest-first, then Survivor tags; glyphs are never dropped. All v2 tags join the single `TagLayout` solve.

**Verification.** EditMode r2: 1398/1398 passed, 0 skipped (`q2-runs/editmode-a1-r2.xml`). r1 had 15 art failures because the worktree held Git LFS pointers; `git lfs checkout` from the local store fixed this. BuildWindows: Success (`build-a1.log`). Four offline exe runs (720/1080, killer-look away/nearest, chase).

**Screenshots.** `shots-a1-720-nearest/killer-crowd.png`: three left-edge chevrons, tags apart. `shots-a1-1080-chase/killer-crowd.png`: three 55 m top-edge chevrons spread, no overlap.

**Unverified.** No Overloaded alert fired in any run, so the on-screen alert path is covered only by EditMode tests.

**Risks.** Chevron tags now sit beside the chevron, not toward the screen centre. Corner cross-edge overlaps are left to `TagLayout`. QA random fuzz `rng.Next(4)` no longer generates Terminal.

## Attempt 2

Implementer = Claude Sonnet subagent.
- F-01: `V2MarkerLayer.Tag()` and `PlaceEdge` (RulesV2World.cs) now reserve `Ceil`ed sizes and place via a new `WholeInside` helper: round the origin, then clamp it to `[Ceil(min), Floor(max - size)]` so the whole reserved rect stays in the safe area; label position equals the reserved origin. `TagLayout.Free` untouched.
- F-02: `TagLayoutQaTests.cs` L311/L428 `rng.Next(5)`.
- F-04: `TagLayout.Compare` compares `(byte)` priorities (no boxing).
- EditMode: 1406 total, 1406 passed, 0 failed (incl. all 8 `EdgeLabelsWiringQaTests`): `q2-runs/editmode-a2.xml`.
- BuildWindows: success: `q2-runs/build-a2.log`.
- Unverified: the measured-size branch in a live panel (no screenshot run).
