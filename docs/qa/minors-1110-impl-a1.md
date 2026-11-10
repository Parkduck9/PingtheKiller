# minors-1110 implementation report (attempt 1, Sonnet)

**Changed:** `PlayMenu.cs` (F1), `TagLayout.cs` (F2), `HUD_SPEC.md` (section 5 line 396 marked 11/10; section 6 clearance note before "Draw order"), tests: `PlayMenuTests`, `PlayMenuQaTests`, `TagLayoutTests`, `TagLayoutQaTests`, new `TagLayoutClearanceTests.cs`.

**F1:** `CapturedTwice` renamed `NextCaptureEliminates` = "Next capture eliminates you." (count >= 2, Normal). Count 1 unchanged. Tests assert no capture line contains "twice".

**F2:** `TagGap` = 4 u (= `EdgeSpread.Gap`). `StepGap` = `TagGap`, so stacked and Slot/NextTo tags land exactly 4 u away. Own glyph keeps plain no-overlap (default offsets are 2 u from it, so tags are not pushed). `EdgePlacement.cs` untouched.
**Chevron decision:** a Survivor tag also avoids Edge chevrons (both are Survivor information, HUD_SPEC 6); previously `Priority <= tag` ignored them.
**Deviation:** `TheCrowdedRightEdgeStaysReadableAt720pAnd1080p` ("everything must fit") needs touching tags in the busiest edge. So an Overloaded/Exit tag with no slot at 4 u retries at 2 u (`MinTagGap`, as EdgeSpread does), then 0 u, before hiding. Other priorities are strict 4 u.

**Tests changed (2 u / 0 u encoded):** `TagLayoutQaTests` Step = height + TagGap, the displaced-tag test (4 u), and "only touch" test now `TagsExactlyTagGapApartDoNotMove`; `TagLayoutTests` message. New: touching Survivor tags, own-glyph offset, chevron clearance, Slot exact gap, two fuzz tests (strict for non-slotted; slotted allow fallback).

**Gates:** EditMode 1475/1475 passed (1468 + 7 new). BuildWindows succeeded. `dotnet build -warnaserror` 0 errors. Killer run: shots in `docs/qa/minors-1110-screens/` (killer-crowd, killer-inview, killer-offscreen); no touching tags visible. Logs: `docs/qa/minors-1110-runs/`.

**Unverified:** the exact qa-int collapse scene (38/39 m) was not reproduced live; the Killer exe was stopped by my 170 s timeout, not -ptk-quit-after.
**Risks:** Survivor tags near chevrons may stack more often; the 0 u fallback can still touch in extreme crowding.
