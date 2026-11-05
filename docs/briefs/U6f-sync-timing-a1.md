# Brief U6f-sync-timing, attempt 1

Implementer: Claude Opus (Codex out of credits). QA: Gemini Flash. Worktree: `C:/Unity/MultiPlayGame/PtK-work/u4h`, branch `claude/sync-great-timing` (from main `9e89ed4`). Never push, never merge.

## Goal

A sync-check press made at the moment the needle is drawn inside the great part must be judged Great. Today it almost never is.

Evidence (`docs/qa/playcheck-1105.md`, finding 1; main checkout `docs/qa/playcheck-1105/surv.log`): an offline match with `-ptk-auto-sync great` got 6/6 Good.

## Diagnosis by the orchestrator (verify it, do not trust it blindly)

The server loop (`GameServer.Run`) ticks at wall-clock points `k*step` (step 50 ms). In `Tick`:
1. Inputs are drained and handled while `_now` = T_k, the tick's start time.
2. Then `_now += dt`, and the rules run.

Two problems follow:
- **The check is stamped one tick late.** `IssueSyncCheck` runs inside `RunRules`, after the increment, so `SyncSentAt = T_k + step`. The packet actually leaves at about wall time T_k.
- **The press is timed by the tick, not by its arrival.** `SyncPress` judges at `_now - RTT/2`, where `_now` is the start of the tick that drained the press, not the moment the press arrived. That adds 0 to 50 ms of quantisation.

Net effect: judged fraction = needle fraction + (q − 50 ms + client frame delay) / 1.1 s, with q in [0, 50) ms. That is a random early bias of up to about 45 ms, while the great part is only 0.035 × 1.1 s = 38.5 ms wide. The early side falls into the leading tolerance band, which is Good.

The latency term is also wrong. The needle starts when the check reaches the client (sent + RTT/2), and the press needs RTT/2 more to come back. So the client-visible elapsed time is recv − sent − **RTT**, not recv − sent − RTT/2. `RttSeconds` is only ever set by tests today; there is no server-side RTT probe.

## Spec

1. **Server clock.** Give `GameServer` one monotonic wall clock.
   - Default: a `Stopwatch` started at construction.
   - Tests can replace it through an internal hook (for example a `Func<double>` on `ServerOptions` marked for tests, or an internal setter).
   - `Run()` keeps its own pacing; it may use the same clock.
2. **Stamp inputs on arrival.** The input queue item (`Input` struct) carries `ReceivedAt` = clock seconds, taken on the receive thread when the packet is enqueued (`GameServer.cs:134`). The offline link uses the same path, so it is covered.
3. **Record the tick start.** At the start of each `Tick`, record `_tickWallStart = clock()` before draining.
4. **Map the press to simulated time.** When handling a `C2S_SyncPress`:
   - `pressAt = _now − clamp(_tickWallStart − input.ReceivedAt, 0, 0.25)`, where `_now` is still the tick's start at that point.
   - Then judge with `pressAt − RttOf(player)` (full RTT, see the latency note above).
   - Pass the receive time through `Handle` only for this packet. Do not change how other packets are handled.
5. **Stamp the check when it leaves.** `SyncSentAt` = the simulated time when the check leaves:
   - `start + clamp(clock() − _tickWallStart, 0, step)`, where `start` is the tick's pre-increment `_now`.
   - Keep whatever is simplest and exact: the point is to remove the one-tick-late stamp.
   - Keep the bot path (`SyncBotPressAt`) consistent with the new origin, so bot Great/Good shares stay as they are today (`BotSyncGreatShare` presses land in the middle of the great part).
6. **Fail limit.** The expiry in `UpdateSyncChecks` must still let a press made at Duration + tolerance arrive. Use the full RTT there as well, and keep the `TimeEpsilon` handling.
7. **`Classify` and constants stay unchanged.** That covers zone, great part, tolerance and duration. This unit fixes measurement, not difficulty.
8. **Client dev autopilot.** `HackPilot.SyncDue` (`Client/Assets/Scripts/Dev/DevAutopilot.cs:91-100`): `-ptk-auto-sync great` aims at the middle of the great part instead of `ZoneStart − GreatLead`. Remove or retune `GreatLead` and update its comment. Update `RulesV2WiringTests` to match.
9. **No protocol change.** Packets, Shared and the wire format stay as they are.

## Tests (required)

`Server.Tests`, new file `Server.Tests/Core/SyncTimingTests.cs`, using an injected clock:
- **Sweep the arrival offset.** A check is issued. A press arrives at wall time = check send + fraction × 1.1 s, with fraction at the great-part middle. Sweep the arrival offset inside the tick over at least 0, 10, 25, 40 and 49 ms. Each case is judged Great.
- **Edges.** Presses that arrive just inside the end of the great part are Great. Presses just after it are Good. Presses before zone start minus tolerance are Fail. Presses after zone end plus tolerance are Fail.
- **RTT.** With `SetRttForTests(0.1)`, a press arriving RTT later than the in-great moment is Great.
- **Clamp.** A press whose `ReceivedAt` is older than 0.25 s before the tick start is clamped and does not throw.
- **Bots.** Bot shares are unchanged. Reuse or extend an existing bot sync test.

Fix existing sync tests whose expectations encoded the one-tick bias. In the report, list each changed assertion and why the new value is right. Do not weaken any test.

## Gates (run all, report numbers)

- `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings.
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`: all pass.
- `dotnet test Tools/Harness.Tests`: all pass. It is slow (about 18 min); run it in the background.
- Harness scored run, 3 times, each with a fresh `--sandbox` server and a fresh normal server, using `--threshold 100 --expect-los`. Follow the method in `docs/PLAN.md` §5, then stop the servers. Reports go to `docs/qa/harness-u6f-run1..3.json`.
- Unity EditMode, from this worktree's `Client` (absolute `-testResults` path): all pass, 0 compile errors.
- Unity BuildWindows: success.
- **Live check (the acceptance test).**
  - Run the built exe offline, windowed 1280x720, with `-ptk-offline -ptk-bots 7 -ptk-start -ptk-role survivor -ptk-dev-hack 240 -ptk-auto-sync great -ptk-quit-after 150 -logFile <abs>`.
  - Count `sync player=<you> ... result=` lines: **at least 80 % Great**, with at least 5 checks.
  - Run again with `-ptk-auto-sync good`: at least 80 % Good.
  - Put both logs in `docs/qa/U6f-sync-timing-runs/`.
- After Unity runs, revert files whose only change is line endings (`git checkout --` them).

## Rules

- **Never send keyboard or mouse input to the desktop.** Launching the exe with flags is fine.
- Do not kill processes you did not start. If a Unity process you started hangs, report it instead.
- Owned files:
  - `Server.Core/Runtime/GameServer.cs`
  - `Server.Core/Runtime/GameServer.Terminals.cs`
  - `Server.Core/Runtime/Player.cs`
  - `Server.Core/Runtime/ServerOptions.cs`
  - `Server.Tests/**`
  - `Client/Assets/Scripts/Dev/DevAutopilot.cs`
  - `Client/Assets/Tests/EditMode/RulesV2WiringTests.cs`
  - the HUD_SPEC §13 sync timing sentence, if one describes the judging
  - `docs/qa/U6f-*`
  
  If you need any other file, say why in the report.
- Commit on the branch when green. The message is in English and ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Report: `docs/qa/U6f-sync-timing-impl-a1.md`, at most 200 words, plus a table of changed test assertions. Cover what changed, the gate numbers, the live Great/Good counts, what is unverified, and risks.
