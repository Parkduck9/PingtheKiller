# Brief U4c — attempt 2 (piece B fix round)

Implementer: Claude Opus subagent (same as attempt 1; quota switch from Codex). QA: Claude Sonnet.
Same approach as attempt 1, with the orchestrator's fix list below. Attempt 1 judgement: **FAIL** (QA `docs/qa/U4c-qa-a1.md`, 3 majors). Anonymity, tracker and rigs passed and must not regress.

## Owned files
Same as `docs/briefs/U4c-a1.md`: `Client/**` except piece A's files and `Client/Assets/Tests/EditMode/QA/**` (QA-owned, read-only for you). Report: `docs/qa/U4c-impl-a2.md`. Screens: `docs/qa/U4c-screens/*.png` (overwrite).

## Fix list
1. **F-01, F-02, F-04: tag and marker collisions (major).** "Not de-cluttered" is no longer an accepted deviation.
   - Add a pure, tested screen-space layout pass for tags (and the marker glyphs they belong to) that runs every frame after projection.
   - **No two tags may overlap.** Resolve a collision by stacking the lower-priority tag vertically: tag height + 2 u, alternating below/above, up to 3 steps. After that, hide the lower-priority tag. Never hide a Survivor marker glyph.
   - **Priority**, highest first:
     1. Signal survivor markers and their tags (in view, occluded);
     2. edge chevrons and their tags;
     3. Exit marker;
     4. terminal markers and tags.
   - A lower-priority tag or glyph must never cover a higher-priority glyph. A chevron's distance tag must not cover a diamond (F-02). A terminal square or tag must not cover a survivor (F-04).
   - The visual draw order stays §6.2: terminal/exit, then Signal markers and edge indicators, then the cluster.
   - Tests: two tags at the same point end up disjoint; priority decides who moves; the step limit leads to a hide; it is deterministic for a given input set, and the input order carries nothing (shuffle the input and get the same layout).
2. **F-03: `ally` teal on the Killer client (major).** HUD_SPEC §0.4 says `ally` is shown to Survivors only, and Q11 says the Killer sees a `text` exit ring.
   - On the Killer client, a completed terminal mesh uses a neutral non-`ally`, non-`signal` colour. Completion stays readable from shading or the `muted` tone; pick one and state it.
   - The open exit ring uses `text`.
   - The Survivor client is unchanged.
   - Make `KillerColourRolesQaTests` and `LightingQaTests` pass. They are QA's; if a test's seam does not match your API, report it rather than editing the test.
3. **F-05 (minor):** the `[DEV]` status log and `Preview` must not read Survivor `Id`, `StatusTimer` or `Status` per entry on the Killer client. On the killer, log blip counts by class from the tracker instead. Survivor-client logging can keep its current form.
4. **F-06 (minor):** a blip must never draw nothing for a frame. When `Project` fails for a held in-view or occluded class (it left the frustum), draw it as an edge chevron at once, while the hysteresis timer keeps running.
5. **Distinct shots (I-2):** make `killer-occluded` show a through-wall diamond near the screen centre with no chevron, and `killer-offscreen` show chevrons with no in-view survivor model on screen. A dev look mode is fine, e.g. `-ptk-killer-look occluded` aims at the nearest occluded blip. Extend DevArgs and its tests; keep it inert when absent.

## Verification (as attempt 1)
- Unity batch: 0 `error CS`.
- **Every** EditMode test passes, the QA suite included (now about 940 cases). Report the totals from the XML.
- `BuildWindows` succeeds.
- Re-take all 7 shot pairs at 1280×720 and 1920×1080. **Open each one yourself with the Read tool** before reporting, and confirm no text collides.
- Run a short live killer session against the main server (`--bind 127.0.0.1`) with 0 exceptions.
- Stop every process you start. Never answer firewall dialogs.

## Report `docs/qa/U4c-impl-a2.md` (≤ 250 words)
- per-fix what changed (files)
- test totals
- screenshot list, with one line on what each shows
- deviations
