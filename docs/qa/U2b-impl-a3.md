# U2b implementation report, attempt 3 (targeted fix pass)

Implementer: Claude Opus. Quota fallback; this is not a failed attempt.

## Changes
- **F0:** `GameServer.ExecutionGraceSeconds = 2`. A touch inside the grace does not execute. It also uses up the re-entry, so an execution needs a new re-entry after 2 s.
- **F4:** without a test hook, every match reseeds the private `_random` and `_decoyRandom` from a CSPRNG. `MatchStart.Seed` is now a separate CSPRNG value. `ServerOptions.Seed` is now only a test hook for the private RNG. A new test hook, `MatchSeedForTests`, fixes the wire seed. The console host never sets either hook.
- **F2:** `ResetForMatch` resets `HasSeq`, `Seq` and the input-rate window.
- New tests: 2 grace tests and `MatchSeedAndRestartTests` (4 tests).
- Re-baselined: `LobbyTests.AllHumansReceiveMatchingSeed…` no longer expects the wire seed to equal 1234.

## Results
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings, 0 errors.
- `dotnet test PingTheKiller.sln`, Server.Tests: 628 of 630 pass. Harness.Tests: 150/150.
- QA's F0 test now passes.
- The 2 failures are QA characterisation tests that assert the bugs fixed here:
  - `U2bAdversarialTests.Result_SeqIsNotResetAtMatchStart_Characterisation_U4QA07` (F2);
  - `U2bSweepTests.Seed_MatchStartSeedFromTheWire_…_Finding` (F4).
  - QA needs to invert them.

Sandbox harness, each run on a fresh server: **90 / 90 / 90**, gates passed every time (H2 p99 63.6, 63.3 and 63.4 ms). Reports: `artifacts/u2b-a3-harness-{1,2,3}.json`.
