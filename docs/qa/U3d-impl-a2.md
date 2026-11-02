# U3d implementation report — attempt 2

Implementer: Claude Opus (quota fallback: Codex out of credits; not a failed attempt). Fix list: `docs/briefs/U3d-a2.md`.

**Changed:**
- `Tools/Harness/KillerSight.cs`: `SightVerdict.AllowedEntry` (Visible/Ambiguous only). `SightJoin` tracks the killer tick range. `Finish()` fails a killer entry on an unjoined tick inside that range, or more than `EdgeTolerance` = 6 ticks outside it.
- `ScenarioCheck.cs`: H6 calls `Finish()` before the final scan, and both scans use `AllowedEntry`.
- `README.md`: limits F1 (fixed), F2 and F3.
- Tests: `LosUnjoinableTick` (ScenarioServer) and `KillerLos.UnjoinableTicks` (LobbyServer) fakes, plus join unit tests.

**Tolerance:** 6 ticks (300 ms). QA measured 3–4 edge ticks under a 200 ms skew, which is 4 ticks at 20 Hz. The extra 2 ticks cover TCP coalescing and read-loop jitter.

**Verified:**
- `dotnet test PingTheKiller.sln`: Server.Tests 713/713, Harness.Tests 522/525.
- Main servers, 3 runs: 100/100 each, 0 visible entries, 0 undecided.

**Red, expected (QA pins):**
- `U3dJudgeProbeTests.Gap_AnEntryOnAnUnjoinableTickIsUndecidedAndItsRealCoordinatesAreAllowedByTheScan`
- `U3dH3Gap_LeakOnUnjoinableTicksIsNotDetectedTests.Gap_BlockedLeakOnRenumberedTicksPassesH3`
- `U3dH6Gap_LeakOnUnjoinableTicksIsNotDetectedTests.Gap_BlockedLeakWithRenumberedTicksPassesH6`

**Gate issue (QA-owned, not edited):** `-warnaserror` fails on `QA/U3d/U3dJudgeAndStressTests.cs:168` (xUnit1031, `Task.WaitAll`). My files have 0 warnings.

**Unverified:** U2e server with this build.
