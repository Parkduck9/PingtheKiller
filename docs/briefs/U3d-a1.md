# Brief U3d — attempt 1: Harness accepts the line-of-sight Killer entry, still catches leaks

Status: **active** (2026-10-30, main after `0e39579`). Implementer: Claude Opus (quota fallback). QA: Claude Sonnet. Run the loop in your session. Commit on your own branch only after the orchestrator's PASS.

**Why:** the user decided that survivors see the Killer when their line of sight is clear (GDD §2, 2026-10-30). The server side is U2e, running in parallel. The contract below is binding for both; it is copied from `docs/briefs/U2e-a1.md`. Do not touch `Server.Core/`, `Server/`, `Shared/` or `Client/`.

**Owned files:**
- `Tools/Harness/**`
- `Tools/Harness.Tests/**`
- `docs/qa/U3d-*.md`
- `docs/briefs/U3d-qa-a1.md`

## Contract
A survivor-bound `S2C_WorldState` may contain the Killer entry **only when all of these hold**:
- the recipient is Normal or Lagging;
- the 2D segment from the recipient's position to the Killer's position, at the same tick, intersects no `FixedMap.Walls` AABB;
- walls are closed and not inflated, so touching an edge or corner means blocked.

When present, the entry has:
- the real Id, Role=Killer, and real X/Z/Yaw;
- Flags=0, StatusTimer=0, CaptureCount=0.

Spectators never receive it, and each recipient is judged separately. Killer coordinates anywhere else in survivor-bound traffic are still a leak, including `S2C_SkillEffect`, unknown-Id frames and other fields.

## Spec
1. **Independent line-of-sight check:** write the harness's own segment-against-AABB test.
   - Do not call a server line-of-sight helper.
   - Take the walls through the existing Map-only access.
   - For pairs within **0.05 m** of a wall boundary (distance from segment to AABB ≤ 0.05), the harness may not decide. Count these as "ambiguous, skipped" in the evidence, never as pass or fail.
2. **H6 and the leak scans:** a killer entry in a survivor world is legal only when the harness line-of-sight check is clear, all fields match the contract, and X/Z match the Killer's own reported position for that tick (ε 0.05 m).
   - When line of sight is blocked, the entry is still a leak, exactly as today.
   - Killer X/Z bytes elsewhere in a frame that legally carries the entry: the entry's own offsets are allowed, and any other occurrence is a leak.
3. **H3:** it still must not pass if a stopped (Silent) survivor leaks. Killer-entry handling must not weaken the survivor-anonymity checks.
4. **Evidence lines** report, per run, the counts of legal line-of-sight killer entries, blocked-and-absent, and ambiguous-skipped. The H6 scenario must observe **at least one legal visible entry and at least one blocked-and-absent tick**. If a scenario never produces line of sight, add a step that walks the survivor into the Killer's view.
5. **Fakes for self-tests (`ScenarioServer`/`LobbyServer`):** add a line-of-sight mode that follows the contract. Add broken variants that must fail:
   - the entry sent while blocked;
   - the entry sent to a spectator;
   - one survivor's line of sight revealing the Killer to another survivor;
   - the entry with a stale (previous-tick) position;
   - non-zero Flags/StatusTimer/CaptureCount;
   - killer X/Z in a SkillEffect while visible.

   One more variant must pass: the contract-correct entry.

## Acceptance
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental` reports 0 warnings, and `dotnet test PingTheKiller.sln` is green.
- Against the current main server (no line-of-sight entries yet), the harness still scores 100 three times, and the evidence shows 0 visible entries. The H6 "at least one visible" requirement applies only when the server announces line-of-sight support: use a `--expect-los` flag, which the orchestrator turns on after U2e merges.
- Update `Tools/Harness/README.md` (known limits, new flag).

## Report
`docs/qa/U3d-impl-a1.md` (≤ 200 words), then QA `docs/qa/U3d-qa-a1.md` ending in `VERDICT: PASS` or `VERDICT: FAIL (...)`.
