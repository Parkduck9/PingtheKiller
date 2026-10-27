# QA brief U4b — round 2 (SkillIds, lighting, lobby order, real-rules verification)

QA: Claude Sonnet. The implementer was a Claude Opus subagent, so a different model.

## Read
- `AGENTS.md` §5
- `docs/briefs/U4b-a2.md` (the spec under test)
- `docs/qa/U4b-impl-a2.md`
- your previous report `docs/qa/U4b-qa-a1.md`
- `docs/ui/SLOP_DEFINITION.md`
- `Shared/Runtime/Game/SkillIds.cs`
- the U2b rules in `Server.Core/Runtime/GameServer.Rules.cs`, `.Skills.cs`, `.Visibility.cs`
- the changed client code: `git diff f58f67a -- Client/Assets/Scripts`
- every `docs/qa/U4b-screens/a2-*.png`, opened with the Read tool

## You may create or edit ONLY
- `Client/Assets/Tests/EditMode/QA/**`
- `docs/qa/U4b-qa-a2.md`

## Do not
- edit product code
- run Unity, the game or the server
- touch `Client/Library`, `Client/Temp` or `Client/Builds`
- commit

## Test work
1. **Supersede by orchestrator ruling** `HudCopyQaTests.TheSkillTableKeepsItsTodoForTheSharedSkillIds`. The brief removes that TODO. Replace it with a test that the client's name and passive tables are keyed by the `SkillIds` constants for every id, and that `IsPassive` is true only for (Killer, `SkillIds.PacketAfterimage`).
2. **Lobby display order**, written from the spec as pure tests:
   - the host is first, then other humans, then bots; empty slots go last;
   - order is stable inside each group;
   - online as a non-host: the host is first, then you and the other humans;
   - row numbers are display positions 1 to 8.
3. **Readouts against the U2b rules**, written from GDD and U2b, not from the implementation:
   - Lagging countdown text from `StatusTimer`;
   - `Terminals k/N` with N = `TerminalsRequired`;
   - Exit open / locked / firewall-locked (`ExitOpen = 1`, `ExitLocked = 1`) wording;
   - Escaped and Eliminated text.
4. **DevArgs** for the new flags (`-ptk-wander`, `-ptk-rematch`): valid, invalid and missing values; the flags are inert when absent.
5. **Lighting**, by code read and screenshots:
   - no Built-in shader is used;
   - the background stays `#101113`;
   - ambient or fill light is set in code. Cite the file and line.

## Review
- Each spec bullet of U4b-a2 §1–§5: PASS/FAIL with evidence.
- **Before/after lighting screenshots:** shaded faces readable, walls, floor, terminals and capsules distinguishable, palette kept, and terminals no longer read like capsules from above.
- **Real-rules screenshots, checked against the `[DEV]` log lines quoted in the impl report:**
  - the HUD shows the right state;
  - the result copy is right for the reason;
  - Back to lobby is disabled until the lobby reopens;
  - the rematch moves.
- **No information leak:**
  - the survivor never sees the killer;
  - the killer sees only Signal survivors, including hacking ones;
  - no killer coordinates appear on any survivor screen.
- Camera-dependent new code is isolated, as the impl report claims (a U4c camera redesign is coming).
- The SLOP §9 score table for the changed screens.
- List the server and balance observations from the impl report separately, as **out of U4b scope**: the matches ended very fast offline, and online survivor bots were stuck at 3/4 terminals until TimeUp. They are not client findings.

## Report `docs/qa/U4b-qa-a2.md`
The last line is `QA-PASS` or `QA-FAIL`.
