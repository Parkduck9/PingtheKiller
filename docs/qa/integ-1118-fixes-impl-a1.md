# integ-1118 fixes impl a1 (Sonnet 5.5)

Constants kept: `SwingRange` 2.95 m, `ExitSwitchSeconds` 17 s.

- **F1:** `BotBalanceTests.LargeSeedOneVersusSevenSurvivorWinRateIsOnTarget`: seeds 20001-22000, win 38-46 %, `Category=BalanceSweep`. 40-seed comment corrected (their band also passes main). Took 1 m 58 s here (machine was loaded; expect about 45 s alone).
- **F2:** GDD-v2 §11 (Korean addendum) and `B1-balance-impl-a1.md`: fixed sets 1v7 47.5/52.5, 1v3 50/47.5, 1v1 37.5/37.5; large means 1v7 41.7 (main 50.6), 1v3 47.9 (53.0); target is the large-seed mean; 1v3 effect is about -5. `GameConstants` comment fixed.
- **F3:** `AttackPromptRange = GameConstants.SwingRange`, no margin. Reason: the prompt then means a hit is possible on the blip positions held; blips are 100 ms old, so a margin would show it where swings miss. HUD_SPEC 6.1p updated; `KillerAttackR1QaTests` test rewritten.
- **F4:** `KillerFxState.Line`: swing result (1 s) now above Slowed; Slowed returns after. HUD_SPEC "Result line, Killer" updated. Test `ASwingResultShowsOverTheSlowedLineForItsSecondThenSlowedReturns`.
- **F5:** `RulesV2Preview` uses `GameConstants.ExitSwitchSeconds`; 15 s comments in `Tools/` changed to 17 s (comments only).
- **F7:** test renamed `APressEvery20TicksGivesEachSwingItsOwnFlagSpan`, comment fixed; new `APressEvery4TicksGivesSeparateSwingsWithAGapOfAtLeastOneTick` (hit and miss cases).
- **F6:** skipped as instructed.

**Gates:** build `-warnaserror` 0/0; Server.Tests non-sweep 1151/1151; BalanceSweep 19/19; Harness.Tests 641/641; harness 100/100 (7783/7784, servers stopped); EditMode 1617 passed, 0 failed, 2 skipped; BuildWindows OK. Live Killer 240 s: 21 swings, server 4 hit / 17 miss, client 4 / 17, 0 exceptions.

**Unverified:** a swing during Slowed did not happen live (unit test only); shots not inspected for it.

Evidence: `docs/qa/integ-1118-fixes/`.
