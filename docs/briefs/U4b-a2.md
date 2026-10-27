# Brief U4b — round 2: real-rules verification, SkillIds, lighting, lobby order

This is a scope extension after U4b a1 passed; it is not a retry.
- **Implementer:** Claude Opus subagent (quota switch from Codex).
- **QA:** Claude Sonnet.
- **Branch:** `claude/u4b-offline`. It already contains the U4b a1 commit and a merge of main `5f83456` (U2b: full rules, GameResult → lobby, `Shared/Runtime/Game/SkillIds.cs`, `--bind`, Seq reset per match). Do not commit.

Read `AGENTS.md`, `docs/briefs/U4b-a1.md`, `docs/qa/U4b-judgement.md`, `docs/qa/U4b-qa-a1.md`, `docs/ui/SLOP_DEFINITION.md`, `Server.Core/Runtime/**` (U2b rules: Rules, Skills, Visibility, Bots) and `Shared/Runtime/Game/SkillIds.cs`.

## Owned files
- `Client/**`, except `Client/Assets/Tests/EditMode/QA/**`
- `docs/qa/U4b-impl-a2.md`
- `docs/qa/U4b-screens/a2-*.png`

Never edit `Server.Core/`, `Server/`, `Shared/`, `Tools/` or `PingTheKiller.sln`.

## 1. SkillIds
- Replace the client's own skill-id numbers with the `SkillIds` constants, and remove the TODO.
- The name table keeps the GDD §4 names.
- `IsPassive` uses `SkillIds.PacketAfterimage`.
- Tests stay green.

## 2. Lighting (user request: wall sides and capsule shadow sides render pure black)
- Create everything by script, URP-compatible:
  - set environment lighting (`RenderSettings.ambientMode` = Flat or Trilight with sensible dark-palette colours and intensity), and/or add a weak fill light;
  - check the URP asset/renderer and fix whatever produces pure-black shaded faces.
- Keep the dark palette (SLOP §8): no bright skybox, background stays `#101113`.
- Shaded faces must be readable. Walls, floor, terminals and capsules must be distinguishable in both views.
- Make wall tops slightly lighter than wall sides so the top-down view reads.
- Terminals must not read like capsules from above: give them a distinct shape or tint within the palette (e.g. a square plinth plus a muted panel colour; never amber, which is reserved for Signal).
- Deliver before/after screenshots from the same camera:
  - `a2-light-before-survivor.png` (you may copy `docs/qa/U4b-screens/offline-play-survivor.png`) and `a2-light-after-survivor.png`
  - `a2-light-before-killer.png` and `a2-light-after-killer.png`

## 3. Lobby slot order (user request)
- Display order: the host (the local player in offline mode) in row 1, then other humans, then bots.
- Do this as a pure client-side sort of `LobbyState.Slots` for display only, with tests. Server.Core slot order stays as is; note in the report that the server's own order could change later.
- Row numbers are display positions 1..8.

## 4. Real-rules verification mode (dev flags, inert when absent; extend DevArgs and its tests)

### Flags
- **`-ptk-wander <seconds>`** — survivor autopilot during play. Hold W while turning the yaw slowly, e.g. 25°/s, and reverse when the wall ahead is within 2 m (use `FixedMap`). This keeps the local survivor Signal so the killer bot hunts it. All input goes through `MovementInput`; the 30 Hz rule still applies.
- **`-ptk-rematch`** — after a `S2C_GameResult`:
  1. wait for the lobby to reopen;
  2. press Back to lobby, then Start (host);
  3. in the second match, run `-ptk-hold-w` again from second 2.
- **`-ptk-shots <dir>`** — add event-triggered shots, each taken once, 1 frame after the event:

| Shot | Event |
|---|---|
| `hacking.png` | first WorldState where any terminal has 1 ≤ Progress ≤ 99 |
| `exit-open.png` | first WorldState with `ExitOpen = 1` |
| `lagging.png` | local survivor `Status = Lagging`, 1 s after the change (countdown visible) |
| `result.png` | result screen shown |
| `lobby-return.png` | lobby shown after Back to lobby |
| `rematch-play.png` | 4 s into the second match |

- **Dev log lines** (`[DEV]`, player log):
  - local position at seconds 2 and 5 of the second match, plus the distance moved;
  - match end reason, winner and escaped ids;
  - terminal progress and exit state each time they change.

### Runs
Do all four. The player runs with `-ptk-quit-after` large enough for a full match (a bot match ends in at most 600 s; let runs go up to 720 s, in the background).

**a. Offline, survivor.** `-ptk-offline -ptk-bots 6 -ptk-role survivor -ptk-start -ptk-wander 600 -ptk-rematch -ptk-hold-w 3 -ptk-shots <dir>`
- Expect hacking, exit-open, lagging, result, lobby-return and rematch-play shots.
- If the local survivor is never caught, record that (bots decide) and repeat once.

**b. Offline, killer.** `-ptk-offline -ptk-bots 6 -ptk-role killer -ptk-start -ptk-shots <dir>`
- Expect the killer view while bots hack (Signal survivors visible with the `Hacking` flag), the exit-open shot and the result.

**c. Online, survivor.** Start the real server from main first:
`dotnet run --project <WORKTREE>\Server -c Release -- --port 7777 --bots 6 --bind 127.0.0.1`
(build it first, or use `dotnet build` and then `dotnet <dll>`). Then run the same flags as (a), with `-ptk-join 127.0.0.1:7777` instead of `-ptk-offline`.

**d. No-socket proof again on the offline build.**
- During run (a), sample `Get-NetTCPConnection -OwningProcess` and `Get-NetUDPEndpoint -OwningProcess`.
- Expect 0 / 0.

Copy the meaningful shots to `docs/qa/U4b-screens/a2-<run>-<shot>.png`.

### What must be true
- HUD lines are readable and correct against the dev log:
  - `Lagging · N s` counts down;
  - `Terminals k/N` matches the log;
  - `Exit open` appears when the exit opens.
- The result screen names the right winner and reason.
- Back to lobby is enabled only after the lobby reopens.
- The second match accepts movement: at least 1 m moved between seconds 2 and 5.
- No exceptions in the player log.

## 5. Also
- **QA cosmetic items.** Fix F-03 to F-07 from `docs/qa/U4b-qa-a1.md` when they are cheap and clearly right; list any you skip.
- **No-input fallback for killer captures.** If a capture only happens near the killer bot, do not add any "move toward killer" logic: the client never knows the killer position.

## Gate (you run it; the orchestrator re-runs it)
- Unity batch: 0 `error CS`, all EditMode tests pass (QA suite included), `BuildScript.BuildWindows` succeeds.
- One Unity process at a time on this project. Another unrelated project's Unity may be running; ignore it.

## Report `docs/qa/U4b-impl-a2.md` (≤ 350 words)
- what changed
- test counts
- per-run outcome with key `[DEV]` log lines (winner, reason, timings)
- the screenshot list
- socket proof
- what still needs a human (audio, E/Q feel, gamepad)
