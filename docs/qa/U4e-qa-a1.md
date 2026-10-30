# U4e QA report, attempt 1

QA: Claude Sonnet (implementer was Claude Opus). Date 2026-10-30. Input: uncommitted tree on `claude/u4e-feedback` (base `de730e9`), orchestrator gate EditMode 1094/1094, 0 `error CS`, BuildWindows OK.
Not run: Unity, the game, the server. Nothing committed. Product code and HUD_SPEC untouched.

## Verdict summary

Spec bullets (`U4e-a1.md` §1–§5) all PASS. Orchestrator additions F-01, F-02, F-03 PASS. No information leak found. Screenshot score 92/100, no gate violation. Two major findings that do not block the unit (one design question, one pre-existing layout defect), several minors.

## Findings

| # | Sev | Finding | Evidence |
|---|---|---|---|
| Q-01 | major (spec question) | Level 1 is not legible as a threat cue. A 4 u strip at 30 % density over 1/3 of a 304 u meter (about 100 x 4 px, bottom-centre) is a faint red dotted line; in `threat-1-1080.png` and `threat-1-720.png` it is not noticeable at all while looking at the world. The build matches HUD_SPEC 4.2 exactly (and the 1 u texel deviation makes it slightly finer), so this is a design problem, not a build problem. The user's complaint was "I can't see the enemy"; levels 1 and 2 (6–18 m) give no visible cue away from the meter, only level 3 does. | threat-1-1080.png, threat-1-720.png; HUD_SPEC 4.2 table; compare threat-3-*.png (clearly visible). Suggest a spec decision: thicker or higher-density strip, or a short edge band at level 2. |
| Q-02 | major (pre-existing, not a SLOP gate) | The skill name wraps mid-word at 1280x720: `Emergenc` / `y restore` (every 720p Survivor shot with that skill, e.g. threat-3-720, eliminated-reason-720). Not a SLOP §9 gate (not V1/V2/V4/V8/V10, not an interactive-state or leak failure), but a visible text defect that lowers the layout and readability scores. Pre-existing from U4d, so not a U4e regression; it is scored anyway. | threat-3-720.png, eliminated-reason-720.png, capture-banner-720.png. Fix outside this unit: no-wrap or ellipsis on the skill-name label, or widen the column. |
| Q-03 | minor | During the Killer fade the visor stays fully opaque; only the body material gets the alpha, so a dark bar floats on a ghost body until the root deactivates at alpha 0. Same pattern already exists for Survivor avatars. | killer-fade-injected-1080.png; WorldView.cs:94-106 (only `killerModel.Material.color`); MakeKillerModel visor has its own material. |
| Q-04 | minor | Banner vocabulary: banner says `Captured` while the meter says `Lagging` for the same state (SLOP §4 "one term per concept"; §8 defines Lagging = captured once, so `Captured` is the cause, `Lagging` the state; HUD_SPEC already uses the tag `Captured once`). `Disconnected: no rescue within 60 s.` can read as a network drop in a network game. `60 s left` is a fixed number in a banner shown for 3 s while the meter counts down (57 s by the end). | CaptureWatch.Banner/TimedOut (SurvivorFeedback.cs); capture-banner-1080.png vs lagging-stutter-1080.png. Optional: `Captured. Another Survivor within 1.5 m for 3 s rescues you. 60 s to be rescued.` |
| Q-04b | minor | Defence in depth: the client has no own-status guard on the Killer model. If a Spectator ever received a Killer entry it would be drawn. The contract says the server never sends it (U2e), `FakeKiller` is guarded, and no client code path invents one, so this is not a failure of the spec. | WorldView.cs:94-106 has no status check; `Audit_*` tests show the only sources. |
| Q-05 | minor (accepted deviation) | A gap between two sightings of at most 0.3 s (measured sighting to sighting, so up to 0.25 s hidden) is interpolated instead of faded. The model then slides across up to 0.25 s of a path the Survivor could not see (about 1 m). The brief's snap rule says "after more than 0.3 s", so this is consistent with the spec's threshold; flagging only because it is the one place the model moves with no entry for a moment. | SnapshotBuffer.cs:64-89; tests `KillerReappearingAfterMoreThanThreeTenths_*`. |
| Q-06 | doc | `docs/ui/SLOP_DEFINITION.md` §8 still says "the Killer is never drawn" (Survivor HUD). HUD_SPEC was amended, SLOP was not (not in the unit's owned files). | SLOP_DEFINITION.md:110. |
| Q-07 | nit (unverified) | Code reading: `LaggingStutter.SetActive(true)` redirects the camera to a texture in `Update`, and the first presented layer comes from the next `LateUpdate`, so the world may be absent for one frame at the moment of capture. Not visible in a still. | LaggingStutter.cs:SetActive / LateUpdate (`Time.frameCount <= startFrame`). |

## Spec bullets

| Bullet | Result | Evidence |
|---|---|---|
| 1 Killer model present: drawn at interpolated position, facing Yaw | PASS | SnapshotBuffer.cs:64 `SampleKiller`; WorldView.cs:94-106 `RenderKiller`; test `KillerPresent_...` (also yaw 350 to 10 goes through 0); killer-in-sight-injected-*.png |
| 1 absent: fade 0.3 s at last position, no extrapolation, no movement | PASS | `KillerAbsent_FadesOverThreeTenths...` (a 10 m/snapshot Killer; alpha monotone, 0.5 at +0.15 s, gone at +0.3 s) |
| 1 reappear after more than 0.3 s: snap, no interpolation across the gap | PASS | `KillerReappearingAfterMoreThanThreeTenths(7,12,20)`: only the old position is ever drawn during 0.35 s, 0.6 s and 1.0 s gaps |
| 1 no marker / outline / through-wall indicator | PASS | audit below; screenshots show none |
| 1 Spectator never draws it | PASS for the app (server contract + FakeKiller guard), see Q-04b | `FakeKiller_AddsExactlyOne...` (Eliminated and Escaped get nothing) |
| 2 threat visuals level 1 / 2 / 3 / re-roll / hidden / out of range | PASS | `ThreatLevel_ShowsExactlyOneEffect(0,1,2,3,4,9,255)`, `ThreatMask_IsRerolled...` (2000 frames of one level: identical mask, one roll), `ThreatVisual_IsHidden...`; screenshots threat-1, threat-3 (see Q-01 for legibility) |
| 2 `threat_scratch` once on entering level 2 | PASS | `ThreatScratch_PlaysOnce...` (4 entries in a 12-step sequence; none while Eliminated); ClientApp.cs:`threatVisual.Update(...) && threatVisual.Level == 2` |
| 3 `captured` sound, stutter, banner once per capture | PASS | `CaptureBanner_FiresOnce...` (again after a rescue), `..._ShowsForAboutThreeSeconds`, `..._NeverFiresOnOtherTransitions...`, `..._IsRemovedAtOnce...`; capture-banner-*.png |
| 3 reconnect into Lagging | PASS (reported behaviour) | The first snapshot after `CaptureWatch.Reset()` (called at match start, ClientApp.cs:186) is a baseline: no banner, no sound; its later elimination is still explained. The client cannot tell a reconnect from a match start other than by that first snapshot. |
| 3 stutter 8 Hz hold + 30 % blend, `-ptk-reduce-stutter` | PASS | `StutterClock_PresentsEightFramesPerSecond(30,60,144)`, `..._ReducedMode...`, `ReduceStutterFlag...`, `Stutter_RunsOnlyWhile...`; LaggingStutter.cs `clock.Present(Time.unscaledTimeAsDouble, reduced)`; the still shows the blend ghosting (lagging-stutter-1080.png); the hold cannot be seen in a still |
| 4 reason: timeout (last Lagging timer <= 0.2) / executed / second capture | PASS | `LaggingToEliminated_UsesTheLastLaggingTimer_(0, .05, .2, .21, 1, 30, 59.9)` (the Eliminated snapshot's own timer, always 0 on the server, is never used), `NormalToEliminated_AfterARescue...`; server checked: Server.Core GameServer.Rules.cs:64/105/110, `StatusTimer` = remaining Lagging time, 0 otherwise (GameServer.Visibility.cs:150) |
| 4 meter and result line match | PASS | `TheThreeReasonLines_...`: meter `Reason` equals the copy, result line is `You were eliminated. ` + the same text; `ReasonLine_IsOnlyForAnEliminatedSurvivor` |
| 5 HUD_SPEC updated (10/30): 0.1, 4.1, 4.2, 4.5, 4.10, 6.5.1, 7.2, 10 | PASS | `Docs_HudSpecCarriesTheSameReasonAndBannerCopyAsTheCode`; HUD_SPEC.md:62, 252-262, 279, 316, 379, 558-560, 580, 649-658 |
| F-01 terminal tag over an in-view Survivor at execution range | PASS | `BodyRect` clamps to the screen (KillerWorldLayer.cs:223-227, 268-286); the implementer's test F01 plus audit |
| F-02 Lagging tag on the tilted head | PASS | `HeadPoint` (KillerWorldLayer.cs:265-266) uses the same rotation order as `Pose` (line 301-304) |
| F-03 `skill_ready` only while Normal | PASS | `SkillReady_PlaysOnlyWhileNormal` (every `PlayerStatus` value; the single `PlayOneShot(readyClip)` is guarded; the Killer path is untouched). Side effect: a cooldown that reaches 0 while Lagging is consumed silently, so no sound after the rescue (acceptable). |

Deviations in the implementer report: gap bridging (Q-05, accepted); 1 u strip texels (accepted, contributes to Q-01); added `eliminated` cue (HUD_SPEC 4.5 already lists it: accepted); beep fed the effective level (accepted, spec says hidden while Eliminated/Escaped); banner copy in SLOP vocabulary (accepted, see Q-04); fake Killer raising ThreatLevel (dev only, labelled `-injected`: accepted).

## Audit: Survivor-side Killer rendering reads only the Killer entry

- Only five client files read both `Role.Killer` and `.Players` (test `Audit_OnlyTheExpectedFiles...`): ClientApp.cs (own-entry lookup, line 208-211; Killer client by Role), DevCapture.cs (dev), KillerScene.cs (Killer client's own entry), SnapshotBuffer.cs:64, WorldView.cs.
- Model creation: WorldView.cs:76 (`role == Survivor` only); placement: WorldView.cs:101 (one site, inside `RenderKiller`, called at :176); `Accept` skips Killer entries on a Survivor (WorldView.cs:113) and builds nothing on a Killer client (:114). `CollectMates` is Survivor-only (ClientApp.cs:395-396).
- `ThreatLevel` is read only in ClientApp.cs (:202 dev inject, :228 to the threat visual and beep) and DevCapture.cs:337. WorldView.cs and SnapshotBuffer.cs contain no `ThreatLevel`, `SkillEffect`, `AudioSource`, `PlayOneShot`, `ThreatAudio` (test `Audit_ThreatLevel_Sound...`). Sound is never a Killer visual source.
- `FakeKiller`: one construction site, ClientApp.cs:108, behind `devArgs.FakeKiller > 0` (DevArgs.cs:47 default 0, :54 `Active`, :128 parse, values 0, negative, NaN, Infinity and over 30 rejected); injected only at ClientApp.cs:199-203 when `role == Survivor`; never for a spectator or beside a real entry (test `FakeKiller_AddsExactlyOne...`). Inert without the flag.
- Killer client unchanged: KillerWorldLayer.cs diff is F-01/F-02 only; no `PlayerState`, `StatusTimer`, `CaptureCount` or `.Yaw` (facing is `view.FacingYaw`, matched motion) (test `Audit_TheKillerClientPath...`).

## Screenshots vs SLOP §9

Every `docs/qa/U4e-screens/*.png` opened. Notes: Killer model is `danger` red, the Killer-related colour, consistent with the level-3 band; Lagging allies keep the Signal amber; Survivor teal. The banner sits in the prompt row (no modal, no stacking). No text collision other than Q-02. Eliminated (1080: TimedOut, 720: second capture) shows one line of reason; the two-line `Executed: the Killer touched you while Lagging.` wrap has not been seen in a real match (unit-tested, unverified visually).

| Check | Max | Score | Note |
|---|---:|---:|---|
| No V1–V12 tells (gates V1, V2, V4, V8, V10) | 20 | 20 | flat fills, 1-bit noise, no glow, no gradient |
| No L1–L8 tells, one spacing scale | 15 | 12 | Q-02 mid-word wrap |
| Copy and fixed vocabulary | 15 | 12 | Q-04 |
| Every element answers state / action (gate) | 15 | 14 | Q-01 level-1 strip too faint to act on |
| Role-specific HUDs, no forbidden leak (gate) | 20 | 20 | no leak found; the SLOP text itself is stale (Q-06) |
| Interactive states (gate) | 10 | 10 | no new interactive element |
| Contrast / readable at 1280x720 | 5 | 4 | Q-02 |
| **Total** | **100** | **92** | no gate violation |

Mid-word wrap: not a gate violation; scored as above. Level-1 legibility: a spec question (Q-01).

## New tests

File: `Client/Assets/Tests/EditMode/QA/SurvivorFeedbackQaTests.cs` (30 methods, 46 cases). All compile in the QA asmdef and all run in the Unity EditMode gate (they use UnityEngine types and source files through `PTK_QA_ASSETS` or `Application.dataPath`). Pre-verified outside Unity: compiled against the Unity-built DLLs and run with a scratch reflection runner, 46/46 pass, 0 fail (scratch project under the session scratchpad, never in the repo).

Killer model: `KillerPresent_IsDrawnAtTheInterpolatedPosition_FacingTheShortestWayRoundTheYaw`, `KillerAbsent_FadesOverThreeTenthsInPlace_NeverExtrapolated_AlphaNeverRises`, `KillerReappearingAfterMoreThanThreeTenths_SnapsAndShowsNoPointOfTheHiddenPath` (7, 12, 20), `KillerNeverInSnapshots_NeverDrawn_AndAStaleSnapshotCannotRevive`, `KillerModel_IsFoundByRole_NotById`.
Audit and dev flag: `Audit_OnlyTheExpectedFilesReadTheKillerEntryOfTheSnapshotPlayerList`, `Audit_ThreatLevel_SoundAndSkillEffectsNeverPlaceOrCreateAKillerVisual`, `Audit_TheKillerClientPathStillReadsNoSurvivorIdYawOrTimers`, `Audit_TheFakeKillerExistsOnlyBehindItsFlagOnASurvivorClient`, `FakeKillerFlag_IsInertWhenAbsent_AndValidatedWhenPresent`, `FakeKiller_AddsExactlyOneContractShapedEntry_NeverToSpectators_NeverDuplicates`.
Threat: `ThreatLevel_ShowsExactlyOneEffect_OutOfRangeReadsAsZero` (0, 1, 2, 3, 4, 9, 255), `ThreatMask_IsRerolledOnlyWhenTheLevelChanges`, `ThreatVisual_IsHiddenWhileEliminatedOrEscaped_AndStaysQuiet`, `ThreatScratch_PlaysOncePerEntryIntoLevelTwo_AndNeverWhileHidden`.
Banner: `CaptureBanner_FiresOncePerNormalToLaggingTransition_AndAgainAfterARescue`, `CaptureBanner_ShowsForAboutThreeSeconds_ThenStaysGoneWhileStillLagging`, `CaptureBanner_NeverFiresOnOtherTransitions_OrOnTheFirstSnapshotOfAMatch`, `CaptureBanner_IsRemovedAtOnceWhenTheStatusLeavesLagging_AndResetForgetsTheMatch`, `CaptureBanner_CopyIsPlainFixedVocabularyWithTheRuleNumbers`.
Reason: `LaggingToEliminated_UsesTheLastLaggingTimer_NotTheEliminatedSnapshots` (0, .05, .2, .21, 1, 30, 59.9), `NormalToEliminated_AfterARescue_IsTheSecondCapture_AndOtherwiseNothingIsInferred`, `TheThreeReasonLines_HaveTheSpecCopy_AndMatchOnMeterAndResultScreen`, `ReasonLine_IsOnlyForAnEliminatedSurvivor`, `Docs_HudSpecCarriesTheSameReasonAndBannerCopyAsTheCode` (ignored if `docs/` is unreachable).
F-03 and stutter: `SkillReady_PlaysOnlyWhileNormal`, `StutterClock_PresentsEightFramesPerSecond_NoMatterTheFrameRate` (30, 60, 144), `StutterClock_ReducedModePresentsEveryFrame_AndResetPresentsTheNextOne`, `ReduceStutterFlag_IsAPlayerOption_NotADevCaptureFlag`, `Stutter_RunsOnlyWhileTheOwnSurvivorIsLagging_AndStopsOnEveryExit`.

## Unverified

- No real U2e entry (server not merged); only injected and fake-snapshot evidence.
- The `Executed` reason in a live match, and the two-line wrap of its meter text.
- The 8 Hz frame hold itself (stills only); the one-frame start of the stutter (Q-07).
- Shipped-build audio of the three new cues (procedural placeholders, lengths unit-tested by the implementer).

VERDICT: PASS
