# Brief U6c-wire — attempt 2: fix list

Attempt 1 was judged **FAIL**. The QA report is `docs/qa/U6c-wire-qa-a1.md`, written by Gemini Flash. The orchestrator then ruled on each finding:

- **Rejected** (QA misread the spec or the image):
  - F3: the Killer urgent bar goes `muted` → `text` per §13.5.
  - F4 "zone invisible": the wedge is the zone and the tick is the needle.
- **Accepted**: everything below.

Same rules as attempt 1. Use the same worktree and the same owned files. No commit, and no desktop input.

## Must fix

1. **S4 for Survivors: Regressing is not shown at all (blocker).**
   - Survivors have no terminal world tags today, so the `Regressing` tag never appears for them.
   - Add a v2 marker layer to the Survivor HUD, or reuse `V2MarkerLayer` there.
   - For every terminal whose `Flags` has Regressing and whose 1.5 m point is on screen, draw the §13.6 Survivor tag above it: `Terminal 3 · 40% · Regressing`, with the 2 u `danger` bar.
   - Nothing off screen, and no edge indicator.
   - Keep it out of the bottom cluster and stack keep-out.
   - Add a test that drives it from a `WorldState` through the same code path the HUD uses. Testing the copy helper alone is not enough.
   - Update §13.6 if your placement differs from its wording.
2. **The second-capture banner says "60 s left" (major).**
   - `CaptureWatch.Banner` is a static string that uses `LaggingTimeoutSeconds`.
   - Make it depend on the stage: 60 s on the first capture, 30 s on the second capture or on a time-out escalation. Use the same source as `SurvivorReadout.LaggingTotal`.
   - Test both stages.
   - Then sweep every other survivor or killer string with a hard-coded Lagging duration.
3. **Per-frame allocations (major).**
   - `V2Wire.Exits(...)` allocates a new `ExitView[]` on every call. It runs one to three times per frame, from `ClientApp`, `KillerWorldLayer.DrawStatic`, and `SwitchInProgress`.
   - Compute exits once per received `WorldState` into a reused buffer, and have every reader use that.
   - Check the rest of the new wiring for per-frame or per-snapshot allocations, including strings built every frame when the value has not changed, and remove them.
   - Add a test asserting that the buffer is reused, for example the same array instance across two snapshots.
4. **Sync needle readability (minor, but fix it).**
   - QA read the zone wedge with its Great head as a "heavy arrow" and the short needle tick as the zone. A first-time player could make the same mistake.
   - Make the needle unmistakably the moving hand. For example, a 3 u line from the ring's inner edge to about 8 u outside the outer edge, with the `bg` keyline. It must stay longer than the Great head is thick.
   - Make sure the zone does not read as an arrow.
   - Keep SLOP: no glow and no `signal`.
   - Update §13.2 to the new numbers, and recapture shots 1 and 2.

## Should fix if cheap

5. Add the 1080p capture of shot 5 (the killer Overloaded alert).
6. **B1 proof.**
   - Recapture "before" and "after" at the **same** framing.
   - Use the same dev flags and seed. For the "before" shot you may temporarily revert only the strip code in a scratch copy, outside the worktree, or capture before reapplying.
   - If you cannot get the same framing, explain why in one line.

## Accepted as-is, or out of scope

- F8: duplicate tags in the preview only. Note it.
- F9: the sounds are out of scope.
- F10: edge label overlap predates this unit. Note it.
- The Killer not receiving Regressing is server masking; it is correct as implemented.
- The kick prompt on 0 % terminals is accepted, because the hint copy explains it.

## Acceptance

- EditMode: all green.
- BuildWindows: success.
- New or recaptured shots in `docs/qa/U6c-wire-screens/`, using the `a2-` prefix. Open them yourself.
- Append an `## Attempt 2` section to `docs/qa/U6c-wire-impl-a1.md`, at most 200 words. List every item above with its status.
