# U4c QA, attempt 2 (fix-round verification)

QA: Claude Sonnet (same reviewer as attempt 1; implementer: Claude Opus subagent). Date 2026-10-27. Branch `claude/u4c-cameras`, nothing committed. I did not run Unity, the game or a server. I opened all 14 retaken screenshots in `docs/qa/U4c-screens/` with the Read tool.

## Verdict
- **QA-PASS**, on the condition that the 50 new cases below pass in the Unity EditMode gate (the orchestrator's 936/936 does not contain them).
- All six findings of attempt 1 and I-2 are FIXED. No blocker, no major. One new minor finding (F-07) and two info items.
- SLOP section 9: Killer HUD **92 / 100** (threshold 85, no gate failure; was 84), Survivor view **94 / 100**.
- What I could run: the scratch runner executed 32 of 32 `TagLayoutQaTests` and 13 of 13 `DevPathAuditQaTests` (no Unity needed). The 5 `KillerFrustumExitQaTests` need `Quaternion.Euler` (a native call) and so only run in Unity; their expected values were hand-checked against `KillerScene.cs` and `HeldClass.cs`. The attempt-1 suites give the same results as before against the new code (only the native-call cases cannot run; `KillerColourRolesQaTests` 4 of 4 pass now, `PanelScaleQaTests` 19 of 19, `DevArgsU4cQaTests` 53 of 53, `CameraMovementQaTests` 8 of 8).

## Status of the attempt-1 findings
| # | Was | Status | Evidence |
|---|---|---|---|
| F-01 | major, overlapping tags garble text | **FIXED** | `Killer/TagLayout.cs` plus `MarkerLayer.Layout` (`MarkerLayer.cs:92-135`). No colliding text in any of the 14 shots. `TagLayoutQaTests` |
| F-02 | major, chevron tag covers a diamond | **FIXED** | `TagLayout.Free` refuses a position on a visible glyph of the same or higher priority (`TagLayout.cs:108-116`). `killer-offscreen-*` and `killer-inview-720` show diamonds clear. `AChevronsDistanceTagNeverCoversADiamond` |
| F-03 | major, `ally` teal on the Killer client | **FIXED** | `WorldView.cs:99,105` choose `Survivor` only when `role == Role.Survivor`, else `Neutral` = `text` (232,230,225). `KillerColourRolesQaTests` 4 of 4 pass; `LightingQaTests` Survivor-side test passes. Terminal squares and ring are white in every Killer shot; teal appears only on the Survivor client |
| F-04 | minor, terminal mark on a Survivor | **FIXED** | Survivor bodies are registered as protected rectangles (`KillerWorldLayer.cs` `markers.Body`); `ASurvivorBodyRectangleIsProtectedLikeAGlyph`. In `killer-inview-1080` Terminal 4 is hidden rather than drawn over the Survivor standing at it |
| F-05 | minor, dev reads of Survivor fields | **FIXED** | `DevCapture.cs` returns at its Survivor-only guard before its only `state.Players` loop (lines 190-198); `ClientApp.Preview` reads world entries only for a Survivor and names lobby slots for the Killer (`ClientApp.cs:225-232`). `DevPathAuditQaTests` |
| F-06 | minor, blip draws nothing while held class lags | **FIXED** | `KillerScene.OutOfFrustum` (`KillerScene.cs:21,105`) and `SelectEdges` (`KillerScene.cs:149-164`); `KillerWorldLayer.cs:223-232` draw the chevron, the held class keeps its 100 ms. `KillerFrustumExitQaTests` |
| I-2 | info, near-identical shots | **FIXED** | `killer-occluded-*`: a diamond near the centre with no chevron. `killer-offscreen-*`: two chevrons and no body, no diamond |

## New tests (`Client/Assets/Tests/EditMode/QA/`, category `U4cQaA2`)
### `TagLayoutQaTests.cs` (32 cases, all pass in the scratch runner)
- `TwoCoincidentTagsEndUpDisjointAndExactlyOneOfThemMoves`
- `ADisplacedTagMovesByExactlyTheTagHeightPlusTwoUnits`
- `TagsThatDoNotCollideNeverMove`
- `TagsThatOnlyTouchDoNotCountAsOverlapping`
- `ASurvivorTagKeepsItsPlaceAgainstEveryOtherPriorityWhateverTheOrderTheyWereAdded`
- `AllFourPrioritiesOnOnePointStackInPriorityOrderAndAreDisjoint`
- `TheStackAlternatesBelowAndAbove`
- `AfterThreeStepsTheLowerPriorityTagIsHiddenAndTheHigherOnesAllStay`
- `ManySameLevelTagsHideOnlyTheOnesThatDoNotFit`
- `ATagThatWouldLeaveTheScreenIsMovedOrHiddenNeverClipped`
- `ATagNeverEntersABlockedRectangle`
- `ASurvivorGlyphIsNeverHiddenNoMatterWhatStandsOnIt`
- `AnExitOrTerminalGlyphOnASurvivorGlyphIsHiddenTogetherWithItsTag`
- `ATerminalGlyphThatCoversNothingStaysAndKeepsItsTag`
- `ASurvivorBodyRectangleIsProtectedLikeAGlyph`
- `AChevronsDistanceTagNeverCoversADiamond`
- `ALowerPriorityTagIsNeverLeftOnAHigherPriorityGlyph`
- `ATerminalGlyphUnderAnEdgeGlyphIsHiddenToo`
- `PermutingTheInputGivesTheSameLayoutAndEveryLayoutIsValid` (8 seeds; 25 permutations each)
- `SolvingTheSameInputTwiceOrAfterAClearGivesTheSameResult`
- `IdenticalItemsGetIdenticalTreatmentRegardlessOfWhichIsWhich`
- `SevenSurvivorsAndFiveTerminalsStackedOnOnePointFinishWithoutOverlapOrException`
- `SevenSurvivorsAndFiveTerminalsOnTheSameSpotWithSeparateGlyphsStillAreValid`
- `ARandomCrowdOfThreeHundredItemsIsAlwaysValid`
- `AnEmptyLayoutAndItemsWithNothingToShowSolveWithoutError`

The shared `AssertValid` checks, on every layout: visible tags are pairwise disjoint and inside the screen, a tag never covers a visible glyph of strictly higher priority, a Survivor or Edge glyph is never hidden, and a visible Exit or Terminal glyph never covers a visible higher-priority glyph.

### `KillerFrustumExitQaTests.cs` (5 cases, need the Unity gate)
- `AHeldInViewBlipThatLeavesTheFrustumGetsAChevronOnThatSameFrameAndKeepsItsClassForTheHold`
- `AHeldOccludedBlipThatLeavesTheFrustumGetsAChevronOnThatSameFrame`
- `ABlipThatComesBackIntoViewLosesItsChevronAtOnceWhileItsClassIsStillHeld`
- `EveryInstanceIsDrawnAsABodyADiamondOrAChevronOnEveryFrameOfAFullTurn`
- `AtMostSixChevronsAreDrawnEvenWhenMoreInstancesLeaveTheFrustum`

### `DevPathAuditQaTests.cs` (13 cases, all pass in the scratch runner)
- `TheDevStatusLogReturnsBeforeItsFirstEntryLoopOnAnyClientThatIsNotASurvivor`
- `NoOtherDevFileReadsAnEntryOfTheWorldStatePlayerList`
- `TheDevCaptureNeverMentionsAnEntryFieldOutsideTheGuardedLoop`
- `TheKillerLogsBlipCountsFromTheTrackerNotFromEntries`
- `ThePreviewReadsSurvivorEntryIdsOnlyOnASurvivorClientAndNamesLobbySlotsForTheKiller`
- `TheKillerBranchOfTheWorldHandlerStillStopsAtItsOwnEntryBeforeAnySurvivorOnlyField`
- `KillerLookOccludedIsAcceptedInAnyCase` (4 cases)
- `AnEarlierNumberStaysAsTheFallbackYawForOccludedAndALaterNumberReplacesIt`
- `OccludedDoesNotChangeTheOtherLookModesOrTheRejectionOfUnknownWords`
- `OccludedWithoutAValueIsAnErrorLikeTheOtherModes`

## Anonymity regression after the F-05 change
The Killer path still reads no Survivor `Id`, `Yaw`, `StatusTimer`, `CaptureCount` or behaviour flag. `KillerAnonymityQaTests` source tests (5) pass against the new code; the runtime ones need Unity.

| File:line | Access | Verdict |
|---|---|---|
| `Killer/KillerScene.cs:69-75` | `Role`, `Status`, `X`, `Z` only | clean |
| `World/WorldView.cs:69-70` | reads `Role`, then breaks for the Killer before `player.Id` (line 71) | clean |
| `ClientApp.cs:179` | own entry by Role on the Killer; `player.Id` is only evaluated for a Survivor (ternary) | clean |
| `ClientApp.cs:181` | break before `player.Flags` (182), `player.StatusTimer` (183), Hacking (185) | clean |
| `ClientApp.cs:225-232` (`Preview`) | `player.Id` only inside `role == Role.Survivor && lastWorld != null`; the Killer uses `lobby.Slots` | clean (was F-05) |
| `Dev/DevCapture.cs:190-198` | `player.Id`, `.Status`, `.StatusTimer`, after the `role != Role.Survivor` return at line 190 | clean on the Killer (was F-05) |
| `Dev/DevCapture.cs:122-132` (`OnKillerFrame`) | blip counts from the layer, no entries | clean |
| `World/SnapshotBuffer.cs:43,52,275` | `Yaw` of the Killer's own entry; `Role` for the key | unchanged, clean |
| `Killer/KillerWorldLayer.cs:88,179` | `effect.CasterId`, `terminal.Id` | not Survivor entries |

## Screenshots
- **Text collisions:** none in any of the 14 shots. The `21 21 m m` garble of attempt 1 is gone (`killer-menu-1080` now shows two separate `21 m` tags).
- **Tags still belong to their marker:** mostly yes. A stacked tag moves by one or two tag heights. Three cases stand out (F-07): the second `21 m` in `killer-menu-1080` (about 47 u below the feet), `Terminal 1` in `killer-occluded-1080` (below the `19 m` tag of the diamond, about 70 u from its square) and `Terminal 6` in `killer-intro-720`. All remain readable and are the cost of the no-overlap rule.
- **Colour roles:** `signal` amber only on Survivor markers (outlines, diamonds, chevrons, the Lagging text). Terminal squares, tags and the ring are `text` or `muted`; no `ally` teal anywhere on the Killer client. The Survivor client keeps its teal characters (`survivor-3p-*`, `survivor-behind-*`).
- **Occluded and off-screen are distinct:** `killer-occluded-*` shows one amber diamond in the middle of a wall with its distance tag and no chevron. `killer-offscreen-*` shows two chevrons at the left and bottom edges (22 m, 21 m) clear of the cluster, and no body.
- **Geometry and type:** cluster blocks, type sizes and the 12 px floor at 720p look as in attempt 1; the play-menu shot shows `Resume` focused and `Leave match` outlined.
- **Survivor view:** third-person framing (feet at about 0.33, head at about 0.80 of the height) and the look-behind shot are unchanged and correct.

## Findings (new)
### F-07 (minor) A higher-priority tag can partly cover a lower-priority square, and a stacked tag can drift
- **Reproduction:** `killer-intro-720.png` (right edge): the Edge chevron's `31 m` tag sits on the lower part of the Terminal 6 square, and `Terminal 6` moves about 40 u below its square. Also `killer-menu-1080.png` (second `21 m`) and `killer-occluded-1080.png` (`Terminal 1` below `19 m`).
- **Expected:** a lower-priority glyph may be hidden but not half covered; a moved tag stays recognisably tied to its marker.
- **Actual:** `TagLayout` only forbids a tag from covering a glyph of the same or higher priority. A Terminal glyph is hidden only when it covers a higher glyph, not when a higher tag covers it.
- **Evidence:** `TagLayout.cs:77-82` (glyph rule), `:113-114` (tag rule).
- **Fix (optional):** hide a Terminal or Exit glyph together with its tag when a higher-priority tag lands on it, or shorten the maximum drift for terminal tags.

### Info
- **I-4:** an Edge glyph and a Survivor glyph may overlap at the safe-rectangle border (both are never hidden by design). The brief's "a lower-priority glyph must never cover a higher-priority glyph" is read as applying to Exit and Terminal; `AssertValid` follows that reading.
- **I-5:** with more than six off-frustum instances the seventh is not drawn (the 6 edge indicators cap of HUD_SPEC 6.2). With seven Survivors this is reachable; it is spec behaviour, tested by `AtMostSixChevronsAreDrawn...`.

## SLOP section 9 re-score
Killer HUD (cluster, markers, intro, menu), 1280x720 and 1920x1080:

| Check | Max | Score | Note |
|---|---:|---:|---|
| No V1-V12 tells | 20 | 18 | flat, dark, no gradients; no colour-role breach any more |
| No L1-L8 tells, one spacing scale | 15 | 12 | no collisions; F-07 drift and one half-covered square |
| Copy and fixed vocabulary | 15 | 15 | spec text, no garbling |
| Every element answers state and action | 15 | 14 | |
| Role-specific HUD, no forbidden leak | 20 | 19 | no leak; `ally` gone from the Killer client |
| Interactive states, keyboard navigable | 10 | 9 | menu focus visible; hover and pressed checked by tests only |
| Contrast and 720p readability | 5 | 5 | |
| **Total** | **100** | **92** | passes (threshold 85, no gate failure) |

Survivor view (third person, look behind): **94 / 100** (unchanged: V 19, L 14, copy 15, state 15, role 20, interactive 9, contrast 5, no gate failure).

## What the orchestrator still needs to run
The Unity EditMode gate over the three new files above: 50 new cases (32 + 5 + 13). Expected result: all pass; `KillerFrustumExitQaTests` is the only file I could not execute.

QA-PASS
