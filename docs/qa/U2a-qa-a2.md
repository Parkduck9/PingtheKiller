# U2a QA report, attempt 2

QA model: Claude Sonnet 5.5 (independent of the attempt-2 implementer, Claude Opus). Build: `dotnet build PingTheKiller.sln -warnaserror` PASS (0 warnings, 0 errors). `dotnet test PingTheKiller.sln`: **485 total, 485 passed, 0 failed**. QA tests (`Server.Tests/QA/U2a*`, theory cases counted): **106 total, 106 passed, 0 failed** (56 attempt-1 tests, of which 6 were rewritten, plus 50 new). The QA suite was run 6 times (3 before and 3 after the final edit) with no flakes. TCP tests use loopback, ephemeral port 0, every wait <= 5 s, port 7777 never used. A 4 s smoke run of `Server --port 0 --bots 7 --autostart` logged a match start without exceptions.

Files: `U2aHelpers.cs` (unchanged), `U2aAdversarialTests.cs` (rewrites below), `U2aNetworkTests.cs` (unchanged), `U2aA2Tests.cs` (new, deterministic and Start()-driven in-memory), `U2aA2NetworkTests.cs` (new, TCP).

## Rewrites (spec changes only; nothing weakened)

1. `Seq_UintWraparound_IsNotHandled_SpecLiteralBehaviour` (a2 fix 4, serial-number comparison): replaced by `Seq_UintWraparound_IsAcceptedInOrder_StaleAfterWrapDropped` (MaxValue-1, MaxValue, 0, 1 all accepted and each steers its own direction; older values after the wrap, incl. MaxValue, MaxValue-1, 1, 0, 2 dropped), `Seq_FirstInputWithUintMaxValue_IsAcceptedAndSessionKeepsWorking` (the attempt-1 hazard now passes), and `Seq_HalfRangeJump_Exactly2Pow31IsDropped_2Pow31MinusOneIsAccepted`. My call on the ambiguous case: a jump of exactly 2^31 gives (int)int.MinValue <= 0 and is DROPPED; 2^31-1 is accepted.
2. `InputSanitizing_NeverNaN_NeverFasterThanRoleSpeedTimesDt` (13 cases): the adversarial input is now resent every tick with a fresh Seq (a single input only holds 0.15 s, fix 1). All checks are kept (finite, step <= cap, non-finite/zero stops) and strengthened: oversize vectors (10, (1,1), 1e30, MaxValue) are checked for full-speed clamping on all 7 ticks instead of one.
3. `RateCap_AtMost60InputsPerSecondAccepted_ThenWindowReopens`: expected motion after the burst changed from about 3.8 m to about 0.4 m (60th input holds 0.15 s). Kept: none of the 140 later inputs applies; the window reopens after >= 1 s. Added: an input at T+0.95 s (same window) is still rejected.
4. `Seq_OutOfOrderAndDuplicateAreIgnored_FirstSeqZeroAccepted`: a fresh accepted +X input renews the hold in each tick and the stale/duplicate packets follow it in the same tick. Kept: older ignored, exact duplicate ignored, newer accepted, first Seq 0 accepted.

`Masking_MovingSurvivorIsNeverOmittedEvenWithoutFurtherInputs` passes unchanged.

## Attempt-1 findings

| Finding | Status | Evidence |
|---|---|---|
| Major: silent-move bypass | FIXED | masking test passes unchanged; new `OneInputThenSilence_*`, `TwentyHertzResend_*`, `FiveHertzInputs_*`, `KillerHeldDirectionAlsoExpires_*`, `BotAndHumanSurvivors_NeverMoveWhileAbsent_ManySeeds` (6 seeds x 1000 ticks), `RandomInputPatterns_VariableDt_*` (15 seeds, dt 0.01-0.20 s incl. NaN/huge inputs, 0 violations) |
| F2 accept loop dies / Stop rethrows | FIXED | `Accept_ThrowingSubscriberOnFirstClient_*`, `Accept_SubscriberThrowsForEveryClient_*` (also throwing Log subscriber), `Accept_ClosedGameDoesNotBreakTransport_*`, `Accept_StopThenRestart*` |
| F3 loop thread has no catch | FIXED | `GameLoop_ThrowingLogSubscriber_*` (Start() running, ticks continue, other subscribers still log), `GameLoop_ThrowingConnectionIdFaults_*` (10 tick faults, 1 log line, loop recovers), `ConnectionWhoseSendThrows_IsClosedAlone_*` |
| F4 Seq wrap-around | FIXED | rewrites above |
| F5 half-open connections | FIXED (connection-count cap still absent, see N1) | `TokenTakeover_*` (in-memory x3, TCP x2), `JoinTimeout_*` (10 s of ticks, Ping before Join answered, joined/late joiner kept, rejected join still times out, 300 idle connections), `Tcp_AcceptedConnectionHasKeepAliveEnabled` (reflection) |
| F6 nickname format chars | FIXED | `Nickname_*` (format chars, lone surrogates, 10 long inputs incl. emoji at the byte limit, longest-prefix, over-the-wire lobby) |
| F7 12 m threat ambiguity | OPEN (docs only, unchanged) | |
| F8 SIGTERM | FIXED by code review | `Program.cs`: `PosixSignalRegistration.Create(SIGTERM)` sets Cancel and the shutdown event, falls back on PlatformNotSupported, stop errors caught and logged. Not testable in-process. |
| Killer spawn (fix 8) | FIXED | `Spawns_*`, `EveryKillerIndexAndSeed_AllSurvivorsStartAtThreatLevelZero` (n = 2..8 x 80 seeds, every killer index seen, distance > 18 m), `ForcedKillerHost_AtEveryIndex*` |

## Cross-unit contract (Unity client)

`CrossUnitContract_30HzInputsForOneSecond_ThenSilence_MovesContinuouslyStopsAndDropsOut` PASS: 30 Hz inputs over 20 Hz ticks for 1 s, then nothing. 20 consecutive ticks move at full speed (0.2 m, +-2%), all visible with Signal; the last move is at t=1.05 s (limit 1.167 s); the entry leaves the killer frame at t=1.10 s and never returns; no tick moves while absent.

## Re-review of new code (task 4)

- Loop-thread ownership holds: takeover, join timeout and `_connections`/`Binding` state are touched only in `Tick` under `_tickGate` (AddConnection only enqueues). Closed handlers are detached before the stale connection is closed, so no double removal. Takeover resets Seq, rate window and direction, preserves host and token.
- No unbounded growth: `_faultLogs` holds only the current second's distinct messages, `_expiredScratch` is cleared in `finally`, players <= 8.
- No Linux-incompatible API (`PosixSignalRegistration`, plain `SetSocketOption(KeepAlive)`).

## New findings

- **[minor] N1: no cap on concurrent unauthenticated connections** — repro: open thousands of sockets and never Join. Expected: bounded. Actual: each lives 10 s and is then closed, but nothing limits the count within that window (and `ExpireUnjoined` scans connections x players per tick). Evidence: code review plus `JoinTimeout_ManyIdleConnections_AllClosed_NoStateLeak` (300 fine). Recommend a per-IP/global cap in U5.
- **[minor] N2: nickname sanitizer lets invisible non-Format characters through** — repro: `SanitizeNickname("ㅤ")`, `"⠀"`, `"ᅟ"` return the character itself (an invisible-only name instead of `Player N`); `"a b"` keeps the line separator. NBSP is trimmed correctly. Expected: not required by the a2 spec (only Category Format is). Actual: spoofable blank names remain. Evidence: `Nickname_InvisibleNonFormatCharacters_Characterisation` (informational, asserts only the byte cap).
- **[minor] N3: Log subscribers can run on I/O threads** — `GameServer.Send` is also called from `DrainConnection` (Ping reply) on the connection's receive thread; a send error there invokes `Log` subscribers off the loop thread. Harmless today (subscribers are isolated), but `Log` handlers must be thread-safe. Not reproduced as a defect.

No blocker or major findings.

QA-PASS
