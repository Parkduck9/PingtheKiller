# Brief minors-1110: two follow-ups from the integration QA (attempt 1)

- **Implementer:** Claude Sonnet. **QA:** Fable (separate run, after you).
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/minors-1110`, branch `claude/minors-1110` (from main 20727da). LFS is checked out. `Client/Library` is not there yet: copy `C:/Unity/MultiPlayGame/PtK-work/qa-int/Client/Library` into it first (same tree, saves the import).
- **Hard rules:** never commit, push or merge; never download anything; never send keyboard or mouse input to the desktop (launching the exe with flags is fine); never kill a process you did not start; leave Unity Hub and every other Unity editor (`C:\Unity\CsharpPrac`, other worktrees) alone.
- Source of the findings: `docs/qa/qa-int-1110.md` minors 1 and 2.

## F1: the play-menu capture line must not count captures

`Client/Assets/Scripts/UI/PlayMenu.cs:33-42`, `PlayMenuCopy.CaptureNote`.

- `CaptureCount` counts Lagging stages, not captures: a first stage that runs out raises it to 2 without a second capture (Q1, HUD_SPEC §4.3 line 251; see `SurvivorReadout.CaptureTag` and its comment at `Client/Assets/Scripts/UI/SurvivorReadout.cs:170-174`).
- So `CapturedTwice` = "Captured twice: the next capture eliminates you." can be false.
- **Spec:**
  - For `CaptureCount ≥ 2` and `Normal`, the line becomes count-free: `Next capture eliminates you.`
  - Rename the constant to match (e.g. `NextCaptureEliminates`).
  - `CaptureCount = 1` stays `Captured once: the next capture leaves 30 s for a rescue.`. It is true: count 1 is exactly one capture.
  - Other states and count 0 give no line, as now.
- **Update the tests that pin the old copy:**
  - `Client/Assets/Tests/EditMode/PlayMenuTests.cs` around lines 136-139 and 259.
  - `Client/Assets/Tests/EditMode/QA/PlayMenuQaTests.cs` around lines 36-41.
  - Add an assertion that no `PlayMenuCopy` capture line contains "twice".
- **HUD_SPEC:** update `docs/ui/HUD_SPEC.md` §5 (line 396, the Q6 Line 3 rule) to the new copy. Mark it `(11/10)` in the same style as the 11/08 Q1 note.

## F2: Killer world tags may touch each other and edge glyphs

`Client/Assets/Scripts/Killer/TagLayout.cs`.

- Evidence: `C:/Unity/MultiPlayGame/PtK-work/qa-int/docs/qa/qa-int-runs/live-killer/v2-killer-collapse.png` around (1255,390).
  - Two in-view Survivor distance tags, "38 m" and "39 m", sit side by side with 0 gap.
  - The "39 m" plate ends right against a small edge chevron glyph.
- Cause found by the orchestrator. `Free()` uses `Rect.Overlaps`, which is strict, so rectangles that only touch count as free. There is no clearance between a tag and other tags or glyphs.
  - Also check the priority filter: `other.Priority <= priority` means a Survivor (priority 0) tag ignores Edge (priority 1) chevron glyphs.
  - Decide whether a tag may cover a Survivor edge chevron. HUD_SPEC treats both as Survivor information, so the orchestrator's view is no. State your decision and why.
- **Spec:**
  - **Clearance:**
    - A placed tag keeps a minimum clearance, `TagGap`, from every other placed tag and from every visible glyph it must avoid. Use 4 u, which matches Q2's edge-marker gap, unless you find a reason in the code or spec for a different value. Say what you chose.
    - Its own glyph keeps the current relation: the default tag offset from its own glyph must not suddenly push every tag off its preferred place. Check how tags are anchored to their own glyph before you change `Free`.
  - **Keep the existing rules:**
    - priority order;
    - stacking steps (below, above, below again, then hidden);
    - Q2's `Slot` path for Overloaded/Exit;
    - the bounds and the blocked rects;
    - determinism (the result depends only on the set of items);
    - no per-frame allocation;
    - Q2 hysteresis in `EdgePlacement.cs`.
  - The `Slot`/`NextTo` candidates must use the same clearance, so a slotted tag lands exactly `TagGap` away and not 2 u.
  - Keep `StepGap` consistent with the new clearance, or explain why not.
- **Tests (EditMode; add them to `Client/Assets/Tests/EditMode/EdgeSpreadTests.cs` or a TagLayout test file):**
  1. Two in-view Survivor tags whose preferred rects touch horizontally end ≥ `TagGap` apart, or one is stacked.
  2. A Survivor tag next to an Edge chevron glyph keeps ≥ `TagGap` from it, according to your decision above.
  3. A slotted Exit tag lands `TagGap` from the obstacle.
  4. A fuzz or invariant test: over random layouts, no two visible tags and no visible tag/avoided glyph are closer than `TagGap`.
     - Q2 already has fuzz tests; extend one if that fits.
     - Keep every existing test green. Change an existing assertion only where it encoded the 2 u or 0 u clearance, and list each change.

## Gates

```
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <abs path xml> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
Client/Builds/Win64/PingTheKiller.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role killer -ptk-shots <dir> -ptk-quit-after 200 -logFile <log>
```

- **How to run:**
  - Use an **absolute** `-testResults` path; a relative one resolves against `-projectPath`.
  - Check the real `-executeMethod` name first.
  - Run Unity commands with `run_in_background`.
- **Pass criteria:**
  - All EditMode tests pass; the count was 1468 on main.
  - BuildWindows succeeds.
  - `dotnet build PingTheKiller.sln -warnaserror` stays green. It should be untouched.
- **Killer run:**
  - Run it once and look at the in-view and collapse shots for touching tags.
  - Copy 2-3 relevant shots to `docs/qa/minors-1110-screens/`.
- **Logs:** put them under `docs/qa/minors-1110-runs/`.
- **After each Unity run:** run `git status` and revert files that changed only in line endings (`git diff --ignore-cr-at-eol --quiet -- <file>`).

## Owned files

- `Client/Assets/Scripts/UI/PlayMenu.cs`
- `Client/Assets/Scripts/Killer/TagLayout.cs`
- `EdgePlacement.cs`, only if you must; say why.
- The tests named above, plus a new test file if you want one.
- `docs/ui/HUD_SPEC.md`: §5 line 396. Add a one-line TagLayout clearance note where §6 describes Killer marker layout, if such a place exists.
- `docs/qa/minors-1110-*`

## Report

Write `docs/qa/minors-1110-impl-a1.md` in English, ≤ 250 words. Cover:
- what changed (files);
- the F2 clearance value;
- the chevron decision;
- the tests added or changed;
- the gate results with counts;
- the screenshot paths;
- what is unverified;
- the risks.

Also write `docs/qa/minors-1110-commit-message.txt`, without an attribution line.
