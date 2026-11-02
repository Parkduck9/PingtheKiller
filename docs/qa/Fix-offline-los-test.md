# Fix Offline LOS Test Note

## What changed
- In `Client/Assets/Tests/EditMode/QA/OfflineLinkQaTests.cs`:
  - Added imports `PingTheKiller.Client.World` and `PingTheKiller.ServerCore.Map`.
  - Renamed test method `SurvivorRoleStartsAMatchAsSurvivorAndNeverReceivesAKillerEntry` to `SurvivorRoleStartsAMatchAsSurvivorAndSeesTheKillerOnlyInLineOfSight`.
  - Added citation comment referencing U2e (GDD section 2, 10/30) replacing U2b rule 10.
  - Re-targeted the assertion checking received player entries:
    - Non-killer player entries are asserted to be `Role.Survivor`.
    - At most one entry per world may have `Role == Role.Killer`.
    - When present, the Killer entry is verified to have `Flags == 0`, `StatusTimer == 0`, and `CaptureCount == 0`.
    - Preserved all existing assertions at original strength (`match.Role`, `match.SkillId`, `sawSelf`, `world.ThreatLevel`).
    - Verified line of sight: when a Killer entry is present, the ray segment from the receiving survivor's position (`joined.PlayerId`) to the Killer entry is checked against `SharedMap.Map.Walls` via `MapGeometry.SegmentHitsBox`.

## Why
- Under U2b rule 10, survivors were never sent a Killer entry.
- Under U2e (GDD §2, 10/30), a survivor receives the Killer entry when line of sight is clear. The stale U2b test failed on `main` whenever line of sight was clear.

## Tolerance
- Shrunk walls by 0.05 m on each side (`minX + 0.05f`, `maxX - 0.05f`, `minZ + 0.05f`, `maxZ - 0.05f`).
- This absorbs the server's ambiguity band and grazing corner/edge cases, preventing flaky failures on near-boundary rays while ensuring static walls correctly block Killer visibility.

## Orchestrator judgement (Claude Opus)
- Author: Antigravity Gemini 3.8 Flash (medium). Reviewer and gate: Claude Opus, a different model family.
- I read the diff:
  - every old assertion is kept (MatchStart role and skill id, self and team visible, ThreatLevel <= 3);
  - non-Killer entries must still be Survivors;
  - the new checks match the U2e contract.
- Unity 6000.3.25f1 EditMode gate on main `91e8891` plus this change: **1151/1151 pass**, 0 `error CS`. `SurvivorRoleStartsAMatchAsSurvivorAndSeesTheKillerOnlyInLineOfSight` passes.
- Note: the line-of-sight branch only runs on worlds that actually contain a Killer entry. Before this fix the same test failed with exactly such an entry within its 6 worlds. So the offline match does produce them, but a given run is not guaranteed to hit one.
- Verdict: PASS.
