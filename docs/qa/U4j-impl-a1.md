# U4j implementation, attempt 1

**quota fallback: Codex out of credits**

**Files.**
- New: `Art/Characters/Mixamo/**` (10 FBX via LFS, plus `UpperBody.mask`), `Resources/Motion/Motion.controller`, `World/CharacterMotion.cs`, `World/KillerAttackKick.cs`, `Editor/Setup/MotionSetup.cs`, `Tests/EditMode/CharacterMotionTests.cs`.
- Edited: `CharacterViews.cs`, `KillerWorldLayer.cs` (lines added only), `DevArgs.cs` and `DevCapture.cs` (`-ptk-preview-motion`), and `HUD_SPEC` §4.10 and §6.3.

**Clip → trigger.**

| Trigger | Motion |
|---|---|
| Normal→Lagging | Killer: Swipe or Punch, alternating. Survivor: HitReaction, then KnockedDown, then Crawl (speed-matched) |
| Lagging→Normal | GettingUp, then locomotion |
| Eliminated | Death, held 6 s, then despawn |
| Hacking and still | ButtonPushing loop |

The 8 Hz Lagging step is kept.

**Killer view.**
- Inputs are Status and speed only. Flags and CaptureCount are zeroed. Decoys behave the same.
- Despawn is instant.
- The capture kick (FOV −4°, 0.12 m, 0.22 s) is off under reduced motion.

**Tests.** EditMode 1263/1263 green (24 new). Results: `u4j-runs/editmode.xml`.

**Build.** `BuildWindows` succeeded.

**Screenshots.** In `U4j-screens/`: `shot-*.png`, `sheet-*.png`, `exe/killer/motion-killer-down.png` and `exe/survivor/motion-knockdown.png`. All viewed. Grounding is within 3 mm and no sliding is visible.

**Gaps.**
- There is no switch-hold flag yet.
- The ButtonPushing hand sits at about 0.95 m, above the 0.72 m terminal top.
- The Killer-view Lagging tag floats above a crawling body.
- The knock-down moves the root about 0.9 m.
- `ReceivedKillerEntry` is flaky: it failed once, then passed.

**U4i conflicts.** All edits are additive. `CharacterArt*` and the materials are untouched. Grounding moves the Animator transform.

## Attempt 2

1. **Knock-down.**
   - The falls are now imported in place (centre of mass, XZ not baked).
   - The hips of down poses stay within 0.1 m. The crawl's shift is held once it settles, so its planted speed is still 0.477.
   - Fixed: a wrong `lost` check made every pose change a hard cut; changes now crossfade.
   - Test: drift 0.120 m, largest step 0.038 m per frame.
   - Sheets: `a1-` and `a2-transition-capture.png`.
2. **Tag.** Placed at the head bone + 0.25 m, at least 0.7 m. The survivor view has no such tag.
3. **Swipe only.**
   - The punch FBX is deleted.
   - The swipe is trimmed to 0.45–1.65 s of the take, so the override ends after 1.2 s.
4. **ButtonPushing.** `MotionReach` lowers the hand 0.30 m by right-hand IK, never below 0.74 m. Fingers measure 0.74–0.82 m.
5. **Kick.**
   - Base + offset is written in `LateUpdate`, with no coroutine.
   - `Restore()` runs on disable.
   - `KillerWorldLayer.OnDestroy` restores and destroys it.

**Results.** EditMode 1269/1269 (30 motion tests). `BuildWindows` succeeded.

**Screenshots.**
- `a2-*`, plus `exe/a2-{killer,survivor,swipe}` (all viewed).
- The `a1-*` editor renders were rebuilt with `PTK_MOTION_A1=1`; the originals had been overwritten.
