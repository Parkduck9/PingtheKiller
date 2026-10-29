# U4d implementation report — attempt 1

Implementer: Claude Opus subagent (quota switch from Codex, not an attempt). Interrupted once (app restart); a second Opus subagent finished it.

## What changed
- `World/SelfState.cs`: pure holder + local clock (count-down, snap, floor 0, once-per-cycle `skill_ready` edge); stub removed.
- `ClientApp.cs`: `S2C_SelfState` pump case; reset on match start/end, link switch, disconnect; Survivor HUD feed.
- `UI/SurvivorHud.cs`, `UI/SurvivorReadout.cs` (new): §4.1 meter incl. row 3 and `Protected`, §4.4 rescuer row, §4.8 ring + `ceil` count.
- `SkillRing`, `KillerHud`, `KillerWorldLayer`, `ExecutionGuidance`, `KillerCues`: §6.1d cooldown, §6.4 grace from SelfState, `skill_ready`.
- `Net/NetClient.cs`: **bug fix** — the socket whitelist rejected `S2C_SelfState` (disconnect online; offline hid it).
- `Dev/DevArgs`, `DevCapture`: `-ptk-skill-at`, `-ptk-chase`, `-ptk-rescue`, the four event shots.
- Tests: `SelfStateTests`, `SurvivorHudTests`, `SixteenEntriesTests`, `NetClientTests` (+2), `DevArgsTests`, `KillerHudTests`.

## Verification
- EditMode: 1007 total, 1006 passed, 1 failed: `KillerHudQaTests.ThereIsExactlyOneMarkedAdapterPointForTheFutureSelfStatePacket` (superseded by this brief; QA to replace). 0 `error CS`. `BuildWindows`: Success.
- Offline sockets (match running, +20 s / +35 s): tcp=0 udp=0 both samples.
- Online (U2c server, `--bots 6`), 0 exceptions:
  - `[net] SelfState received (first of the match)`
  - `[DEV] t=3.05 selfstate #5 skillCooldown=15.00` … `t=17.11 cooldown local=0.94` → `[net] skill_ready`
  - Killer: `[DEV] t=15.20 blips tracked max=11` (6 Survivors → phantoms); `t=6.96 execution prompt=Grace grace=2.00` → `StepBack`

## Screenshots (`docs/qa/U4d-screens/`, all natural)
- `killer-crowd`: ≥ 6 tracked blips (offline, final build).
- `killer-grace`: `Wait 2.0 s, step back` (offline, final build).
- `survivor-cooldown`: ring `14` (720 offline; 1080 online).
- `survivor-rescue`: `Rescuing Bot N` + ally bar (1080 offline final build; 720 offline pre-NetClient-fix build, offline path unchanged).

## Deviations
- 1080 rescue needed 6 offline + 2 online tries (bot Killer captures early).
- Survivor "Not used" (§4.8) not implemented (not in brief).

## Needs a human
QA replacement for the superseded QA test.
