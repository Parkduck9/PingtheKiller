quota fallback: Codex out of credits → Claude Opus implements

# Q6-survivor-menu — implementation attempt 1

**Changed:** new `UI/PlayMenu.cs` (menu, gate, copy) and `PlayMenuTests.cs` (11 tests). `KillerHud` hosts a Stacked `PlayMenu` (same API and classes). `SurvivorHud`: Row menu in the prompt slot, prompt/hold gate, `ClusterRect`. `ClientApp`: `SetMenu` for both roles, Esc/Start/B, click re-lock blocked under the menu, gate for look/WASD/E/Q/Space, `ResetMenu` on start/result/leave, line-3 feed. `ClientScreens` (`OpenPlayMenu`, Settings focus, dev seams), `RoleCamera.Look(bool, Vector2)`, USS block, `DevArgs`/`DevCapture` (`-ptk-menu-leave`), `DevArgsTests`, HUD_SPEC §5.

**Changed test assertion:** `CameraMovementQaTests.TheKillerPlayMenu…` now pins `PlayMenuGate.Stop(...)` and `menuGate.Active(...)` (brief spec 4: the gate moves into a pure class).

**Copy:** the v1 line 3 is false under GDD v2 (3rd capture eliminates). Now: count 1 → `Captured once: the next capture leaves 30 s for a rescue.`; count ≥ 2 → `Captured twice: the next capture eliminates you.`; Normal only.

**Verified:** EditMode 1398/1398, 0 `error CS`. BuildWindows succeeded. Offline exe: Survivor 720p + 1080p (`-ptk-menu 5 -ptk-settings -ptk-menu-leave`), Killer 720p; every PNG viewed; leave logged `cursor lock=None visible=True`; leave run repeated 3×, all passed. Files in `docs/qa/q6-runs/`.

**Unverified:** line 3 on screen (needs a capture; unit-tested only); real gamepad/keyboard input.

**Outside the brief:** the worktree had LFS pointer files (15 art/font tests failed, broken font). I copied the 852 LFS files from the main checkout after a sha256 check against each pointer oid. Content diff is empty, but `git status` lists them until `git update-index --refresh`. Unity rewrote 20 assets to LF; I restored CRLF.

**Pre-existing, not fixed:** the Settings Reduced-motion note overflows the panel.
