# U2e QA report, attempt 1

Reviewer: Claude Sonnet 5.5 (implementer: Claude Opus, quota fallback). Branch `claude/u2e-los`, uncommitted tree on top of `de730e9`.

**Counts.** Added `Server.Tests/QA/U2eAdversarialTests.cs`: 51 test cases (37 theory rows + 14 facts). `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings, 0 errors. `dotnet test PingTheKiller.sln` 3 times: Server.Tests 792/792 each (742 existing + 50), Harness.Tests 386/386 each. After the last added test, Server.Tests 793/793 in about 30 further runs, with one exception (see F5). Product code untouched by QA.

## Contract (U2e brief), through `GameServer` + `InMemoryTransport` (decoded wire frames, deterministic `Tick`)

| # | Item | Result | Evidence |
|---|---|---|---|
| 1 | Entry only for Normal/Lagging recipient with a wall-free 2D segment of the same tick, closed AABB, no inflation, touching blocks | PASS | `MapSweep_*`: 9000 survivor-frame checks (4885 with entry, 4115 without) against an independent exact reference; `WallHugging*`/`SameTick_OneFloatStep*`: 3999 corner-aligned and one-ulp placements, entry flips on exactly the float step that clears the corner |
| 2 | Entry fields exact (real Id, Role Killer, X/Z/Yaw bit-equal, Status; Flags, StatusTimer, CaptureCount = 0) | PASS | `CheckFrame` on every frame; `ActiveOverclock_*` (killer's own view shows Signal, survivor entry Flags 0) |
| 3 | Eliminated and Escaped never | PASS | `LaggingRecipient..._EliminatedAndEscapedNever...`: 60 frames with geometric sight to the dead survivor, none carries the entry; Escaped final frame; eliminated survivor after reconnect |
| 4 | Per recipient only | PASS | `MapSweep_*`: 2160 ticks in which the three survivors disagreed; 2-survivor and 3-survivor scenarios |
| 5 | No linger, no prediction | PASS | `SameTick_MovingKillerAndMovingSurvivor_*` (killer and survivor moving, 6 and 13 flips, every frame checked against the same tick's positions); `SameTick_OneFloatStep*` |
| 6 | `ThreatLevel` unchanged; survivor-bound `S2C_SkillEffect` carries no killer coordinates; killer-bound view unchanged | PASS | `ThreatLevelIsTheDistanceBand*`, threat checked in all 9000 sweep frames; PingScanner and Firewall Block effect bytes scanned; Overclock sends no survivor-bound effect; killer frame is one own entry, Flags 0, in every sweep sample |

Also PASS: Lagging recipient receives the entry; reconnect of an active survivor (clear and blocked, join tick and next tick, 6 rounds) obeys the same rule.

Geometry (independent reference: exact BigInteger arithmetic over float bits plus a separating-axis test, no shared code): 37 hand-checked cases (face grazing, corner exactly and one float step either side, start/end on a boundary, endpoints inside walls, negative coordinates, very long segments, the (0,0) junction of two border walls); 43296 constructed segments through a corner or ending on a face (all blocked); 120000 random/snapped/ulp segments, 0 mismatches for reachable coordinates; `SegmentBlocked` allocates 0 bytes over 500000 calls. The map has no gap between two adjacent interior walls; only the border walls touch.

## Re-targeted tests (implementer's list)

All nine keep the blocked-case strength: `KillerSight.Verify` asserts no entry and no killer bytes on every blocked frame, and on frames with sight the check runs with that one entry removed. None deleted. No weakened assertion found. See F4 for one scenario that never reaches a blocked frame.

## Harness

I re-ran with a fresh sandbox + fresh normal server: **55/100** (H1 25, H2 20, H5 10; H3 "Killer 1 leaked to survivor at tick=1361", H6 same at tick=1406, H4 "NaN input moved player by 0.199828 m"). Fresh sandbox, `--only H1,H2,H4,H5`: **70/70**. This matches the implementer's three runs (`docs/qa/U2e-harness-run*.json`). H3/H6 first fail at tick 1279-1406, never in the first 1200 ticks, and the failing check is only the old "any killer entry = leak" rule, consistent with a legal line-of-sight entry (which U3d fixes). H4 is a cascade: H3 throws while its moving-input task keeps running, and H4 passes on its own. Servers stopped afterwards.

## Findings

- [minor] F1 `PlaceForTests` is safe: internal, reachable only through `InternalsVisibleTo("Server.Tests")`, takes `_tickGate` (the same lock `Tick` holds), refuses non-standable points and unknown ids. It does not reset input or visibility state, which no test here needs. No action.
- [minor] F2 Per-tick allocation: the sight decision allocates 0 bytes, but a frame with the entry costs more: 13 KB to 20 KB per tick for 7 recipients (+7 KB, about 150 KB/s at 20 Hz): one extra list slot per recipient plus a larger encoded frame. Brief says "allocate nothing"; this is the entry's unavoidable cost and is bounded. Optional: pre-size `world.Players`. `PerTickAllocation_*` records it.
- [minor] F3 `SegmentBlocked` loses exactness for denormal/tiny coordinates (|v| < 2^-10, e.g. 1e-45): the double delta absorbs the tiny term, giving a false "blocked" and asymmetric results (765 of 54575 such samples). Unreachable: players stay at least 0.4 from the border, and no mismatch occurs for any in-map coordinate. No action.
- [minor] F4 `U2aAdversarialTests.KillerHidden_BotKiller_*` only exercises frames with sight (its own comment says so). Blocked frames with a bot killer take the same `BroadcastWorld` path and are covered by the moving-human re-target and by `MapSweep_*`.
- [minor] F5 One Server.Tests failure in the first run after a rebuild (test not captured: output had only counts); not reproduced in about 30 further full and class runs. Probably timing. Harness.Tests `H6FailsWithMatchingEvidence` did not fail in any of the 3 runs.
- Beyond the brief, checked and fine: game state only on the loop thread; bots untouched (they never read the survivor wire); `Walls` still exposed read-only; wire layout and PacketIds unchanged; byte scan for 2.0f-style coordinates is limited to patterns with 3 distinct bytes (they match across field boundaries) while the decoded-entry and (X,Z)-pair checks always apply. Unity compilation not verified (optional).

VERDICT: PASS
