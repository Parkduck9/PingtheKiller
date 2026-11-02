# Area C re-verification (U4c, U4d, U4e): orchestrator judgement (Claude Opus)

**QA model actually run:** Antigravity Gemini 3.8 Flash. No Claude model did QA in this pass.
- Code and tests: `gemini-3.8-flash-medium`, report `docs/qa/Reverify-client-flash.md`.
- Screenshots: `gemini-3.8-flash-high`, report `docs/qa/Reverify-client-flash-screens.md`.
- Flash wrote both reports and all test files itself. Nothing was saved through the stdout fallback.

**agy setup:**
- Headless print mode.
- Session-only `--settings`, never saved. The final run used the orchestrator's `agy-qa-session-settings.json`. Earlier runs used an equivalent read-only set limited to the user-approved rules.
- No `--mode auto`, no `--dangerously-skip-permissions`.
- Flash was told never to call shell commands. agy denies unsandboxed commands even when an allow-rule matches.

## Gate (orchestrator, Unity 6000.3.25f1 batch)
Run on `C:\Unity\MultiPlayGame\PtK-reqa\client\Client`, branch `claude/reqa-client-flash`, base main `91e8891`.

| Run | Total | Passed | Failed |
|---|---|---|---|
| 1 | 1186 | 1184 | 2 |
| 2 (after Flash fixed its own test) | 1187 | 1186 | 1 |

- 0 `error CS` in both runs.
- Reverify suite: **36/36 pass**.
- **Run 1, Flash test failure:** `DevCapture_Preview_NeverReadsPlayerStateOnKillerRole` looked for `Preview` in `DevCapture.cs`; it is in `ClientApp.cs:266`. The orchestrator checked the product behaviour: the Killer branch reads only `lobby.Slots`, so it is correct. Flash corrected its test.
- **Remaining failure:** `OfflineLinkQaTests.SurvivorRoleStartsAMatchAsSurvivorAndNeverReceivesAKillerEntry` (see F-01).

## Findings, each checked against the code
| # | Flash severity | Orchestrator ruling |
|---|---|---|
| **F-01** | Major | **Confirmed, major. Pre-existing; a test defect, not a product defect.** The U4b QA test asserts that a Survivor never receives a Killer entry, which is U2b rule 10. U2e (merged in main as `7eb1fd3`) intentionally sends the line-of-sight Killer entry, so main's client EditMode suite is red. Nobody ran Unity on merged main after U2e. As a side effect, this proves that real U2e Killer entries reach the client through the in-process offline server. Flash's new `SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract` passes against the same server. Fix: re-target the old test (separate branch `claude/fix-offline-los-test`). |
| D-01 | Minor | Accepted deviation (U4e Q-05): sighting gaps of 0.3 s or less are interpolated. |
| D-02 | Minor | Accepted deviation (U4c): facing the terminal while hacking is visual only. |
| D-03 | Minor | Accepted (U4c I-1): the boom sphere cast also hits the floor. |
| M-01, M-03 (screens) | Minor | Confirmed cosmetic: chevron/tag proximity and stacked distance tags. They match the U4c F-07 follow-up. |
| M-02 (screens) | Minor | The visor alpha in `killer-fade-injected-*` predates the Q-03 fix. Covered by a test; the shot was never re-taken. |

**Anonymity audit:**
- Flash's file:line trace of every `PlayerState` read on the Killer path, dev code included, matches the code.
- One citation was wrong: `Preview` is in `ClientApp.cs`, not `DevCapture.cs`. The behaviour it describes is correct.
- No Survivor `Id`, `Yaw`, `StatusTimer`, `CaptureCount` or forbidden flag is read on the Killer path.

**Screenshot scores:** U4c 99, U4d 100, U4e 100. These are more generous than the earlier Sonnet scores (92 to 94). No gate violations in either review.

## Verdict
- **Units U4c, U4d, U4e: PASS.** No defect in the client product code.
- **Overall, Flash's verdict stands: FAIL (0 blockers, 1 major).** The major is F-01, the stale test that keeps main's client suite red. Fixing it is the orchestrator's and the user's decision, and is prepared separately.
