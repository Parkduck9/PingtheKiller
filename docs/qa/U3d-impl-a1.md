# U3d implementation report — attempt 1

Implementer: Claude Opus (quota fallback: Codex out of credits; not a failed attempt).

**Changed:** `Tools/Harness/` — new `LineOfSight.cs` (own Liang-Barsky, signed margin, 0.05 m band), `KillerSight.cs` (judge, `SightJoin` by Tick, counts); `MatchChecks.cs` (H3), `ScenarioCheck.cs` (H6: sight step, streaming + final judgement, `--expect-los`), `KillerLeakScan.cs` (legal entry's X/Z only), `Options.cs`, `Program.cs`, `README.md`; `Measurements.KillerHidden` removed. Tests: `LineOfSightTests.cs`, `LineOfSightScenarioTests.cs`, `FakeSight.cs`, LOS modes in `ScenarioServer`/`LobbyServer`, updated `LobbyFlowTests`, `MeasurementTests`, `ScenarioTests`.

**Verified:** `dotnet build PingTheKiller.sln -warnaserror --no-incremental` 0 warnings. `dotnet test PingTheKiller.sln`: Server.Tests 713/713, Harness.Tests 427/429. Real main servers, 3 runs: 100/100 each, H3/H6 "0 legal line-of-sight killer entries"; H6 152 blocked-and-absent.

**QA tests failing only from the spec change (not edited):**
- `QaH3Tests.LeakingServerFailsH3("killer-entry-in-survivor-world")`: QaServer's entry is contract-shaped with clear sight (off-map positions), so it is legal.
- `U3bLeakKillerEntryByIdOnceTests`: its single entry (A Lagging, contact, real position, zero fields) is legal.

**Unverified:** real U2e server; `--expect-los` only against fakes.

**Risks:** band/undecided entries are counted, not failed (README U3d-1..4).
