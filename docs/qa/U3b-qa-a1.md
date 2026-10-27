# U3b QA report — attempt 1 (H6 scenario check + flaky-test fix)

QA agent: Claude Sonnet (implementer: Claude Opus). Branch `claude/u3b-h6`, nothing committed. Tests written: `Tools/Harness.Tests/QA/U3b/**` (own rules fake `QaRulesServer`, 48 broken-fake classes, controls, CLI/scorer tests, leak-scan unit tests). No product code or existing test was edited.

## Verdict: FAIL

Two blockers (H6 passes against a broken server) and one major (H6 can fail spuriously against a correct server). The QA tests that prove them are left red on purpose; none was weakened.

## 1. Broken fake -> expected -> actual

Expected for every row: H6 score 0, bounded time. "FALSE PASS" = H6 scored 10 against a broken server.

| Broken fake (own fake `QaRulesServer`) | Actual | Evidence (shortened) |
|---|---|---|
| Correct control | PASS, 10 | 49 s, 0 executions |
| Movement 10 % slower than spec | PASS | slack works |
| Movement 20 % speed | FAIL | `[hack terminal 0] A stuck on leg ... (limit 4.9 s for 5.1 m)` after 5 s |
| Players never move | FAIL | `stuck on leg 'A to terminal 0': at (5.00,26.00), waypoint, target after 4.9 s` (5 s) |
| Silent right after MatchStart / after 2 s / during capture | FAIL | `server sent nothing for 3.0 s` or `no WorldState within 3.0 s`, 3 / 5 / 38 s |
| Survivor B socket closed during hack / after capture; lagging A closed; killer closed | FAIL | `B: IOException: Server disconnected...`, 3-29 s |
| Exit already open at tick 0 | FAIL | `[setup] ... ExitOpen=1; expected ... exit closed` |
| **Exit opens at 5 s although no terminal is complete** | **FALSE PASS** | passes in 49 s |
| Terminal progress never shows 100 | FAIL | `Progress == 100 ... not observed within 18.0 s` |
| Capture leaves Normal | FAIL | `[capture: killer to A] A captured ... not observed` |
| Lagging with CaptureCount 0 / 2; Lagging with StatusTimer 0 | FAIL | `expected Lagging/1/>0` |
| Killer execution on capture | FAIL | `player 2 has status 3` |
| Rescue returns CaptureCount 0 | FAIL | `[rescue] A rescued ... not observed within 8.0 s` |
| Rescue flips the rescuer to Lagging | FAIL | `[rescue] A received an early GameResult` |
| Result the moment rescue completes | FAIL | `[rescue] ... early GameResult` |
| **Valid SurvivorEscaped result 1 s after the escaper starts walking (46 m from exit)** | **FALSE PASS** | passes in 37 s, evidence says `escape: B walked ...` |
| GameResult never sent; to killer only; to killer not; to one survivor only; skip standing survivor | FAIL | `No S2C_GameResult...`, `[GameResult] killer/A: no S2C_GameResult within 3.0 s` |
| GameResult twice to survivor (same tick / 0.5 s later); killer's copy differs; B's copy differs | FAIL | `received 2 S2C_GameResult`, `GameResult ... expected exactly [3]` |
| EscapedIds empty / both / wrong / containing killer | FAIL | `EscapedIds=[]`, `[2,3]`, `[2]`, `[3,1]`; `expected exactly [3] (B)` |
| WinnerRole Killer; Reason TimeUp / AllSurvivorsDown; killer wins AllSurvivorsDown | FAIL | `winner=1`, `reason=2`, `reason=1` |
| Killer PlayerState by Id (every frame: `[setup]` at tick 1; once during retreat) | FAIL | `Killer 1 leaked to survivor at tick=576` |
| Role Killer under fresh Id (once) | FAIL | `leaked to survivor` |
| Survivor-shaped fresh-Id entry with killer X/Z (once) | FAIL | `killer coordinate 11.78 bytes leaked: 1 occurrence(s) ... only 0 legitimate` |
| Killer X/Z in SkillEffect, killer caster (once); only X (once) | FAIL | `bytes leaked ... S2C_SkillEffect` |
| Killer X/Z in an unsolicited Pong timestamp (once) | FAIL | `... S2C_Pong (14 bytes)` |
| Killer Z in a LobbyState slot id (once) | FAIL | `... S2C_LobbyState (74 bytes)` |
| Killer X in a legitimate survivor field plus a second copy elsewhere (two variants) | FAIL | `2 occurrence(s) ... only 1 legitimate` |

The brief's "lagging survivor executed" case fails correctly. Timing: an input delay of 2 ticks makes the fake execute A and H6 then fails (`player 2 has status 3`). A delay equal to normal next-tick processing passes. This depends on the pending U2b rule decision: a server that applies inputs one tick late, or a harness reaction slower than 50 ms, would make H6 fail on the real server. Concurrent load (below) did not trigger it.

## 2. Findings

| # | Severity | Finding | Test |
|---|---|---|---|
| F1 | blocker | H6 passes when the server ends the match with a valid `SurvivorEscaped` result before the escaper is anywhere near the exit. `EscapeAsync` calls `WalkAsync(..., endOnResult: true)`, which returns silently on any GameResult. The distance check exists only in the no-result branch. Expected: fail. Fix: after the result, require the escaper's last observed position within `ExitRadius` plus two ticks of travel. | `U3bResultBeforeAnyoneReachesExitTests` (red) |
| F2 | blocker per brief item 3 | H6 passes when `ExitOpen == 1` appears before the required terminals are complete. Only the first WorldState is checked for a closed exit. Fix: assert on every frame that `ExitOpen == 1` implies at least `TerminalsRequired` terminals at Progress 100. | `U3bExitOpensWithoutTerminalsTests` (red) |
| F3 | major | The raw any-alignment byte scan produces false positives on clean frames. Round killer floats, such as the spawn (5.0, 5.0) with bytes `00 00 A0 40`, can match across the entry's `Role`/`Status` zero bytes plus the low two bytes of a survivor X (`0x412040A0`). Live repro: a correct fake failed H6 with `B: killer coordinate 5 bytes leaked: 1 occurrence(s) ... only 0 legitimate` (QA run, tick 203). Measured 9 flagged of 200,000 random clean 2-survivor frames (0.0045 %). One run has about 1,200 frames while the killer stands at spawn, so roughly 5 % of runs fail spuriously (10 points). Suggested fix: compare decoded float and int fields, or skip raw windows for values with two or more zero bytes. | `U3bLeakScanUnitTests.CleanSurvivorFrameIsNotFlaggedWhenFieldBoundariesFormAKillerFloat` (red) |
| F4 | minor (by spec) | Legitimate-field tolerance can be laundered. A killer X/Z copied into one survivor entry's X/Z, or into a SkillEffect with a survivor `CasterId`, is not detected. Characterized, passes, labelled `KNOWN-LIMIT`. | `U3bKnownLimit*` |
| F5 | minor (by spec) | A duplicate GameResult arriving more than 1 s after the first copy is not seen (drain is 1 s). | `U3bKnownLimitDuplicateResultAfterDrainTests` |
| F6 | minor | H6 does not check rule magnitudes. Terminals completing 20x faster and a 0.1 s rescue still pass. Evidence prints the durations but nothing asserts them. | `U3bKnownLimitFastRulesTests` |
| F7 | info | The same failure produces different texts (`server sent nothing for 3.0 s` or `no WorldState within 3.0 s`). Harmless, but do not assert on one. | |
| F8 | info | The `--h6-port` same-server test is a case-insensitive string compare. `localhost` and `127.0.0.1` on the same port are treated as different servers (README says string comparison). | |

## 3. `--h6-port`, `--help`, guard, scorer

- `--h6-port` rules match the spec: `--only H6` uses `--port` and `--host`; H6 with each of H2-H5 and no distinct port gives exact `H6 needs its own fresh server: pass --h6-port`. No H6 connection was made (a counting listener saw the same connections for `--only H5` and `--only H5,H6`). Same port on another host is allowed. Invalid values are rejected.
- A default run against a closed port exits 1; the JSON has 6 items with unchanged property names, none "skipped". `--help` and the README describe the two-server run (`--sandbox` and normal), `--h6-host`, and the fail-fast rule.
- Server.Core guard, on a scratch copy outside the repo: a non-Map `ServerCore` usage was caught by both the source scan and the metadata scan. A newline-split `ServerCore` token was caught. An extra PackageReference was caught by the csproj test. A Map-only addition stays green, as intended. Blind spot: runtime reflection with a constructed type name (`Type.GetType("PingTheKiller.Server" + "Core.GameServer, ...")`) is not detected (info).
- Scorer: six passes give 100 and five passes without H6 give 90. JSON shape unchanged, H6 is a real check, and the default threshold stays 90.

## 4. Flaky-fix assessment

The change is in `QaServer.cs` (a fake, not a test): the naive decoder now discards the rest of a read when a garbage frame fails to decode, instead of letting `PacketFormatException` fault the connection task. The fake is still broken in the same way: it does not reassemble, drops data, and `BrokenServerFailsH1` still requires H1 to fail. Nothing was weakened. Evidence: `QaH1Tests` under 128 busy processes, old `QaServer` from HEAD (scratch copy) gave 6 of 6 loops failing (10 failing cases, `naive-framing-no-reassembly` and `one-frame-per-read`). The fixed code gave 7 of 7 loops at 20/20. The deterministic regression test passes. Assessment: the fix is real and sound.

## 5. Test counts

- `dotnet test PingTheKiller.sln` x3 (final test set): Server.Tests 485/485 each time. Harness.Tests 249 of 252 each time, with the same 3 red tests (F1, F2, F3). The 187 existing tests all passed in every run.
- Load stress x3: Harness.Tests concurrently with 12 consecutive Server.Tests loops, 24 busy processes and 12 concurrent correct H6 runs. 248 of 250 each time, only F1 and F2 red. Server.Tests 36 of 36 loops at 485/485. 36 of 36 concurrent correct H6 runs passed. No non-deterministic outcome except the F3 false positive seen once earlier in a half-speed run.
- Earlier QA-only runs: 6 more concurrent correct H6 runs, all passed.

## 6. Not verified

H6 against the real U2b server. U2b timing (execution within 1.2 m of a Lagging survivor) is a pending rule decision.

QA-FAIL
