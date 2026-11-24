# Play check 2026-11-05 (main 9e89ed4, Opus)

Build: `Client/Builds/Win64/PingTheKiller.exe` from the merge gate. Two offline 1v7 matches ran at once for 330 s each, 1280x720 windowed, dev autopilot only (no desktop input):
- Survivor: `-ptk-role survivor -ptk-dev-hack 240 -ptk-dev-switch -ptk-auto-sync great -ptk-skill-at 8`
- Killer: `-ptk-role killer -ptk-chase 300 -ptk-killer-look nearest -ptk-skill-at 8`

Evidence: `playcheck-1105/surv.log`, `killer.log`, `surv/*.png` (22), `killer/*.png` (8). No exception or error lines in either log.

## Findings

1. **major: Sync check Great is nearly unreachable.** Auto-sync aimed 0.01 before the great part on 6 checks; all 6 were judged Good (e.g. check 1: zone 0.68, great 0.68-0.715, client needle 0.675, server Good). The great part is 0.035 x 1.1 s = 38.5 ms, shorter than one 20 Hz tick (50 ms). `GameServer.SyncPress` judges at the tick's `_now - RTT/2`, so a press waits up to 50 ms in the queue before it is timed, and the client needle starts when the check reaches the main thread (one frame or more after `SyncSentAt`). A human pressing on the visual great part lands late. Fix direction: timestamp the press when the session receives it (server clock), and/or align the needle origin; server change, needs its own unit.
2. **major (known, U6d): matches do not reach the end game.** At 322 s the Survivor match had 2/5 terminals, the Killer match 0/5 at 278 s; neither ended in 330 s. Exits, collapse and hatch were never reached live.
3. **minor: elimination copy says "caught a third time" after two real captures.** Captured at 48 s, the 60 s timer ran out (stage 2 per GDD-v2 §3), rescued at 111 s, captured again at 154 s -> `Eliminated reason=third capture`, line "Disconnected: caught a third time." The rule matches the GDD; the wording does not match what the player saw.
4. **minor: survivors stand inside each other.** At a terminal (two hackers) and during rescue the bot and the player overlap (`surv/v2-sync-check.png`, `surv/survivor-rescue.png`).
5. **minor: lobby bot names skip a number.** With the player in slot 4 the list reads Bot 1, 2, 3, 5, 6, 7, 8 (`surv/lobby.png`).
6. **minor: terminal count wording differs by role.** Survivor "Terminals 0/5", Killer "Terminals 0 of 5".
7. **known: Killer edge distance labels overlap** (`killer/v2-killer-overloaded.png`, "Terminal 4 overloaded · 43 m" over "21 m").

## Not defects

- Killer skill press refused with "Packet afterimage is passive.": the Killer drew skill 2, which is passive.
- Killer view pointing at the sky at 278 s: mouse look runs only with focus and a locked cursor (`RoleCamera.cs:40`); most likely real mouse movement over the focused window.
- Server.Tests run without `Category!=BalanceSweep` fails 4 U2d sweep cases (known U6d gap); with the usual filter 955/955.
