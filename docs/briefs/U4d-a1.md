# Brief U4d — attempt 1: wire S2C_SelfState (protocol v0.3) into the client; verify against the U2c server

Implementer: Claude Opus subagent (quota switch from Codex; it does not count as an attempt). QA: Claude Sonnet.

Branch: `claude/u4c-cameras`. It already contains U4c (`2a2e926`) and the merge of U2c (`025125d`, merge commit `260e91e`). Do not commit.

## Read
- `AGENTS.md`
- `docs/ui/HUD_SPEC.md`, which is binding:
  - §4.1, rows 3 and the `Protected` line;
  - §4.4 (rescuer row);
  - §4.8 (Survivor skill ring);
  - §6.1d (Killer skill block);
  - §6.4 (execution guidance);
  - §6.5 (`skill_ready` cue);
  - §11 Q5, Q6 and §11.1 N5.
- `docs/ui/SLOP_DEFINITION.md`
- `Shared/Runtime/Packets/ServerPackets.cs` (`S2C_SelfState`), `Shared/Runtime/Protocol.cs` (`MaxWorldEntries = 16`)
- `docs/qa/U2c-impl-a2.md` (SelfState is sent on change plus 1 Hz; the killer view is anonymous and carries phantoms)
- `docs/qa/U4c-judgement.md`
- The current client:
  - `World/SelfState.cs` (the seam);
  - `Killer/ExecutionGuidance.cs`, `UI/KillerHud.cs`, `UI/SkillRing.cs`;
  - the Survivor HUD in `UI/ClientScreens.cs` and `HudCopy`;
  - `ClientApp.cs` (the packet pump).

## Owned files
- `Client/**`, except `Client/Assets/Tests/EditMode/QA/**`. That folder belongs to QA: you may read it, and you must keep it passing.
- `docs/qa/U4d-impl-a1.md`
- `docs/qa/U4d-screens/*.png`

Never edit `Shared/`, `Server.Core/`, `Server/`, `Tools/` or `PingTheKiller.sln`. If the client needs something from them, stop and report it.

## Scope
1. **Wire the packet.**
   - Handle `S2C_SelfState` in the pump and feed the `ISelfState` holder.
   - Set `Available = true` on the first packet of a match.
   - Reset on match start, match end, leaving and reconnect.
   - Remove the `// U2c: feed from S2C_SelfState` stub and any "pending" placeholders.
   - The offline link (in-process server) must deliver it too.
2. **Local countdown.**
   - Between packets, decrease `SkillCooldown`, `InvulnerableLeft` and `ExecutionGraceLeft` by real time, floored at 0.
   - Snap to every received value.
   - `RescueProgress` is shown as received and is not extrapolated.
   - Implement this as a pure, tested class.
3. **Survivor readouts** (HUD_SPEC exact copy, colours and geometry):
   - skill ring cooldown arc with the `Cooldown N s` state (`ceil`), and the `skill_ready` cue when the cooldown reaches 0;
   - §4.1 row 3, `Rescue in progress — stay close`, with the `ally` fill = `RescueProgress`;
   - `Protected 1.4 s` while `InvulnerableLeft > 0`;
   - the §4.4 rescuer row and bar.
4. **Killer readouts:**
   - the §6.1d ring and `Cooldown N s`, plus `skill_ready`;
   - §6.4 execution guidance (Grace / Step back / Ready) from `ExecutionGraceLeft` and the existing armed/re-arm class. Remove its "hidden until U2c" path.
5. **Sixteen entries.** The killer path must handle up to `Protocol.MaxWorldEntries` (16) entries per snapshot, including phantoms. That means no pool, array or cap sized for 7 or 8 (tracker, markers, outlines, audio voices excepted: the voices stay at 4 by spec).
   - Add a test that feeds 15 anonymous entries plus the Killer for 200 ticks with crossing paths.
   - It must have no exceptions and stable counts, and no two blips may be drawn at the same position from one entry ("avatar collapse").
6. **Dev flags** for the shots. Extend `DevArgs` and its tests; keep them inert when absent. For example:
   - a survivor `-ptk-skill-at <s>` that uses the skill once;
   - a killer chase mode that steers toward the nearest Lagging or Signal blip so captures happen.

   Add event shots:
   - `survivor-cooldown` (cooldown > 0);
   - `survivor-rescue` (`RescueProgress > 0`, own or as rescuer);
   - `killer-grace` (`ExecutionGraceLeft > 0`);
   - `killer-crowd`, the first frame with at least 6 blips tracked at once.

   If a state cannot be reached in an offline bot match within 3 tries, say so in the report. **Do not fake it.** A synthetic preview is allowed only if it is clearly labelled `-synthetic` in the file name and the report.

## Verification you run
- **Unity batch:**
  - 0 `error CS`;
  - **every** EditMode test passes (QA suite included; about 990 + yours). Report the totals from the XML.
  - `BuildWindows` succeeds.

  You own the single Unity process on this project.
- **Offline:** the in-process match still opens no socket. Sample `Get-NetTCPConnection` / `Get-NetUDPEndpoint -OwningProcess` twice during a match.
- **Online, against this branch's U2c server:** `dotnet run --project Server -c Release -- --port 7777 --bots 6 --bind 127.0.0.1`. Run one killer and one survivor session. `[DEV]` log lines must show:
  - SelfState received;
  - cooldown values counting;
  - blip counts, including more than 7 when phantoms exist.

  0 exceptions.
- **Screenshots** at 1280×720 and 1920×1080, in `docs/qa/U4d-screens/`:
  - `killer-crowd`
  - `survivor-cooldown`
  - `survivor-rescue`
  - `killer-grace`

  Open every one yourself with the Read tool before reporting.
- Stop every process you start. Never answer firewall dialogs.

## Report `docs/qa/U4d-impl-a1.md` (≤ 300 words)
- what changed
- test totals
- the screenshot list, with what each shows and whether it is natural or synthetic
- `[DEV]` log lines from the online runs
- deviations
- what needs a human
