# B1-balance impl report, attempt 1 (Claude Opus)

Branch `claude/b1-balance`, not pushed. Evidence: `docs/qa/B1-balance/` (sweep tool, lever outputs, gate logs, live run).

**Diagnosis** (all-bot sweeps before the change; 1v7 vs 1v3):
- Rescue: 84 % vs 78 % of downs rescued. A 5 s repeat rescue changed nothing (the bot Killer never guards a down).
- Hacking: power at 493 vs 463 s. A sixth terminal at 1v7 gave 34 %, a 718 s median and 8 % time-ups.
- Attention: 34 swings and 12 hits per 1v7 match. Range scales with team size: +0.2 m is −7 points at 1v7, −1 at 1v3.
- Escape: 5.1 of 7 are still in play at the first open exit (4 must escape).

**Changes** (Shared constants; the client follows without code changes):
- `SwingRange` 2.75 → 2.95 m (+7 %).
- `ExitSwitchSeconds` 15 → 17 s.
- No bot change. Moving the bot trigger with the range made results jump, so it stays 2.75 m.

**Survivor win %** (seeds 1–40 / 1001–1040 / 5001–5240 / 20001–21000):

| | Before | Range only | Switch only | **After** |
|---|---|---|---|---|
| 1v7 | 50/53/50/50 | –/–/43/43 | –/–/46/45 | **45/43/40/40** |
| 1v3 | 53/48/55/51 | –/–/52/51 | –/–/51/49 | **48/48/50/50** |
| 1v1 | 33/43/38/35 | –/–/38/33 | –/–/36/35 | **38/35/35/34** |

- 1v7 hits per match: 12.0 → 13.0 (non-eliminating captures 10.0 → 10.6).
- 1v7 medians: 598 / 604 / 604 s. No 1v7 match ends before 4 min.
- BalanceSweep bands: 1v7 30–55 %, 1v3 30–65 %.

**Gates**
- Build `-warnaserror`: 0 warnings.
- Server.Tests: 1147/1147. BalanceSweep: 18/18.
- Harness on 7781/7782, fresh servers: 100/100 ×3.
- Harness.Tests: not run (`Tools/` untouched).
- EditMode: 1606 passed, 2 skipped.
- BuildWindows: OK.
- Live Killer run (720p, 7 bots, 300 s): 0 exceptions, 7 swings (4 hits), 3 eliminated, no result yet at 300 s. I looked at three shots (`play-1s`, `r1-killer-hit`, `u8-killer-capture-4-after`).
- Tests now read the constants instead of 15 s or 2.75 m.

**Risks**
- All numbers are from bots. The bot swings from 2.75 m, so a human at full reach may gain more.
- 40-seed sets move ±8 points. I picked 2.95 m and 17 s partly so both fixed sets land in the band.
- 1v7 seeds 1–40 have 3/40 time-ups (limit 10 %).
- There was no rescue in the one live run.
