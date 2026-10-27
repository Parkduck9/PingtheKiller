# QA brief U2c — attempt 1

QA: Claude Sonnet. The implementer was Claude Opus. Branch `claude/u2c-protocol` in the main checkout. Do not commit. Do not edit product code (`Shared/`, `Server.Core/`, `Server/`). You own `Server.Tests/QA/**` and `docs/qa/U2c-qa-a1.md`.

Read first:
- `docs/briefs/U2c-a1.md` (spec)
- `docs/qa/U2c-impl-a1.md` and `docs/qa/U2c-impl-a2.md`
- `docs/qa/U2b-qa-a2.md` (your previous findings F1–F7)
- the code under `Server.Core/Runtime/` and the new Shared packet

## Orchestrator rulings already made (do not re-litigate)
- **Indistinguishability gate:** gated per step (speed, still steps) plus afterimage tail. Per-track averages still differ (D≈0.49) because WhiteNoise lives only 1.5 s; this is an accepted residual. Lifetime and spawn distance are reported only.
- **Bot balance:** moved out of U2c into a separate unit (U2d). The killer-bot median of 600 s comes from a hidden idle human and is not a U2c finding.
- **Entry cap:** raised to 16 with the byte layout unchanged. Reserved slots per hidden player.

## Tasks
1. **Migrate the 39 superseded QA tests** (42 cases) listed in `U2c-impl-a2.md`. Keep what each protects, re-targeted to the U2c rules:
   - The killer view is anonymous and shuffled.
   - Phantoms no longer borrow Ids.
   - Killer-view Progress is 0 or 100.
   - The private RNG is xoshiro.
   - Bot timing has changed.

   Delete a test only when its invariant no longer exists, and say why.
2. **Adversarial tests** in `Server.Tests/QA/U2cAdversarialTests.cs`:
   - **Killer-bound leaks.** Any survivor Id, Yaw, CaptureCount, StatusTimer or non-Signal flag, at the byte level. Any order that is not uniformly random (chi-square). Any field that differs between real and phantom entries in the same snapshot. Exact terminal progress. A survivor Id in a killer-bound SkillEffect.
   - **Behavioural tells.** Can a simple classifier (position tracking at 20 Hz) separate phantom from real Normal tracks better than the accepted residual says? Report the AUC with ground truth.
   - **RNG.** Nothing derivable from `MatchStart.Seed`. The private seed injection is deterministic. No `System.Random` in hidden draws.
   - **SelfState.** Sent only to its owner, never about anyone else. Sent on change plus 1 Hz. Values match the server timers. `ExecutionGraceLeft` is only for the killer. Survivors never receive killer-only fields.
   - **Entry cap 16.** It is never exceeded, and decoders accept up to 16 entries.
   - **Regressions.** Everything U2b QA proved still holds: masking, capture/rescue/grace, the Seq reset, and sandbox isolation.
3. **Run:**
   - `dotnet build PingTheKiller.sln -warnaserror --no-incremental`
   - `dotnet test PingTheKiller.sln`
   - the harness 3 times, using fresh servers each time: `--port P` with `--sandbox`, and `--h6-port P+1` normal, both with `--bots 0 --bind 127.0.0.1`. Stop all servers afterwards.

   The current main harness is expected to score 80 (H3 times out because it looks up survivors by Id). Also build the U3c harness from `refs/heads/claude/u3c-anon` into a temporary directory: `git worktree add` into your scratch area, build only `Tools/Harness`, run it against the U2c servers 3 times and expect 100. Remove the temporary worktree afterwards.
4. **Look beyond the spec.** Report anything wrong that the brief did not mention.

## Report
Write `docs/qa/U2c-qa-a1.md`. For each finding give severity, reproduction, expected vs actual, and evidence. Include a table of brief sections 1–6 marked PASS, FAIL or UNTESTED, plus the counts and harness totals for both harness builds. Finish with the line `VERDICT: PASS` or `VERDICT: FAIL (n blockers, m majors)`.
