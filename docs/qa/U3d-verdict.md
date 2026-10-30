# U3d verdict: PASS (attempt 3, different approach)

- **Implementer:** Claude Opus (quota fallback; Codex out of credits).
- **QA:** Claude Sonnet.
- **Judge:** Claude Opus.

| Attempt | QA | Result |
|---|---|---|
| a1 | `U3d-qa-a1.md` | FAIL — F1: entries on ticks the killer never received were undecided, and the scan allowed their offsets |
| a2 | `U3d-qa-a2.md` | FAIL — F6: the 6-tick edge tolerance was keyed on tick numbers, so a `Tick 0` leak passed; F7: false fail |
| a3 | `U3d-qa-a3.md` | **PASS** |

**Attempt 3 approach (different design).**
- No tolerance.
- **Contract clarification (orchestrator):** a correct server sends every WorldState tick to every human.
- Per-stream tick integrity: ticks rise by 1 or 2.
- Coverage by tick:
  - H3 seeds the join from retained killer worlds and reads the killer stream until it covers the window.
  - H6 covers all three streams from the first WorldState to the end of the drain.
- Every unjoined survivor world fails.
- The scan runs only on final verdicts.

**Orchestrator review fix (one line).** The H6 stream-silence message now says `(no S2C_GameResult received)`. The branch only runs when that stream has had no GameResult. Before the fix, the race between the silence timeout and the GameResult wait made `U3bResultSkipsTheStandingSurvivorTests` fail 1 of 3 runs on evidence text. H6 still failed with score 0 in every case, so this was never a false pass.

**QA reclassification of 13 tests.** QA flipped or replaced 13 QA tests whose expectations encoded the removed tolerance or limits now closed. Each is recorded with its reason in `U3d-qa-a3.md` §2. The orchestrator checked the U3c flips: one was must-pass → must-fail with "no unique own entry"; the other was must-fail → must-pass at 500 ms lag. Both are stronger.

## Gate
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings.
- `dotnet test PingTheKiller.sln`:
  - QA: 3 runs plus 1 loaded run, Server.Tests 713/713 and Harness.Tests 616/616.
  - Orchestrator, after the review fix: see the commit message.

## Real-server runs (attempt-3 harness)
Each run used fresh servers:
- sandbox for H1–H5: `--port P --bots 0 --bind 127.0.0.1 --sandbox`
- normal for H6: `--port P+1 --bots 0 --bind 127.0.0.1`

The harness was invoked as `--port P --h6-port P+1 --threshold 100`. All servers were stopped after each run.

| Server | Run | Total | H2 p99 | H5 rejoin | H3 sight (visible / blocked / ambiguous) | H6 sight (visible / blocked / ambiguous) |
|---|---|---:|---|---|---|---|
| main `de730e9` | 1 | 100 | 63.5 ms | 64.4 ms | 0 / 0 / 0 | 0 / 152 / 5 |
| main | 2 | 100 | 63.2 ms | 48.4 ms | 0 / 0 / 0 | 0 / 152 / 5 |
| main | 3 | 100 | 63.2 ms | 112.3 ms | 0 / 0 / 0 | 0 / 152 / 5 |
| U2e `57839b6`, `--expect-los` | 1 | 100 | 63.5 ms | 47.5 ms | 83 / 0 / 0 | 2092 / 152 / 5 |
| U2e, `--expect-los` | 2 | 100 | 63.4 ms | 63.6 ms | 84 / 0 / 0 | 2090 / 152 / 5 |
| U2e, `--expect-los` | 3 | 100 | 63.4 ms | 48.4 ms | 83 / 0 / 0 | 2092 / 152 / 5 |

H4 passed in every run, including against U2e. The old harness's H4 failure on U2e was a knock-on effect, not a server defect.

## Accepted limits (README "Known limits")
- **F2:** the ±0.05 m ambiguous band is not judged, per the spec.
- **H6 pacing:** H6 fails with survivor B more than 500 ms late. This predates U3d: a server with no line-of-sight entries fails identically. The 300 ms requirement passes.
- **Retained-buffer seeding:** retained killer worlds used to seed H3 are not covered by `TickSequence`. Exploiting this is contrived, and H6 would catch it.
