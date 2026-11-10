# Q1-playcheck-polish — orchestrator judgement, attempt 1

Quota switch (2026-11-08): Codex had no credits left and the agy Gemini Flash sign-in had expired. Implementer: Claude Opus subagent. QA: Claude Sonnet subagent. Judge: Claude Opus (orchestrator).

**Verdict: PASS.**

| Item | Verdict | Evidence |
|---|---|---|
| 1. The elimination copy matches the captures the player saw | PASS | Two pieces: `CaptureWatch.Caught` and `CaughtTimes`, which is set only when the watch saw the whole match and its count agrees with the server's. Live run: 3 real captures, and the meter shows "Disconnected: caught 3 times." (`shots-a/eliminated-reason.png`). The escalation and unknown-history cases are covered by unit tests. |
| 1b. Meter tag | PASS | At `CaptureCount ≥ 2` the tag reads `Next capture eliminates`, measured in PlaycheckPolishTests. This was the same bug class, found while writing the brief: "Captured twice" was shown after one capture plus an escalation. |
| 2. No stacked bodies | PASS | `VisualSeparation` is pure and draw-only, with an eased offset bounded by 0.8 m. The own avatar and the Killer model are pinned. `shots-b/survivor-rescue.png`: the rescuer stands beside the downed body, not inside it. |
| 3. Contiguous bot names | PASS | `LobbyCopy.ContiguousBotNames` is applied once where the lobby state arrives in ClientApp. Unit tests cover the player in slots 1, 4 and 8, two humans, a human-turned-bot, and empty or null slots. In the live lobby the player was in slot 1. |
| 4. One terminal format | PASS | `HudCopy.Terminals` is shared by both roles, so both read `Terminals n/m`. HUD_SPEC §6.1a was updated. |
| 5. SLOP / HUD_SPEC | PASS | The orchestrator updated the stale HUD_SPEC §4.1 copy (tag table, reason table, U6c-wire note) and restored the doc-parity assertion. It now also checks `Caught(3)`, `Caught(2, escalated)` and the new tag. |

## Beyond the brief

- **Departure accepted: the Killer model is pinned.** An existing audit test requires the Killer model to be placed exactly from its entry. A Survivor drawn next to the Killer model is moved 0.8 m away from it, which is cosmetic.
- **Minor risk accepted:** a pushed body can be drawn up to 0.8 m into level geometry. There is no wall check, and nothing authoritative changes.

## Not verified

- The escalation copy and the slot-4 lobby were not seen live; unit tests only.
- The Killer-side `Terminals n/m` was not seen live; unit test and code only.

## Gates

- Unity EditMode, orchestrator run after the doc and test fixes: see `judge-editmode.xml`.
- BuildWindows succeeded (implementer). No change to Shared or the server, so the dotnet gates were not run.
