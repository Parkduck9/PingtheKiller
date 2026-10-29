# QA brief U4d — attempt 1 (S2C_SelfState wiring, 16 entries, NetClient whitelist)

QA: Claude Sonnet. The implementers were Claude Opus subagents, so a different model.

## Read
- `AGENTS.md` §5
- `docs/briefs/U4d-a1.md` (the spec under test)
- `docs/qa/U4d-impl-a1.md`
- `docs/ui/HUD_SPEC.md`:
  - §4.1 row 3 and the `Protected` line
  - §4.4
  - §4.8
  - §6.1d
  - §6.4
  - §6.5
  - §11 Q5/Q6, §11.1 N5
- `docs/ui/SLOP_DEFINITION.md` §9
- `Shared/Runtime/Packets/ServerPackets.cs` (`S2C_SelfState`) and `Shared/Runtime/Protocol.cs`
- The U4d changes: `git diff HEAD -- Client` plus the untracked files in `git status`. HEAD `260e91e` is U4c plus the U2c merge.
- Every `docs/qa/U4d-screens/*.png`, opened with the Read tool.

## You may create or edit ONLY
- `Client/Assets/Tests/EditMode/QA/**`
- `docs/qa/U4d-qa-a1.md`

Do not edit product code. Do not run Unity, the game or the server. Do not commit, and do not spawn subagents.

## Ruling (orchestrator)
`KillerHudQaTests.ThereIsExactlyOneMarkedAdapterPointForTheFutureSelfStatePacket` is superseded: U4d removes the stub on purpose. Replace it with a test that:
- no `U2c: feed from S2C_SelfState` stub remains;
- `S2C_SelfState` is handled by exactly one pump path that feeds the holder.

## Test work (write from the spec, not the implementation)
1. **Local countdown class:**
   - `SkillCooldown`, `InvulnerableLeft` and `ExecutionGraceLeft` decrease with real time and floor at 0;
   - every packet snaps the value (up and down);
   - `RescueProgress` is not extrapolated;
   - `Available` is false before the first packet and resets on match start, end, leave and reconnect.
2. **Readout copy and states:**
   - `Cooldown N s` with `ceil`, and `Ready` at 0;
   - the `skill_ready` cue fires once per transition to 0 (not on every frame, and not at match start);
   - `Protected 1.4 s` (one decimal) only while `InvulnerableLeft > 0`;
   - rescue row 3 vs. the rescuer row;
   - Killer §6.4 Grace / Step back / Ready, including the armed and re-arm rule (1.2 m) and the one-decimal seconds;
   - the passive skill (PacketAfterimage) gets no cooldown ring, and FirewallBlock shows `After the Exit opens`.
3. **NetClient whitelist:** every `S2C_*` `PacketId` is accepted from the server, and every `C2S_*` id is rejected. Write it so that a future S2C id added to Shared without a whitelist update makes the test fail (for example, enumerate `PacketId` by name prefix).
4. **Sixteen entries:**
   - 15 anonymous entries plus the Killer, shuffled, `Id = 0`, for many ticks with crossing paths: no exception, the blip count equals the entry count, and no two blips share a handle;
   - 16 entries with no Killer entry (defensive);
   - more than 16 must not crash.

## Review
- Each bullet of `U4d-a1.md` §1–§6: PASS, FAIL or DEVIATION, with evidence.
- **The `NetClient` fix:** read it and say whether any other v0.3 path could still be rejected or dropped.
- **Information leak:**
  - the Killer never shows Survivor cooldowns, rescue or protection;
  - the Survivor never sees `ExecutionGraceLeft`;
  - the Killer path still reads no Survivor `Id`, `Yaw`, `StatusTimer`, `CaptureCount` or forbidden flags. Cite file:line.
- **Screenshots:**
  - text collisions and wrapping (for example, does the Survivor skill name wrap to 2 lines, and does HUD_SPEC allow it?);
  - in `killer-grace-720.png`, a `Terminal 1` tag is drawn over the in-view Survivor model; judge it against U4c's F-04 rule;
  - the free-floating `Lagging` tag;
  - colour roles;
  - geometry and type sizes.
- SLOP §9 scores for the changed Survivor HUD and the Killer cluster.
- Anything wrong that the brief did not mention.

## Report `docs/qa/U4d-qa-a1.md`
Each finding gets a severity, reproduction steps, expected vs actual, and evidence. List the new test names. The last line is `QA-PASS` or `QA-FAIL`.
