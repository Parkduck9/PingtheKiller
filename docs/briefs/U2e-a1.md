# Brief U2e — attempt 1: Survivors see the Killer in line of sight (server)

Status: **active** (2026-10-30, main after `0e39579`). Implementer: Claude Opus (quota fallback; Codex is out of credits). QA: Claude Sonnet. Run the loop in your session (implement → independent QA → report to the orchestrator). Do not commit to main; commit on your own branch only after the orchestrator's PASS.

**User decision (2026-10-30):** after a playtest, the user said survivors could not see the Killer and did not understand why they died. The user chose "시야 안이면 보이기": the DbD-style rule. Survivors see the Killer when line of sight is clear. GDD §2 and §3.2 were updated on main.

**Parallel units with the same contract:**
- U3d: harness H3/H6 leak checks.
- U4e: client rendering and survivor feedback.

Do not touch `Tools/`, `Client/` or `docs/ui/`.

**Owned files:**
- `Server.Core/Runtime/**`
- `Server.Tests/Core/**`
- `Server.Tests/QA/**`, only to re-target tests whose invariant changed: list each one.
- `docs/qa/U2e-*.md`
- `docs/briefs/U2e-qa-a1.md`

`Shared/` is unchanged: wire layout and PacketIds stay as they are.

## Contract (shared with U3d and U4e — do not change without the orchestrator)
1. A **survivor-bound** `S2C_WorldState` contains the Killer's `PlayerState` entry **only when all of these hold**:
   - the recipient's `Status` is `Normal` or `Lagging`;
   - the 2D segment from the recipient's position to the Killer's position, using the same tick's server positions, intersects **no wall** in `FixedMap.Walls`.
   - A wall is a closed AABB with no inflation. A segment that touches a wall edge or corner counts as blocked.
2. Entry fields:
   - real `Id`, `Role = Killer`, current `X`/`Z`/`Yaw`, and `Status`;
   - `Flags = 0`, `StatusTimer = 0`, `CaptureCount = 0`.
3. Eliminated and Escaped survivors (spectators) **never** receive the Killer entry.
4. Per recipient only. One survivor's line of sight never reveals the Killer to another survivor.
5. No linger and no prediction. The tick after line of sight breaks, the entry is gone.
6. Everything else is unchanged:
   - `ThreatLevel` is computed and sent as before, regardless of line of sight.
   - Killer coordinates must still never appear in survivor-bound `S2C_SkillEffect`, or in any other packet or field.
   - The Killer-bound view (anonymous blips) is unchanged.

## Implementation notes
- Add `FixedMap.SegmentBlocked(ax, az, bx, bz)` (or similar): segment against each wall AABB using a slab test. Treat a zero-length segment as not blocked.
- Unit-test the edge cases: parallel to a face, touching a corner, an endpoint inside a wall (blocked), and a long diagonal across the map.
- Build the survivor view from the same tick's positions that the snapshot sends. The Killer entry and the line-of-sight decision must never come from different ticks.
- Cost: 7 recipients × number of walls per tick is trivial. Allocate nothing per tick in the hot path.
- Bots: survivor bots may use the same line-of-sight result through server-internal state, never through the wire. This is optional here; U2d owns balance.

## Acceptance
- Tests:
  - clear line of sight means the entry is present with exact fields;
  - blocked means it is absent, at byte level, with no killer X/Z float bytes in that frame;
  - the corner-touch case;
  - spectators never receive it;
  - per-recipient isolation (two survivors, one with line of sight);
  - the entry appears and disappears on the same tick as the line-of-sight change;
  - `ThreatLevel` is unchanged.
- Existing leak tests that asserted "the killer is never in survivor worlds" are re-targeted to "never in survivor worlds **when blocked**". Keep the old strength for the blocked case.
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental` reports 0 warnings, and `dotnet test PingTheKiller.sln` is green.
- Harness: run 3× as in PLAN §5.
  - The current harness H3/H6 treat any killer entry as a leak. If they fail only because of a legal line-of-sight entry, report that with the evidence lines and do not edit `Tools/`; U3d fixes it.
  - All other checks must pass.

## Report
`docs/qa/U2e-impl-a1.md` (≤ 200 words), then QA `docs/qa/U2e-qa-a1.md` ending in `VERDICT: PASS` or `VERDICT: FAIL (...)`.
