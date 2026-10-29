# U4c implementation, attempt 2 (piece B fix round)

## Fixes
- **F-01/02/04:** new pure `Killer/TagLayout.cs`. `MarkerLayer` records glyph, tag and in-view body rectangles and runs one layout pass per frame. Priority: Survivor > Edge > Exit > Terminal. A colliding tag stacks by height + 2 u (below, above, below), then hides. An Exit or Terminal glyph covering a higher glyph hides with its tag; Survivor glyphs never hide. Draw order is unchanged. `TagLayoutTests` (8).
- **F-03:** in `WorldView.Accept`, a done terminal and the open Exit ring use `text` on the Killer client; done reads as a brighter panel than `muted` idle. Survivor client unchanged.
- **F-05:** `DevCapture` reads entries on Survivors only; the Killer logs blip counts. On the Killer, `Preview` uses lobby slots.
- **F-06:** `KillerScene.OutOfFrustum`: such blips become chevrons at once; the 100 ms hold keeps running. Test added.
- **I-2:** `-ptk-killer-look occluded`, plus stricter event-shot rules.

## Tests
936 total, 936 passed (XML). 0 `error CS`. BuildWindows succeeded.

## Screens (`*-720/1080.png`, all opened, no text collisions)
- `survivor-3p`: from behind.
- `survivor-behind`: facing the camera.
- `killer-inview`: outlines, stacked tags.
- `killer-occluded`: centred diamond, no chevron.
- `killer-offscreen`: chevrons only.
- `killer-intro`: intro cluster.
- `killer-menu`: play menu.

## Live
Main server, killer: moved 12.77 m, blip counts logged, 0 exceptions.

## Deviations
- Stack steps are +1, -1, +2.
- Tag widths are estimated from the text.
- Occluded and offscreen shots use 1 and 2 bots.
