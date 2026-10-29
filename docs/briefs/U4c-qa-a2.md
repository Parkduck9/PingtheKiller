# QA brief U4c — attempt 2 (fix-round verification)

QA: Claude Sonnet, the same reviewer as attempt 1. The implementer was a Claude Opus subagent.

## Read
- your attempt-1 report `docs/qa/U4c-qa-a1.md`
- `docs/briefs/U4c-a2.md` (the fix list under test)
- `docs/qa/U4c-impl-a2.md`
- the changes since attempt 1: product code under `Client/Assets/Scripts/**`, especially the new `Killer/TagLayout.cs`, `UI/MarkerLayer.cs`, `Killer/KillerWorldLayer.cs`, `World/WorldView.cs`, `Dev/DevCapture.cs`, `ClientApp.cs` and `Killer/HeldClass.cs`
- every `docs/qa/U4c-screens/*.png` (all retaken), opened with the Read tool

## Ownership
Same as attempt 1. You may create or edit only:
- `Client/Assets/Tests/EditMode/QA/**`
- `docs/qa/U4c-qa-a2.md`

Do not run Unity, the game or the server. Do not commit, and do not spawn subagents.

## Work
1. **Verify each finding** (F-01 to F-06, I-2): FIXED / NOT FIXED / PARTIAL, with evidence.
2. **Independent `TagLayout` tests, written from `U4c-a2.md`:**
   - two coincident tags end up disjoint;
   - priority decides which tag moves (Survivor > Edge > Exit > Terminal);
   - after 3 steps the tag is hidden;
   - a Survivor glyph is never hidden;
   - a lower-priority glyph never covers a higher-priority one;
   - the result is deterministic, and permuting the input gives the same layout;
   - many tags (e.g. 7 survivors and 5 terminals stacked on one point) finish without overlap or an exception.
3. **F-06:** test that a held in-view or occluded blip that leaves the frustum gets a chevron on that same frame.
4. **Regression:** the anonymity audit still holds after the F-05 change. The killer path still reads no Survivor `Id`, `Yaw`, `StatusTimer`, `CaptureCount` or forbidden flags, and dev code on the killer path does not either. Cite file:line.
5. **Screenshots:**
   - no text collisions;
   - tags still readable as belonging to their marker (judge whether a stacked tag drifts too far from its owner to be useful);
   - colour roles: no `ally` on the Killer client;
   - occluded and offscreen shots now show distinct cases.
6. **SLOP §9 re-score** for the killer HUD (threshold 85) and the survivor view.
7. Anything new that is wrong.

## Report `docs/qa/U4c-qa-a2.md`
- the per-finding status table
- new test names
- SLOP §9 scores
- findings (severity, reproduction, expected vs actual, evidence)

The last line is `QA-PASS` or `QA-FAIL`.
