# U4c QA, attempt 1

QA: Claude Sonnet (implementers: Claude Opus subagents for pieces A and B, a different model). Date 2026-10-27. Branch `claude/u4c-cameras`, nothing committed. I did not run Unity, the game or a server. I read all 14 screenshots in `docs/qa/U4c-screens/` with the Read tool.

## Verdict
- **QA-FAIL.** 0 blockers, 3 majors, 3 minors, 3 info items.
- SLOP section 9: Killer HUD **84 / 100** (threshold 85, no gate failure), Survivor view **94 / 100**.
- The Killer HUD misses the threshold by one point because of F-01, F-02 and F-03. All three have small fixes.
- The pure logic is sound: the anonymity key risk PASSES in every test that could run outside Unity, and the code audit found no product-path read of a Survivor `Id`, `Yaw`, `StatusTimer`, `CaptureCount` or behaviour flag on the Killer path.

## Findings
| # | Sev | Finding |
|---|---|---|
| F-01 | major | Overlapping marker tags garble the text. |
| F-02 | major | A chevron's distance tag covers a through-wall diamond. |
| F-03 | major | `ally` teal appears on the Killer client (completed terminal, open exit ring). |
| F-04 | minor | Terminal marker and tag draw on top of Survivors standing at the terminal. |
| F-05 | minor | Dev-only code reads Survivor `Id`, `Status`, `StatusTimer` on the Killer client. |
| F-06 | minor | For up to 100 ms an Occluded-held blip that has left the frustum draws nothing. |
| I-1 | info | The boom sphere cast also hits the floor. |
| I-2 | info | The `killer-occluded` and `killer-offscreen` captures show nearly the same scene. |
| I-3 | info | Lagging amber tint on the Survivor client is pre-existing U4 (not in scope). |

### F-01 (major) Overlapping tags garble text
- **Reproduction:** `-ptk-offline -ptk-role killer -ptk-bots 6 -ptk-start -ptk-menu 3 -ptk-shots <dir>` at 1920x1080 and 1280x720, then open `killer-menu-1080.png`, `killer-menu-720.png`, `killer-inview-720.png`.
- **Expected:** every text label is readable (SLOP section 9 layout and copy checks; HUD_SPEC 0.3 type sizes are only worth anything if the text is legible). Two tags that land in the same place must not draw on top of each other.
- **Actual:**
  - `killer-menu-1080.png` shows `21 21 m m`: two distance tags of different instances on the same pixels.
  - `killer-inview-720.png` and the 720 shots show `21 r` and `erminal 1`: a `Terminal N` tag cut by another tag.
- **Evidence:** `MarkerLayer.cs` and `KillerWorldLayer.cs` place each tag independently at its anchor; no pass resolves collisions.
- **Deviation judgement:** the implementer listed "overlapping tags are not de-cluttered (no spec rule)". Not acceptable. The spec has no rule because it assumes legible labels. A minimal rule is enough (offset the later tag by one tag height, or merge equal distances).

### F-02 (major) A chevron's distance tag covers a through-wall diamond
- **Reproduction:** `killer-offscreen-1080.png` and `killer-inview-720.png`.
- **Expected:** the 16 u `signal` diamond of an occluded instance (HUD_SPEC 6.2) stays fully visible; edge chevrons never hide another marker.
- **Actual:** in `killer-offscreen-1080.png` the diamond is hidden behind a chevron tag; in `killer-inview-720.png` half of it is hidden.
- **Evidence:** the draw order puts edge tags above the diamonds; step 6 of the placement only keeps chevrons out of the cluster rectangle, not away from other markers.

### F-03 (major) `ally` teal is shown on the Killer client
- **Reproduction:** read `WorldView.cs:98` (`item.Panel.color = terminal.Progress == 100 ? Survivor : PanelIdle`) and `WorldView.cs:104` (`exit ... sharedMaterial.color = open ? Survivor : ...`). Both run on every client. Run the two tests below.
- **Expected:** HUD_SPEC 0.4: `ally` is "shown to Survivors only". Q11: the Killer sees a `text` ring for the Exit, Survivors an `ally` ring. Orchestrator ruling 2: on the Killer client a completed terminal must not be `ally` and never `signal`.
- **Actual:** the Killer client paints a completed terminal and the open Exit ring in `Survivor` (63,184,175), the team teal.
- **Evidence:** `KillerColourRolesQaTests.AKillerClientNeverPaintsACompletedTerminalWithTheAllyTeal` and `...AKillerClientNeverPaintsTheOpenExitRingWithTheAllyTeal` both FAIL by design (checked in a scratch runner: the statement text has no `role` in it).
- **Deviation judgement:** the implementer's "a completed terminal stays teal in the killer view (QA lighting test binds the colour)" is not acceptable. The old test is re-scoped by ruling 2 (see `LightingQaTests` below).
- **Fix:** pass the role into `Accept`'s colour choice: Survivor keeps `Survivor`, Killer uses `text` or `muted`.

### F-04 (minor) Terminal marker draws on top of Survivors
- **Reproduction:** `killer-inview-1080.png`, `killer-inview-720.png`.
- **Expected:** the Survivor outline and marker are not hidden by terminal furniture.
- **Actual:** `Terminal 4` and its square sit over a Survivor standing at that terminal.
- **Evidence:** draw order in `KillerWorldLayer.cs` draws terminals after outlines; the in-view-within-8-m rule shows only the tag but the tag is still opaque.

### F-05 (minor) Dev-only reads of Survivor fields on the Killer client
- `DevCapture.cs:175-183` reads `player.Id`, `player.Status`, `player.StatusTimer` of every entry for its `[DEV]` status log, on the Killer client too.
- `ClientApp.cs:227-228` (`Preview`) reads `player.Id` and `player.Role`.
- Both are behind `DevArgs` (inert for a normal player) and feed no rendering, so this is not a leak. Keep them out of the Killer path or label them dev-only.

### F-06 (minor) 100 ms hole in the held class
- `HeldClass` keeps a blip as Occluded for up to 100 ms after it has actually left the frustum; `KillerWorldLayer.Project` (`KillerWorldLayer.cs:157`) then fails and the diamond is not drawn for those frames.
- Impact is at most six frames; fix by drawing the diamond clamped or by forcing the class switch when `Project` fails.

### Info
- **I-1:** the Survivor boom sphere cast also hits the floor. At pitch -10 the boom is about 2.88 m (the end would otherwise be underground). Acceptable and sensible.
- **I-2:** `killer-occluded-*` and `killer-offscreen-*` are nearly the same scene; both show the through-wall diamond and the chevron (together, in the offscreen shot), so the brief's check passes, but the shots do not separate the two cases.
- **I-3:** Lagging amber tint on the Survivor client belongs to U4 (not changed by U4c).

## Spec bullets of `U4c-a1.md`
| Bullet | Result | Evidence |
|---|---|---|
| 1 Survivor camera: boom 4.0, look 0.7 m, pitch -10..60 default 20, vFOV 50, Hor+ | PASS | `SurvivorOrbitRig.cs`; `CameraRigQaTests` (pitch clamp, default, framing 0.334 / 0.805) |
| 1 Movement camera-relative, 720 deg/s, `Yaw` = facing, still keeps facing, orbit sends nothing | PASS | `MovementInput.cs`; `CameraMovementQaTests` (8 tests, all ran and pass) |
| 1 Hacking faces the terminal (visual only) | DEVIATION, acceptable | not sent in `MoveInput`; `CameraRigQaTests.WhileHackingAStandingCharacterFacesTheTerminalButAMoveStillWins` |
| 1 Wall pull-in: 0.2 m sphere vs AABB, shortens at once, 150 ms ease-out | PASS | `CameraRigQaTests` (sphere cast, ease, reappearing wall); the wall x 18..20 is the real `FixedMap` |
| 1 Dither below 1.0 m | PASS | 8x8 Bayer 40 % discard shader in `Resources/Shaders`; test `TheOwnCharacterIsDitheredExactlyWhileTheBoomIsShorterThanOneMetre` |
| 1 No reticle, no own tint, others without outline, no Killer drawn | PASS | `survivor-3p-*.png`; `WorldView.cs:70,77` |
| 2 Killer eye 1.7, pitch +-70, hFOV 90 at 16:9 (vFOV 58.7), `Yaw` = view yaw, no body or crosshair | PASS | `KillerViewRig.cs`; `CameraRigQaTests`; `CameraMovementQaTests.AKillerPacketCarriesTheViewYawAsItsYaw...` |
| 3 Killer stream never reads `Id`, `Yaw`, `StatusTimer`, `CaptureCount`, flags; own entry by Role | PASS | audit table below; `KillerAnonymityQaTests` source tests (all pass) |
| 3 Anonymity: shuffled, garbage in unread fields, identical output | PASS (by code; runtime tests unrun) | the 12 runtime anonymity tests need `Quaternion` (native), so they run only in the Unity gate; the 5 source and `BlipTracker` tests ran and pass |
| 3 Classification with 100 ms hysteresis | PASS (by code) | `HeldClass.cs`; `ClassificationHysteresisQaTests` (3 ran and pass; 6 need the Unity gate) |
| 3 Rendering per class (in view outline, occluded diamond, off-screen chevron) | PASS, with F-01 and F-02 | screenshots |
| 3 Edge placement steps 1-6 as a pure function | PASS (by code) | `EdgePlacement.cs`; `EdgePlacementQaTests` (20; 19 need the Unity gate) |
| 3 Terminal markers (no percentage, 100 = done, in view within 8 m tag only) | PASS | `KillerHudQaTests.ProgressOneHundred...`, `NoStringInTheKillerViewCanFormatAPercentage` (ran, pass) |
| 3 Exit marker (Locked dashed muted, Open solid text, Locked by Killer + bar + `Exit Locked N s`) | PASS | `KillerHudQaTests.TheExitMarkerHasThreeStates...` (ran, pass). Open ring colour: FAIL, see F-03 |
| 3 Draw order per 6.2 | DEVIATION, see F-02 and F-04 | |
| 4 Info block `m:ss`, 60 s bar, `Terminals k of N`, hint at N-1 | PASS | `KillerHudQaTests` clock, count, hint (ran, pass) |
| 4 Effect line (Overclock / Recovering / Slowed + danger bar) | PASS | `TheEffectLinesUseTheSpecWords...` (ran, pass) |
| 4 Skill block; no cooldown ring while SelfState is unavailable | PASS | `KillerHud.cs`; ring and state-line tests (need the Unity gate for the widget form); copy test ran |
| 4 Execution guidance (pure class, hidden without SelfState) | PASS | `ExecutionGuidance.cs`; 6 tests ran and pass |
| 4 Intro 3 s, never blocks input | PASS | `killer-intro-*.png`; `KillerHudCopy` intro copy test |
| 4 Play menu (Esc, one zero input, cursor released, match runs) | PASS | `CameraMovementQaTests.TheKillerPlayMenuAndLostFocusStopTheCharacterWithAZeroInput`; `killer-menu-*.png` (but see F-01) |
| 4 Geometry and type (S floor 12 px at 720p), Inter noted | PASS | `PanelScaleQaTests.SmallTextNeverRendersBelowTwelve...`; cluster geometry matches 6.1 in the shots |
| 4 Panel scale clamped [0.8, 1.0] | PASS | `ClientScreens.MinScale .8`, `MaxScale 1`; `PanelScaleQaTests` (19, all pass) |
| 5 UI cues (7 cues plus match_start) and positional sound | PASS | `KillerCues.cs:69-76`; `KillerAudio.cs:13-15,73-74,89-98,136` (spatialBlend 1, dopplerLevel 0, low-pass 1500, 6 dB / 300 ms duck, 50 ms fade, 4 voices) |
| 5 Survivor client never plays Killer world sound | PASS | `KillerWorldLayer` is added only for the Killer role (`ClientApp.cs:164`) |
| 6 SelfState seam, `Available = false`, one marked adapter point | PASS | `SelfState.cs`; `ThereIsExactlyOneMarkedAdapterPoint...` (ran, pass) |
| 7 DevArgs `-ptk-orbit`, `-ptk-killer-look`, `-ptk-menu`, inert when absent | PASS | `DevArgsU4cQaTests` (53, all ran and pass) |
| 7 Event shots `killer-inview`, `-occluded`, `-offscreen`, `survivor-3p`, `-behind` | PASS | `DevCapture.cs:118-120,223-224`; all 14 PNGs present |

## Spec rules of `U4c-a1-tracker.md`
| Rule | Result | Evidence |
|---|---|---|
| 1 Shuffle, greedy nearest match, random tie break, gate `0.4 x max(1, gap)`, gap > 10 = no match | PASS | `BlipTracker.cs:139-145,155-185` |
| 2 Matched pairs keep the Handle, update Prev, history of 4 | PASS | `BlipTracker.cs:257-276` |
| 3 Unmatched entries spawn with Speed 0 and face the nearest terminal | PASS | `BlipTracker.cs:290-329` |
| 4 Unmatched old blips despawn at once | PASS | `BlipTracker.cs:188-198` |
| 5 Facing: last displacement >= 0.03 m, else nearest terminal | PASS | `BlipTracker.cs:278-287` |
| 6 Events: Normal to Lagging, CaptureConfirm 0.5 s limit, ExecuteConfirm | PASS | `BlipTracker.cs:193-198,213-244`; the confirms are suppressed on the first snapshot, a tick gap over 10 and a tick regression (impl judgement call, acceptable: nothing is observable as changed) |
| 7 Handles increasing, never from `Id` or order | PASS | `BlipTracker.cs:294` (`nextHandle++`; `Reset` keeps counting) |
| Speed = mean of last <= 4 displacements / 0.05 s | DEVIATION, acceptable | divides by the tick gap, so packet loss does not inflate speed |
| Sound model: thresholds with hysteresis, intervals, clamps, ranges, attenuation, 4 voices with 1 m hysteresis, -4 dB occluded, decoy parity | PASS | `BlipSoundModel.cs`; the impl suite of 82 tests passes in the implementer's scratch runner; checked by code reading |

## Impl-report deviations
| Deviation | Verdict |
|---|---|
| Inter instead of Pretendard | acceptable (the brief says note it) |
| Dither: 8x8 Bayer 40 % discard, lit by main light and SH only | acceptable |
| Boom sphere cast also hits the floor | acceptable (I-1) |
| Extra dev flag `-ptk-menu <s>` | acceptable (documented and tested) |
| Capsule plus visor character stand-in | acceptable |
| Overlapping tags not de-cluttered | **finding F-01** |
| Breath variant swap is fade-out / fade-in on one source | acceptable |
| Hacking-facing is visual only | acceptable |
| Completed terminal stays teal in the Killer view | **finding F-03** |
| "758 total, 5 failed (all PanelScaleQaTests)" | superseded by ruling 1 (PanelScaleQaTests replaced, see below) |

## Code audit of every Killer-path file
Scope: `Killer/**`, `UI/KillerHud.cs`, `UI/MarkerLayer.cs`, `UI/SkillRing.cs`, the Killer branches of `WorldView`, `SnapshotBuffer`, `ClientApp`. Looked for Survivor-entry `.Id`, `.Yaw`, `.StatusTimer`, `.CaptureCount`, `.Flags` (Hacking, Rescuing, Stealthed, Afterimage).

| File:line | Field access | Verdict |
|---|---|---|
| `Killer/*.cs` (all), `UI/KillerHud.cs`, `UI/MarkerLayer.cs`, `UI/SkillRing.cs` | none of the five fields | clean |
| `Killer/KillerScene.cs:68-75` | `Role`, `Status`, `X`, `Z` of each entry only | clean |
| `Killer/KillerWorldLayer.cs:88` | `effect.CasterId` | not a Survivor entry (a skill effect) |
| `Killer/KillerWorldLayer.cs:179` | `terminal.Id` | a terminal, not a Survivor |
| `World/WorldView.cs:69-70` | reads `Role`, then breaks for the Killer before `player.Id` at line 71 | clean for the Killer |
| `World/SnapshotBuffer.cs:43,52` | `a.Yaw` | the Killer's own entry only |
| `World/SnapshotBuffer.cs:275` | `Role` used for the Killer key | clean |
| `ClientApp.cs:179` | the Killer's own entry found by Role | clean |
| `ClientApp.cs:181` | break before `player.Flags`, `StatusTimer` and `Hacking` (182-185) | clean for the Killer |
| `Dev/DevCapture.cs:175-179` | `player.Id`, `.Status`, `.StatusTimer` over all entries | dev-only, F-05 |
| `ClientApp.cs:227-228` (`Preview`) | `player.Id`, `player.Role` | dev-only, F-05 |

## SLOP section 9 scores
Killer HUD (cluster, markers, intro, menu), 1280x720 and 1920x1080:

| Check | Max | Score | Note |
|---|---:|---:|---|
| No V1-V12 tells | 20 | 17 | `ally` teal on the Killer client is a colour-role breach (F-03); the rest is flat, dark, no gradients |
| No L1-L8 tells, one spacing scale | 15 | 11 | tag collisions (F-01, F-02, F-04) |
| Copy and fixed vocabulary | 15 | 14 | copy is the spec text; garbled labels cost one point |
| Every element answers state and action | 15 | 14 | |
| Role-specific HUD, no forbidden leak | 20 | 16 | no information leak found; `ally` on the Killer client costs four |
| Interactive states, keyboard navigable | 10 | 8 | play-menu states checked by tests only, not in a screenshot |
| Contrast and 720p readability | 5 | 4 | overlapping text (F-01) |
| **Total** | **100** | **84** | below 85: **fail** (no gate failure) |

Survivor view (third person, look behind): 94 / 100 (V 19, L 14, copy 15, state 15, role 20, interactive 9, contrast 5, no gate failure). Framing is correct (feet at 0.334, head at 0.805 of the viewport); the old U4 Survivor HUD is out of scope.

## New and replaced tests (`Client/Assets/Tests/EditMode/QA/`)
Namespace `PingTheKiller.Client.QA.Tests`, category `U4cQaA1` except `PanelScaleQaTests`.

| File | Cases | Covers |
|---|---:|---|
| `KillerAnonymityQaTests.cs` | 17 | garbage fields and shuffled order (seeds 1-6), different Ids same result, swapped Ids, borrowed Id jump, own entry by Role, source audit of the Killer path |
| `EdgePlacementQaTests.cs` | 20 | 6.2 steps 1-6, behind camera, safe rectangle, keep-out, nearest-6, voiced first |
| `ClassificationHysteresisQaTests.cs` | 9 | 100 ms hold, flicker, long gap, wall-ray flag, independent keys |
| `CameraRigQaTests.cs` | 18 | rig numbers, sphere cast, ease-out, dither, 720 deg/s facing, Killer eye, pitch, FOV |
| `CameraMovementQaTests.cs` | 8 | `MovementInput` Yaw, orbit sends nothing, release packets, `ClientApp` wiring, play menu |
| `KillerHudQaTests.cs` | 34 | copy, widgets, ExecutionGuidance, SelfState, marker and exit rules, colours |
| `KillerColourRolesQaTests.cs` | 4 | ruling 2, Killer side (2 expected failures, see F-03) |
| `DevArgsU4cQaTests.cs` | 53 | `-ptk-orbit`, `-ptk-killer-look`, `-ptk-menu` valid, invalid, missing, inert |
| `PanelScaleQaTests.cs` (replaced) | 19 | ruling 1: clamp [0.8, 1.0], `TheScaleIsTheSmallerAxisRatioClampedBetweenPointEightAndOne`, `TheBoundsAreTheSpecValuesZeroPointEightAndOne` |
| `LightingQaTests.cs` (edited) | 1 test renamed | ruling 2, Survivor side: `TerminalColoursAreMutedAndNeverAmberAndDoneIsTheTeamTealOnTheSurvivorClientNotSignalAmber` |

Total 182 new or replaced cases.

### How they were checked
- All QA tests and the whole `Client/Assets/Scripts` tree compile with 0 errors and 0 warnings in a scratch net10 project (C# 9, the real `UnityEngine.*Module.dll`, `Shared`, `ServerCore`, `Unity.InputSystem`, URP and Unity's `nunit.framework`).
- A scratch runner executed them. 410 cases pass. The only logic failures are the 2 expected ones (F-03).
- 57 cases cannot run outside Unity (`Quaternion.Euler`, `GameObject`, `VisualElement` are native ECalls): 12 anonymity runtime, 19 edge placement, 6 hysteresis, 8 camera rig, 9 KillerHud widget, plus 3 older tests. They compile, and every expected value was verified by hand against the source. **They need the Unity EditMode gate.**
- The scratch runner crashed in a finalizer after `LightingQaTests`, so older suites after that point were not re-run; `PanelScaleQaTests` was run alone (19 pass).

## Expected failures in the Unity gate
- `KillerColourRolesQaTests.AKillerClientNeverPaintsACompletedTerminalWithTheAllyTeal` (F-03)
- `KillerColourRolesQaTests.AKillerClientNeverPaintsTheOpenExitRingWithTheAllyTeal` (F-03)

Both pass once `WorldView.Accept` chooses these two colours by role.

QA-FAIL
