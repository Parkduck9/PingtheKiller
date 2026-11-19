# S3-credits implementation, attempt 2

Implementer: Claude Sonnet 5.5. Fixes for `docs/qa/S3-credits-qa-a1.md` (SLOP 75/100).

- **F-01:** `CreditsScreen` handles `NavigationMoveEvent` Up/Down (arrows, d-pad, stick): scroll 72 u, clamped; PageUp/PageDown scroll a page; Back keeps focus; wheel and bar unchanged. Shoulder buttons not added. New test `UpAndDownScrollTheBodyClampedAtBothEndsAndBackKeepsFocus` (real EditorWindow panel, layout forced by reflection, asserts both clamps; not inconclusive). How to play has no ScrollView (fixed body), so it has no such gap and was not changed.
- **F-02:** `ClientUI.uss` scopes a scroller style to `.cr-scroll` (the only ScrollView): 12 u dark tracker, muted dragger, light on hover/focus, no arrow buttons, no horizontal bar.
- **Minors:** `InterNotice` is now `Inter (font)` (OFL.txt untouched); OFL body 14 u; `ShowCredits` / `ShowHowTo` / Settings guard each other; text column 840 u (about 90 chars) inside an 880 u view; docs (HUD_SPEC 1.8) updated.
- **Gates:** EditMode 1628 total, 1626 passed, 0 failed, 2 skipped (old). BuildWindows OK. Offline run: no exceptions. `dotnet build -warnaserror` clean.
- **Shots:** `docs/qa/S3-credits/` `menu`, `credits-top`, `credits-licence`, `credits-end`.
- **Unverified:** real gamepad. Unity-touched assets reverted.
