# INTEG-R-minors implementer report, attempt 1 (Claude Sonnet)

Branch `claude/integ-1117b`, on 12b31d9.

## Fixes
1. **Orphan.** `HelpCopy.cs`: each controls legend breaks on purpose after the Hold E item, so line 2 is `Q Skill · Esc Menu` (Killer) or `While hacking: ... · Q Skill · Esc Menu` (Survivor). Test: `HelpTests.NoLineOfEitherControlsLegendIsALoneItem`.
2. **Esc.** `IntroCardState.ConsumeEscape`, called in `ClientApp.cs` before the menu toggle. Esc on a visible card dismisses it alone and does not open the menu that press, so the card cannot return. Gamepad Start still opens the menu. Test: `HelpTests.EscOnTheIntroCardDismissesItAloneAndItNeverReturns`.
3. **Attack prompt.** `KillerHudCopy.SwingClear` checks FixedMap walls plus WorldState Down shutters (mirrors the server). `KillerWorldLayer.NearestSwingableBlip` feeds the prompt. No protocol change. Test: `KillerAttackR1QaTests.ADownShutterOrAWallBetweenTheKillerAndABlipMakesTheSwingUnclear`. The shutter box uses a local slab test, because `MapQaTests` forbids `new Wall(` in client sources.
4. **Rescue term.** `SkillFeedback.cs` now reads `Rescued N`, `N rescued you`, `N rescued N`. `SkillFeedbackTests` and HUD_SPEC (14.2.8 and a few `restored` mentions) are updated. `ally` is left: SLOP §8 fixes no teammate term, and HUD_SPEC's U2b "no ally" note is older than the downed line.
5. **PROTOCOL.md.** Reason 6 now says Firewall block has no target (no exit with progress and no terminal with progress). Reason 7 is defined but never sent. The `SkillRejectReason.cs` comments match.
6. **RTT test.** Bound 0.05 to 0.25 s, with a comment.
7. **Comment.** `ProtocolV08Tests` comment fixed to say what is asserted.

## Gates
- Build with `-warnaserror`: 0 warnings, 0 errors.
- Server.Tests non-sweep: 1147/1147. Sweep: 18/18.
- Harness, fresh servers on 7793 (sandbox) and 7794, 3 runs: 100/100 each.
- Unity EditMode: 1606 passed, 0 failed, 2 skipped. The first run failed one test, `MapQaTests.NoHardCodedMapCopyOrItsTodoRemains`, and was fixed as in item 3.
- BuildWindows: succeeded.
- Live runs on the fresh exe at 1280x720: `killer/play-1s.png`, `survivor/play-1s.png`. Neither card shows an orphan.

Evidence is in `docs/qa/INTEG-R-minors/`.

## Unverified and risks
- No key or mouse input was sent in the live runs, so Esc was not exercised live (unit test only).
- The prompt suppression was not seen live.
- The legend contains a literal `\n`; How to play shows it as a line break.
