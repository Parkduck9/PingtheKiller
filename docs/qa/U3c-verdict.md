# U3c verdict: PASS (attempt 1)

- **Implementer:** Claude Opus (quota fallback; Codex is out of credits, so this was not a failed attempt).
- **QA:** Claude Sonnet, `U3c-qa-a1.md`, QA-PASS.
- **Judge:** Claude Opus.

## Gate
- `dotnet build Tools/Harness/Harness.csproj -warnaserror` and `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings.
- `dotnet test PingTheKiller.sln`, run 3 times by QA with no load: Server.Tests 662/662 and Harness.Tests 386/386 every time (323 impl + 63 QA U3c).
- Under moderate load (16 busy processes) the same counts passed. Under heavy load (40 busy processes on 32 CPUs), 11 timing tests failed; see the accepted limits below.

## Real-server runs against today's main (`6495a7d`, the U2b view, final U3c build)
Each run used:
- a fresh sandbox server: `Server.dll --port P --bots 0 --bind 127.0.0.1 --sandbox`
- a fresh normal server: `Server.dll --port P+1 --bots 0 --bind 127.0.0.1`
- one harness invocation: `Harness.dll --host 127.0.0.1 --port P --h6-port P+1 --threshold 100`

All servers were stopped after each run.

| Run | H1 | H2 (p99) | H3 (killer-view entries attributed by tick+position) | H4 | H5 (rejoin) | H6 | Total | Exit |
|---|---|---|---|---|---|---|---:|:---:|
| 1 | 25 | 20 (63.4 ms) | 20 (230 entries, 0 unexplained, 0 to the stopped survivor) | 15 | 10 (48.2 ms) | 10 | 100 | 0 |
| 2 | 25 | 20 (63.5 ms) | 20 (315 / 0 / 0) | 15 | 10 (61.8 ms) | 10 | 100 | 0 |
| 3 | 25 | 20 (63.4 ms) | 20 (281 / 0 / 0) | 15 | 10 (48.4 ms) | 10 | 100 | 0 |

These runs confirm on the real server that killer-view and survivor-view positions at the same tick are bit-identical, which is the premise attribution relies on.

## Spec bullets
| Bullet | Result |
|---|---|
| H3: tick join, position attribution, robust to bots passing or stopping 0.04–0.5 m away, in both views | PASS |
| No survivor lookup by Id in the killer stream | PASS (QA fake with random nonzero killer-view Ids) |
| H6: survivor-side ground truth; quantised killer progress accepted; per-frame ExitOpen rule | PASS |
| Unknown Ids skipped and counted; malformed frames and known-Id decode failures still fault | PASS |
| Unknown frames raw-scanned for killer X/Z on survivor streams | PASS |
| Anonymous-view fakes | PASS |

## Accepted limits (minor; README "Known limits")
- **Adversarial offset leaks H3 cannot catch (QA findings 1–3).** An offset leak of 0.05 m or less goes undetected in three cases:
  - it sits nearer a coincident bot than the stopped survivor;
  - it is disguised as a duplicate of the killer's own entry;
  - it is emitted only on ticks the survivor stream never received.

  Each case needs a deliberately adversarial server. Exact-position leaks are caught in every shape tested.
- **Exact-tie ambiguity rule is unreachable (QA 4).** The raw X/Z byte scan, unchanged since U3, fails first. A bot that shares the stopped survivor's exact X or Z float would therefore fail H3 conservatively. This did not happen in any real run.
- **H3 stream-lag tolerance is about 250 ms (QA 5).** This is a pre-existing window.
- **H6 contact hold uses wall-clock time (QA 6).** Under heavy CPU starvation, a fake that executes after 2.5 s slipped past it. H6 is not a gate, and this did not occur in unloaded or moderate-load runs.
- **`S2C_SelfState` frames before Shared has the packet (U3c-2).** They are raw-scanned, so a survivor timer exactly equal to a killer X/Z could false-fail H6. This goes away once U2c's Shared is compiled in.

## Pending
**Real-server gate against U2c, 100/100 three times.** This runs after U2c merges into main and main is merged into this branch, so the harness compiles with U2c's Shared (`S2C_SelfState` decoded structurally). Only the real U2c server can confirm two things:
- anonymous killer-view positions are bit-identical to survivor-view positions;
- `--sandbox` emits no decoys or afterimages.
