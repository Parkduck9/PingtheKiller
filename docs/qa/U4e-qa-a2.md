# U4e QA report, attempt 2 (re-check of the fix round)

QA: Claude Sonnet. Input: `docs/briefs/U4e-a2-fixes.md`, "Attempt 2" in `docs/qa/U4e-impl-a1.md`, the Q-01 ruling (HUD_SPEC 4.2: meter strip 8 u tall at 45 % density, `(10/30)`). Orchestrator gate: 1143 tests, 1142 pass; the only failure was the superseded banner test. Not run: Unity, the game, the server. Nothing committed; product code and HUD_SPEC untouched.

## 1. Superseded test, updated

`SurvivorFeedbackQaTests.CaptureBanner_CopyIsPlainFixedVocabularyWithTheRuleNumbers` now asserts, per HUD_SPEC 4.1 (10/30, attempt 2): the banner starts with the meter's own state word (read from `SurvivorReadout.Meter` for a Lagging own entry, `Lagging`) followed by `.`, no longer contains `Captured`, and still checks `Survivor`, `1.5 m`, ` 3 s`, `60 s left`, no `!`, no draft wording, at most 70 characters, `GameConstants` values 1.5 / 3 / 60, the danger bar, and that HUD_SPEC carries the same string. Passes headless against the current code.

## 2. New tests (`Client/Assets/Tests/EditMode/QA/SurvivorFeedbackQaTests.cs`)

| Test | Checks | Needs Unity gate |
|---|---|---|
| `A2_Q01_StripSpec_EightUnitsTallAtFortyFivePercent_BandUnchanged_FractionsOneAndTwoThirds` | strip 8 u, density 0.45 (mask measured over 24 000 texels, within 0.02), band still 24 u at 30 %, fractions 1/3 and 2/3, level 3 is the band, HUD_SPEC carries `8 u (10/30)` and `45 % density (10/30)` | no (ran headless: pass) |
| `A2_Q01_TheHudDrawsTheStrip_EightUnitsTall_OneThirdAndTwoThirdsOfTheMeter` | the real `SurvivorHud` strip element: height 8, width 1/3 and 2/3 of the 304 u meter, texture 8 u of rows, about 45 % `danger` texels | yes |
| `A2_Q02_NoSkillNameBreaksInsideAWord_AtEitherCanvas` | every Survivor and Killer skill name from the tables: independent width estimate (0.62 x size per character, wider than HUD_SPEC's 0.55), longest word fits the 100 u name column at S 14 u (1080p) and 15 u (720p floor), at most two lines; Killer name one line in 232 u at M; HUD and `HudCopy` tables agree; USS `.sname` width equals the constant and wraps at spaces | no (ran headless: pass) |
| `A2_Q03_TheVisorFadesWithTheBody_ReadFromTheMaterials` | builds a `WorldView`, reads the `Killer body` and `Killer visor` child materials directly: both alpha 1 while present, identical alpha at +0.05 / 0.15 / 0.25 / 0.29 s, root inactive after 0.3 s | yes |
| `A2_Q04b_NoKillerModelIsDrawnWhileSpectating_EvenWithAnEntryInEverySnapshot` (Eliminated, Escaped) | an entry in every snapshot, rendered across 9 snapshots: model root never active, alpha 0 | yes |
| `A2_Q04b_ControlTheModelIsStillDrawnForALivingSurvivor` (Normal, Lagging) | control: model drawn at alpha 1 | yes |

Verification: everything compiles against the Unity-built DLLs (0 errors). With the scratch reflection runner outside Unity, 48 pass; the 6 that fail there are exactly the ones that need Unity natives (HUD texture, `WorldView` primitives, `LogAssert`) and are marked above. They are written against the same API the implementer's own tests use but have not been run, so the Unity gate is their first run.

## 3. Findings re-checked

| # | Result | Evidence |
|---|---|---|
| Q-01 level-1 strip | FIXED (ruling applied), residual note below | `ThreatVisual.StripHeight = 8`, `StripDensity = .45`, 2 u texels (SurvivorFeedback.cs:17); HUD strip height set in SurvivorHud.cs:51 and the texture painted with the strip density (:150); HUD_SPEC 4.2 lines 274, 280, 282 carry `(10/30)`; test `A2_Q01_*`; threat-1-*.png, threat-2-*.png |
| Q-02 mid-word wrap | FIXED | USS `.sskill` 176 u, name column 100 u (ClientUI.uss:120-122); `emergency-restore-720.png`, `threat-1-720.png`, `threat-3-720.png`, `capture-banner-720.png`, `eliminated-reason-720.png` all show `Emergency` / `restore`; test `A2_Q02_*` |
| Q-03 visor fade | FIXED in code and tests; not re-shot | WorldView.cs:94 visor made with the transparent flag, RenderKiller sets visor alpha with the body (:118-121); test `A2_Q03_*`. The `killer-fade-injected` shots were not retaken, so the visor fade is not confirmed visually. |
| Q-04 banner wording | FIXED | `CaptureWatch.Banner` = `Lagging. A Survivor within 1.5 m for 3 s rescues you · 60 s left` (SurvivorFeedback.cs:66); capture-banner-*.png shows banner and meter both say `Lagging`; HUD_SPEC 4.1 updated. Not part of the fix round and still open as minors: `Disconnected:` wording, and the fixed `60 s left` in a 3 s banner. |
| Q-04b spectator guard | FIXED | `WorldView.KillerDrawable` (WorldView.cs:33) used in `RenderKiller` (:110-111) with the own avatar's status; tests `A2_Q04b_*` |

## 4. Screens re-scored (SLOP §9)

Opened with Read: threat-1-1080/720, threat-2-1080/720, threat-3-1080/720, capture-banner-1080/720, eliminated-reason-1080/720, emergency-restore-720.

- threat-1: a 100 x 8 u red noise strip on the right of the meter top edge; it touches no glyph. At 1080 and 720 it is now clearly red noise and no longer a faint dotted line.
- threat-2: the same strip at 2/3 width (about 200 u), reads as "more".
- threat-3: the 24 u band, unchanged and clearly visible.
- capture-banner: `Lagging. A Survivor within 1.5 m for 3 s rescues you · 60 s left`, one line, danger bar; meter shows `Lagging`: one term for one state.
- eliminated-reason: 1080 shows `Disconnected: no rescue within 60 s.` on one line. 720 shows `Executed: the Killer touched you while / Lagging.` on two lines inside the 38 u block, no collision (first natural sighting of the Executed reason).
- emergency-restore-720: `Emergency` / `restore`, wrapped at the space, the prompt row text unchanged.

| Check | Max | Score | Note |
|---|---:|---:|---|
| V tells (gates V1, V2, V4, V8, V10) | 20 | 20 | no change |
| L tells, one spacing scale | 15 | 15 | wrap fixed; no collision |
| Copy and vocabulary | 15 | 14 | banner and meter agree; `Disconnected` and fixed `60 s left` remain minor |
| Every element answers state / action (gate) | 15 | 14 | the level-1 strip is better but still small |
| Role-specific HUD, no leak (gate) | 20 | 20 | guard added; no leak path changed |
| Interactive states (gate) | 10 | 10 | none new |
| Contrast / readable at 720p | 5 | 5 | |
| **Total (changed screens)** | **100** | **98** | no gate violation |

Does level 1 now read as a threat cue? Yes, as a cue you notice when you look at the meter: red noise 100 x 8 u next to the state word, easy to identify in the stills. It is still a small mark at the bottom centre, so it is a close-range HUD cue, not a world-space one; level 3 remains the only cue that fills the periphery. That is the design the user ruled on, not a build defect. Suggest one in-play check by the user.

## Unverified
- A2 Unity-only tests have not run (first run is the gate).
- Visor fade visually (no re-shot).
- The real U2e entry; the `Executed` reason in a real match is now seen once, offline.

VERDICT: PASS
