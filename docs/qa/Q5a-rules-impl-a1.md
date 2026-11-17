# Q5a-rules implementation, attempt 1 (Claude Opus)

**Protocol (S0 a28ed47, v0.7):** no new packet. `S2C_WorldState` ends with `[count ≤ 10][ShutterState]` (self-healing: late join and reconnect need no extra path; ≤ 11 B per frame). `PlayerState` gets a trailing `Chase` byte (Pulling 1, Vaulting 2, Breaking 4, Stunned 8). Killer-bound anonymous entries keep 0 so they stay indistinguishable from phantoms. `ActionKind` gets Pull 4, Vault 5, Break 6.

**Placements** (`WorldChaseProps`, boxes 0.3 m thick):
- Shutters: (21,19.5) (68,57.5) (28,22.5) (40.5,18.5) (47.5,14) (19.5,33.5) (58,44) (15,66) (54,67) (65,62).
- Rails: (6.5,57) (7,53.5) (68.5,24) (71,29.5) (56,33.5) (36.5,26.5) (40.5,56) (57.5,20).
- Clearances from any prop: terminals ≥ 5 m, exits and switches ≥ 6 m, spawns ≥ 5 m, hatch points ≥ 3 m. With every shutter Down the map stays one region.
- `SHARED_CHANGED`: after S0 I moved rails 0, 4 and 7 off the existing test lanes.

**Rules:** as briefed, plus three choices:
- A pull or vault starts only on a fresh press.
- A vault lands mirrored, at least 0.7 m from the centre line.
- A capture interrupts a pull or vault and lands the survivor on the nearer standable side.

**Priority:** for a survivor, rescue contact > nearest prop in its own range > switch or terminal. For the killer, rail vault or shutter break > hatch close > kick.

**Bots:** A* treats Down shutters as walls and adds rail vault links (cost +3 m for survivors, +7.5 m for the killer). `ChaseTactics`:
- A chased survivor runs past an Up shutter and pulls it once the killer is within 2 m. It vaults, or runs to a rail when the killer is within 2.5 m.
- The killer breaks a shutter when going around is more than 8 m longer.
- 6 all-bot 1v7 matches: 5 pulls, 4 stuns, 4 rail vaults, 4 breaks.

**Balance, 1v7:** before, U6d: 58 %, median 566/594 s. After (two seed sets): 53 % / 557 s and 55 % / 558 s.
- Tuned the bot use rate only, not the 🔶 numbers: the 1v3 QA set had reached 77.5 %.
- Stall test: I exempted the wait after every bot survivor is resolved (seed 1012, where the idle human was left alone).

**Gates:**
- build: 0 warnings
- Server.Tests: 1115/1115, BalanceSweep 18/18
- harness: 100/100 three times
- Harness.Tests: 641/641 (17 min 25 s)

**Risks:**
- The harness ran before a one-line change to `MovePlayers` (moving now cancels a break instead of being swallowed).
- No Unity compile was run.
