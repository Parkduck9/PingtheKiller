# QA brief U3 — attempt 1: prove the harness cannot pass a broken server

QA agent: Claude Sonnet (implementer was Codex gpt-6.1-sol — different family). Read `AGENTS.md`, `docs/briefs/U3-a1.md` (the implementation spec), `docs/briefs/U3-a2.md` (fix list now applied), `docs/PLAN.md` §5, `docs/qa/U3-impl-a2.md`, and the code under `Tools/Harness/**` and `Tools/Harness.Tests/**`.

## Allowed writes (nothing else)
- `Tools/Harness.Tests/QA/**` (new test files; they compile into the existing `Harness.Tests` project)
- `docs/qa/U3-qa-a1.md` (your report)

You never edit product code (`Tools/Harness/**`, existing files in `Tools/Harness.Tests/` outside `QA/`, `Shared/`, anything else). If a test needs a hook the harness does not expose, record that as a finding instead of changing the harness. Do not commit.

## Main job: false-pass hunting
Build fake TCP servers (in `Tools/Harness.Tests/QA/`, using the Shared serializer/`PacketAssembler`) that are each broken in exactly one way, run the matching harness check against them through the harness's public entry points, and assert that check **fails** (passed = false, score < max). Also run a correct fake for each where practical and assert it passes, so a fail is not just "everything fails".

1. **H1** — server drops one pong; server reorders two pongs; server alters one timestamp; server sends a duplicate pong; server closes the connection after N pongs. Each must fail H1.
2. **H3** — killer-bound WorldState still contains the stopped survivor's entry (flag only); killer-bound frame omits the entry but carries the survivor's last X/Z float bytes somewhere else in the payload; survivor-bound WorldState contains the killer's Id. Each must fail H3.
3. **H4** — server crashes/stops answering Ping after a Length = -1 frame (control client then fails); server accepts direction (100,100) and moves the mover faster than max × 1.05; server answers nothing after int.MaxValue length. Each must fail H4.
4. **H2** — WorldState interval p99 > 120 ms, or a Tick gap > 2. Must fail H2.
5. **H5** — rejoin with token returns a different PlayerId. Must fail H5.

## Also check
- Scorer math: totals, threshold edge (exactly 90 passes, 89 fails), a failed gate with total ≥ threshold → exit code non-zero.
- JSON shape exactly matches `{ total, gatesPassed, items: [{ id, score, max, gate, passed, evidence }] }` (property names and casing).
- H6 reports skipped, score 0, passed false, and is never counted as passed.
- Every network wait has a timeout: a fake server that accepts and then stays silent must make a check fail within a bounded time (put an xUnit timeout or a stopwatch assertion on it) — the harness must never hang.
- Flakiness: run the full `dotnet test` three times; report any test that changes result.

## Rules
- Never weaken, skip, or delete a failing assertion to get green. A harness check that passes against a broken fake is a **blocker** finding — leave the test failing (or, if you must keep the suite green for other checks, mark it with a clear `[Fact(Skip = "QA-BLOCKER: ...")]` only after recording it as a blocker in the report).
- Every finding: severity (blocker/major/minor), reproduction (test name), expected vs actual, evidence.

## Commands
`dotnet build Tools/Harness/Harness.csproj -warnaserror`, `dotnet test Tools/Harness.Tests/Harness.Tests.csproj`.

## Report
`docs/qa/U3-qa-a1.md` (English): table of each broken fake → check → expected fail → actual; scorer/JSON/exit-code results; flakiness results; findings list; last line exactly `QA-PASS` or `QA-FAIL`.
QA-PASS only if every broken fake makes its check fail, all checks fail within bounded time on a silent server, and no blocker/major findings remain.
