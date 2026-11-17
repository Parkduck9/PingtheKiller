# Brief U8-hitfeel: the capture moment feels laggy, the swipe is too fast, the hit effects are not visible (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Time box:** report within about 75 minutes. Three other units are running in parallel.
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/u8-hitfeel`, branch `claude/u8-hitfeel`, from main 3675877.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/q3-skill-reject/Client/Library` into `Client/Library`. Do not copy an `ArtifactDB-lock` if one exists.
- **Hard rules:**
  - Never commit, push, merge or download.
  - Never send keyboard or mouse input to the desktop. Launching an exe with flags is fine.
  - Never kill a process you did not start.
  - Leave Unity Hub and every Unity editor you did not launch alone. The user has editors open on `PingtheKiller/Client` and `C:\Unity\Number`.
  - Do not spawn subagents.
- **Skills:** before you design anything, read these project skills under `.claude/skills/`: `game-feel`, `performance-optimization`, `unity-performance`, `unity-lighting-vfx`, `unity-animation`. Say in the report which advice you applied.
- Read `AGENTS.md` and `docs/ui/SLOP_DEFINITION.md`.

## User report (Korean, translated)

"When you get hit there is a stuttering lag, the hitting motion is far too fast, and the effects can't be seen. These are big problems."

## What the orchestrator found

1. **The stutter is designed.** `Client/Assets/Scripts/World/LaggingStutter.cs` (HUD_SPEC §4.5) deliberately presents the world at **8 Hz** while the own Survivor is Lagging. It holds and blends frames. To a player this reads as the game lagging, and Lagging lasts 30-60 s.
   - There may also be a real one-off hitch at the capture moment: first-use instantiate, shader compile, or audio decompression of `survivorCues[SurvivorCue.Captured]`. Measure it; do not guess.
2. **The swipe.**
   - `World/CharacterMotion.cs:28-30` trims the Killer swipe on import. It starts at `SwipeFromSeconds = .45f`, which skips most of the wind-up, and lasts `SwipeSeconds = 1.2f`.
   - It plays after the server reports the capture, so the strike has no anticipation.
   - `World/KillerAttackKick.cs` is the Killer's own first-person feedback.
3. **The effects.** It is unknown why the hit effects are invisible. Find the cause:
   - layer or camera culling mask;
   - URP particle material or shader;
   - scale or lifetime;
   - spawn position inside the body or behind the camera;
   - sorting or alpha;
   - never spawned at all.
   Look at `World/SkillFx.cs`, `CharacterViews.cs` and how capture events are routed in `ClientApp.cs` (around lines 480-500).

## Spec

### S1. No fake lag

1. Remove the 8 Hz frame hold. The world always renders at full frame rate.
2. Replace it with a **capture impact** plus a **Lagging look**:
   - **Hit-stop on the capture:** about 80-120 ms, applied only to presentation (the world view and animators). Never `Time.timeScale` if that would affect network interpolation, input or audio. Check how time is used first.
   - **Glitch burst:** about 0.3-0.5 s. Examples: chromatic split, scanline tear, a brief colour flash in the Killer-red family.
   - **Steady Lagging look:** full rate, for as long as the status is Lagging. Examples: desaturation, vignette, faint scanlines. URP post-processing (Volume) or a UI overlay is fine. Pick the cheaper option that looks right at 1280x720.
3. Keep the **Reduced motion** setting and `-ptk-reduce-stutter`. In Reduced mode:
   - no burst, no tear and no flash;
   - the steady look only.
4. Update HUD_SPEC §4.5 to the new behaviour, marked `(11/17, U8)`. Keep "one indicator per state": the meter still says Lagging.
5. **Measure the capture moment:**
   - Log the frame time for ±1 s around a capture in the offline exe. A dev log line is fine.
   - Remove any spike over 33 ms. Examples: prewarm the capture FX and audio at match start, set the audio clip Load Type, warm up the shader.
   - Report the before and after numbers.

### S2. The swipe reads as a deliberate attack

1. Restore the anticipation. The visible swing must last about 0.9-1.3 s, and the strike pose must be clearly readable at 30 fps. Options:
   - start the clip earlier;
   - play the wind-up slower than the strike;
   - add a short hold on the strike pose.
   Choose with frame captures, not by guessing, and say what you chose.
2. **The Killer's own view (`KillerAttackKick`):** same idea. A short wind-up feel and a strike kick with a camera impulse. Use the trauma-based shake from the `game-feel` skill, kept small. Respect Reduced motion.
3. Do not change any server timing or rule. This is presentation only.

### S3. The hit effects are visible

1. Find and fix the root cause of the invisible effects. Report it in one sentence.
2. Add a clear **impact effect** at the captured Survivor:
   - Use a Hovl Studio hit or impact particle already in `Client/Assets/ThirdParty/Hovl Studio`, tinted to the Killer colour. Do not download anything.
   - It must be visible from the Survivor's own third-person camera, from other Survivors, and from the Killer view while the body is drawn.
   - Lifetime about 0.5-0.8 s.
3. Pool or prewarm the effect, so the first capture does not allocate a new system.
4. **Masking:** effects for a Silent Survivor must follow the existing rules. The Killer never sees anything at a position it would not otherwise receive. Check `KillerWorldLayer`.

## Tests

- EditMode tests for:
  - the new stutter/look state machine: full rate, burst timing, Reduced mode;
  - the hit-stop window;
  - the swipe timing constants;
  - FX pool reuse.
- Update the existing `LaggingStutter`/`StutterClock` tests that encoded the 8 Hz hold. List every test you change.

## Gates

```
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <ABSOLUTE xml> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
Client/Builds/Win64/PingTheKiller.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role <survivor|killer> -ptk-shots <abs dir> -ptk-quit-after 200 -logFile <abs log>
```

- **Pass criteria:**
  - All EditMode tests pass. Main had 1486.
  - BuildWindows succeeds.
  - `dotnet build PingTheKiller.sln -warnaserror` stays green.
- **Live runs:**
  - Do one Survivor run and one Killer run.
  - Find dev flags that force a capture of the human player early, and screenshot the capture moment: before, during and after the burst. If no flag exists, add a dev-only one, guarded like the existing `-ptk-*` dev args.
- **Paths:** shots go in `docs/qa/U8-screens/`, logs in `docs/qa/U8-runs/`.
- **After each Unity run:** revert files that changed only in line endings (`git diff --ignore-cr-at-eol --quiet`).

## Owned files

- `Client/Assets/Scripts/World/{LaggingStutter,CharacterMotion,KillerAttackKick,SkillFx,CharacterViews}.cs`, plus new client files you need.
- The capture section of `ClientApp.cs`.
- Client tests.
- Animator or import settings for the Killer swipe clip.
- URP Volume or profile assets, if you use them.
- `docs/ui/HUD_SPEC.md` §4.5 and §4.10 motion lines.
- `docs/qa/U8-*`.

Other units run in parallel:
- Q5 adds shutters and vault rails: new server, Shared and client files, plus small `ClientApp` hooks.
- The IP audit renames UI copy.

Keep `ClientApp.cs` edits minimal and local to the capture handling.

## Report

Write `docs/qa/U8-hitfeel-impl-a1.md` in English, ≤ 300 words. Cover:
- the root causes;
- what changed;
- the frame-time numbers before and after;
- the swing timing chosen;
- the FX root cause;
- the tests;
- the gates with counts;
- the shot paths;
- what is unverified;
- the risks.

Also write `docs/qa/U8-hitfeel-commit-message.txt`, without an attribution line.
