# U2b implementation report, attempt 2

This attempt follows the orchestrator fix list. Implementer: Claude Opus (quota fallback, not a failed attempt).

## Changes
- `ServerOptions.Sandbox` (default false) and the `--sandbox` console flag.
- Sandbox runs U2a rules only. There is no hacking, capture, Lagging, rescue, skills, decoys, afterimage, escape or win/lose; TimeUp still ends the match at 600 s. Bots use the U2a behaviour (`GameServer.Sandbox.cs`).
- The in-process path reaches sandbox only by setting the option.
- New `Server.Tests/Core/SandboxTests.cs` with 5 tests:
  - the option is off by default;
  - sandbox has no capture, hacking or skills, while the default keeps all three;
  - a sandbox match ends only by TimeUp;
  - sandbox bots never capture.
- `CoreFixture` gets an optional `sandbox` parameter (setup only).
- `Shared/Runtime/Game/SkillIds.cs.meta` copies the sibling layout and has a fresh guid.

## Results
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings, 0 errors.
- `dotnet test PingTheKiller.sln`: Server.Tests 526/528, with the 2 known U2a QA tests left to QA. Harness.Tests 150/150.

Real-server harness, each run on a fresh server started with `--bots 0 --bind 127.0.0.1 --sandbox`. Reports are in `artifacts/u2b-a2-harness-{1,2,3}.json`.

| Run | H1 | H2 (p99) | H3 | H4 | H5 | Total | Gates |
|---|---|---|---|---|---|---:|:---:|
| 1 | 25 | 20 (63.4 ms) | 20 | 15 | 10 (62.4 ms) | 90 | pass |
| 2 | 25 | 20 (63.4 ms) | 20 | 15 | 10 (48.1 ms) | 90 | pass |
| 3 | 25 | 20 (63.2 ms) | 20 | 15 | 10 (47.6 ms) | 90 | pass |
