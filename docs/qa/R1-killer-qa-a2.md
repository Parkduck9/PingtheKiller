# R1-killer QA a2 (Fable; implementer Opus)

**Verdict: PASS.** Every a1 major is fixed; only minors remain.

## a1 list
- **F1 NaN on a first miss: FIXED.** `Offset`/`Shake` are zero outside their windows and `Apply` is NaN-guarded. Live: the first swing of the session missed and the world rendered (`R1-qa-screens/a2/killer/r1-killer-first-swing-miss.png`).
- **F2 immediate feedback: FIXED.** The press plays the whoosh and sweep at once (log t=22.11 press, 22.15 server start). A hit is still shown only from the server (capture confirm, or the flag length).
- **F3 eliminating hit: FIXED by test, not reached live** (no third capture in 245 s).
  - The length is counted in server ticks (`WorldState.Tick`), so lag and jitter cannot change it. The server sends every tick over TCP and drops no snapshot.
  - A blip that only went Silent holds the verdict; the 20-tick fall then says `Missed` (seen live, correct). Two blips vanishing at once behave the same.
  - It uses only the Killer's own `Swinging` flag: no new data.
- **F4: FIXED.** Other slots are `0xFF` per recipient; `ChooseSkill` is limited to 4/s (tests for both).
- **F5: FIXED.** Picker lines are 18 u in `text` with even breaks; `Missed` is in `text` for 1 s.
- **Firewall relock: PASS.** 20 s after the lock ends, pinned by a server test; another terminal can still be locked.

## Gates
- build `-warnaserror`: 0/0
- Server.Tests: 1148/1148; BalanceSweep 18/18
- Harness 3× on 7791/7792, fresh servers: 100/100 each
- EditMode: 1567 passed, 0 failed, 2 skipped
- Exe 17:33:58, newer than every source file
- Live Killer, 245 s, 1280×720: 10 presses, 3 hits, 7 misses; the client result matched the server on all 10; 0 exceptions. Logs: `docs/qa/R1-qa-runs/a2/`.

## Findings (all minor)
- **M1.** `Predict` plays the whoosh and sweep even when the server will refuse the press (stunned, vaulting, breaking). The own `Chase` flags could gate it.
- **M2.** An eliminating hit is shown at the flag's fall, 0.6 s after the strike. On a Survivor the Killer never saw, it still reads `Missed` (`LateHit` only logs).
- **M3.** `Missed` comes 1.0 s after the press, not 0.55 s, when a nearby blip went Silent (2 of 7 misses live).
- **M4.** `HitTicksMax` is 18. A miss with two lost snapshots at the rise would read as a hit. Not reachable today; no test covers 18.

## Balance risk
1v7 is 50 % / 55 % (in target). 1v3 is 62 % / 68 %, above main's 55 % / 60 %, so the Killer is not stronger there. 1v1 is 35 % / 25 %.

## Suggested follow-ups (not blocking)
1. Gate `Predict` on the own Stunned, Vaulting and Breaking flags.
2. Show a `LateHit` instead of leaving `Missed`.
3. Add the 18-tick boundary to the watch test, or lower `HitTicksMax` to 17.
