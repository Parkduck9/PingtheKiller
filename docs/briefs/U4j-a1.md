# Brief U4j — attempt 1: Combat and state animations (Mixamo)

- **Implementer:** Claude Opus subagent, because of the quota fallback (Codex is out of credits).
- **QA:** Gemini Flash `-high`, visual and code.
- **Base:** branch `claude/u4h-world-art` (bcbb4bb).
- **Branch and worktree:** branch `claude/u4j-motion`, worktree `C:\Unity\MultiPlayGame\PtK-work\u4j`.
- **Parallel units:**
  - U4i (toon materials) edits `CharacterArt.cs`, `CharacterLook.cs`, `CharacterArtSetup.cs`, `WorldMaterials.cs`, `KillerWorldLayer.cs` line ~70 and all `.mat` files. **Do not edit those.**
  - U6e and U6a are server and map work.

Read these first:
- `AGENTS.md`
- `docs/ui/SLOP_DEFINITION.md`
- `docs/ui/HUD_SPEC.md` §4.10, §6.2 and **§6.3** (anonymous blips: facing and animation rules)
- `docs/GDD-v2.md` §4

Do **not** commit.

## Goal

The user says there is no attack motion and no hurt motion. Today characters only have Idle/Walk/Run locomotion (`Art/Characters/Locomotion.controller`, driven by `CharacterInstance.Animate`).

Add state and event animations from Mixamo clips that the user approved and the orchestrator downloaded. They are already in `C:\Unity\MultiPlayGame\downloads\mixamo-20261103\`: Y Bot rig, FBX 2019, without skin, 30 fps.

| File | Use |
|---|---|
| `Mixamo@KillerSwipe.fbx` | Killer attack on a capture (main) |
| `Mixamo@KillerPunch.fbx` | Killer attack, alternative (pick one, or alternate) |
| `Mixamo@HitReaction.fbx` | Survivor hit flinch; first part of the capture reaction |
| `Mixamo@KnockedDown.fbx` | Survivor goes down: Normal → Lagging |
| `Mixamo@Crawling.fbx` | Lagging survivor moving; in place |
| `Mixamo@InjuredIdle.fbx` / `Mixamo@InjuredWalk.fbx` | Optional: a Normal survivor with CaptureCount ≥ 1 (survivor view only, see the leak rules) |
| `Mixamo@FallingBackDeath.fbx` | Eliminated |
| `Mixamo@GettingUp.fbx` | Rescued: Lagging → Normal |
| `Mixamo@ButtonPushing.fbx` | Hacking or holding an exit switch, as a loop (survivor view only) |

Copy them into `Client/Assets/Art/Characters/Mixamo/`, which goes through Git LFS like the other FBX files (check `.gitattributes`). Import them as **Humanoid**:
- Use the Mixamo source avatar created from one clip, and copy it to the others.
- Set loop flags.
- Bake root motion into pose for the in-place clips.
- Retarget them to Rio (survivor) and Kyle (killer).

## Information rules (hard)

Animations must never leak what a role may not know.

1. **Killer view (§6.3):**
   - The Killer reads only `Role`, `Status`, `Flags.Signal`, `X`, `Z` of Survivor entries.
   - **Never** play a Hacking, ButtonPushing, Rescuing, injured or CaptureCount-based animation in the Killer view.
   - Decoys and afterimages must animate exactly like real Normal survivors.
   - Lagging survivors may crawl, and the knock-down may play on a Normal → Lagging transition of a tracked blip.
   - Follow §6.3 for despawns: an unmatched old blip still despawns instantly. Play the death clip only where the spec allows it, or not at all in the Killer view, and state which.
2. **Survivor view:** full state is allowed (hacking, rescue, injured), using the fields the survivor client already receives.
3. **Killer swipe:**
   - Survivors see it on the Killer model only while that model is drawn (line of sight, §4.10).
   - The local Killer player is first person: give them a short attack feedback, such as a ≤ 0.25 s camera FOV/position kick, in a **new** component. Do not edit `KillerViewRig.cs` or `RoleCamera.cs`.
   - Respect the reduced-motion flag: no kick when it is on.

## Triggers (client-side, from existing state)

- **Capture (a survivor goes Normal → Lagging):**
  - The killer model plays the swipe once.
  - The survivor plays HitReaction blended into KnockedDown.
  - The survivor then holds the down/crawl state while Lagging. Use crawl when moving and the last crawl pose (or a lying pose) when still.
- **Rescue (Lagging → Normal):** GettingUp, then locomotion.
- **Eliminated:** FallingBackDeath once, then hold the last pose until despawn (survivor view).
- **Hacking or switch hold (survivor view):** loop ButtonPushing while the flag is on and the speed is about 0.
- The existing Lagging 8 Hz stutter (`CharacterInstance.Animate`'s stepped clock) must still apply to Lagging animation.

## Owned files

- **New:**
  - `Client/Assets/Scripts/World/CharacterMotion.cs`: the trigger logic as a plain, testable state tracker, plus a thin Animator driver.
  - `Client/Assets/Scripts/World/KillerAttackKick.cs`, or a similar name.
  - `Client/Assets/Editor/Setup/MotionSetup.cs`: import settings and the controller builder. Make it idempotent.
  - `Client/Assets/Resources/Motion/Motion.controller`: a **new** controller with locomotion plus the new states. Assign it at runtime from `CharacterViews`, so `CharacterArt.asset` and `Locomotion.controller` stay untouched.
  - `Client/Assets/Art/Characters/Mixamo/**`
  - `Client/Assets/Tests/EditMode/CharacterMotionTests.cs`
- **Edit:**
  - `Client/Assets/Scripts/World/CharacterViews.cs`: the hook.
  - `Client/Assets/Scripts/Killer/KillerWorldLayer.cs`: **added calls only**; do not touch the material/fallback code near line 70.
  - `Dev/DevArgs.cs` and `DevCapture.cs`: add one flag, `-ptk-preview-motion <clip>`, for screenshots only.
  - `docs/ui/HUD_SPEC.md`: a short "Character motion" note in §4.10 and §6.3, covering what plays in which view.
- **Reports:** `docs/qa/U4j-impl-a1.md` and `docs/qa/U4j-screens/`.

## Acceptance

- **EditMode:** all green, 0 compile errors.
  - Use the command in `AGENTS.md` §3 with `-testResults "$PWD/docs/qa/u4j-runs/editmode.xml"`.
  - The first import is long. You may first copy the Library cache:
    `robocopy C:\Unity\MultiPlayGame\PtK-work\u4h\Client\Library C:\Unity\MultiPlayGame\PtK-work\u4j\Client\Library /E /NFL /NDL /NJH /NJS`
    This is read-only use of u4h.
- **New tests:**
  - the trigger state machine: every transition above, including no swipe on a status change that is not a capture;
  - Killer-view filtering: a hacking, rescuing or capture-count survivor in the Killer view gets only locomotion, crawl or knock-down; decoys match real survivors;
  - reduced motion disables the kick;
  - every clip imports as Humanoid with the expected loop flags.
- **Build:** `BuildScript.BuildWindows` succeeds.
- **Screenshots (look at each):** use `-ptk-preview-motion` or editor renders, at 720p:
  - mid-swipe;
  - mid-knock-down;
  - crawling;
  - getting up;
  - button pushing at a terminal;
  - death.
  - Also include one exe frame of a survivor down in the Killer view, if a dev flag can stage it.
  - Check that the feet and hands do not sink into the floor and the character does not slide.
- **No desktop input automation, ever:** no SendKeys, input simulation, AutoHotkey or window focusing. Earlier keystrokes leaked into the user's other windows.
  - Screenshots come only from `-ptk-*` DevCapture flags or EditMode/editor renders.
  - If a shot cannot be staged, skip it and report the gap.
- **Revert unintended re-serialized files** (DOTween, URP, ProjectSettings, `CharacterArt.asset`) with `git checkout --`.

## Report

`docs/qa/U4j-impl-a1.md`, at most 250 words. Start with the header note "quota fallback: Codex out of credits". Include:
- files;
- clip → trigger table;
- the Killer-view rules applied;
- tests;
- the build result;
- screenshots;
- what is unverified;
- the expected merge conflicts with U4i, if any.
