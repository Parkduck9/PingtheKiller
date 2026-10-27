# U3b implementation report — attempt 1 (H6 scenario + flaky self-test)

**Quota fallback:** implementer Claude Opus (Codex out of credits; not a failed attempt). Uncommitted on `claude/u3b-h6`.

## Files
New: `Tools/Harness/{ScenarioCheck,Planner,KillerLeakScan}.cs`; `Tools/Harness.Tests/{ScenarioServer,ScenarioTests,ServerCoreGuardTests,PlannerAndLeakScanTests}.cs`.
Changed: `Tools/Harness/{Harness.csproj,Options,HarnessRunner,Program}.cs`, `README.md`; `Tools/Harness.Tests/{ScorerTests,LobbyFlowTests}.cs`; `QA/QaServer.cs` (flaky fix); `QA/QaScorerAndExitCodeTests.cs` (two H6 tests asserted "skipped": now "[setup]", plus an explicit closed `--port`).

## Run
Sandbox server `--port 7777 --bots 0 --bind 127.0.0.1 --sandbox` (H1–H5) and normal server `--port 7778 --bots 0 --bind 127.0.0.1` (H6), then `Harness --port 7777 --h6-port 7778`.

## Tests
- `-warnaserror` build: 0 warnings. Harness.Tests: 187/187 (51 s).
- `dotnet test PingTheKiller.sln` 3× in a row: 485/485 + 187/187 each time.

## Flaky test
`QaH1Tests.BrokenServerFailsH1` (`naive-framing-no-reassembly`, and `one-frame-per-read`). The fake's naive decoder ran into garbage after a read that began mid-frame. The resulting `PacketFormatException` faulted the connection task, and `DisposeAsync` rethrew it. TCP segmentation timing decided whether this happened. The fix discards the rest of that read, with a deterministic regression test. Under sustained load (3 Server.Tests loops, 64 threads), the old code failed 1/8 and 1/6 runs; the fixed code passed 6/6 (187/187). The other wall-clock bounds stayed far below their limits in the trx timings.

## Judgement calls
- In U2b, a killer touching a Lagging survivor executes them at once. A killer is still within 1.2 m one tick after a capture, so a human killer executes almost every survivor they capture. H6 approaches at half speed and reverses on the first captured frame (0 executions). **Orchestrator decision needed.**
- Leak-field legitimacy counts only A/B entries and survivor-cast SkillEffects.

## Unverified until U2b merges
H6 against the real server.
