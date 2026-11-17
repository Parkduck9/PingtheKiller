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

## Attempt 2 (Claude Opus): F2, bot prop use

**Cause.** Attempt 1 used props by chance: a shutter within 3 m straight ahead, a rail with the killer within 2.5 m, a break only beside the shutter. A human seat lowers that rate further.

**Fix (`ChaseTactics`; no rule number changed).**
- Survivors chased within 8 m plan for the nearest shutter or rail within 9 m that they reach first. They pull when the killer nears the box (2.3 m, or after 2 s), or they vault.
- The killer walks up to a Down shutter within 6 m to break it when the detour exceeds 8 m. It vaults a rail its target loops.
- A* vault costs 2 m and 6 m; phantoms never vault.
- A Dummy cast mid-vault no longer spawns its phantom over the rail.

**Use per 300 s all-bot 1v7 match:**

| | pulls | stuns | rail vaults (killer) | breaks |
|---|---|---|---|---|
| before | 1.06 | 0.56 | 0.38 (0) | 0.63 |
| after | 2.50 | 1.58 | 8.25 (1.33) | 2.17 |

**Balance** (two seed sets):
- 1v7: 53 % / 589 s and 63 % / 600 s
- 1v3: 60 % and 55 %
- 1v1: 38 % and 40 %

**Gates:**
- build: 0 warnings
- Server.Tests: 1120/1120, twice
- BalanceSweep: 18/18
- harness: 100/100 ×3
