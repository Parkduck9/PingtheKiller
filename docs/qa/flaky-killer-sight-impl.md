# Flaky test fix: SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract

Implementer: Claude Sonnet subagent. The orchestrator (Claude Opus) made the cap change and ran the verification.

**Cause.** The probe Survivor stood still, so it was Silent. The random Killer bot hunts only noticed Signal blips (`KillerBrain.Look`). Survivor spawns are out of sight by design (map v2). The bot often never came into view within 60 s, so the test failed in about 1 of 3 runs. It failed 3 times on 2026-11-04.

**Change (test only, no product code).**
- After each WorldState the probe sends `C2S_MoveInput`, walking a square that changes side every 10 snapshots. Any non-zero input sets `LastSignal` (`GameServer.Simulation.cs:25`), so the Killer bot chases the probe.
- The cap is raised from 1200 to 2400 snapshots (120 s).
- The U2e assertions and the "at least one Killer entry" assertion are unchanged.
- The test logs `SNAPSHOTS_TO_FIRST_KILLER`.

**Verification.**
- 5 single runs in a row all passed. Snapshots to the first Killer entry: 103, 526, 442, 333, 599. The worst of these is 25 % of the new cap.
- The full EditMode suite passed 1356/1356.
