# QA brief U3c — attempt 1: anonymous killer view and unknown packets

QA agent: Claude Sonnet. The implementer was Claude Opus, a different model.

Read first:
- `AGENTS.md`
- `docs/briefs/U3c-a1.md` (spec)
- `docs/qa/U3c-impl-a1.md`
- `docs/briefs/U2c-a1.md` §1, §5 and §5b (server contract)
- `Tools/Harness/README.md`
- the code under `Tools/Harness/**` and `Tools/Harness.Tests/**`

## Allowed writes
- `Tools/Harness.Tests/QA/**`. New tests go under `QA/U3c/`. You may also update existing QA tests whose expectations are made obsolete only by the spec change, such as evidence text that names a survivor Id in the killer view. Never change what a test proves, and list every such edit in your report.
- `docs/qa/U3c-qa-a1.md`

Do not commit, switch branches or touch git stash.

## Main job: H3 is a gate, so hunt both false passes and false fails
Use fakes that emit the U2c anonymous killer view: Id 0, Yaw/CaptureCount/StatusTimer 0, Signal-only flags, a shuffle per snapshot, and progress quantised to 0 or 100.

1. **False passes.** H3 must fail when any of these happens:
   - the stopped survivor is still emitted anonymously at its exact position;
   - it is emitted at a slightly offset or rounded position;
   - it is emitted only every Nth tick;
   - it is emitted with a one-tick delay (stale position);
   - it is emitted under a bot's real Id in Id mode;
   - an unexplained extra entry appears;
   - the survivor never appears while moving (baseline).
   Also probe whether attribution can be fooled: a server that emits the stopped survivor's position while a bot sits within epsilon of it.
2. **False fails.** H3 must pass when:
   - sandbox-like bots walk right past, or stop next to, the stopped survivor (0.1–0.5 m);
   - several bots cluster;
   - streams arrive with skewed tick timing between killer and survivor clients;
   - some survivor-stream ticks are dropped (within coverage);
   - the shuffle order changes every tick.
   Run each in Id mode and anonymous mode.
3. **H6 in anonymous mode:**
   - passes with quantised killer progress;
   - fails if the killer stream shows the exit open while its own (quantised) terminals show fewer than required at 100;
   - any killer-stream lookup by survivor Id would break, so prove there is none, e.g. a fake whose killer view uses random nonzero fake Ids.
4. **Unknown packets:**
   - unknown-Id frames interleaved in every stream: H1–H6 still pass, and the skip counts appear in evidence;
   - a known Id with a bad payload or trailing bytes still fails;
   - a bad length still fails;
   - killer X/Z inside an unknown frame sent to a survivor fails H6;
   - an unknown frame with an Id that collides with a future v0.3 id (0x10) is skipped.
5. **Regressions:** all earlier QA suites (U3, U3b) still mean what they meant. Run `dotnet test PingTheKiller.sln` 3 times, plus one loaded run, and report any non-deterministic outcome.

## Rules
- Never weaken, skip or delete a failing assertion.
- A false pass on a gate (H3) is a **blocker**. A false fail on a gate under realistic bot movement is a **major** finding.
- Every finding needs: severity, reproduction (test name), expected vs actual, evidence.

## Report
`docs/qa/U3c-qa-a1.md` must contain:
- a table: fake → expected → actual (Id mode / anonymous mode);
- the unknown-packet results;
- stress counts;
- the findings;
- as its last line, exactly `QA-PASS` or `QA-FAIL`.
