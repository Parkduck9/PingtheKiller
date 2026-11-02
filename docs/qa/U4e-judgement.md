# U4e — orchestrator judgement (Claude Opus)

**Verdict: PASS on attempt 2.**
- **Implementer:** Claude Opus subagent (quota fallback).
- **QA:** Claude Sonnet, a different model.

**Trigger:** the user's playtest. Their words: "적도 안보이고 능력은 써지는거같고 왜죽는지도 모르겠어" (the enemy isn't visible, abilities seem to fire, and it's unclear why you die).

## Attempt 1
- **QA:** `docs/qa/U4e-qa-a1.md`. VERDICT: PASS, SLOP §9 92/100.
- **The orchestrator ordered a short second round anyway:**
  - **Q-02:** the 720p skill name broke mid-word (`Emergenc` / `y restore`). It came from U4d, but it is a legibility failure on the screens the user plays.
  - **Q-03:** the visor did not fade with the body.
  - **Q-04b:** there was no client guard against drawing the Killer while spectating.
  - **Q-04:** the banner word did not match the meter's state word.
- **Q-01 (parent orchestrator ruling):** the level-1 strip matched the spec but was barely visible, which misses the playtest goal. HUD_SPEC §4.2 was amended to an 8 u strip at 45 % density `(10/30)`.

## Attempt 2
- **QA:** `docs/qa/U4e-qa-a2.md`. VERDICT: PASS. Q-01, Q-02, Q-03, Q-04 and Q-04b are FIXED.
- **Ruling:** QA's banner test asserting `Captured.` was superseded by the approved Q-04 wording, and QA updated it.

## Gate (orchestrator runs, Unity 6000.3.25f1 batch)
- **Final EditMode: 1151/1151.** This includes QA's 6 Unity-only attempt-2 tests on their first run.
- 0 `error CS`.
- `BuildWindows`: Success.
- **Offline, implementer run:** 0 TCP / 0 UDP while the in-process match ran.

## Spec (U4e-a1 §1–§5)
1. **Killer model for Survivors:** PASS.
   - It is drawn only from the Killer entry, interpolated and facing `Yaw`.
   - When the entry goes, it fades over 0.3 s at the last position, with no extrapolation.
   - If the entry returns after more than 0.3 s, the model snaps.
   - No marker or outline.
   - It is not drawn while spectating.
   - **Deviation, accepted:** gaps of 0.3 s or less are interpolated. That reveals at most about 1 m of path.
2. **§4.2 threat visuals:** PASS. They follow the amended strip sizes, `threat_scratch` plays on entering level 2, everything is hidden while Eliminated or Escaped, and the beep is unchanged.
3. **Capture cue:** PASS. The `captured` sound, the Lagging stutter (with `-ptk-reduce-stutter`), and a one-time banner, `Lagging. A Survivor within 1.5 m for 3 s rescues you · 60 s left`.
4. **Elimination reason:** PASS. Timeout, Executed, and caught a second time. The result line matches the meter. `Executed` was seen in a natural offline match.
5. **HUD_SPEC amendments:** PASS. Marked `(10/30)` in §0.1, §4.0–§4.2, §4.5, §4.8, §4.10, §6.5.1, §7.2 and §10.
6. **Folded U4d findings:** F-01, F-02 and F-03 are all fixed, with tests.

## Screenshots
`docs/qa/U4e-screens/`, at 720 and 1080. The orchestrator viewed them.
- **Natural:**
  - `threat-1`, `threat-2`, `threat-3`;
  - `capture-banner`, `lagging-stutter`, `eliminated-reason`;
  - `emergency-restore-720`.
- **Injected** (dev `-ptk-fake-killer`, until U2e lands): `killer-in-sight-injected`, `killer-fade-injected`.

## Open (not blocking)
- The real U2e Killer entry has not been exercised. That waits for the U2e merge.
- `killer-fade` was not re-shot after the visor fix. The fade is covered by a test.
- **Copy minors:**
  - `Disconnected:` can read as a network drop.
  - The banner's `60 s left` is fixed for its 3 s.
- `docs/ui/SLOP_DEFINITION.md` §8 still says "the Killer is never drawn". That doc belongs to the orchestrator.

## Needs a human
- Whether the threat strip and band read as danger in play.
- The capture sound and the feel of the stutter.
- The Killer model's fade timing against the real U2e line-of-sight entry.
