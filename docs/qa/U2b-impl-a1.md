# U2b implementation report, attempt 1

Quota fallback: Claude Opus implemented this because Codex is out of credits. This is not a failed attempt.

## Files
- New: `Shared/Runtime/Game/SkillIds.cs` and `Server.Core/Runtime/GameServer.{Rules,Skills,Visibility,Bots}.cs`.
- Changed: `GameServer.cs`, `GameServer.Simulation.cs`, `GameServer.Lobby.cs`, `Player.cs`, `TcpTransport.cs` (new `Address`), `Server/Program.cs` (new `--bind`).
- Tests: 38 new tests in `Server.Tests/Core/`.

## Results
- `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings, 0 errors.
- `dotnet test PingTheKiller.sln`, Server.Tests: 521 of 523 pass. The 2 failures are QA tests listed below.
- `dotnet test PingTheKiller.sln`, Harness.Tests: 150 of 150 pass when run alone. The known flaky `QaH1Tests.BrokenServerFailsH1` failed once in the concurrent run.
- `Server --bind not-an-ip` exits with code 2.
- Real-server harness: **25/100 on all 3 runs. The gates fail.**
  - H1 passes.
  - H2–H5 time out. The harness killer stands idle, so survivor bots open the exit and escape about 20 s in. The match ends as rule 6 requires and WorldStates stop.
  - H3 also requires the survivor's Id to be absent from the killer view. Rule 9 decoys and Packet Afterimage legitimately break that.
  - Fixing this needs a `Tools/Harness` change.

## Judgement calls
1. Execution needs a fresh touch. The contact that captures a survivor never executes it on later ticks.
2. The killer view carries only the Signal and Afterimage flags. Afterimage entries are Signal plus Afterimage, frozen at the last seen position. Entries are sorted by Id.
3. Skills run after movement. An effect lasts from the cast to cast plus its duration.
4. Killer-bound TrafficOverload and survivor-bound PingScanner use X/Z = 0,0.
5. The killer bot ignores Lagging survivors.
6. `Interact.Active=2` sent over the wire is still rejected by the Shared codec, which disconnects. Rule 13 validation applies after decoding.

## Existing tests changed
- `ReconnectAndBotTests.SurvivorBotWalksAndPauses…` was replaced by `SurvivorBotWalksToNearestTerminalAndHacksIt…`. The U2a random-walk bot was a placeholder.
- `TenSecondDeadlineReplacesHuman…` now waits up to 60 ticks for the bot to become visible, because the bot may open with Stealth Relay.

## QA tests asserting U2a behaviour (not edited)
- `U2aAdversarialTests.KillerHidden_MovingHumanKiller_…`: bot TrafficOverload slows the killer, so its "threat should vary" sanity check fails.
- `U2aA2Tests.BotAndHumanSurvivors_NeverMoveWhileAbsent…`: a Lagging survivor moves, then is executed and omitted in the same tick.

## Unverified
- Unity compilation was not run.
- `SkillIds.cs` has no `.meta` file.
- Nothing is stubbed.
