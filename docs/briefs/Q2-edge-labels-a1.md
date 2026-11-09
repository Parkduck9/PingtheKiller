# Brief Q2-edge-labels: Killer edge labels never overlap (attempt 1)

- **Implementer:** Claude Opus subagent (quota fallback: Codex is out of credits).
- **QA:** Claude Sonnet subagent (quota fallback: Antigravity Gemini Flash sign-in expired).
- **Worktree:** `C:\Unity\MultiPlayGame\PingtheKiller\.claude\worktrees\objective-borg-dc2fe1`, branch `claude/objective-borg-dc2fe1`, base main `53f4ef9`.
- Do not commit, push or merge. Never send keyboard/mouse input to the desktop (launching the exe with flags is fine). Do not kill processes you did not start (Unity Hub, the user's `C:\Unity\CsharpPrac` editors). No downloads.
- Other sessions run in parallel in other worktrees (Q1 copy files + `CharacterViews.cs`; Q4 `RulesV2Wiring.cs` + new `Audio/`; Q6 `SettingsScreen.cs`, `ClientScreens.cs`, `RoleCamera.cs`). Never touch those.

## Why

On the Killer HUD, labels overlap when several markers sit on one screen edge. Evidence:
- `docs/qa/U6c-wire-screens/5-killer-overloaded-exitmarker-720.png`: `Terminal 2 overloaded · 29 m` drawn over a `30 m` edge tag, with a second `30 m` tag under it.
- `docs/qa/U6c-wire-screens/a2-5-killer-overloaded-1080.png`: two `42 m` edge tags and `Terminal 6 overloaded · 41 m` stacked on top of each other at the right edge.

The Killer HUD must stay readable at its busiest moment.

## Root cause (orchestrator's reading; verify it)

1. `KillerWorldLayer.Render` runs one `TagLayout` pass (`MarkerLayer.Layout`) over the Static and Signals channels, but the rules v2 tags (Overloaded alert, Exit tags, Killer Regressing tag) are drawn by `V2MarkerLayer` (`Client/Assets/Scripts/UI/RulesV2/RulesV2World.cs`), which places each tag on its own and never enters the layout. So an Overloaded tag can cover any other tag.
2. Several off-screen targets in similar directions get the same or nearly the same `EdgePlacement.Place` point. Their chevrons/squares sit on top of each other, and `TagLayout`'s 3-step vertical stacking either fails (and hides) or still collides with the v2 tag.

## Owned files

- `Client/Assets/Scripts/Killer/EdgePlacement.cs`, `Client/Assets/Scripts/Killer/TagLayout.cs` (the logic lives here, pure and tested).
- EditMode tests: `Client/Assets/Tests/EditMode/EdgePlacementTests.cs`, `TagLayoutTests.cs`, or a new test file next to them.
- **Scope extension granted by the orchestrator** (wiring only, keep the diff small, say why in the report): `Client/Assets/Scripts/Killer/KillerWorldLayer.cs`, `Client/Assets/Scripts/UI/MarkerLayer.cs`, `Client/Assets/Scripts/UI/RulesV2/RulesV2World.cs` (`V2MarkerLayer` only). These three are not owned by any parallel unit.
- Do not change the signature or behaviour of existing public APIs that `RulesV2Preview.cs` and `SurvivorHud.cs` call (`EdgePlacement.Place`, `ClampOutside`, `SafeRect`, `KeepOut`, `TagAnchor`, `V2MarkerLayer` public methods). Add new API instead. The Survivor HUD must look and behave the same.
- No `Shared/` or server changes.

## Spec

1. **Edge spreading (EdgePlacement).** Add a pure, allocation-free-per-frame edge spreader (e.g. a small `EdgeSpread` class with reusable buffers) that takes every off-screen edge marker of a frame (Survivor chevrons and off-screen Overloaded squares together), each with its ideal edge point, its glyph size and its tag size, and moves them along their edge so that no two **marker+tag footprints** on the same edge overlap, with a minimum gap of at least 4 u.
   - Assign each marker to one edge (top/bottom/left/right) from its placed point; corners go to a deterministic edge.
   - Order along the edge by the ideal coordinate, with a stable tie-break (a caller-supplied stable key such as the blip handle or terminal id), never by input order.
   - Resolve with a displacement-minimising 1D packing (cluster merge around the mean of ideal positions, then clamp to the usable edge segment: the safe rect minus the keep-out). The output must be a continuous function of the inputs while the order does not change.
   - Anti-jitter: keep the previous frame's order when two neighbours' ideal coordinates differ by less than a small hysteresis (about 6 u); round outputs to whole units. An input change of ≤ 1 u must move no output by more than 1 u (after rounding, allow 1 u).
   - If the footprints do not fit in the segment, drop tags (keep glyphs; Survivor glyphs are never dropped) by priority: Survivor information wins, so drop the lowest priority first (Overloaded tag before Survivor edge tag), then the farthest first. First try shrinking the gap toward 2 u before dropping anything. Say what you chose in the report.
2. **One layout pass for all Killer tags (TagLayout + wiring).** The Overloaded alert tag, Exit tags and the Killer Regressing tag must take part in the same `TagLayout` solve as the `MarkerLayer` tags, so no tag of either layer can overlap another. Use their measured sizes (V2MarkerLayer already measures from the previous layout; keep the one-frame hidden rule for unmeasured tags). Choose a priority for the Overloaded alert (suggestion: between Edge and Exit) and document it in the `LayoutPriority` comment; do not renumber in a way that breaks existing tests without updating them.
   - The edge-spread positions are inputs to the layout; the layout must not undo the spread (an edge tag that the spread already cleared should keep shift 0).
   - Every visible tag ends fully inside the 24 u safe rect and outside the HUD cluster keep-out.
3. Zero GC allocation per frame in the new paths (reuse lists; no LINQ; no closures in the hot path).
4. Survivor HUD path unchanged (it calls `V2MarkerLayer.Regressing` and `EdgePlacement`). The `RulesV2Preview` dev scenarios still compile and work.
5. Follow `docs/ui/SLOP_DEFINITION.md` (no new visual style, no glow/animation, same tag styles, same copy). Spacing comes from the existing scale (2/4/8/12/16/24 u).

## Acceptance tests (EditMode, add them)

- **Same-edge N ≥ 5:** 5, 7 and 10 markers with tags on the right edge (and repeat for top/bottom/left), with ideal points clustered within 20 u: no two resulting label rects overlap and every pair keeps ≥ 4 u gap; all inside the safe rect; none inside the keep-out.
- **Stable ordering:** shuffling the input order yields identical output.
- **No jitter:** run 120 frames where each ideal point drifts by random ≤ 0.5 u per frame (fixed seed); no output moves more than 1 u between consecutive frames and the order never flips while two neighbours stay within the hysteresis.
- **Crowded case at 1280×720 and 1920×1080:** 3 off-screen Overloaded terminals + 1 Exit tag near the right edge + 1 Survivor edge chevron with tag + 1 Survivor diamond, all aimed at the right edge, with the real Killer keep-out (`EdgePlacement.KeepOut` of the Killer cluster rect): every glyph and every visible tag fully on screen inside the safe rect, no tag overlaps any other tag or any Survivor glyph, and no tag is hidden at these two resolutions (all must fit).
- **Mixed layers:** a TagLayout test where a v2 tag (Overloaded) and a MarkerLayer edge tag start on the same rect: after Solve they do not overlap.
- Existing `EdgePlacementTests`, `TagLayoutTests`, `QA/EdgePlacementQaTests`, `QA/TagLayoutQaTests`, `QA/KillerHudQaTests` stay green (update only if a test encodes the old overlapping behaviour, and say so).

## Verification you must run

- Unity Library: this worktree has none. Copy the warm Library from `C:\Unity\MultiPlayGame\PingtheKiller\Client\Library` (read-only source; `robocopy /E /MT:8`, do not modify the source) before the first Unity run, if no Unity editor has that main project open; otherwise let it import cold (run in the background).
- Unity EditMode all green: `"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults docs/qa/q2-runs/editmode-a1.xml -logFile docs/qa/q2-runs/editmode-a1.log`. Known random flake: `SurvivorKillerRenderingReverifyTests.SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract`: rerun once and report both.
- `BuildWindows`: `-batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile docs/qa/q2-runs/build-a1.log` succeeds (find the output exe path in `BuildScript.cs`).
- Offline exe Killer run, for example `-ptk-offline -ptk-bots 7 -ptk-start -ptk-role killer -ptk-shots docs/qa/q2-runs/shots-a1 -ptk-quit-after 90` (read `DevArgs.cs` for exact flags/screen size; try both 1280×720 and 1920×1080 via `-screen-width/-screen-height -screen-fullscreen 0`). Look at the screenshots yourself. Off-screen Overloaded alerts are rare in a short run; try `-ptk-killer-look`, `-ptk-orbit` or other existing dev flags that turn the camera, and several runs. If you cannot stage a busy edge without new dev code, say so plainly (do not add dev code).
- After Unity runs, revert Client files whose only change is line endings (`git diff --ignore-all-space --stat` vs `git status`). Do not commit.

## Report

Write `docs/qa/Q2-edge-labels-impl-a1.md` (English, ≤ 200 words, header says implementer = Claude Opus subagent, quota fallback because Codex is out of credits): files changed (and why each scope-extension file), design in 3 lines, commands + results, screenshots inspected and what they show, what is unverified, risks.
