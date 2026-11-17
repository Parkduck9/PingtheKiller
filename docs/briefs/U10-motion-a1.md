# Brief U10-motion: interaction motions that fit the network theme (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Time box:** about 80 minutes. Other units are running in parallel.
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/u10-motion`, branch `claude/u10-motion`, from main 3675877.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/q3-skill-reject/Client/Library` into `Client/Library`. Skip any `ArtifactDB-lock`.
- **Hard rules:**
  - Never commit, push, merge or download. This includes Mixamo and Asset Store: use only the clips already in the project.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
- **Skills:** first read the project skills `unity-animation`, `game-feel` and `unity-lighting-vfx` under `.claude/skills/`. Say which advice you applied.
- Also read `AGENTS.md`, `docs/ui/SLOP_DEFINITION.md`, and `docs/ui/HUD_SPEC.md` §4.10 (U4j character motion rules: motion comes only from fields the client receives, and reveals nothing the HUD does not).

## User report (Korean, translated)

"Our game has a packet / network feel now. But when repairing, I mean hacking a terminal, the motion doesn't play. Honestly a different motion would be better, one that fits our game's concept. The animations are a bit meh."

## Context

- `Client/Assets/Scripts/World/CharacterMotion.cs`:
  - pose `Working` = `ButtonPushing` (Mixamo) when the `Hacking` flag is set and the measured speed < `StillSpeed`;
  - plus a right-hand IK reach (`ButtonDrop`/`ButtonFloor`).
- `Editor/Setup/MotionSetup.cs` builds the Animator controller.
- **Clips available:**
  - `Client/Assets/Art/Characters/Mixamo/`: ButtonPushing, Crawling, FallingBackDeath, GettingUp, HitReaction, InjuredIdle, InjuredWalk, KillerSwipe, KnockedDown;
  - `ThirdParty/Kevin Iglesias/Human Animations` (Idles, Movement incl. Jump, Social/Conversation/Talk01).
- **Parallel units:**
  - U8-hitfeel changes the **swipe** parts of `CharacterMotion.cs` and `MotionSetup.cs`: `SwipeFromSeconds`/`SwipeSeconds`, a Swipe state speed of 0 driven by clip time, and the `KillerAttackKick` file. **Do not touch the swipe or kick code.** Keep your edits in those two files limited to the Working/interaction states, and put new logic in new files, so the merge stays easy.
  - Q5b adds `World/ChaseActionPose.cs`, a procedural pose overlay for pull, vault and break. Do not touch it.

## Spec

### S1. Find out why the hacking motion does not show, and fix it

Check every plausible cause and name the real one:
- the own character versus other Survivors;
- the `Hacking` flag on the own entry;
- the speed threshold while standing at a terminal (server-side slide?);
- the IK reach failing;
- the state not in the controller;
- an Avatar mask problem;
- the clip being too subtle at camera distance.

Prove the fix with live shots.

### S2. Redesign the interaction motions for the network theme

The interactions are:
- **hack** a terminal;
- **hold** an exit switch;
- **restore** (rescue) a Lagging ally;
- the Killer **jamming** a terminal.

For each one, give the motion a readable, theme-fitting silhouette, built from:

1. **A body motion** from the existing clips, plus procedural layers (IK, additive offsets).
   - **Hack:** both hands raised to chest height in front, fingers working a floating interface. Use ButtonPushing or Talk01 gestures with both-hand IK targets on a virtual panel, and small rhythmic hand offsets. The silhouette must read as "typing on a hologram", not "pushing one button".
   - **Exit switch hold:** one palm pressed on the switch panel; the body leans in.
   - **Restore an ally:** crouch or lean over the ally, both hands extended down toward them.
   - **Killer jam:** a forceful two-hand slam onto the terminal, repeated.
   Pick what looks best from the clips you have, and state your choice.
2. **A holographic prop and effect** (generated only, from existing materials and particles):
   - a translucent cyan panel or keyboard grid floating in front of the hands while hacking;
   - a thin data stream of small packet squares flowing from the terminal into the panel;
   - a soft pulse on each "keystroke" beat.
   Restore uses a green link line between the two characters. Jam uses a red glitch burst on each slam. Use the HUD_SPEC colour tokens: one meaning per colour (SLOP). Check that the colours do not clash with U9's skill effects (U9 is redoing the skill effects in parallel). Use a distinct shape language here: panels and streams, not rings.
3. **Masking and safety.**
   - Motion and props are driven only by the received entries' flags (`Hacking`, `Rescuing`, and the switch-holding state, if it is exposed; check what exists).
   - The Killer sees them only for instances it already receives.
   - Nothing reveals information the HUD does not.
4. **Performance:** the props are pooled per character, so nothing is instantiated per frame.

### S3. Spec

Update HUD_SPEC §4.10 motion rules, marked `(11/17, U10)`.

## Tests

EditMode tests for:
- the flag → interaction pose mapping, including the S1 bug as a regression test;
- the prop pool;
- the colour tokens used;
- the Killer view showing the props only on received instances.

## Gates

```
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -quit -projectPath Client -executeMethod <MotionSetup method, check the name> -logFile <log>   (if you regenerate the controller)
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <ABSOLUTE xml> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
Client/Builds/Win64/PingTheKiller.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role survivor -ptk-dev-hack 120 -ptk-shots <abs dir> -ptk-quit-after 200 -logFile <abs log>
```

- **Dev flags:** check `Client/Assets/Scripts/Dev` for the hack, rescue and preview flags. `MotionPreviewClip` exists for editor previews.
- **Pass criteria:**
  - All EditMode tests pass. Main had 1486.
  - BuildWindows succeeds.
  - `dotnet build -warnaserror` stays green.
- **Shots** at 1280x720:
  - the own character hacking, from the third-person camera;
  - a bot hacking, seen by another Survivor;
  - a restore;
  - an exit switch hold, if reachable in a run;
  - a Killer jam.
  Editor preview shots are fine where a live situation cannot be forced; label them.
- **Paths:** shots go in `docs/qa/U10-screens/`, logs in `docs/qa/U10-runs/`.
- **After each Unity run:** revert files that changed only in line endings.

## Owned files

- The interaction parts of `World/CharacterMotion.cs` and `Editor/Setup/MotionSetup.cs`.
- New files `World/InteractionMotion*.cs` and `World/HoloPropFx*.cs`.
- Generated assets in a new folder `Client/Assets/Art/Holo/`.
- Client tests.
- `docs/ui/HUD_SPEC.md` §4.10.
- `docs/qa/U10-*`.

## Report

Write `docs/qa/U10-motion-impl-a1.md` in English, ≤ 300 words. Cover:
- the S1 root cause;
- the motion choices per interaction;
- the props and effects;
- the safety check;
- the tests;
- the gates with counts;
- the shot paths;
- what is unverified;
- the risks.

Also write `docs/qa/U10-motion-commit-message.txt`.
