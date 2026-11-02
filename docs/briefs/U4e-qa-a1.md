# QA brief U4e — attempt 1 (Killer visible to Survivors, threat visuals, capture and elimination feedback)

QA: Claude Sonnet. The implementer was a Claude Opus subagent, so a different model.

## Read
- `AGENTS.md` §5
- `docs/briefs/U4e-a1.md` (the spec under test)
- `docs/briefs/U2e-a1.md` (the server contract)
- `docs/GDD.md` §2
- `docs/qa/U4e-impl-a1.md`
- HUD_SPEC: the sections the implementer amended (marked `(10/30)`): §0.1, §4.1, §4.2, §4.5, §4.10, §6.5.1, §7.2, §10
- `docs/ui/SLOP_DEFINITION.md`
- The changes: `git diff de730e9 -- Client docs/ui`, plus the untracked files (`git status`): `Dev/FakeKiller.cs`, `UI/SurvivorFeedback.cs`, `World/LaggingStutter.cs`, `SurvivorFeedbackTests.cs`
- Every `docs/qa/U4e-screens/*.png`, opened with the Read tool

## Orchestrator additions to the brief
The implementer also folded in three minor U4d findings; verify each:
- **F-01:** terminal tag over an in-view Survivor at execution range.
- **F-02:** `Lagging` tag anchored to the tilted head.
- **F-03:** `skill_ready` gated on own status.

A dev-only `FakeKiller` injection was added for screenshots. `-injected` shots are labelled as such.

## You may create or edit ONLY
- `Client/Assets/Tests/EditMode/QA/**`
- `docs/qa/U4e-qa-a1.md`

Do not edit product code or HUD_SPEC. Do not run Unity, the game or the server. Do not commit, and do not spawn subagents.

## Test work (from the spec, not the implementation)
1. **Killer model on the Survivor client** (fake snapshots):
   - present → drawn at the interpolated position, facing `Yaw`;
   - absent → fades over 0.3 s at the last position, with no extrapolation and no movement;
   - reappears after more than 0.3 s → snaps, with no interpolation across the gap;
   - no marker, outline or through-wall indicator;
   - a Spectator client never draws it.
2. **Audit:** the Survivor-side Killer rendering reads only the Killer entry of the current snapshot. Nothing else creates a Killer visual: not `ThreatLevel`, not sound, not `SkillEffect`. `FakeKiller` is inert unless its DevArgs flag is set. Cite file:line.
3. **Threat visuals per level:**
   - level 0: none;
   - level 1: strip 1/3;
   - level 2: strip 2/3 plus `threat_scratch` once on entry;
   - level 3: screen band, no strip;
   - the mask is re-rolled only on a level change (same level over many frames gives an identical mask);
   - hidden while Eliminated or Escaped;
   - out-of-range levels read as 0.
4. **Capture banner:**
   - fires exactly once per Normal → Lagging transition, and again on a second capture after a rescue;
   - never on reconnect into an already-Lagging state, if the implementation can tell. Report what it does.
5. **Elimination reason:**
   - timeout (last Lagging `StatusTimer` ≤ 0.2 s);
   - executed (> 0.2 s);
   - second capture (Normal → Eliminated directly);
   - the result screen's personal line matches the meter.
6. **F-03:** `skill_ready` does not play while Lagging, Eliminated, Escaped or spectating, and does play when Normal.

## Review
- Each spec bullet of `U4e-a1.md` §1–§5, and each orchestrator addition: PASS / FAIL / DEVIATION, with evidence. Judge each deviation the impl report lists.
- **Screenshots against SLOP §9**, scored ≥ 85 with no gate violation. Look at least at:
  - **Mid-word wrap.** The Survivor skill name wraps as `Emergenc` / `y restore` at 720p (`threat-3-720.png`, `eliminated-reason-720.png`). Decide whether this is a SLOP gate violation. The implementer called it pre-existing. Report it either way: pre-existing does not exempt it from the score.
  - **Strip visibility.** The level-1 strip in `threat-1-*.png` is the HUD_SPEC 4 u size, but it is very faint at 1080p. The user's complaint was "I can't see the enemy." Report whether level 1 is legible as a threat cue. If it is a design problem rather than a build problem, mark it as a spec question.
  - **Colour roles:** the Killer model colour, Lagging allies, and the danger bands.
  - **Banner copy:** check it against SLOP §6 vocabulary.
  - **Lagging stutter:** check the frame, and the reduce-stutter path in code.
- **Information leak:**
  - the Survivor sees the Killer only via the entry;
  - the Killer view is unchanged;
  - the Killer path still reads no Survivor `Id`, `Yaw`, timers or forbidden flags.
- Anything wrong that the brief did not mention.

## Report `docs/qa/U4e-qa-a1.md`
- each finding with severity, reproduction, expected vs actual and evidence;
- the new test names, marking which need the Unity gate;
- the SLOP §9 score table.

The last line is `VERDICT: PASS` or `VERDICT: FAIL (...)`.
