# U4c implementation, attempt 1 (piece B)

## What changed (Client/)
- Cameras: `SurvivorOrbitRig` (orbit, sphere-cast boom, 150 ms ease-out, dither < 1 m, 720°/s facing), `KillerViewRig` (eye 1.7 m, 90° h-FOV), `RoleCamera` facade, `ViewPose`, `MapGeometry`.
- Killer: `KillerScene` (pure, on piece A's tracker), `EdgePlacement`, `HeldClass`, `KillerWorldLayer`, `KillerAudio`, `KillerCues`, `KillerHudCopy`, `ExecutionGuidance`; UI `KillerHud`, `MarkerLayer`, `SkillRing`; outline and dither shaders.
- `SelfState` seam (`Available=false`, `// U2c: feed from S2C_SelfState`), `CharacterLook`, `WorldView`, `SnapshotBuffer`, `MovementInput` (facing yaw), `ClientApp`, `ClientScreens` (scale [0.8, 1.0], S floor 12 px), `ClientUI.uss`, `DevArgs`/`DevCapture` (`-ptk-orbit`, `-ptk-killer-look`, `-ptk-menu`).

## Tests
Unity EditMode: 758 total, 753 passed, 5 failed (all `PanelScaleQaTests`, obsolete by design: max scale 2.0 → 1.0). New: CameraRig 9, EdgePlacement 9, KillerScene 6 (incl. anonymity), KillerHud 11, DevArgs 41, PanelScale 9; piece A 39 + 43 pass. 0 `error CS`; BuildWindows succeeded.

## Screenshots (`docs/qa/U4c-screens/`, each `-720` and `-1080`)
survivor-3p, survivor-behind, killer-inview, killer-occluded, killer-offscreen, killer-intro, killer-menu.

## Live session (main server, fresh server per client, `--bind 127.0.0.1`)
- Killer: `t=5.00 local position 7.66,18.00 moved=13.27 m`; `t=11.76 killer look Nearest yaw=82.82 from=10.95,21.12 blip=49.03,25.92`; `event killer-inview/occluded/offscreen` at 0.14/1.92/4.29 s.
- Survivor: `t=5.01 local position 50.00,59.60 moved=9.60 m`.
- 0 exceptions in either player log. A reused server keeps its bot match running after a client leaves, hence one server per run.

## Deviations from HUD_SPEC
- Inter instead of Pretendard (not in the project).
- Dither is an 8×8 Bayer 40% discard, lit by main light + SH only (Forward+ safety).
- Boom sphere cast also hits the floor.
- Extra dev flag `-ptk-menu <s>` for the play-menu shot.
- Capsule plus visor stands in for the character model.
- Overlapping marker tags are not de-cluttered (no spec rule).
- Breath variant swap is fade-out/fade-in on one source.
- Hacking-facing is visual only (not sent in MoveInput).
- A completed terminal stays teal in the killer view (QA lighting test binds the colour).

## Pending until U2c
Cooldown ring and `skill_ready` cue, execution guidance prompt, rescue/invulnerability readouts (all read `ISelfState`, now unavailable).

## Needs a human
Camera feel, mouse sensitivity, audio mix, dither look near walls (not reached in captures).
