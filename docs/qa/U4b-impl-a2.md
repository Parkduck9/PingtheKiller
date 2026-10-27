Implementer: Claude Opus subagent (quota switch from Codex) — U4b round 2

## Changed (`Client/` only)
- `HudCopy`: names keyed by Shared `SkillIds`; TODO gone; `IsPassive` uses `SkillIds.PacketAfterimage`.
- `World/SceneLighting` (new seam): the empty scene has no lighting data, so the ambient probe was black. It now sets a dark trilight `RenderSettings.ambientProbe` (brighter from above: wall tops > sides), a key light and a weak fill, no skybox. `WorldView` terminals: square plinth + muted panel, teal when done.
- `LobbyCopy.DisplayOrder`: host, humans, bots, empty; display only. Server.Core's own order could change later.
- F-04: menu line is now `Enter the IP and port of the host PC to join its lobby.` Skipped: F-03 bot ids (server), F-05 to F-07 (info/protocol).
- Dev: `-ptk-wander`, `-ptk-rematch`, event shots, `[DEV]` logs. Seams for U4c: yaw only via `RoleCamera.Turn`; steering in `DevCapture.Steer` + pure `Dev/Wander`.

## Gate
EditMode 545: 544 pass. 1 fails by design: QA `HudCopyQaTests.TheSkillTableKeepsItsTodoForTheSharedSkillIds`. 0 `error CS`. Build: Success.

## Runs
- **a offline survivor** (repeated once): caught at 10.1 s; countdown `Lagging · 60 s`→`59 s`… Killer bot downed all six survivors: `AllSurvivorsDown` at 18.1 s (19.6 s), so no exit-open. Back to lobby `enabled=False` at the result, `True` after LobbyState. Match 2: (5,36)→(5,47.62), moved 11.62 m.
- **b offline killer**: `Terminals 4/4 · Exit open` at 16.29 s; `SurvivorEscaped [6,7]` at 18.4 s.
- **c online** (3 runs): 1: exit-open at 66.7 s, `SurvivorEscaped [6]` at 80 s. 2: the local capture ended the match on the same tick; a dev bug took `lagging.png` over the result screen, now skipped and logged. 3 (final build): `TimeUp` at 599.93 s; survivor bots stuck at 3/4 from 115 s; local never caught. Match 2 moved 9.60 m.
- HUD = log (`a2-a-lagging`: `Lagging · 59 s`, `Terminals 1/4` at 11.1 s). 0 exceptions.

## Screenshots `docs/qa/U4b-screens/`
`a2-light-{before,after}-{survivor,killer}` (before = HEAD build, same camera); `a2-a-{hacking,lagging,lagging-run1,result,lobby-return,rematch-play}`; `a2-b-{hacking,exit-open,result}`; `a2-c-{hacking,result,lobby-return,rematch-play,exit-open-run1}` (run1 = previous lighting).

## Socket proof (run a)
`Get-NetTCPConnection` 0 and `Get-NetUDPEndpoint` 0 at 8 s and 30 s, both runs. Online control: →127.0.0.1:7777 Established.

## Needs a human
Audio, E/Q feel, gamepad, F3 line.
