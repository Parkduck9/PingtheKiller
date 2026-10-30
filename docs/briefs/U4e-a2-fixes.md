# U4e — attempt 2: short fix round (orchestrator)

QA attempt 1 (`docs/qa/U4e-qa-a1.md`) returned PASS, 92/100. The orchestrator asks for one short round anyway, on the same approach and with the same owned files as `U4e-a1.md`. Do not commit.

## Fixes
1. **Q-02: mid-word wrap.** No label may break inside a word, at either 1280×720 or 1920×1080 (`Emergenc` / `y restore`).
   - Fix the skill-name block layout in the Survivor HUD so that the longest skill name ("Emergency restore", and every other name in the `SkillIds` tables) wraps only at spaces, or fits.
   - Check the Killer cluster skill name too.
   - Add a test that every skill name's longest word fits the name column width at the S/M type size, using the same width estimate as `TagLayout` or a measured constant.
2. **Q-03:** the Killer's visor fades together with the body during the 0.3 s fade.
3. **Q-04b:** client guard. The Killer model is never drawn while the own status is Eliminated or Escaped (spectating), even if an entry arrives. Add a test.
4. **Q-04: banner wording.** Use the meter's state word, so the banner starts `Lagging.` instead of `Captured.`. Keep the rest. Record the change in HUD_SPEC §4.1 `(10/30)`. The elimination reasons keep the brief's binding copy.

## Not in scope
- **Q-01** (level-1 strip legibility) is a spec question for the user. Do not change the 4 u strip.
- **Q-06** (SLOP §8 text) is the orchestrator's.

## Verification
- Unity gate: all EditMode tests pass, QA's new `SurvivorFeedbackQaTests` included. Report the totals from the XML. `BuildWindows` succeeds.
- Re-take only the shots that change, at 1280×720 and 1920×1080: `threat-3`, `eliminated-reason` and `capture-banner`. Add one 720p shot that shows the "Emergency restore" skill name. Open each one with Read.
- Append an "Attempt 2" section to `docs/qa/U4e-impl-a1.md`.
