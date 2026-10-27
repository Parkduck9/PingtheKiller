# QA brief U3b — attempt 1: H6 cannot pass a broken server; flaky fix is real

QA agent: Claude Sonnet (implementer was Claude Opus — different model). Read `AGENTS.md`, `docs/briefs/U3b-a1.md` (spec), `docs/qa/U3b-impl-a1.md`, the server contract `C:\Unity\MultiPlayGame\PingtheKiller\docs\briefs\U2b-a1.md`, `Tools/Harness/README.md`, and the code under `Tools/Harness/**` and `Tools/Harness.Tests/**`.

## Allowed writes (nothing else)
- `Tools/Harness.Tests/QA/U3b/**` (new tests; compile into `Harness.Tests`)
- `docs/qa/U3b-qa-a1.md` (report)
Never edit product code or existing tests. Do not commit, switch branches or touch git stash.

## Main job: false-pass hunting for H6
Using your own fakes (or the implementer's fake if it is reusable without editing it), build servers each broken in exactly one way and assert H6 **fails** (score 0) — plus a correct control that passes:
1. GameResult: never sent; sent twice to one client; sent to only 2 of 3 humans; `EscapedIds` empty / both survivors / the wrong survivor / contains the killer; `WinnerRole` Killer; `Reason` AllSurvivorsDown or TimeUp; copies differ between clients.
2. Killer-coordinate leaks to survivors: killer `PlayerState` in a survivor WorldState (by Id; and with Role Killer under a fresh Id); killer X/Z inside a survivor-bound `SkillEffect`; killer X/Z appended to a `LobbyState`/`Pong`/`ThreatLevel`-adjacent bytes; a leak that happens only once in the whole scenario (e.g. one frame during the retreat); a leak where the killer's coordinate coincides with a legitimate survivor field value **and** an extra copy is present.
3. Scenario steps faked or skipped: exit opens without terminals reaching 100; capture leaves status Normal; Lagging without `CaptureCount` 1; rescue never completes; server ends the match with SurvivorEscaped before anyone reached the exit; the killer executes the Lagging survivor (Eliminated) — H6 must not report pass.
4. Navigation robustness: a server whose map matches `FixedMap` but whose movement is 10% slower than spec still passes (leg timeouts have slack); a server that never moves a player fails with a clear "stuck" evidence within bounded time.
5. Bounded time: silent server after MatchStart, server that closes one survivor socket mid-scenario → H6 fails within its deadline.

## Also check
- `--h6-port` rules exactly as the spec (incl. fail-fast without network I/O when H6 is combined with H2–H5 and no distinct port), `--help` text, README two-server instructions.
- Server.Core guard: the harness uses only `PingTheKiller.ServerCore.Map`; prove the guard test would catch a violation (e.g. reason about or demonstrate on a scratch copy outside the repo — do not edit product code).
- Scorer: six passes = 100; JSON shape unchanged; H6 no longer "skipped".
- Part B flaky fix: identify which test was changed, judge whether the fix weakened what it checks, and stress it: run `dotnet test PingTheKiller.sln` 3 times and Harness.Tests concurrently with Server.Tests under load at least 3 times; report any non-deterministic outcome.

## Rules
Never weaken, skip or delete a failing assertion. H6 passing against a broken fake is a **blocker**. Every finding: severity, repro (test name), expected vs actual, evidence.

## Commands
`dotnet build Tools/Harness/Harness.csproj -warnaserror`, `dotnet test Tools/Harness.Tests/Harness.Tests.csproj`, `dotnet test PingTheKiller.sln`.

## Report
`docs/qa/U3b-qa-a1.md` (English): broken fake → expected → actual table; `--h6-port`/guard/scorer results; flaky-fix assessment with stress counts; findings; last line exactly `QA-PASS` or `QA-FAIL`.
