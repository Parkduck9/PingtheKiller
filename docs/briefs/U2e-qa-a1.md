# QA Brief U2e — attempt 1: survivors see the Killer in line of sight

QA agent: Claude Sonnet (independent of the implementer, Claude Opus). Branch `claude/u2e-los`, uncommitted working tree.

Read: `AGENTS.md` §5, `docs/briefs/U2e-a1.md` (the contract is binding), `docs/GDD.md` §2/§3.2, the implementer's report `docs/qa/U2e-impl-a1.md`, and the diff (`git diff de730e9`, plus untracked files from `git status`).

## Ownership
- You may add tests only under `Server.Tests/QA/U2e*` and write `docs/qa/U2e-qa-a1.md`.
- Never edit product code, `Shared/`, `Tools/`, `Client/` or `docs/ui/`. Never weaken an assertion. Do not commit.

## Tasks
1. **Contract, item by item (PASS/FAIL with evidence):** contract items 1–6 of the U2e brief, through the public API (`GameServer` + `InMemoryTransport`, deterministic `Tick(dt)`), decoding the bytes the survivor connection actually receives.
2. **Adversarial tests** (`Server.Tests/QA/U2eAdversarialTests.cs`):
   - Geometry: segments grazing a wall face exactly (collinear with an edge), passing exactly through a corner, starting or ending exactly on a wall boundary, endpoints inside a wall, a gap between two adjacent walls (if the map has one), negative coordinates, very long segments; compare against an independent reference you write in the test (do not reuse the product's function).
   - Map sweep: for many seeded random survivor/killer positions on walkable tiles, the killer entry is present **iff** your reference says clear; never present while blocked (byte scan for the killer's X and Z float32 LE bytes in the whole survivor-bound frame).
   - Same-tick: the entry and the LOS decision use the positions sent in that same snapshot (move the killer across a wall edge tick by tick; no one-tick lag or linger).
   - Per-recipient isolation with 2+ survivors; spectators (Eliminated, Escaped) never; a Lagging recipient does receive it.
   - Exact entry fields; `Flags`, `StatusTimer`, `CaptureCount` are 0 even when the Killer has an active skill (e.g. Overclock).
   - Unchanged: `ThreatLevel` with and without LOS; survivor-bound `S2C_SkillEffect` never carries killer coordinates; Killer-bound view unchanged (anonymous, decoy rules).
   - Reconnect: a survivor reconnecting mid-match gets the entry only under the same rule.
3. **Re-targeted tests:** check every test the implementer re-targeted (listed in the report). Each must keep the old strength for the blocked case. Flag any test that was weakened or deleted.
4. **Review beyond the brief:** per-tick allocations in the hot path, thread safety (game state only on the loop thread), bots reading LOS through the wire, anything else wrong.
5. **Gates:** `dotnet build PingTheKiller.sln -warnaserror --no-incremental` and `dotnet test PingTheKiller.sln` (run the QA suite 3× for flakes). Re-check the implementer's harness evidence in `docs/qa/U2e-harness-run*.json`; if H3/H6 failed, confirm whether it was only a legal LOS killer entry.

## Report
`docs/qa/U2e-qa-a1.md`: header with model and counts; contract table; findings as `[blocker|major|minor] title — repro — expected vs actual — evidence`; the last line exactly `VERDICT: PASS` or `VERDICT: FAIL (...)` (any blocker or major → FAIL).
