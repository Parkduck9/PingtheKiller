# U2b QA report, attempt 1

VERDICT: FAIL (0 blockers, 2 majors)

QA: Claude Sonnet 5.5 (implementer: Claude Opus, quota fallback; different model from the implementer, same family as the orchestrator's fallback rule allows). Branch `claude/u2b-rules`, nothing committed. Files I touched: `Server.Tests/QA/**` and this report. Nothing else was edited.

## 1. Results

| Check | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror` | PASS, 0 warnings, 0 errors |
| `dotnet test PingTheKiller.sln`, Server.Tests | **624 total, 623 passed, 1 failed** (the failure is F0, a deliberate finding test) |
| `dotnet test PingTheKiller.sln`, Harness.Tests | 150 total, 150 passed |
| New QA tests (`U2b*.cs`) | 95 total (theory cases counted), 94 passed, 1 failed (F0). Plus 1 new sandbox variant in `U2aA2Tests` |
| Real-server harness, 3 fresh servers `--bots 0 --bind 127.0.0.1 --sandbox` | **90/100, 90/100, 90/100, gates passed in all 3** |

Harness runs (ports 17821-17823, reports `artifacts/u2b-qa-harness-{1,2,3}.json`, every server stopped afterwards, no process left):

| Run | H1 | H2 (p99) | H3 | H4 | H5 | H6 | Total | Gates |
|---|---|---|---|---|---|---|---|---|
| 1 | 25 | 20 (69.6 ms) | 20 | 15 | 10 (49.0 ms) | 0 skipped | 90 | pass |
| 2 | 25 | 20 (66.8 ms) | 20 | 15 | 10 (106.2 ms) | 0 skipped | 90 | pass |
| 3 | 25 | 20 (67.8 ms) | 20 | 15 | 10 (47.6 ms) | 0 skipped | 90 | pass |

Extra evidence (not one of the 3 required runs): the same harness against a server **without** `--sandbox` scores **25/100, gates failed** (H2-H5 time out because the harness killer stands idle and the survivor bots open the exit and escape). This confirms the implementer's note: the harness validates only U2a behaviour (sandbox); no automated socket-level check of the U2b rules exists (F5).

## 2. Findings

### F0 [major] GDD 10/27 "execution grace" is not implemented
- Source: `docs/GDD.md` section 3.3, row "처형 유예" (added 10/27, after `U2b-a1.md` was written; the working tree shows GDD.md modified): after a capture nobody can be executed for 2 s, and an execution needs the killer to leave 1.2 m and touch again.
- Implemented: the fresh-touch half (`ExecutionArmed`). Missing: the 2 s grace.
- Reproduction: killer captures a survivor, steps out of 1.2 m for one tick, touches again.
- Expected: survivor stays Lagging until at least 2 s after the capture. Actual: Eliminated 0.10 s after the capture (two quick touches kill a survivor in 0.1 s).
- Evidence: `U2bAdversarialTests.Capture_ExecutionGraceTwoSecondsAfterCapture_GddRuleAdded1027_NotInTheBrief` (FAILS, message "executed 0.10 s after the capture, inside the 2 s grace"). The brief predates the rule, so the orchestrator may treat it as a new requirement for attempt 2.
- Fix hint: in `SetStatus(Lagging)` set a `ExecutionGraceUntil = _now + 2`; `Capture()` must not execute (and may keep `ExecutionArmed=false`) while `Active(ExecutionGraceUntil)`.

### F4 [major] `S2C_MatchStart.Seed` lets any client predict the hidden White Noise decoys
- Reproduction: human-only match (no bot draws). A client takes `Seed` from `S2C_MatchStart`, replays `new Random(seed)`: one killer-index draw, one skill draw per player, then White Noise points (x, z, yaw).
- Expected: decoy positions are unknowable to the killer. Actual: every decoy position shown to the killer equals a point predicted from the public seed (3 of 3 shown decoys, exact float equality).
- Why it matters: the server sends its real RNG seed (`Seed = _seed`) while U2b draws hidden decisions from the same `_random` (White Noise points) and `_decoyRandom = seed * 7919 + 17` (borrowed Ids). With bots the stream is interleaved with bot draws (patrol targets, detours), which makes replay harder, but the first casts of a match remain predictable. A cheating killer client can filter White Noise exactly.
- Evidence: `U2bSweepTests.Seed_MatchStartSeedFromTheWire_LetsAnyClientPredictWhiteNoiseDecoyPositionsExactly_Finding` (passes, i.e. the leak exists).
- Recommendation: never send the RNG seed that drives gameplay. Send a cosmetic seed derived by a one-way hash (or a separate random value), and draw hidden decisions (decoys, bot choices, skills) from an RNG whose seed is not on the wire in online mode. Tests keep their explicit `ServerOptions.Seed`.

### F3 [minor, spec-level, recommendation requested by the HUD spec review] Decoy Id tracking
See section 4. Short answer: yes, a per-Id tracker reveals more than rule 9 intends for White Noise (81 % of decoys are provably fake, zero false positives); for Dummy Packet almost nothing (6 %), except the "re-appears at the cast point" case. Rated minor because it needs a modified client or a HUD that tracks identities, and rule 9 itself prescribes random walkable points; the orchestrator may raise it to major.

### F1 [minor] Out-of-range enum bytes close the connection over the real wire
- `C2S_Interact.Active > 1`, `C2S_SetRole` outside {0, 1, 255}, `C2S_SetBots > 7` are rejected by the Shared codec (`PacketFormatException`), so the server closes the connection. Rule 1/13 say "ignore the packet, never disconnect for a semantic error". The implementer documented this (judgement call 6). Server-side validation itself is correct: with the codec bypassed, every value 2..255 for Interact, every invalid SetRole/SetBots, and all 256 UseSkill ids are ignored, never throw, never disconnect.
- Evidence: `Input_OutOfRangeEnumBytesOverTheRealCodec_AreAWireFormatViolation_ConnectionCloses_Characterisation` (pins the behaviour), `Input_OutOfRangeBytesThatSkipTheCodec_*` (PASS), `Input_UseSkillEveryByteOverTheRealCodec_NeverDisconnects` (PASS).

### F2 [minor] MoveInput `Seq` filter is not reset at match start (U4 QA-07 asked U2b to confirm)
- A client that restarts `Seq` at 1 in a second match is ignored until it passes the previous match's last `Seq`. The shipped U4 client keeps one Seq per connection, so this is not hit today. `Player.ResetForMatch` leaves `Seq`/`HasSeq` alone.
- Evidence: `Result_SeqIsNotResetAtMatchStart_Characterisation_U4QA07` (pins current behaviour). Fix hint: `HasSeq = false` in `ResetForMatch` (and clear `InputTimes`).

### F5 [minor, process] No automated real-rules socket check
Harness H2-H5 only work in sandbox mode, H6 is skipped. All U2b rules are verified only through in-memory deterministic tests and the 3 loopback harness runs prove U2a behaviour. A harness unit for the real rules is needed before U2b can be called end-to-end verified.

### Observations (no action required)
- O1 Execution needs a fresh touch (judgement call 1): a killer that stands still next to a Lagging survivor never executes it until it leaves 1.2 m. This is what the new GDD row asks for. Pinned by `Capture_ContinuingContactNeverExecutes_FreshTouchExecutes_Characterisation`.
- O2 The capture tick already counts as the first rescue-contact tick (rescue runs after capture), so an adjacent ally rescues 59 ticks after the capture (still inside 3 s, within the test tolerances).
- O3 Growth: per-tick allocation is flat (about 3.1-4.5 KB/tick, no growth across 12 000 sandbox ticks or a 4 800-tick U2b match: 4181 vs 4209 bytes/tick early vs late). `_decoys`, `_skillRequests`, `_escaped`, `_inputs`, `_connections`, `InputTimes` stay bounded.
- O4 Races: 6 client threads joining, spamming Ping/Interact/UseSkill/SetRole/SetBots/StartMatch/MoveInput and disconnecting for 4 s against the live loop thread, then `Stop()`: no "tick error", no deadlock (`Race_LoopThreadVersusConcurrentClientsJoinSpamAndDisconnect_NoFaultNoDeadlock`).
- O5 Stability data (600 s sim, 1 human + 7 bots, 10 seeds each): human killer idle: SurvivorEscaped 10/10 (bots always open the exit); human survivor random-walking against a bot killer: AllSurvivorsDown 9, TimeUp 1. Exactly one GameResult every time, no NaN, no fault. 12 further seeds with an idle human survivor all terminate.
- O6 (code read, untested) a live `Join` during a match takes over the last bot in the list, which can be the killer bot. U2a behaviour, not U2b.

## 3. Migrated U2a QA tests (nothing deleted, nothing weakened)

| Test | Why it failed | Migration |
|---|---|---|
| `U2aAdversarialTests.KillerHidden_MovingHumanKiller_*` | A bot's Traffic Overload halves the killer's speed, so the sanity check "threat level should vary" fails | Runs with `sandbox: true` (exact U2a rules), comment explains. The same leak and threat-band invariants under full U2b rules are now checked on every frame of the leak sweep (`ThreatLevel` vs true distance included) |
| `U2aA2Tests.BotAndHumanSurvivors_NeverMoveWhileAbsentFromKillerFrame_ManySeeds` | A Lagging survivor moves 1 m/s and is executed or times out in the same tick, so it moves and is omitted | Tightened to the U2b rule: moving and absent is allowed only if the survivor frame shows Eliminated/Escaped that tick or the Stealthed flag; everything else (incl. a moving Lagging survivor) is still a violation. The match may end early, scenario-sanity counts are summed over seeds. A copy of the original unmodified test runs in sandbox mode (`Sandbox_BotAndHumanSurvivors_NeverMoveWhileAbsentFromKillerFrame_ManySeeds`) |

`QaFixture` got an optional `sandbox` parameter, `Watch`/`SurvObs` got optional fields.

## 4. Decoy Id tracking (QA brief task 4)

Method: seeded sweep, 14 seeds x 3 consecutive matches = 42 matches (106 316 killer frames, 1 human killer driven by a chase script, 1 human survivor, 6 bots, all skills in play). The attacker is a modified killer client: it remembers each Id's last non-afterimage observation and flags a "jump" when the Id is somewhere a 4 m/s survivor could not have reached (bound = 4 m/s x elapsed + 0.25 m). Ground truth (labels) come from the survivor frame of the same tick.

| Measurement | Value |
|---|---|
| Decoy entry-frames | 13 985 |
| Decoy runs (one run = one decoy shown continuously) | 519 (Dummy 121, White Noise 398) |
| Runs revealed by an impossible jump (at appearance or when the real owner shows up again) | 328 of 519 = 63 % |
| Dummy Packet runs revealed | 7 of 121 = 6 % |
| White Noise runs revealed | 321 of 398 = 81 % |
| Jump events | 659 (316 into a decoy, 343 out of a decoy) |
| Jump events not caused by a decoy (false positives of the bound) | **0** |
| Same position, different Id between consecutive frames (re-borrow after the owner reappears) | 30 |
| Stationary Signal-only entries (>= 3 identical frames, not at a terminal, not next to a Lagging ally): decoy vs real | 436 vs 151 |

Answer: the tracker reveals more than rule 9 intends for White Noise. Rule 9 makes a decoy indistinguishable by field pattern and by Id (borrowed from an absent survivor), but places White Noise points uniformly on the whole map, so a survivor last seen a few seconds ago "teleports". The bound has no false positives, so a modified client (or any HUD that draws a per-Id marker) can be certain. Dummy Packet is almost safe because it sits at the caster's own position; the exception is characterised in `DummyPacket_CasterWalksAwayAndStops_DecoyReappearsAtCastPoint_IdJumpsBack_Characterisation` (the decoy re-appears at the old cast point under the same Id after the caster walked 2 m away and went Silent).

Recommendations (spec text for rule 9 plus code):
1. White Noise: choose a borrowed Id first, then a point inside the disc of radius `4 m/s x (now - LastVisibleAt)` around that Id's last seen position (`VisibleX/Z` already exist). Ids never seen can take any point.
2. When a White Noise decoy's owner becomes visible elsewhere, end that decoy (do not re-borrow another Id at the same position).
3. Dummy Packet: end the decoy for good the first time the caster is visible again (never resurrect it at the cast point).
4. Optional: let decoys drift at survivor speed instead of standing still for 1.5-3 s (stationary Signal away from terminals is 74 % decoy today).
5. Fix F4 first, otherwise 1-4 are moot for human-only matches.

## 5. Leak sweep exclusions (what the byte checks allow, and why)

The sweep encodes every packet both humans receive and searches the bytes. Survivor-bound: killer X/Z pair never present; single coordinates allowed only when they equal a float the survivor frame legitimately carries (survivor X/Z/Yaw/timer, terminals, exit, TimeLeft; 0 hits needed an exemption that hid a real leak, see below). Killer-bound: the true (X,Z) pair of every Silent survivor must be absent, with exactly these exclusions:
1. An afterimage entry of the same Id: it sits at the last position the killer legitimately saw (checked to equal the last real Signal position, flags exactly Signal|Afterimage, age <= 1 s). If the survivor stayed still, true and shown positions coincide.
2. A Dummy Packet decoy at the cast position of the same Id (the caster stood still after casting). The cast is learned from the survivor-bound SkillEffect.
3. A single coordinate (never the pair) that repeats because the caster slid along a wall after casting, or because other entries share it (spawn points share X=5 / Z=26). The pair check is never relaxed.
Two QA-side bugs were found and fixed while building this (start-tick casts not tracked, Lagging positions not recorded as "last seen"); neither was a server defect. Result: 0 violations over 216 172 absent-survivor checks, 14 219 afterimage entries, 13 985 decoy entries, 148 535 real Signal entries; all four killer skills and all five survivor skills seen; no duplicate Id, ordering is strictly by Id, decoy field pattern (Role Survivor, Status Normal, flags Signal, timer 0, CaptureCount copied, inside no wall) identical to a real Signal, Eliminated/Escaped never present, Lagging faithful. A byte-search self-check test plants a pair and confirms the detector fires.

## 6. Rules 1-14

| # | Rule | Result | Evidence (test names, `U2b*` unless noted) |
|---|---|---|---|
| 1 | Interact | PASS (wire-level disconnect for >1: F1) | `Input_OutOfRangeBytesThatSkipTheCodec_*`, `Hacking_HackerCapturedMidHack_*` (hold cleared on status change), `Disconnect_SurvivorLeavesMidHack_*` |
| 2 | Hacking | PASS | `Hacking_SingleHacker*`, `Hacking_CoHackRateIsCappedAtThreeHackers` (1,2,3,4,6), `Hacking_ProgressKept*`, `Hacking_OutOfRangeAt1_6m_InRangeAt1_5m`, `Exit_OpensOnTheSameTick*` (2,3,5,6,7 survivors) |
| 3 | Capture | PASS vs brief; FAIL vs GDD 10/27 grace (F0) | `Capture_FirstContact*`, `Capture_ExactRadiusEdges*`, `Invulnerability_*`, `Capture_ContinuingContact*` |
| 4 | Lagging | PASS | `Capture_LaggingSurvivorMovesAtQuarterSpeed*`, timeout at 60.0 s in `Capture_FirstContact*`, `InvalidUses_LaggingSurvivorCannotCast*` |
| 5 | Rescue | PASS | `Rescue_TwoRescuers*`, `Rescue_ContactBrokenForOneTick*`, `Rescue_OneOfTwoRescuersCaptured*`, `Rescue_SoleRescuerBecomesLagging*` |
| 6 | Escape and win order | PASS | `Win_*` (escape + all-down, capture in the escape tick, two escapees, exact time-up tick, all-down vs time-up), `Exit_Firewall*` |
| 7 | Match end | PASS (F2 minor) | `Result_SentExactlyOncePerHuman_*` (lobby re-broadcast, token valid, second match clean, second result), sweep (exactly one result per human in 42 matches) |
| 8 | Skills | PASS | `PingScanner_*`, `Overclock_*`, `FirewallBlock_*`, `PacketAfterimage_*`, `DummyPacket_*`, `StealthRelay_*`, `TrafficOverload_*`, `EmergencyRestore_*`, `WhiteNoise_*`, `SurvivorSkills_CooldownIsFifteenSeconds` (3 cases), `InvalidUses_*` |
| 9 | Decoys | PASS (letter); intent not met against a tracker: F3, F4 | `DummyPacket_*`, `WhiteNoise_*`, sweep decoy checks, tracking measurement, `Seed_*` |
| 10 | SkillEffect visibility | PASS | `EffectRouting_MatchesRule10_*` (8 skills), sweep survivor-bound effect check |
| 11 | Masking | PASS | leak sweep (every frame), migrated U2a tests, `Sandbox_KillerFramesContainOnly*` |
| 12 | Bots | PASS | `Bots_SurvivorBotsNeverSteerByTheKillerPosition_*`, `Bots_SurvivorBotStopsAtThreatTwoOrMore*`, `Bots_KillerBotIgnoresSilentSurvivors*`, `Bots_KillerBotUsesPingScannerOnlyWhenNoSurvivorIsVisible` |
| 13 | Input validation | PASS (F1) | `Input_NonFiniteAndExtremeMoveInputFloats*` (600 combos of NaN/Inf/MaxValue/Epsilon/1e30 over DirX, DirZ, Yaw; speed never above the role speed), `Input_OutOfRangeBytes*` |
| 14 | `--bind` | PASS | `Process_SandboxIsNotEnabledByDefault_BindDefaultsToAnyAndLoopbackIsHonoured`, `Process_InvalidBindExitsWithCodeTwoAndAMessage` (4 cases, exit code 2) plus the implementer's loopback listener test |

Sandbox (attempt 2): `Sandbox_IsOffByDefault_InEveryConstructionPath`, `Sandbox_NoCaptureHackingSkillsOrMatchEndBefore600Seconds_ThenExactlyOneTimeUp` (killer on top of a survivor, Interact and all skill ids spammed for the full match, one TimeUp at 600 s), process test (no sandbox text by default). Stability: `Stability_*` (10 seeds x 2 roles, plus 12 idle-human seeds).

## 7. Test files

- `Server.Tests/QA/U2bHelpers.cs` (Spy reflection set-up, `QaRig`, `QaDirect`, `DirectRig`, wire helpers)
- `Server.Tests/QA/U2bAdversarialTests.cs` (capture, rescue, hacking, exit, win order, result/lobby)
- `Server.Tests/QA/U2bSkillTests.cs`
- `Server.Tests/QA/U2bInputSandboxBotTests.cs`
- `Server.Tests/QA/U2bSweepTests.cs` (leak sweep, tracking, stability, growth, races, disconnects, seed)
- Edited: `U2aHelpers.cs` (optional `sandbox`), `U2aA2Tests.cs`, `U2aAdversarialTests.cs` (migrations above).

Reflection (`Spy`) only arranges scenarios (teleport, assign a skill, pre-set Lagging or the match clock) and reads bot positions for the isolation tests; every asserted behaviour is observed on the wire. Re-run: `dotnet test Server.Tests --filter "FullyQualifiedName~U2b"`.
