# integ-1117 minors: implementation report

**M3.** `InteractionMap.Resolve` takes `byte chase = 0` and returns None when it is non-zero (so props hide). `CharacterViews.Avatar` now stores `Chase` and passes it. Test: `AnyChaseFlagGivesTheArmsToTheChaseOverlay...` (InteractionMotionTests).

**M4.** `KillerHud.Stunned` is set by `ChasePropsLayer.ApplyKillerHud` from `stun.Showing`. While it is set, the skill block shows the existing Disabled ring with the text `Stunned` (`KillerHudCopy.SkillState(..., stunned)`, new `KillerHud.RingState`). Passive stays Passive. Test: `AStunnedKillerSeesTheSkillBlockUnavailable`.

**M5.** `CaptureImpactFx.Killer` now uses `SkillFx.Danger` (229, 72, 77). `LaggingStutter` already used danger. No test pinned the old value. The new test pins the new one.

**M1.** `DevCapture` chase shots (shutter Down and Broken, stun, vault) need the prop within 12 m of the own position and on screen. `ClientApp` passes the on-screen predicate in `Initialize`, because `DevCapture` may not mention "Camera" (WanderQaTests). The shots wait 0.25 s of unscaled time (new `pendingAt` queue). Entry reads stay inside `ChasePlayerShots`. Both audit tests pass.

**Gates.**
- EditMode: 1552 passed, 0 failed, 2 skipped (baseline 1550 plus 2 new).
- BuildWindows: Success.
- Live Killer run, 150 s: 0 exceptions.

**Unverified.** No chase event shot fired in the live run (no on-screen shutter event), so the M1 gating and delay were not seen working live. No stunned-Killer HUD screenshot. Not committed.
