# U8-hitfeel QA, attempt 1

- **Reviewer:** Fable (QA only; no product code edited). **Implementer:** Claude Opus.
- **Worktree:** `PtK-work/u8-hitfeel`, branch `claude/u8-hitfeel`, uncommitted on main 3675877.
- **Verdict: FAIL (narrow).** All gates are green and S1 and S3 work in the build. Three things need an attempt 2: F1 (an audit test was sidestepped, not updated), F2 (Killer-view burst size), F3 (swipe timing was not chosen from frame captures and the wind-up is now played faster than the source clip). None is a blocker.

## Gates (re-run by QA)

| Gate | Result | Evidence |
|---|---|---|
| Unity EditMode | **1491 / 1491 passed**, 0 failed, 0 skipped | `docs/qa/U8-runs/qa/editmode.xml` |
| BuildWindows | **Success** | `docs/qa/U8-runs/qa/build-windows.log:6793` |
| `dotnet build PingTheKiller.sln -warnaserror` | 0 warnings, 0 errors | console |
| Live Survivor (`-ptk-chase 140`, 150 s) | exit 0, 0 exceptions, 2 own captures | `docs/qa/U8-runs/qa/survivor.log` |
| Live Killer (`-ptk-chase 140`, 150 s) | exit 0, 0 exceptions, 4 captures | `docs/qa/U8-runs/qa/killer.log` |

Files Unity changed only in line endings were reverted after each run (18 files). The working tree holds the implementer's 16 modified files and nothing else.

## Spec table

| Spec | Result | Notes |
|---|---|---|
| S1.1 8 Hz hold removed | PASS | `LaggingStutter` no longer touches `targetTexture`; `StutterClock` deleted. Lagging mean frame time 6.2-6.8 ms in the live run. |
| S1.2 hit-stop, presentation only | PASS | `HitStop.Present` only changes the time passed to `world.Render` and the camera follow point. No `Time.timeScale` anywhere in `Scripts/`. `link.Pump`, input, `v2Audio.Frame`, HUD and the particle systems (`useUnscaledTime`) use the real clock. See F5 for the resume jump. |
| S1.2 burst 0.3-0.5 s / steady look | PASS | Burst 0.4 s, flash 0.15 s, fade-in 0.25 s. See F4 (flash strength). |
| S1.3 Reduced motion / `-ptk-reduce-stutter` | PASS | `Evaluate(now, reduced)` returns no burst, flash or tear. The Killer kick and shake are off too. Code and tests only; no Reduced live run (run budget). |
| S1.4 HUD_SPEC §4.5 | PASS | Updated, marked `(11/17, U8)`; §4.10 and §12.5 too. |
| S1.5 frame-time measurement | PASS with a reporting defect | No spike over 33 ms in 6 QA captures. "Before" was never measured and the reported Survivor numbers are the better of two captures (F6). |
| S2.1 swipe 0.9-1.3 s, readable | PARTIAL | 1.21 s, 0.12 s hold, strike 0.24 s. Not chosen from frame captures as the brief required; see F3. |
| S2.2 Killer view kick + trauma shake | PASS | Peak shake 0.06 m x 0.5^2 = 1.5 cm, roll 0.6 degrees, 0.34 s. Off in Reduced. |
| S2.3 no server change | PASS | No file under `Server/` or `Shared/` changed. |
| S3.1 root cause | PASS | Confirmed on main: a capture triggered only the cue sound, the banner, the stutter and the kick. No effect was ever spawned. |
| S3.2 impact visible from all three views | PASS / see F2 | Own Survivor: `qa/survivor/u8-capture-1-burst.png`. Other Survivors: 4 `[fx]` plays for other Survivors' captures in the log. Killer: `qa/killer/u8-killer-capture-0-strike.png`. 0.6 s. Procedural (no Hovl hit prefab exists; accepted). |
| S3.3 pooled / prewarmed | PASS | Pool of 3 built in `Warm` at match start; `Play` never instantiates. First capture: max 30.3 ms. |
| S3.4 masking | PASS | The Killer client plays only at `scene.Views` entry with the `CaptureConfirm` handle, a tracker instance built from received entries. `CharacterViews.Impact` is never raised on a Killer client. No new packet field. |

## Findings

### F1 (major, test integrity; no runtime leak): the dev-log audit test is passed by renaming, not by updating the test

- **Where:** `Client/Assets/Scripts/Dev/DevCapture.cs:105` and `:462`; test `RulesReadoutQaTests.TheDevLogOnlyEverPrintsTheLocalPlayersPosition` (`Tests/EditMode/QA/RulesReadoutQaTests.cs:228`).
- **What the test asserts:** in `DevCapture.cs`, the first literal `player.X` comes after `if (player.Id != localId) continue;`. Its message: "no player coordinate is read before the local-player guard".
- **Actual:** the new seek mode reads the Killer entry's X and Z **before** that guard, through `SeekPoint(PlayerState killer) => new Vector3(killer.X, 0, killer.Z)`. The parameter is named `killer`, so the literal `player.X` never appears and the test stays green while the invariant it states is no longer true.
- **Is the behaviour itself legitimate? Yes.**
  - A Survivor client legitimately receives the Killer entry (AGENTS §1: Survivors receive full state; the server sends the entry with line of sight). Nothing is sent anywhere: the server still sees only `MoveInput`.
  - The position is never logged. QA run: 31 `chase killer` lines, all `from=<own position> yaw=...`, no `to=`. Source: the `to=` part is dropped when `seek` is true.
  - It is guarded like the other dev args: `-ptk-chase` is parsed by `DevArgs`, and `DevCapture` exists only when `devArgs.Active`. It is in every build, as all `-ptk-*` flags are. It only steers the player's own Survivor into the Killer, so it is not an advantage.
  - The Killer-side audits (`DevPathAuditQaTests`) are unaffected: the read is behind `role != Role.Survivor) return;`.
- **Why it is still a finding:** the test's title intent (only the local position is printed) holds, but its mechanism was bypassed with a helper whose stated second purpose is to keep the check passing. The implementation report does not mention it. A later change could log `killerEntry` and no test would notice.
- **Expected:** change the test openly, in this unit, and list it under "changed tests":
  1. Read the Killer entry inline or keep the helper, but assert the new rule: the only coordinate read before the local-player guard is the Killer-role entry, into `killerEntry`.
  2. Assert `killerEntry` is used only as the argument of `Chase(...)` and that no `Debug.Log` expression contains `killerEntry`, `goal` or `target` when `seek` is true (the `to=` branch).
  3. Say so in the report.

### F2 (major, visual): the impact burst is far too large in the Killer's first-person view

- **Repro:** Killer run, any capture. `docs/qa/U8-screens/qa/killer/u8-killer-capture-0-strike.png`, also the implementer's `killer2/u8-killer-capture-0-strike.png`.
- **Actual:** the capture happens about 1 m from the eye. Streaks are about 30 px wide and 250-350 px long; one fragment in the implementer's shot is about 160 x 90 px, next to the skill panel. They read as flat red rectangles stuck on the screen, not as an impact, and can cover the reticle label and HUD panels.
- **Expected:** a burst that reads at the Survivor's body and stays clear of the HUD. Survivor third-person view at 3 m is fine (`qa/survivor/u8-capture-1-burst.png`).
- **Concrete change (Killer client only; keep the Survivor values):**
  - streaks `startSize` 0.035-0.07 -> 0.012-0.025, `startSpeed` 3.5-7 -> 1.5-3, `velocityScale` 0.06 -> 0.03;
  - fragments `startSize` 0.09-0.16 -> 0.03-0.06, `startSpeed` 1.2-2.8 -> 0.6-1.4;
  - set `ParticleSystemRenderer.maxParticleSize` to 0.04 (default 0.5) on both systems as a hard cap;
  - target on screen at 720p: streaks at most 8 px wide and 100 px long, fragments at most 30 px.

### F3 (major, spec S2.1): the swipe timing was not chosen from frame captures, and the wind-up now plays faster than the clip

- **Brief:** "Choose with frame captures, not by guessing." The report says no swipe frame-capture probe was run.
- **Numbers from the code (`MotionTiming.SwipeRealKeys` / `SwipeClipKeys`):**
  - wind-up: 0.81 s of clip in 0.55 s real = **1.47x** speed (the brief's option was to play the wind-up slower);
  - strike: 0.18 s of clip in 0.24 s = 0.75x; hold 0.12 s; follow-through 0.36 s of clip in 0.30 s = 1.2x.
  - Total on screen 1.21 s, against 1.20 s before. The user's complaint was "far too fast"; apart from the 8 Hz hold being gone, the only slower part is 0.24 s long.
- **Strike lands after the victim reacts.** The strike pose is at +0.79 s after the capture (old build: about +0.84 s). The victim's flinch starts at +0 s and the knock-down at +0.3 s; the impact burst, flash and hit-stop are all at +0 s. In `qa/survivor/u8-capture-1-burst.png` (+0.15 s) the Killer's arms are still spread in the wind-up while the burst is already out; in `u8-capture-2-after.png` (+0.8 s) the Survivor is already going down. So the hit-stop freezes the frame before the swing starts, not the contact. Pre-existing in part, but this unit was meant to make the strike read as the cause.
- **Clip keys are unverified.** Where the strike sits in the clip (0.99 s) comes from the U4j sheets, not from this import (frames 9-49). The imported clip is 40 frames = 1.333 s while `SwipeClipSeconds` is 1.35 s (Mathf.Round(40.5) = 40); a 1.2 % scale error, harmless.
- **Expected for attempt 2:**
  1. Run `-ptk-preview-motion swipe` with shots every 1/30 s and choose the keys from them; attach the sheet.
  2. Put contact close to the capture: wind-up 0.15-0.20 s (fast anticipation), strike reaching the contact pose at about +0.25 s, hold 0.12 s, and spend the rest (about 0.6 s) on a slow follow-through. The swing still lasts 0.9-1.3 s.
  3. Delay the victim's presentation to the contact: start the impact burst, the flash and the hit-stop at the strike time (about +0.25 s), not at +0 s. Presentation only; no server timing changes.

### F4 (minor, readability / accessibility): the red flash is stronger on screen than its 38 % figure

- **Repro:** `docs/qa/U8-screens/qa/survivor/u8-capture-0-hit.png`.
- **Actual:** at the hit frame the whole view is red; the Killer model (also red) almost disappears and the world is unreadable for the first frames. The UI blends in linear space over a dark scene, so 38 % alpha looks like roughly 60 %. It coincides with the 100 ms hit-stop, so the frozen frame is the red one.
- **Expected:** a clear flash that still shows who hit you. One flash per capture is fine for photosensitivity (well under 3 per second).
- **Concrete change:** `FlashPeak` 0.38 -> 0.20, keep 0.15 s. Better: draw the flash with the existing vignette texture (edges 0.35, centre 0) so the centre of the view stays clear.
- Related: one of the three tear bars uses the `ally` cyan. That colour means "ally" everywhere else (SLOP V7). Use `danger` and a neutral grey.

### F5 (minor, feel): the world jumps 100 ms when the hit-stop ends

- **Where:** `ClientApp.cs:499-502`. `Present` returns the frozen time, then the real time.
- **Actual:** at the end of the stop the next `world.Render` is 100 ms + one frame later, so every other character moves 100 ms of travel in one frame (about 0.4 m for a running character) and `CharacterViews.Render` gets one `dt` of about 0.11 s. Not visible in stills; it can read as a small hitch, which is the user's original complaint.
- **Expected:** resume without a jump. For example, ease the presentation time back to real time over 0.15 s (`shown = from + (now - until) * k` catch-up), or keep a fixed offset that decays.
- Not measured live (no video capture is allowed here); from the code only.

### F6 (minor, reporting): the frame-time numbers in the implementer report are the favourable capture

- **Implementer log `docs/qa/U8-runs/survivor.log`:** first own capture (line 201) has before max 76.3 ms, after max 90.3 ms, 18 frames over 33 ms. Second capture (line 385): before 39.6 ms, after 31.7 ms. The report gives only the second and says "No spike follows a capture".
- **QA judgement:** the claim is still credible. That first sample ran at a mean of 20 ms with spikes spread evenly before and after the capture, which is machine load from parallel units, not the capture. QA's own runs on a quiet machine:

| Run | Capture | before mean / max (ms) | after mean / max (ms) | > 33 ms |
|---|---|---|---|---|
| Survivor | own 1 | 6.2 / 30.3 | 6.8 / 30.3 | 0 |
| Survivor | own 2 | 6.8 / 12.7 | 6.2 / 12.7 | 0 |
| Killer | 1 | 6.2 / 30.4 | 6.3 / 24.2 | 0 |
| Killer | 2 | 6.1 / 6.1 | 6.2 / 18.2 | 0 |
| Killer | 3 | 6.2 / 12.1 | 6.3 / 12.1 | 0 |
| Killer | 4 | 6.1 / 6.1 | 6.3 / 12.1 | 0 |

- The 30 ms frames are at the first capture of each run (first use), under the 33 ms limit. The old build was never measured, so there is no before/after for the original hitch; only the first-pass regression (106-203 ms from about 77k scanline quads) and its fix are documented.
- **Expected:** report every sample, and say which ones were taken under load.

### F7 (minor): `[fx] capture impact at x,z` is logged in every build

- `ClientApp.PlayImpact` calls `Debug.Log` without a dev guard. On the Killer client this writes a tracker-instance position to `Player.log`. It is a position that client already draws, so it is not a masking leak, but earlier audits kept Survivor coordinates out of Killer-client logs. Guard it with `dev != null` or drop the coordinates.

### F8 (note): steady look and scanlines

- The steady look is readable at 1280x720 (`qa/survivor/lagging-stutter.png`): Killer, Survivor and HUD are clear. It is dark on an already dark map; if playtests find it too dark, lower `VignetteAlpha` 0.6 -> 0.45.
- The scanline texture is 240 rows stretched with point filtering: even at 720p (3 px), uneven at 1080p (4.5 px). Build it for `Screen.height / 3` rows if banding shows.

## Files outside the owned list

| File | Change | Judgement |
|---|---|---|
| `Killer/KillerWorldLayer.cs` | stores the `CaptureConfirm` handle, adds `CapturePoint` | Justified: it is the only masked source of the position. Safe. |
| `Dev/DevCapture.cs` | Survivor seek, capture shots | Allowed by the brief (dev-only flag). See F1. |
| `UI/SurvivorFeedback.cs` | `StutterClock` removed | Justified: dead after S1. No other reference; build and tests green. |
| `Editor/Setup/MotionSetup.cs` | Swipe state speed 0, clip length constant | In scope (swipe import and animator settings). Only the `Swipe` state on layer 1 changed in `Motion.controller`; base-layer poses are untouched. Driving the state with `animator.Play(hash, 1, normalizedTime)` each frame is sound and keeps the swipe on the presentation clock, so it also freezes in the hit-stop. |

## SLOP §9 score (capture and Lagging presentation)

| Check | Points |
|---|---|
| No V1-V12 tells (gates V1, V2, V4, V8, V10 clear; the glitch look is tied to one state, not "on everything") | 15 / 20 (cyan tear bar, V7; flat oversized rectangles in the Killer view) |
| Layout | 15 / 15 |
| Copy | 15 / 15 (no new player-facing copy) |
| Every element answers "what state, what action" | 12 / 15 (the flash hides the attacker at the hit frame) |
| Role HUDs, no forbidden leak | 20 / 20 |
| Interactive states | 10 / 10 (no new control) |
| Contrast, readable at 720p | 4 / 5 |
| **Total** | **91 / 100, no gate failure** |

## Not verified

- Reduced motion in a live run; swipe readability frame by frame; a human playtest; resolutions other than 1280x720.
- QA shots: `docs/qa/U8-screens/qa/{survivor,killer}/`. Logs: `docs/qa/U8-runs/qa/`.
