# U2b QA report, attempt 2 (re-verification of implementation attempt 3)

VERDICT: PASS

QA: Claude Sonnet 5.5 (implementer of attempt 3: Claude Opus, quota fallback). Branch `claude/u2b-rules`, nothing committed. Files touched: `Server.Tests/QA/**` and this report. Reviewed: `docs/qa/U2b-impl-a3.md`, `GameServer.cs`, `GameServer.Lobby.cs`, `GameServer.Rules.cs`, `Player.cs`, `ServerOptions.cs`, `Server/Program.cs`.

## 1. Results

| Check | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror --no-incremental` | PASS, 0 warnings, 0 errors |
| `dotnet test PingTheKiller.sln`, Server.Tests | **662 total, 662 passed, 0 failed** (attempt 1: 624; +32 QA tests) |
| `dotnet test PingTheKiller.sln`, Harness.Tests | 150 total, 150 passed |
| Sandbox harness, 3 fresh servers (ports 17831-17833, `--bots 0 --bind 127.0.0.1 --sandbox`), each stopped afterwards, no server process left | **90/100, 90/100, 90/100**, gates passed 3 of 3 (H1 25, H2 20 with p99 63.5-63.7 ms, H3 20, H4 15, H5 10, H6 0 skipped "needs U2b") |

Harness reports: `artifacts/u2b-qa2-harness-{1,2,3}.json`.

## 2. Status of attempt-1 findings

| Id | Attempt 1 | Now | Evidence |
|---|---|---|---|
| F0 execution grace | major | **FIXED** | `Capture_ExecutionGraceTwoSecondsAfterCapture_...` passes, plus 11 boundary cases and 4 new scenarios below |
| F4 wire seed predicts hidden randomness | major | **FIXED** | attack kept, now fails; trace invariance; stream scan (below) |
| F2 Seq filter not reset | minor | **FIXED** | `Result_SeqIsResetAtMatchStart_...` (inverted), 3-match test, rate-window test, reconnect test |
| F1 invalid enum bytes close the connection (codec) | minor | open, unchanged | not in the scope of attempt 3 |
| F3 decoy Id tracking reveals 81 % of White Noise decoys | minor | open, unchanged | decoy placement was not touched; the measurement in `U2b-qa-a1.md` section 4 still stands |
| F5 harness cannot score real rules | minor, process | open | H6 still skipped; harness scores only sandbox rules |

## 3. Inverted characterisation tests

- `U2bAdversarialTests.Result_SeqIsNotResetAtMatchStart_Characterisation_U4QA07` is now `Result_SeqIsResetAtMatchStart_ClientMayRestartAtOne_U4QA07_FixedInAttempt3`: the very first input of the new match (Seq 1) moves the survivor, ten inputs move it 1.7-2.0 m, and an old Seq inside the new match is still dropped (the filter was not removed).
- `U2bSweepTests.Seed_MatchStartSeedFromTheWire_..._Finding` is replaced by three tests around the same attack code (`AttackWhiteNoise`: replay `Random(wireSeed)` the way the server consumed it: killer index, one skill draw per player, then x, z, yaw per White Noise point):
  - `Seed_AttackControl_WhenTheHookMakesPrivateSeedEqualTheWireSeed_...`: positive control. With the test hooks forcing private seed == wire seed the attack predicts every decoy, so the attack code is correct and a failure in production mode is meaningful.
  - `Seed_FixedInAttempt3_ReplayingTheWireSeedNeverPredictsAnyWhiteNoiseDecoy_ManyMatches`: production seeding, 12 fresh servers, the wire seed fixed by the hook so the attacker's input is known: **0 of 36 decoy points predicted**.
  - `Seed_FixedInAttempt3_AttackerKnowing...StillMissesOnSecondAndThirdMatch`: same attack in match 1, 2 and 3 of one server: 0 hits.

## 4. Adversarial verification of the three fixes (new file `Server.Tests/QA/U2bA2Tests.cs`, 30 tests)

### F0 grace (7 tests, 21 cases)
- `Grace_LeaveAfterCaptureThenTouchAgain_ExecutesOnlyFromTwoSecondsOn` (11 cases over 3 tick lengths): the killer leaves right after the capture and touches again at tick k. No execution at 1.95 s (0.05 s ticks, tick 39), execution at exactly 2.00 s (tick 40) and 2.05 s; the same boundary holds for 0.1 s ticks (19 vs 20) and 0.025 s ticks (79 vs 80). If the touch was inside the grace, staying in contact for 10 more seconds never executes (the touch is spent), and a leave plus touch afterwards executes.
- `Grace_TouchInsideGraceThenLeavingRearmsAtOnce_ReEntryAfterTheGraceExecutes`: several touches inside the grace, each spent; leaving re-arms; the first re-entry after 2 s executes.
- `Grace_ContinuousContactFromTheCaptureNeverExecutes_ForTenSeconds_...`: 10 s of continuous contact with jitter, then leave plus touch executes.
- `Grace_EachSurvivorHasItsOwnTwoSecondTimer`: S0 captured at tick 0, S1 at tick 20; at tick 42 the killer touches both: S0 (2.1 s) is executed, S1 (1.1 s) is not; S1 is executed at 2.05 s.
- `Grace_ThreeMatchesBackToBack_NoStaleArmedOrGraceState_AcrossMatches`: matches 1 and 2 end with the survivor Lagging and armed; the next match's capture is again protected for 2 s; the execution after the grace works in match 3.

Judgement call confirmed against GDD 3.3 (row of 10/27): "a touch during the grace uses up the re-entry" matches the rule text "execution needs leaving 1.2 m and touching again": a touch inside the grace never executes and contact that persists past 2 s is not a new touch. A killer that is already outside 1.2 m when the grace ends executes on the first touch.

### F4 no path from `MatchStart.Seed` to server randomness (6 tests)
- `Seed_WireSeedHasNoInfluenceOnAnyGameplayRandomness_FullTraceIdenticalForEveryWireSeed` (theory: host Killer, Survivor, Random): deterministic. The private seed is fixed by the hook (77); the wire seed takes 9 values (0, -1, 2, 77, 424242, int.MaxValue, int.MinValue, 77*7919+17, 1). Over **three consecutive matches with 1 human + 7 bots** the complete observable trace (SHA-256 over every encoded packet except MatchStart/JoinResult) and the server's ground truth (every player's role and skill at each start, every position every 25 ticks) are **bit-identical**. That covers killer assignment, all skill draws, bot patrol, detours and skill use, White Noise points and decoy Id picks. The baseline is run twice first to prove the harness is deterministic.
- `Seed_PrivateSeedChangesTheWholeTrace_SoTheInvarianceAboveIsNotVacuous`: six private seeds give six different traces (hash and truth), so the invariance is a real property.
- `Seed_StreamScan_NoServerRandomStreamIsAnyOffsetOfAStreamSeededFromTheWireSeed_AcrossThreeMatches`: reads `_random` and `_decoyRandom` by reflection after 40 ticks of each of 3 matches, 5 wire seeds (including int.MinValue/MaxValue, 0, -1), and searches the first 20 000 outputs of `Random(c)` for the 12 draws, for 11 candidate functions c of the wire seed (identity, negation, complement, +-1, `*7919+17`, xor, LCG and multiplicative transforms). No hit. Positive control: with the legacy coupling both streams are found.
- `Seed_EveryMatchReseedsBothPrivateStreamsAndDrawsAFreshWireSeed_AllHumansAgreeOnTheWireSeed`: three matches give three different `Random` objects for each stream and three different wire seeds, all humans of a match see the same value; with the private-seed hook the stream is deliberately not reseeded.
- `Seed_GameLogDoesNotPrintAnyPrivateSeed`: log shows only the cosmetic match seed.
- `Seed_ProductionSeeding_ServersWithTheSameWireSeedPlayDifferentMatches_LongTraceDesign`: the deterministic replacement for the implementer's flaky-by-design test (see F6): four production servers with the same wire seed produce four different traces (three matches, roles, skills, positions, packets).

Code review of the seeding path: the only `new Random(` calls are in the constructor and `ReseedForMatch`; both are fed either by the hook or by `RandomNumberGenerator`; `Server/Program.cs` and the Unity client never set `Seed` or `MatchSeedForTests`; no `Environment.TickCount`/`DateTime` based seeding exists in `Server.Core`, `Server` or `Shared`.

### F2 two and three matches back to back (3 tests)
- `Matches_ThreeBackToBack_BothRolesRestartSeqAtOneEachTime_AndNothingIsHeldOverTheBoundary`: three matches; before each restart both players send Seq 100001 while moving in the last tick; in the new match both restart at Seq 1 and are obeyed from the first input; neither keeps a held direction across the boundary.
- `Rate_WindowIsClearedAtMatchStart_ButStillLimitsSixtyInputsPerSecondInsideAMatch`: 60 inputs accepted and the next 40 dropped inside a match; after saturating the 1 s window right before the match ends, the first input of the next match (0.25 s later) is accepted.
- `Reconnect_WithTheTokenMidMatch_AcceptsASequenceRestartingAtOne`.

## 5. Findings (open)

### F6 [minor, test design] `EqualWireSeedsDoNotGiveEqualBotPaths` compares too little entropy
Question 3 of the brief: the test is **probabilistic by design**. It compares only 10 s of survivor positions plus the human's own skill and the bot casts of two CSPRNG-seeded servers. The information that decides those observations is small (which of 7 bots is the killer, the human's skill, which bots cast early), so a collision of two honest random servers is possible, and the reported failure ("about 1 in 7") is consistent with the killer index alone.
- Measurement, current code: **0 failures in 1500 in-process runs** and **0 failures in 25 fresh-process runs** of the whole `MatchSeedAndRestartTests` class, plus 0 of 300 on every suite run (`Measure_ImplementerTest_EqualWireSeedsDoNotGiveEqualBotPaths_FailureRateOver300Runs`). The residual failure probability is therefore below about 0.2 % per run, not 1/7, so I could not reproduce the one-off failure; it may come from an earlier state of the test. The design remains non-deterministic and should not be relied on.
- Deterministic replacement design (implemented in `U2bA2Tests`, no edit to `Server.Tests/Core` was made): (a) fix the private seed by the hook and vary only the wire seed, then assert the traces identical (deterministic, no collision possible); (b) vary the private seed through the hook and assert the traces differ (deterministic); (c) for production seeding compare long traces (roles, skills, positions and packets of three 8-player matches, over 50 bits) or the private `Random` streams themselves. All three are in section 4. The implementer's test can be deleted or reduced to the (c) form.

### F7 [minor, hardening] the private gameplay RNG has only 31 bits of seed
`ReseedForMatch` seeds `System.Random` with a 32-bit CSPRNG value, and `Random(x) == Random(-x)`, so the state space is 2^31 and the generator is not cryptographic. Measured: 873 ns per candidate seed (construct plus two draws) on one core, so a complete sweep of 2^31 seeds costs about **31 CPU-minutes**, seconds on a multi-core machine. The attack needs observations to filter candidates (own killer slot and skill, other players' skills when they cast, the first White Noise points); in a human-only match that is very few, with bots the stream is interleaved with bot decisions, which makes the replay much harder but not impossible for a determined cheater who reimplements the simulation. This is not exploitable through the protocol as shipped (the wire seed no longer helps), hence minor.
- Recommendation: draw the hidden decisions (White Noise points, decoy Id picks, bot patrol decisions that are observable) from a generator with 128 bits or more of state seeded from `RandomNumberGenerator` (e.g. xoshiro256** seeded with 32 random bytes), or call `RandomNumberGenerator` for the low-frequency hidden draws (White Noise, decoy picks).
- Evidence: `Measure_PrivateSeedSpace_SystemRandomTakesAtMostThirtyOneBits_BruteForceCost`.

### Still open from attempt 1 (unchanged, minor)
F1 (the Shared codec closes the connection for `Interact` > 1, `SetRole` outside {0,1,255}, `SetBots` > 7 instead of ignoring the packet), F3 (decoy Id tracking, recommendations in `U2b-qa-a1.md` section 4) and F5 (harness has no real-rules scoring; H6 skipped).

## 6. Rules 1-14 (after attempt 3)

| # | Rule | Result | Note |
|---|---|---|---|
| 1 Interact | PASS | F1 note (wire level) unchanged |
| 2 Hacking | PASS | |
| 3 Capture, grace | **PASS** | F0 fixed; GDD 10/27 row verified at tick resolution |
| 4 Lagging | PASS | |
| 5 Rescue | PASS | |
| 6 Escape and win order | PASS | |
| 7 Match end | PASS | Seq and rate window reset (F2 fixed) |
| 8 Skills | PASS | |
| 9 Decoys | PASS on the letter; F3 open (placement vs Id tracking); F4 fixed | |
| 10 SkillEffect visibility | PASS | |
| 11 Masking | PASS | leak sweep green on every frame, 42 matches |
| 12 Bots | PASS | |
| 13 Input validation | PASS | F1 note |
| 14 `--bind` | PASS | |

## 7. Files

- New: `Server.Tests/QA/U2bA2Tests.cs`.
- Edited (QA-owned): `U2aHelpers.cs` (`QaFixture` gains `matchSeed` and `csprng`), `U2bHelpers.cs` (`QaRig` gains the same options and `NextMatch()`), `U2bAdversarialTests.cs` (F2 test inverted), `U2bSweepTests.cs` (F4 test replaced by three tests around the same attack).
- Re-run only the new coverage: `dotnet test Server.Tests --filter "FullyQualifiedName~U2bA2Tests"`.

VERDICT: PASS
