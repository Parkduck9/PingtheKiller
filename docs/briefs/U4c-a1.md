# Brief U4c — attempt 1 (piece B, main): new cameras, Killer first-person world layer and HUD, SelfState seam

Implementer: Claude Opus subagent (quota switch from Codex). QA: Claude Sonnet.
Branch `claude/u4c-cameras` (from main `6495a7d`). Do not commit.

## Read
- `AGENTS.md`
- `docs/GDD.md` §2
- **`docs/ui/HUD_SPEC.md`**, which is binding. Read in full: §0, §3 (Killer intro), §4.10, all of §6, §8 and §11/§11.1.
- `docs/ui/SLOP_DEFINITION.md`
- `docs/briefs/U2c-a1.md`: the server contract running in parallel (anonymous killer view, `S2C_SelfState`, killer Progress 0/100)
- the current client, `Client/Assets/Scripts/**` (U4 and U4b)

**Piece A** (`docs/briefs/U4c-a1-tracker.md`) is built in parallel by another implementer. It covers `BlipTracker`, `BlipSoundModel` and `SurvivorSoundSynth` under `Client/Assets/Scripts/Killer/`, plus their tests. Do **not** create those files. Code against the API in that brief. The orchestrator will drop piece A's files into this worktree when they are ready and tell you. Until then, keep your integration behind the API, or stub nothing and integrate last.

## Owned files
- `Client/**`, except:
  - piece A's files listed above;
  - `Client/Assets/Tests/EditMode/QA/**` (QA-owned).
- `docs/qa/U4c-impl-a1.md`
- `docs/qa/U4c-screens/*.png`

Never edit `Server.Core/`, `Server/`, `Shared/`, `Tools/` or `PingTheKiller.sln`.

## Scope (HUD_SPEC numbers are 🔶 starting values; use them exactly)

### 1. Survivor third-person orbit camera (§4.10)
- **Rig:**
  - look target 0.7 m above the character;
  - boom 4.0 m;
  - yaw free 360° (mouse or right stick only);
  - pitch −10…+60°, default +20°;
  - vertical FOV 50° (Hor+).
- **Movement:**
  - Movement is relative to camera yaw.
  - The character turns toward the movement direction at 720°/s.
  - `C2S_MoveInput.Yaw` = the character's facing.
  - Standing still keeps the facing; orbiting never turns the character and never sends input.
  - While own `Hacking` is set, the character faces the terminal.
- **Wall pull-in:**
  - Cast a 0.2 m sphere from the look target toward the camera against the static `FixedMap` walls. This is analytic (sphere vs AABB): the world has no colliders.
  - The boom shortens at once and eases out over 150 ms.
  - Below 1.0 m, draw the own character with a 40 % screen-door dither. If that is not feasible without a custom shader, use a 60 %-alpha fade and report it as a deviation.
- **Characters:**
  - No reticle.
  - The own character has no Signal tint.
  - Other Survivors are drawn as characters with no outline.
  - The Killer is never drawn on a Survivor client.

### 2. Killer first-person camera (§6.0)
- Eye height 1.7 m.
- Yaw free; pitch ±70°.
- Horizontal FOV 90° at 16:9 (vertical 59°, Hor+).
- `MoveInput.Yaw` = view yaw.
- No body, no hands, no crosshair.

### 3. Killer world layer (§6.2) on top of piece A's tracker (§6.3)
- **Killer stream:**
  - The Killer stream **never reads** Survivor `Id`, `Yaw`, `StatusTimer`, `CaptureCount`, or the `Hacking`, `Rescuing`, `Stealthed` and `Afterimage` flags.
  - Find the Killer's own entry by `Role == Killer`.
  - Feed every other entry into `BlipTracker.Accept`.
  - Render each `Blip` tweened Prev → current over one tick (50 ms).
- **Survivor stream:** keeps the U4/U4b Id-based `SnapshotBuffer` interpolation; survivors receive real Ids.
- **Classification** per blip, with 100 ms hysteresis:
  - in view, occluded or off-screen;
  - occlusion = a ray from the camera to the blip at 1.2 m height against full-height static walls.
- **Rendering per class**, exactly as the §6.2 table:
  - **In view:** the shared Survivor model (one neutral material, no per-player look) with a 1 u `signal` outline. An inverted-hull outline is acceptable. Lagging gets the 8 Hz stutter pose and a `Lagging` tag. Beyond 20 m, add a distance tag.
  - **Occluded:** a 16 u `signal` diamond drawn on top of walls, plus the distance tag. Lagging adds a ring.
  - **Off-screen:** the 6 nearest get edge chevrons. Voiced instances (piece A `IsVoiced`) are chosen first.
  - All marker shapes carry a 1 u `bg` keyline.
- **Edge-indicator placement:** the exact §6.2 algorithm, steps 1–6, including behind-camera handling and the keep-out of the cluster expanded by 8 u. Write it as a pure, tested function.
- **Terminal markers:**
  - square outline plus `Terminal N`;
  - **no percentage**;
  - treat Progress 100 as done and anything else as not done (U2c quantises to 0/100);
  - done = no marker;
  - in view and within 8 m: tag only.
- **Exit marker** at 2.5 m: Locked dashed `muted` / Open solid `text` / Locked by the Killer, with a bar and `Exit Locked N s` (seconds from the own FirewallBlock `SkillEffect.Duration`).
- **Draw order** per §6.2.

### 4. Killer HUD, bottom-left cluster (§6.1), intro (§3) and play menu (§6.6)
- **Info block:**
  - `m:ss`, with a `text` bar in the last 60 s;
  - `Terminals k of N`;
  - `One more opens the Exit.` when done = N − 1.
- **Effect line:** Overclock / Recovering / Slowed by traffic overload (danger bar), from `S2C_SkillEffect`.
- **Skill block:** ring, name and state line from §8 and §6.1d.
  - The cooldown comes from `S2C_SelfState.SkillCooldown` through the SelfState seam (item 6).
  - Until SelfState exists, show `Ready` / `Passive — always on` / `After the Exit opens` and no cooldown ring. Mark it pending.
  - Icons: simple 1 u line drawings per §8. Painter2D or VectorImage is fine.
- **Execution guidance (§6.4):** from the seam's `ExecutionGraceLeft` and the tracker's Lagging instances. Implement the armed/re-arm logic as a pure, tested class. With no SelfState, the prompt is hidden (pending U2c).
- **Intro:** the 3 s Killer intro block, which never blocks input.
- **Play menu:** Esc opens the §6.6 menu (Resume / Leave match). It sends one zero MoveInput, releases the cursor, and the match keeps running.
- **Geometry and type:** §6.1 geometry, 0 radius, §0.3 type sizes with the 720p S floor of 12 px. The font stays Inter: Pretendard is not in the project, so note it.
- **Panel scale:** `ClientScreens.PanelScale` is clamped to **[0.8, 1.0]** (§0.2 and Q15). This reverts U4b's 2.0.

### 5. Killer sounds
- **UI cues (§6.5):**
  - `blip_first`, `capture_confirm`, `execute_confirm` (from piece A's events);
  - `exit_open`, `skill_use`, `skill_ready`, `time_60`;
  - `match_start` (§3).
  - Short procedural placeholder tones are fine.
- **Positional sound (§6.5.1):**
  - Play piece A's `BlipSoundModel` output through pooled 3D `AudioSource`s. Use 4 footstep voices and 4 breathing loops, each moved to its instance's tweened position.
  - Apply `AudioLowPassFilter` at 1.5 kHz when occluded, cross-faded over 100 ms.
  - Pan by azimuth, with no Doppler (`dopplerLevel = 0`).
  - Duck world sound by 6 dB for 300 ms under every UI cue.
  - Fade out over 50 ms on despawn.
  - The Survivor client never plays Killer-related world sound.

### 6. SelfState seam
- Define `ISelfState { bool Available; float SkillCooldown; float RescueProgress; float InvulnerableLeft; float ExecutionGraceLeft; }` and a holder that is updated from packets.
- `S2C_SelfState` does not exist in Shared yet; it lands with U2c. Leave a single clearly marked adapter point: `// U2c: feed from S2C_SelfState`.
- Keep `Available = false` until then.
- Survivor-side readouts that U2c will feed (rescue progress, invulnerability) are **not** built in this unit beyond the seam.

### 7. Dev capture flags (extend DevArgs and its tests; inert when absent)
- `-ptk-orbit <deg>`: survivor camera yaw offset from the character's facing, applied once at match start + 2 s. Used for the "look behind" shot: 180.
- `-ptk-killer-look nearest|away|<deg>`: the killer view turns toward the nearest blip, away from it (180°), or to a fixed yaw. It re-aims every 0.5 s. Dev only.
- Event shots for the killer:
  - `killer-inview.png`: first frame with an in-view blip;
  - `killer-occluded.png`: first frame with an occluded marker;
  - `killer-offscreen.png`: first frame with an edge chevron.
- Survivor shots: `survivor-3p.png`, `survivor-behind.png`.

## Verification you run (as in U4b)
- Unity batch: 0 `error CS`, every EditMode test passes (QA suite included), `BuildWindows` succeeds. You own the single Unity process on this project.
- **Screenshots** at **1280×720 and 1920×1080**, in `docs/qa/U4c-screens/`:
  - survivor third-person;
  - survivor looking behind;
  - killer with an in-view Survivor (outline visible);
  - killer through-wall marker;
  - killer off-screen chevron;
  - killer cluster with the intro;
  - killer play menu.

  Use offline bot matches. Bots hack, so the Killer sees Signal blips.
- **Live session against the main server:**
  - `dotnet run --project Server -c Release -- --port 7777 --bots 6 --bind 127.0.0.1`;
  - one killer run and one survivor run;
  - `[DEV]` log lines confirm movement, blips tracked and no exceptions.
- **Anonymity:** the main server still sends real Ids. Your killer path must not care. Add an EditMode test that feeds the killer pipeline (`WorldView`, or a pure adapter around it) with WorldStates whose survivor entries have `Id = 0` and shuffled order, and asserts stable tracking and rendering counts.
- Stop every process you start. Never answer firewall dialogs.

## Isolation for later units
Keep the camera rigs (`SurvivorOrbitRig`, `KillerViewRig`) and the killer world layer in their own classes. `ClientApp` only wires them together.

## Report `docs/qa/U4c-impl-a1.md` (≤ 350 words)
- what changed
- test counts
- screenshot list
- live-session log lines
- deviations from HUD_SPEC with reasons
- pending until U2c (SelfState: cooldown ring, execution guidance, rescue/invulnerability readouts)
- what needs a human: camera feel, audio, sensitivity
