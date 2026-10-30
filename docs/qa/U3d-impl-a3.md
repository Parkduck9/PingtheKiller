# U3d implementation report — attempt 3

Implementer: Claude Opus (quota fallback: Codex out of credits; not a failed attempt). Brief: `docs/briefs/U3d-a3.md`.

**Changed:**
- `KillerSight.cs`: tolerance removed. `Finish()` fails every unjoined survivor world. `TickSequence` checks ticks rise by 1 or 2.
- `WireClient.cs`: keeps the last 64 WorldStates per client.
- `MatchChecks.cs` (H3) and `ScenarioCheck.cs` (H6): coverage waits, tick-based checks, last-tick equality, and the contact hold compared at equal ticks (fixes F3).
- `README.md`: coverage rule and contract clarification.
- Tests: `SkewProxy.cs`, `SkewTests.cs` (+300 ms), join and tick-sequence unit tests, updated messages.

**Windows:**
- **H3:** survivor ticks from the moving baseline to the end of the Silent window. The join is seeded from retained killer worlds, and the killer stream is read until it covers the last survivor tick.
- **H6:** every stream from MatchStart to the end of the drain.
- Both checks are bounded by `--timeout`.

**Verified:**
- Build with `-warnaserror --no-incremental`: 0 warnings.
- Server.Tests: 713/713.
- Harness.Tests: 557/570. The 13 red tests are all QA pins of attempt-2 behaviour:
  - `U3dH6KillerStreamStartsLateTests`: must-fail now.
  - `U3dEdgeToleranceUnitTests` ×4: the tolerance was removed.
  - `U3dH6ExpectLosNeedsBlockedAndAbsentTests` ×2: the fake moves ticks off the killer's stream, so it breaks the contract.
  - U3c `Gap_OffsetLeak…` ×2: this gap is closed; the harness now reports "no unique own entry".
  - U3c `Limit_StreamsMoreThanTheSignalMarginLateFailH3` ×4: H3 now passes with 500 ms of lag.

- Real server (main build, threshold 100): 100/100 in all 3 runs.

**Risk:** a server that drops a WorldState for one human now fails, by design.
