# U3b implementation report — attempt 2

**Quota fallback:** implementer Claude Opus. Uncommitted on top of attempt 1.

## Changes
- `ScenarioCheck.cs`:
  - Full-speed approach. Capture is detected in A's own stream, with no killer-stream lookup by Id.
  - The killer stops and holds contact for 2 s; every A frame must show Lagging and distance ≤ 1.2 m. Then the killer retreats; after the rescue it must still be more than 2.5 m from A.
  - F1: the escaper's last own state before the GameResult must be Escaped or within 2.5 m of the exit.
  - F2: on every frame of every stream, ExitOpen needs the required terminals at 100.
- `KillerLeakScan.cs` (F3): decode/re-encode equality plus a field-level comparison; the API is unchanged.
- `ScenarioServer.cs`: armed leave-and-re-enter rule plus `ExecutionGrace`; new faults `ExecuteOnContinuedContact`, `RearmsInContact`, `PrematureEnd`.
- Tests: pass with grace off and on, the three new broken fakes, structural leak tests.
- README: hold, structural scan, Known limits F4–F8.

## Results
`-warnaserror`: 0 warnings. `dotnet test PingTheKiller.sln` ×3: Server.Tests 485/485. Harness.Tests 231/258, 231/258 and 230/258; outside `QA/U3b` there are 0 failures.

## QA/U3b failures (QaRulesServer's literal execution rule executes A at the tick after capture)
- **Always (25):** Control, SlowServer, ConcurrentControlRuns, 4 KnownLimit tests (expect a pass), and 18 broken fakes whose expected evidence names a later step.
- **Racy (3):** LaggingWithCaptureCountZero, LaggingWithCaptureCountTwo, LaggingWithoutTimer.
