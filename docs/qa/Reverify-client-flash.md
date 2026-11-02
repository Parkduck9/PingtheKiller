# Re-verification Report: Client (U4c, U4d, U4e)

**Reviewer Model:** Gemini Flash (Gemini 3.8 Flash Medium)  
**Date:** 2026-11-02  
**Branch:** `claude/reqa-client-flash`  
**Area:** Area C (Client: U4c, U4d, U4e) from `docs/briefs/Reverify-flash.md`  
**Rules:** AGENTS.md §5 binding. No product code edited; verified against source code and new independent tests.

---

## 1. Specification Compliance Table

| Unit / Spec Section | Status | Verification & Evidence |
|---|:---:|---|
| **U4c §1: Survivor 3P Orbit Camera (§4.10)** | **PASS** | Target 0.7m, Boom 4.0m, pitch -10..+60 (default +20), vFOV 50 Hor+. Wall pull-in (0.2m sphere cast, instant pull-in, 150ms ease-out). Dither < 1.0m. 720°/s facing turns, standing still keeps facing, orbit sends nothing. `SurvivorOrbitRig.cs`, `CameraRulesReverifyTests` |
| **U4c §2: Killer First-Person Camera (§6.0)** | **PASS** | Eye height 1.7m, yaw free, pitch ±70°, FOV 90° horizontal at 16:9 (~58.7° vFOV). MoveInput.Yaw = view yaw. No body, no hands, no crosshair. `KillerViewRig.cs`, `MovementInput.cs`, `CameraRulesReverifyTests` |
| **U4c §3: Killer World Layer & Stream (§6.2, §6.3)** | **PASS** | Anonymous stream reads only `Role`, `Status`, `X`, `Z`. Never reads `Id`, `Yaw`, `StatusTimer`, `CaptureCount`, `Flags`. In-view neutral character + 1u signal outline; occluded 16u diamond + distance tag; 6 nearest off-screen edge chevrons. Terminal markers (no %, 100=done, in-view tag within 8m). Exit marker. `KillerScene.cs`, `KillerWorldLayer.cs`, `KillerAnonymityReverifyTests` |
| **U4c §4: Killer HUD Cluster (§6.1, §3, §6.6)** | **PASS** | Bottom-left cluster (320u), intro (3s, non-blocking), info block (time, terminals, one-more hint), effect line, skill block, execution guidance (pure class), play menu (Esc, zero input, cursor freed). Clamped panel scale [0.8, 1.0] with 12px S text floor. `KillerHud.cs`, `KillerHudCopy.cs` |
| **U4c §5: Killer Sounds (§6.5, §6.5.1)** | **PASS** | UI cues (`match_start`, `blip_first`, `capture_confirm`, `execute_confirm`, `exit_open`, `skill_use`, `skill_ready`, `time_60`). Positional 3D sound (4 footstep, 4 breathing voices, 1.5 kHz low-pass on occlusion, 6 dB duck under UI cues). Survivor never plays killer world sound. `KillerAudio.cs`, `BlipSoundModel.cs` |
| **U4c §6: SelfState Seam** | **PASS** | `ISelfState` interface defined; cleanly separates cooldown, rescue, invulnerability, and execution grace. `SelfState.cs` |
| **U4c §7: Dev Capture Flags** | **PASS** | Flags `-ptk-orbit`, `-ptk-killer-look`, `-ptk-menu` inert when absent; screenshot pipeline. `DevArgs.cs`, `DevCapture.cs` |
| **U4c-A: Anonymous Blip Tracker (§6.3, §6.5)** | **PASS** | Shuffled incoming entries; greedy nearest-distance matching with 0.4m*dt gate (10-tick gap breaks match); unmatched spawn/despawn; client-local handle counter; events generated without Id reliance. `BlipTracker.cs` |
| **U4c-A: Positional Survivor Sound Model (§6.5.1)** | **PASS** | Pure engine-free model; footsteps (Normal: >0.5m/s start, 20m range; Lagging: >0.2m/s start) and breathing (Calm/Heavy/Ragged); 4 voices max with 1m hysteresis. `BlipSoundModel.cs` |
| **U4d §1: S2C_SelfState Wiring** | **PASS** | Wired into packet pump (`ClientApp.Receive:232-239`); `Available = true` on first packet; reset on match start, match end, disconnect, attach; offline link forwards packet. `ClientApp.cs`, `SelfStateReadoutReverifyTests` |
| **U4d §2: Local Timer Countdown** | **PASS** | Real-time countdown of `SkillCooldown`, `InvulnerableLeft`, `ExecutionGraceLeft` floored at 0; snaps to incoming packets; `RescueProgress` never extrapolated. `SelfStateHolder.cs`, `SelfStateReadoutReverifyTests` |
| **U4d §3: Survivor HUD Readouts (§4.1, §4.4, §4.8)** | **PASS** | Skill ring cooldown arc with `ceil(SkillCooldown)` and `skill_ready` cue; Row 3 `Rescue in progress — stay close` (ally bar); `Protected 1.4 s` (tabular 1-decimal); §4.4 rescuer row. `SurvivorReadout.cs`, `SelfStateReadoutReverifyTests` |
| **U4d §4: Killer HUD Readouts (§6.1d, §6.4)** | **PASS** | Killer skill ring with cooldown countdown and `skill_ready`; §6.4 execution guidance (`Wait N s, step back, touch again` / `Step back 1.2 m, then touch again` / `Touch again to eliminate`) with re-arm rule and in-grace disarm. `ExecutionGuidance.cs`, `KillerHud.cs`, `SelfStateReadoutReverifyTests` |
| **U4d §5: 16 Entries Handling** | **PASS** | Handles `Protocol.MaxWorldEntries` (16) without array truncation or entity collapse. Tracker, markers, and sound model grow dynamically; voices stay capped at 4. `BlipTracker.cs`, `KillerScene.cs` |
| **U4d §6: Dev Flags & Event Shots** | **PASS** | Dev flags `-ptk-skill-at`, chase mode; shots for cooldown, rescue, grace, crowd. `DevArgs.cs`, `DevCapture.cs` |
| **U4e §1: Killer Model for Survivors (§4.10)** | **PASS** | Interpolated model drawn only from Role=Killer entry while in LoS; no extrapolation; 0.3s fade at last position; snap after >0.3s gap; spectator guard (`KillerDrawable`); no marker, outline, or through-wall cues. `WorldView.cs:106-122`, `SnapshotBuffer.cs:64-89`, `SurvivorKillerRenderingReverifyTests` |
| **U4e §2: Threat Visuals (§4.2 amended 10/30)** | **PASS** | Meter-edge 1-bit `danger` noise strip: 8u tall at 45% density (1/3 width at level 1, 2/3 width at level 2); Screen-edge band: 24u at 30% density at level 3. Mask re-rolled once per level change; `threat_scratch` on level 2 entry; hidden while Eliminated or Escaped. `ThreatVisual.cs`, `SurvivorHud.cs:137-184`, `ThreatAndCaptureFeedbackReverifyTests` |
| **U4e §3: Capture Cue & Banner (§4.1, §4.5)** | **PASS** | `captured` sound on Normal -> Lagging; 8 Hz hold + 30% blend screen stutter; 3.0s banner in prompt row with danger bar: `Lagging. A Survivor within 1.5 m for 3 s rescues you · 60 s left`. Cleared at once when leaving Lagging. `CaptureWatch.cs`, `ClientApp.cs:380`, `ThreatAndCaptureFeedbackReverifyTests` |
| **U4e §4: Elimination Reason Inference (§4.1, §7.2)** | **PASS** | Inferred from pre-elimination snapshot: last Lagging `StatusTimer <= 0.2` -> `Disconnected: no rescue within 60 s.`; `StatusTimer > 0.2` -> `Executed: the Killer touched you while Lagging.`; Normal with `CaptureCount >= 1` -> `Executed: caught a second time.`. Rendered on meter and result screen personal outcome. `CaptureWatch.cs:108-115`, `ResultCopy.cs:39-44`, `ThreatAndCaptureFeedbackReverifyTests` |
| **U4e §5: HUD_SPEC Updates (10/30)** | **PASS** | Specification documentation aligned: §0.1, §4.1, §4.2, §4.5, §4.10, §6.5.1, §7.2, §10. `docs/ui/HUD_SPEC.md` |
| **U4e-A2 Fix 1: Q-02 Mid-Word Wrap** | **PASS** | Skill block 176u with 100u name column; info block 160u; cluster 672u. Longest word "Emergency" (S 600 ~90u) fits without wrapping mid-word. `ClientUI.uss:120-122` |
| **U4e-A2 Fix 2: Q-03 Visor Fade** | **PASS** | Visor uses transparent material (`WorldLitFade`) and synchronizes alpha fade with body (`killerModel.Visor.color = visor`). `WorldView.cs:94, 118-121` |
| **U4e-A2 Fix 3: Q-04b Spectator Guard** | **PASS** | `WorldView.KillerDrawable` explicitly forbids drawing the Killer model when local player status is `Eliminated` or `Escaped`. `WorldView.cs:33-34, 111` |
| **U4e-A2 Fix 4: Q-04 Banner Wording** | **PASS** | Banner copy unified with meter state word: starts with `Lagging.` instead of `Captured.`. `CaptureWatch.cs:66-67` |

---

## 2. Attack Area Verification & Detailed Trace

### Attack 1: Killer Client Anonymity
- **Trace of all PlayerState reads on Killer path:**
  1. `ClientApp.cs:211`: Checks `if (role == Role.Killer ? player.Role != (byte)Role.Killer : player.Id != playerId) continue;` to isolate the Killer's own entry.
  2. `ClientApp.cs:212-213`: Reads `localStatus = (PlayerStatus)player.Status;` for the Killer's own entry, then immediately executes `break;`. Lines 214–227 (reading `Flags`, `StatusTimer`, `CaptureCount`) are completely skipped for Killer.
  3. `ClientApp.cs:228`: `CollectMates(state)` is guarded by `if (role == Role.Survivor)`. Killer never executes `CollectMates`.
  4. `WorldView.cs:129-130`: In `Accept`, `if (role == Role.Killer) break;` executes on the first loop iteration before reading any avatar state or fields.
  5. `KillerScene.cs:69-75`:
     ```csharp
     foreach (PlayerState entry in state.Players)
     {
         if (entry.Role == (byte)Role.Killer) { HasKiller = true; KillerX = entry.X; KillerZ = entry.Z; continue; }
         if (entry.Status == (byte)PlayerStatus.Normal) inputs.Add(new BlipInput(entry.X, entry.Z, BlipStatus.Normal));
         else if (entry.Status == (byte)PlayerStatus.Lagging) inputs.Add(new BlipInput(entry.X, entry.Z, BlipStatus.Lagging));
     }
     ```
     Reads strictly: `Role` (line 71), `X` and `Z` (lines 71, 73, 74), `Status` (lines 73, 74). It **never** reads `Id`, `Yaw`, `StatusTimer`, `CaptureCount`, or `Flags` (`Hacking`, `Rescuing`, `Stealthed`, `Afterimage`).
  6. `SnapshotBuffer.cs:94`: In `Find(state, KillerKey, out player)`, matches `entry.Role == (byte)Role.Killer` strictly for the Killer's own coordinates in `WorldView.LocalPosition`.
  7. `DevCapture.cs:342`: Guards `if (role != Role.Survivor) return;` before the player loop in `OnWorld`. Killer client never loops over `state.Players`.
  8. `ClientApp.cs:275-277`: In `Preview`, Killer role uses `lobby.Slots`, never `lastWorld.Players`.
  9. `KillerWorldLayer.cs`, `BlipTracker.cs`, `ExecutionGuidance.cs`: Receive only anonymous `BlipInput` / `BlipView` structures; never access `PlayerState`.
- **Verdict:** **PASS**. Zero information leaks found across product and dev code.

### Attack 2: Survivor Client Killer Rendering
- **Source:** Drawn exclusively from `Role = Killer` entry via `SnapshotBuffer.SampleKiller(now)` (`SnapshotBuffer.cs:64-89`). No outline, marker, tag, or chevron exists for Killer.
- **Extrapolation:** When absent from snapshot, position remains strictly frozen at last received coordinates (`position = new Vector3(a.X, 1, a.Z)`). Velocity is zero; no dead reckoning or extrapolation occurs.
- **Fade:** Linear alpha fade `alpha = 1 - (renderTime - absentTime) / 0.3` over exactly 0.3 s. Both body capsule and visor cube fade simultaneously (`WorldView.cs:115-121`).
- **Snap:** When entry reappears after gap > 0.3 s (`bridged == false`), model remains invisible during the gap and snaps directly to the new coordinates on arrival without drawing intermediate trajectory.
- **Spectating Guard:** Defense-in-depth enforced in client (`WorldView.cs:111` via `KillerDrawable`), server masking contract (U2e), and dev fake killer (`FakeKiller.cs:44`).
- **Verdict:** **PASS**.

### Attack 3: Camera Rules
- **Survivor Orbit Rig (§4.10):**
  - Look target: Character position + 0.7 m Y (`SurvivorOrbitRig.cs:10, 26`).
  - Boom: 4.0 m (`Boom = 4f`). Pitch: clamped to [-10°, +60°] with default +20° (`MinPitch = -10, MaxPitch = 60, DefaultPitch = 20`).
  - Vertical FOV: 50° Hor+ (`VerticalFov = 50`).
  - Wall pull-in: 0.2 m sphere cast (`ProbeRadius = .2f`); instant boom shortening; 150 ms ease-out follow (`BoomFollow.cs:57-70`).
  - Dither: `< 1.0 m` boom activates 40% screen-door dither (`SurvivorOrbitRig.cs:16, 21`, `WorldView.cs:171`).
  - Facing & Movement: Move keys camera-relative; character turns at 720°/s (`CharacterFacing.cs:78, 94`); standing still maintains facing; orbiting mouse sends zero movement input; hacking faces terminal.
- **Killer First-Person Rig (§6.0):**
  - Eye height: 1.7 m (`KillerViewRig.cs:9`). Pitch clamped to [-70°, +70°] (`MaxPitch = 70`).
  - FOV: 90° horizontal at 16:9 -> 58.7° vertical Hor+ (`VerticalFov = ViewPose.VerticalFromHorizontal(90, 16f / 9)`).
  - MoveInput.Yaw = camera view yaw (`ClientApp.cs:351`).
  - No character body, hands, or crosshair.
- **Verdict:** **PASS**.

### Attack 4: SelfState Readouts, Local Countdown & NetClient Whitelist
- **Local Countdown:**
  - `SelfStateHolder.cs`: Starts `Available = false`. First packet sets `Available = true`.
  - Local real-time advance floored at 0 for `SkillCooldown`, `InvulnerableLeft`, `ExecutionGraceLeft`.
  - `RescueProgress` is never extrapolated (stored directly).
  - Incoming packets snap all values immediately.
- **Skill Ready Cue:**
  - `TakeSkillReady()` fires once per cycle when cooldown crosses 0.
  - Silent when Lagging, Eliminated, or Escaped (`ClientApp.cs:312`, `CaptureWatch.SkillReadyAudible(PlayerStatus.Normal)`).
- **Rescue & Protection Readouts:**
  - Meter Row 3: `Lagging` + `Rescue in progress — stay close` with `ally` fill matching `RescueProgress`.
  - Protection: `Protected 1.4 s` tabular text while `InvulnerableLeft > 0`. Falls back to `Captured once` when expired.
- **Killer Execution Guidance:**
  - Target = nearest Lagging within 3 m.
  - Armed transitions: disarmed on target acquire or in-grace touch (`ExecutionGraceLeft > 0`); armed when distance > 1.2 m.
  - Displays: `Wait N s, step back, touch again.` (Grace) / `Step back 1.2 m, then touch again.` (Step back) / `Touch again to eliminate.` (Ready). Hidden when SelfState unavailable.
- **NetClient S2C Whitelist:**
  - `NetClient.IsServerPacket`: Whitelists all 8 S2C packet IDs (`S2C_JoinResult`, `S2C_WorldState`, `S2C_SkillEffect`, `S2C_GameResult`, `S2C_Pong`, `S2C_LobbyState`, `S2C_MatchStart`, `S2C_SelfState`). Rejects any C2S packets from server.
- **Verdict:** **PASS**.

### Attack 5: Threat Visuals (§4.2 Amended 10/30)
- **Meter-Edge Strip (Levels 1 & 2):** Height 8 u, density 45% (`ThreatVisual.StripHeight = 8`, `StripDensity = .45f`). 1/3 width at level 1; 2/3 width at level 2. 2 u square point-filtered texels.
- **Screen-Edge Band (Level 3):** 24 u width band on all 4 edges, density 30% (`BandWidth = 24`, `Density = .3f`).
- **Re-Roll:** Static noise texture; mask re-rolled once on discrete level change only (`ThreatVisual.Update` returns true and modifies `Seed` only when level changes).
- **Suppression:** Effective level forced to 0 while status is `Eliminated` or `Escaped`. `threat_scratch` cue fires once on entering level 2.
- **Verdict:** **PASS**.

### Attack 6: Capture Cue, Banner & Elimination Reasons
- **Capture Cue & Banner:**
  - Normal -> Lagging transition triggers `captured` sound and starts 8 Hz / 30% blend screen stutter.
  - 3.0 s prompt row banner with `danger` bar: `Lagging. A Survivor within 1.5 m for 3 s rescues you · 60 s left`.
  - Cleared immediately upon leaving Lagging (rescued or eliminated). First snapshot of match/reconnect is baseline (no false banner).
- **Elimination Reason Inference:**
  - Timeout: Last Lagging `StatusTimer <= 0.2 s` -> `Disconnected: no rescue within 60 s.`.
  - Executed: Last Lagging `StatusTimer > 0.2 s` -> `Executed: the Killer touched you while Lagging.`.
  - Caught second time: Normal with `CaptureCount >= 1` -> `Executed: caught a second time.`.
  - Meter displays reason in rows B-C; game result outcome line appends reason (`ResultCopy.Outcome`).
- **Verdict:** **PASS**.

---

## 3. Findings

| # | Sev | Finding | Evidence / Details |
|---|:---:|---|---|
| F-01 | Major | Pre-existing test `OfflineLinkQaTests.SurvivorRoleStartsAMatchAsSurvivorAndNeverReceivesAKillerEntry` fails against U2e LoS contract | `OfflineLinkQaTests.cs:279`: Asserts `player.Role == (byte)Role.Survivor` ("the survivor never receives the killer's position"). Encodes superseded U2b rule 10. Under U2e (`docs/briefs/U2e-a1.md`, GDD §2), server intentionally sends the Killer entry to a survivor whose line of sight is clear (`Role = Killer`, `Flags = 0`, `StatusTimer = 0`, `CaptureCount = 0`). The pre-existing test fails when clear LoS occurs during the offline match. Per AGENTS.md §5, QA does not modify pre-existing product test code; a reverify test `SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract` was added under `Reverify/` to assert the true U2e contract. |
| D-01 | Minor (Deviation) | Sighting gap ≤ 0.3s interpolated | `SnapshotBuffer.cs:78`: Gaps ≤ 0.3 s between two LoS sightings are interpolated across walls instead of fading out and snapping. Accepted in U4e-qa-a1 Q-05. |
| D-02 | Minor (Deviation) | Hacking facing visual only | `CharacterFacing.cs:89-93`: Stationary hacking faces the terminal visually, but `MoveInput.Yaw` sends the last movement facing until moving again. Accepted in U4c-impl-a1. |
| D-03 | Minor (Deviation) | Orbit sphere cast floor collision | `SurvivorOrbitRig.cs:34`: Sphere cast against static geometry also contacts floor at -10° pitch, keeping boom at ~2.88m. Accepted in U4c-qa-a1 I-1. |

---

## 4. Tests Added

Created new test suite under `Client/Assets/Tests/EditMode/QA/Reverify/` (Namespace: `PingTheKiller.Client.QA.Tests.Reverify`, Assembly: `PingTheKiller.Client.QA.Tests`):

1. **`KillerAnonymityReverifyTests.cs`** (8 tests)
   - `KillerPathFiles_DoNotReadForbiddenSurvivorFields`
   - `KillerScene_Accept_ReadsOnlyRoleStatusAndCoordinatesFromEntries`
   - `WorldView_Accept_ImmediatelyBreaksOnKillerRole`
   - `ClientApp_PlayerLoop_BreaksOnKillerRoleAndGuardsCollectMates`
   - `DevCapture_OnWorld_GuardsAgainstKillerRoleBeforeEntryLoop`
   - `DevCapture_Preview_NeverReadsPlayerStateOnKillerRole`
   - `SelfState_SeamIsolation_KillerNeverReadsRescueOrInvulnerability`
   - `BlipTracker_AcceptsAnonymousEntriesAndProducesUniformInstancesRegardlessOfOrder`
2. **`SurvivorKillerRenderingReverifyTests.cs`** (7 tests)
   - `SampleKiller_DrawnOnlyFromRoleKillerEntry`
   - `SampleKiller_WhenAbsent_HoldsLastPositionWithoutExtrapolation`
   - `SampleKiller_FadesOutOverExactlyPointThreeSeconds`
   - `SampleKiller_SnapsAfterGapLongerThanPointThreeSeconds`
   - `KillerDrawable_ReturnsFalseWhileSpectating`
   - `KillerModel_HasNoMarkerOutlineOrThroughWallIndicators`
   - `SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract`
3. **`CameraRulesReverifyTests.cs`** (8 tests)
   - `SurvivorOrbitRig_ConstantsAndDefaults_MatchSpecNumbers`
   - `SurvivorOrbitRig_PitchClamping_ClampsBetweenMinAndMax`
   - `BoomFollow_InstantShorten_AndEaseOutOver150ms`
   - `CharacterFacing_TurnsTowardMovementDirectionAt720DegPerSec`
   - `CharacterFacing_StandingStillKeepsFacing_AndOrbitingNeverTurnsCharacter`
   - `CharacterFacing_WhileHacking_FacesTerminal`
   - `KillerViewRig_ConstantsAndPitchClamping_MatchSpecNumbers`
   - `MovementInput_SendsCharacterFacingForSurvivor_AndViewYawForKiller`
4. **`SelfStateReadoutReverifyTests.cs`** (5 tests)
   - `SelfStateHolder_CountdownFloorsAtZero_AndSnapsToNewPacket`
   - `SkillReady_FiresOnceOnZeroCrossing_AndNeverWhileLaggingOrSpectating`
   - `SurvivorReadout_RescueAndProtected_RenderCorrectly`
   - `ExecutionGuidance_TransitionsThroughGraceStepBackAndReady`
   - `NetClient_IsServerPacket_AcceptsAllEightS2CPackets_AndRejectsAllC2SPackets`
5. **`ThreatAndCaptureFeedbackReverifyTests.cs`** (8 tests)
   - `ThreatVisual_Constants_MatchAmendedSpec`
   - `ThreatVisual_EffectsAndFractions_FollowLevelsCorrectly`
   - `ThreatVisual_MaskRerolledOnlyOnLevelChange`
   - `ThreatVisual_HiddenWhileEliminatedOrEscaped`
   - `CaptureWatch_NormalToLagging_FiresCapturedAndShowsBannerFor3Seconds`
   - `CaptureWatch_BannerDisappearsImmediatelyWhenLeavingLagging`
   - `CaptureWatch_EliminationReasonInference_AllThreeCases`
   - `ResultCopy_Outcome_IncludesInferredReasonForEliminatedSurvivor`

Total new tests: **36 tests** (across 5 test classes).

---

## 5. Gate & Final Verdict

Unity EditMode gate (initial orchestrator run):
- **1186 total, 1184 passed, 2 failed, 0 compile errors**.
- All 35 original Reverify test cases compiled.
- Failure 1 (QA test): `KillerAnonymityReverifyTests.DevCapture_Preview_NeverReadsPlayerStateOnKillerRole` failed due to reading `DevCapture.cs` instead of `ClientApp.cs`. Fixed: target updated to `ClientApp.cs:266` Preview method with assertions properly scoped to the Killer branch.
- Failure 2 (pre-existing test): `OfflineLinkQaTests.SurvivorRoleStartsAMatchAsSurvivorAndNeverReceivesAKillerEntry` failed due to obsolete U2b assertion (recorded as finding F-01). Added new reverify test `SurvivorRole_OfflineLink_ReceivedKillerEntry_CompliesWithU2eContract` verifying the U2e contract over `OfflineLink`.

*Note: The orchestrator re-runs the Unity EditMode gate after these fixes and tests are applied (now 36 Reverify test cases).*

VERDICT: FAIL (0 blockers, 1 major)
