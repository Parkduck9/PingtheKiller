# S3-credits implementation, attempt 1

Implementer: Claude Sonnet 5.5. Branch `claude/s3-credits` (from `claude/store-prep`).

**Changed:** new `Scripts/UI/CreditsCopy.cs` (all copy, `Developer = "NIPAD"`, OFL loaded at runtime from `Resources/Fonts/OFL` TextAsset) and `CreditsScreen.cs` (scrolling panel, Back, Esc, gamepad B via NavigationCancel); `ClientScreens` gets the Credits button (`set-open`) and Show/Close with focus return; `HelpUI.uss` styles; `-ptk-credits` capture flag (`DevArgs`, `DevCapture`); tests `Tests/EditMode/CreditsTests.cs` (8, incl. active `ReleaseGate` developer-name test, no Ignore); docs `CREDITS.md` (screen exists, NIPAD, unity-mcp/Newtonsoft marked stripped by S2) and `HUD_SPEC.md` 1.8.

**Verified:** EditMode 1627 total, 1625 passed, 0 failed, 2 skipped (pre-existing). `dotnet build -warnaserror` clean. `BuildScript.BuildWindows` succeeded. Offline run (7 bots, survivor, 40 s) exited 0, no exceptions (`offline.log`). Screenshots in `docs/qa/S3-credits/`: `menu.png`, `credits-top.png`, `credits-licence.png`.

**Unverified:** gamepad B on real hardware (tested via NavigationCancelEvent only). **Risks:** the EscAndGamepadBClose test opens a temporary EditorWindow; Robot Kyle and Rio publishers remain unnamed per CREDITS.md. Unity-touched asset changes were reverted.
