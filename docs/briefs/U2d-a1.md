# Brief U2d — attempt 1: Bot balance (server)

Status: **active** (2026-11-02, main `4b7d335`, after U2e line-of-sight). Implementer: Claude Opus (quota fallback; Codex is out of credits). QA: Claude Sonnet. Run the loop in your session. Commit on your own branch only after your PASS. Do not merge to main and do not push.

**Why:** in offline play, the bots decide whether a match is fun. U2c measured, over 15 seeds:
- with the killer bot, median 600 s, because in 9 of 15 seeds the survivor bots were all eliminated with 0–1 terminals done and the match timed out on the hidden idle human;
- with an idle killer, median 384.7 s.

Survivor bots are too passive and too easy to catch. The user also found matches confusing as a survivor. U2e now gives survivors line of sight to the Killer, so a fair survivor bot can flee from what it sees.

**Owned files:**
- `Server.Core/Runtime/GameServer.Bots.cs`, `Server.Core/Runtime/Map/NavGrid.cs`, and new bot-only files under `Server.Core/Runtime/`.
- `ServerOptions`, for test hooks only.
- `Server.Tests/Core/**`.
- `Server.Tests/QA/**`, only to re-target bot-timing tests: list each one.
- `docs/qa/U2d-*`, `docs/briefs/U2d-qa-a1.md`.

Do not touch `Shared/`, `Tools/` or `Client/`. Do not change the rules or numbers in GDD §3–§5 (speeds, radii, timers, cooldowns, terminal counts). Balance comes from **bot behaviour only**. If behaviour alone cannot reach the target, stop at attempt 2 and report which rule numbers would have to change, with measurements; the user decides.

## 1. Fair information (hard rule)
Bots use only what a human client in the same role could know.
- **Survivor bot:**
  - its own state, all teammates, terminals and the exit;
  - `ThreatLevel` (distance band only);
  - the Killer position **only while that bot's own line of sight is clear** (the U2e rule, with the same `FixedMap` helper), plus the last position it saw.
- **Killer bot:**
  - positions of entries in its own anonymous killer view, meaning Signal survivors, phantoms and Lagging survivors;
  - no Ids, no Silent survivors, no exact terminal progress (0/100 only).
- Add a test that fails if a bot reads hidden state. For example, run with a hook that hides Silent survivors from the bot query, and check that bot decisions are identical with and without the true hidden positions available.

## 2. Measurement harness (test-only)
- Add a test hook (`ServerOptions`, never set by the console or offline paths) that lets a match run with **all players as bots**: 1 killer bot plus N survivor bots, no human. Today's "idle human" method distorts the result.
- Deterministic per private seed. Run the seeds in-process, fast. A full 600 s match is 12000 ticks.
- Report, per configuration and seed set (at least 40 seeds):
  - survivor win rate;
  - median and p25/p75 match length;
  - end-reason mix;
  - terminals done at end;
  - captures, rescues and executions per match;
  - timeouts.

## 3. Targets
| Config | Survivor win rate | Median length | Other |
|---|---|---|---|
| 1 killer bot vs 7 survivor bots | **35–65 %** | **3–8 min** | timeouts ≤ 15 % |
| 1 vs 3 | 25–75 % | 2–8 min | — |
| 1 vs 1 | 20–80 % | 1.5–8 min | — |
| Idle human killer + 7 survivor bots | survivors win ≥ 90 % | 2–6 min | sanity: bots still finish without pressure |

Per match, ≥ 1 rescue in at least 30 % of 1v7 matches, so rescues happen in practice.

## 4. Behaviour levers (suggestions, not required)
- **Survivor:**
  - React to seeing the Killer: break line of sight, or move away and go Silent when ThreatLevel ≥ 2.
  - Wait before resuming a hack.
  - Spread across terminals.
  - Rescue when the Killer is not seen and ThreatLevel ≤ 1.
  - Use skills sensibly: Dummy or White noise when chased, Stealth relay when hacking near teammates, Traffic overload when a teammate is being chased.
  - Use per-bot reaction delays drawn from the private RNG.
- **Killer:**
  - Chase the last seen blip.
  - Patrol terminals weighted by "not done".
  - Do not camp a Lagging survivor indefinitely.
  - Use skills.
  - Add a small reaction delay so it is not frame-perfect.
- All randomness comes from the private RNG. No `System.Random`.

## 5. Must not regress
- The U2c/U2e leak and anonymity tests.
- Phantom movement stays on the survivor-bot NavGrid policy. The indistinguishability gate must still pass: if survivor-bot motion changes, phantoms must change the same way, and the AUC stays within the accepted 0.85 ceiling.
- The 3/4-terminal stall fix.
- `--sandbox` stays U2a rules only, and the harness stays at 100 ×3 with `--expect-los` (PLAN §5).

## Acceptance
- The §3 table is met on ≥ 40 fixed seeds per config, with a before/after table in the report.
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental` reports 0 warnings, and `dotnet test PingTheKiller.sln` is green. The new balance tests run in reasonable time; mark long sweeps with a trait so they can be filtered out.
- Harness: 3 runs with `--threshold 100 --expect-los` as in PLAN §5.

## Report
`docs/qa/U2d-impl-a1.md` (≤ 200 words plus the before/after table), then QA `docs/qa/U2d-qa-a1.md` ending in `VERDICT: PASS` or `VERDICT: FAIL (...)`. QA must independently re-run the seed sweep and try to find an unfair-information path.
