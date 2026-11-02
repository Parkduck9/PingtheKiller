# QA Brief U2d — attempt 1: bot balance

QA agent: Gemini 3.8 Flash (medium) via Antigravity `agy` (independent of the implementer, Claude Opus; CLAUDE.md quota rule: QA stays on a non-Claude model). On a Flash quota/rate-limit failure: retry once, then `claude-sonnet-5-5-medium` via agy, recorded in the report header. Branch `claude/u2d-balance`, uncommitted working tree.

Read: `AGENTS.md` §5, `docs/briefs/U2d-a1.md` (binding), `docs/GDD.md` §2–§5, the implementer's report `docs/qa/U2d-impl-a1.md`, and the diff (`git diff 48abe4a` plus untracked files from `git status`).

## Ownership
- You may add tests only under `Server.Tests/QA/U2d*` and write `docs/qa/U2d-qa-a1.md`.
- Never edit product code, `Shared/`, `Tools/`, `Client/` or `docs/ui/`. Never weaken an assertion. Do not commit.

## Tasks
1. **Independent seed sweep.** Do not reuse the implementer's sweep test or its seed list. Write your own driver against the public/internal API (the all-bot hook), with a **different set of ≥ 40 seeds per config** (1v7, 1v3, 1v1, idle human killer + 7 survivor bots). Report every brief §3 metric and compare with the implementer's table. A target met only on the implementer's seeds is a major finding.
2. **Unfair-information hunt.** Try to find any path by which a bot uses state a human client of its role could not know:
   - survivor bots reading the Killer position without their own clear line of sight (including via teammates, `ThreatLevel` arithmetic beyond the band, skill effects, shared caches, last-seen data refreshed while blocked);
   - the killer bot reading Ids, Silent survivors, phantom/real distinction, exact terminal progress, `Flags`, `StatusTimer`, `CaptureCount`;
   - any direct access to `_players` or other server internals from bot decision code that bypasses the per-bot query.
   Prove each finding with a test (perturb hidden state → bot decisions change). Also judge the implementer's own fairness test: would it catch a regression?
3. **Must-not-regress:** U2c/U2e leak and anonymity tests; phantom movement on the survivor-bot NavGrid policy and the indistinguishability gate (AUC ≤ 0.85); the 3/4-terminal stall fix; `--sandbox` stays U2a rules only.
4. **Rules untouched:** confirm no GDD §3–§5 number changed (speeds, radii, timers, cooldowns, terminal counts) and that the test hook is unreachable from `Server/Program.cs` and offline paths.
5. **Beyond the brief:** determinism per seed, no `System.Random`, game state only on the loop thread, per-tick allocations, re-targeted tests keep their strength.
6. **Gates:** `dotnet build PingTheKiller.sln -warnaserror --no-incremental`; `dotnet test PingTheKiller.sln` (QA suite 3× for flakes); re-check the harness evidence in `docs/qa/U2d-harness-run*.json` (`--threshold 100 --expect-los`, 3 runs) or re-run it.

## Report
`docs/qa/U2d-qa-a1.md`: header with model and counts; your sweep table next to the implementer's; fairness findings; findings as `[blocker|major|minor] title — repro — expected vs actual — evidence`; the last line exactly `VERDICT: PASS` or `VERDICT: FAIL (...)` (any blocker or major → FAIL).
