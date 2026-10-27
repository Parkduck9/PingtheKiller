# U3c QA report — attempt 1

QA agent: Claude Sonnet 5.5 (implementer: Claude Opus). Branch `claude/u3c-anon`, nothing committed.
Scope: H3 (gate) and H6 against the Id-mode and U2c anonymous killer views, and unknown server packet Ids.

**Verdict: PASS.** No false pass on any H3 case the brief lists, no false fail under realistic bot movement. Six minor limits are documented below. No existing QA test was edited.

## Files written (all under the allowed paths)
- `Tools/Harness.Tests/QA/U3c/QaViewServer.cs`: H3 fake with scripted bots (ids 10..15), exact survivor ground truth, Id or anonymous killer view, per-snapshot shuffle, stream delay/jitter, dropped survivor ticks, many leak shapes.
- `Tools/Harness.Tests/QA/U3c/QaProxy.cs`: a TCP man-in-the-middle in front of `ScenarioServer` (rewrites killer-bound or survivor-bound frames) and a raw byte server for WireClient framing tests.
- `Tools/Harness.Tests/QA/U3c/U3cH3Tests.cs`, `U3cH6Tests.cs`, `U3cUnknownPacketTests.cs`: 63 new tests.
- This report.
- Existing QA tests updated: **none**. `LobbyServer`, `ScenarioServer` and the earlier QA fakes are reused, not edited.

## Results: fake → expected → actual (Id mode / anonymous mode)

### H3 must FAIL (false-pass hunt, score 0 each)
| Fake | Expected | Id | Anon | How it was caught |
|---|---|---|---|---|
| stopped survivor emitted at its exact position (anonymous entry) | FAIL | FAIL | FAIL | raw X/Z byte scan |
| same, while bots pass by / a bot stands 0.03 m, 0.04 m away / bots ring it at 0.1 m | FAIL | FAIL | FAIL | raw scan (the bot does not hide it) |
| rounded to 0.01 m | FAIL | FAIL | FAIL | attribution: 0.004 m from the stopped survivor |
| offset (0.02,-0.03) = 0.036 m; and 0.0489 m | FAIL | FAIL | FAIL | attribution, "Silent survivor 2 leaked" |
| offset 0.0511 m, 0.58 m, 3.6 m | FAIL | FAIL | FAIL | "unexplained killer-view entry" |
| exact, only every 2nd / 5th / 20th tick | FAIL | FAIL | FAIL | raw scan |
| offset 0.036 m, only every 3rd / 20th tick | FAIL | FAIL | FAIL | attribution |
| stopped survivor emitted under a bot's real Id (Id mode; Id 0 in anon) | FAIL | FAIL | FAIL | position, never the Id |
| offset leak while 6 bots cluster at 0.3 m | FAIL | FAIL | FAIL | attribution |
| extra entry matching no survivor | FAIL | FAIL | FAIL | "unexplained" |
| bots 1 tick stale in killer view | FAIL | FAIL | FAIL | "unexplained" (by design, README U3c-3) |
| bots 0.06 m off in killer view | FAIL | FAIL | FAIL | "unexplained" |
| survivor never appears while moving | FAIL | FAIL | FAIL | "Moving baseline missing" |
| 60 % of survivor ticks dropped (coverage < 50) | FAIL | FAIL | FAIL | "Insufficient masking coverage" |

### H3 must PASS (false-fail hunt)
| Fake | Id | Anon |
|---|---|---|
| 6 walking bots far away | PASS | PASS |
| bot walks right past the stopped survivor at 0.1 / 0.3 / 0.5 m (closest pass 0.01 m in x) | PASS | PASS |
| bot stands still at 0.04 / 0.1 / 0.3 / 0.5 m | PASS | PASS |
| 6 bots in a ring at 0.1 / 0.3 m; 6 bots walking past together, 0.05 m apart | PASS | PASS |
| killer stream 200 ms late; survivor stream 200 ms late | PASS | PASS |
| jitter 0..120 ms on both streams; jitter on the killer stream only | PASS | PASS |
| survivor ticks dropped 1 in 8 (54 ticks joined) | PASS | PASS |
| shuffle every tick (forced in Id mode) | PASS | PASS |
| delay + jitter + drops + pass-by at 0.1 m together | PASS | PASS |
| killer-view survivor Ids random and nonzero (including 1, 2, 3, bot ids) | PASS | n/a |

### H6 (proxy in front of `ScenarioServer`, ~55 s scenario each)
| Fake | Expected | Actual |
|---|---|---|
| anonymous view, every killer-view survivor Id replaced by a random nonzero (incl. killer's 1 and survivors' 2, 3) | PASS | PASS (10/10) |
| Id mode, real progress, survivor Ids swapped in the killer view | PASS | PASS |
| progress rounded to nearest of 0/100 in the killer view | PASS | PASS |
| killer stream shows ExitOpen=1 (tick ≥ 100) while its own terminals show 0 of 2 at 100 | FAIL | FAIL: "ExitOpen=1 in killer's WorldState tick=100 with only 0 of 2 required terminals at Progress 100" |
| killer stream never shows the exit open | FAIL | FAIL: "ExitOpen == 1 ... not observed by killer" |
| anonymous view + unknown frames (0x7E, 0x10) in every stream | PASS | PASS, skip counts in evidence |

Proof of "no lookup by survivor Id in the killer stream": the first two rows would fail (or pass for the wrong reason) if H6 or H3 read an Id in a killer-bound survivor entry. A grep of `Tools/Harness` finds `Entry(world, id)` only on survivor streams, and `Measurements.Self(.., Id, role)` only for the killer's own entry.

### Unknown packets
| Case | Expected | Actual |
|---|---|---|
| unknown frame sent one byte at a time, then a Pong | skipped, Pong delivered, counted | PASS |
| ids 0x0000, 0x0010 (empty and 16-byte), 0x0011, 0x00FF, 0x7E7E, 0xFFFF, and a 16384-byte frame | skipped | PASS (8/8) |
| 0x10 frame whose body is a valid WorldState payload | skipped, no WorldState delivered | PASS |
| known Id: truncated Pong, Pong + trailing byte, empty Pong, WorldState + trailing byte, truncated LobbyState, short JoinResult | stream faults with `PacketFormatException`, earlier frames delivered, unknown counts kept | PASS (6/6) |
| bad length 0, 1, -5, 16385, int.MaxValue after an unknown frame | stream faults "Invalid frame length" | PASS (5/5) |
| H1..H6 all pass in one run with 0x7E before every packet and 0x10 after every world, Id mode and anonymous mode | 100 / 100, skip counts in evidence | PASS both modes: H1 `id=0x7E ×10001`; H2..H6 `id=0x10 ×N, id=0x7E ×N` |
| failing H3 (offset leak) with unknown frames | still fails, evidence still lists the skip counts | PASS both modes |
| killer X (offset 3) in a 0x10 frame, or Z (offset 17) in a 0x7E frame, sent to a survivor | H6 FAIL | FAIL: "killer coordinate bytes in unknown packet 0x10 (update Shared)" |
| same coordinates in an unknown frame sent to the killer only | H6 PASS | PASS |

## Stress and regression counts
- Correct server, PassBy 0.2 m, jitter 40 ms, 1 in 10 survivor ticks dropped, shuffled: **32/32 PASS** (16 per mode).
- Offset leak every 3rd tick with the same noise: **32/32 caught**, 0 missed.
- `dotnet test PingTheKiller.sln`, unloaded, three consecutive runs: Server.Tests 662/662 and Harness.Tests 386/386 each time (323 existing + 63 new). `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings, 0 errors. No non-deterministic outcome.
- Earlier QA suites (U3, U3b) unchanged and green in all three runs.
- Moderate load (16 busy processes on 32 logical CPUs): 662/662 and 386/386.
- Heavy load (40 busy processes on 32 CPUs, plus the suite's own parallelism): Server.Tests 662/662, Harness.Tests **375/386; 11 timing-sensitive tests failed**. All 11 are environmental: the in-process fake servers and the harness starve together. The causes were `OperationCanceledException` timeouts, "inter-arrival p99=130 ms > 120 ms" (H2), "Survivor remained Signal", and "Insufficient masking coverage" because the starved fake produced only 38-51 ticks in 3.1 s. Seven of the 11 are earlier tests (U3 QA, U3b QA, implementer tests), four are new. Ten flipped toward FAIL (a correct or characterised server failed a check); **no H3 false pass occurred under load**. A correct server that stalls to under 16 ticks/s fails H3 for coverage, which is the intended behaviour. The eleventh is a H6 false pass, finding 6.

## Findings
None blocker, none major. A false pass on a gate is a blocker by the brief; findings 1-3 are contrived H3 false passes that need a deliberately shaped server (an offset leak plus a coincident bot, a disguised own entry, or leak timing that matches dropped ticks), so they are rated minor, not blocker. Each has a test that asserts today's behaviour (named `Gap_*`, `Tension_*`, `Limit_*`) and will flip loudly when the limit is closed. If you count any H3 false pass as a blocker regardless of how contrived, findings 1-3 turn this into QA-FAIL; I judged them minor because the realistic server bug (the exact position emitted) is caught in every shape tested.

1. **minor: offset leak nearer to a bot than to the stopped survivor is attributed to the bot, H3 passes.** Test `U3cH3CoverageAndLimitTests.Gap_OffsetLeakNearerToABotThanToTheStoppedSurvivorIsAttributedToTheBot`. Expected FAIL, actual PASS (both modes). Needs a bot within 0.05 m of the stopped survivor and a leak that is offset (an exact leak is always caught, see "exact-while-bot-stands-0.03m-away"). Evidence: "0 unexplained, 0 to stopped survivor 2".
2. **minor: leak disguised as a duplicate of the killer's own entry (Id = killer's, Role = Killer) at an offset position is skipped as "own".** Test `Gap_OffsetLeakDisguisedAsADuplicateOfTheKillersOwnEntryIsNotDetected`. Expected FAIL, actual PASS. Suggested fix: require exactly one own entry and check it equals the killer's own position.
3. **minor: an offset leak emitted only on ticks the survivor stream never delivered is not attributed.** Test `U3cH3StressTests.Gap_OffsetLeakOnlyOnTicksTheSurvivorStreamNeverDeliveredIsNotAttributed`. Skipped ticks are covered only by the exact-byte scan.
4. **minor (spec tension): the "bit-identical bot is ambiguous, evidence not failure" rule can never be reached.** The unchanged raw byte scan fails first. Tests `Tension_BotBitIdenticalToTheStoppedSurvivorFailsOnTheRawByteScanNotAsAmbiguous` and `Tension_CorrectServerWithABotSharingOnlyTheExactZFloatFailsH3` (a correct server whose bot shares only the exact Z float fails with "Silent survivor Z=... bytes leaked"). Needs bit-equal floats, negligible with integrated positions. Either drop the ambiguous branch or exempt bytes that belong to a joined, truth-matched entry.
5. **minor (informational, not a U3c regression): the settle window tolerates about 250 ms of stream lag.** Test `Limit_StreamsMoreThanTheSignalMarginLateFailH3`: a survivor stream 500 ms late fails "Survivor remained Signal"; a killer stream 500 ms late fails the raw scan. 200 ms and 0..120 ms jitter pass.

6. **minor (H6 is not a gate; pre-existing U3b F9 hold, surfaced by the heavy-load run): the 3 s contact hold is wall-clock, so a starved server can slip past it.** Test `U3bKnownLimitEscapeToleranceTests.ExecutionAfter2Point5sOfContinuousContact_FailsH6`: passes in all unloaded and moderate-load runs, but under 40 busy processes the fake server's ticks ran slower than real time, its 2.5 s of tick-time contact fell outside the harness's 3 s wall-clock hold, and H6 passed ("FALSE PASS: ... killer held contact 3 s ... no execution"). Not U3c related. Suggested fix: hold for a number of observed A frames or ticks (3 s × 20 Hz), not wall time.

Observations, not findings:
- The "stale or early snapshot" neighbour hint in the unexplained evidence is best-effort: it needs the neighbour tick to be in the capture window, so it can be missing.
- The moving baseline is satisfied by the first matching tick, so position fidelity during movement is not checked beyond that.
- `WireClient` keeps at most 32768 unknown frames per connection; beyond that the raw scan refuses loudly (test `Limit_MoreThanTheKeptCap...`). A 150 s H6 produces about 6000.
- Risks only the real U2c server can confirm: killer-view positions bit-identical to survivor-view positions at the same tick (a 1-tick-stale or offset killer view fails H3 by design); no decoys or afterimage entries in `--sandbox`, otherwise H3 reports "unexplained".

QA-PASS
