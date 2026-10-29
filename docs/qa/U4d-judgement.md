# U4d — orchestrator judgement (Claude Opus)

**Verdict: PASS on attempt 1.**

**Implementers:** Claude Opus subagents, as a quota switch from Codex.
- The first implementer was lost when the app restarted, before it reported.
- A second implementer continued from the same working tree. This is not a retry; nothing was discarded.

**QA:** Claude Sonnet, a different model.

## Scope
- U2c `025125d` merged into `claude/u4c-cameras` as merge commit `260e91e`. The merge was clean, with no conflicts, and the new Server.Core `.meta` files came with U2c.
- `S2C_SelfState` (protocol v0.3) is wired through a pure local-countdown holder. Covered:
  - the Survivor skill ring, its cooldown and `skill_ready`;
  - rescue row 3 and the rescuer row;
  - `Protected`;
  - the Killer §6.1d ring;
  - §6.4 execution guidance.

  The seam stub is gone.
- The Killer view handles up to 16 entries, anonymous Survivors and phantoms included.
- **Real bug found in the online run:** `NetClient.IsServerPacket` rejected `S2C_SelfState`, so online clients disconnected right after match start. The whitelist now accepts all 8 S2C ids. QA added a test that fails if a future S2C id is added without updating the whitelist.

## Gate (orchestrator runs, Unity 6000.3.25f1 batch)
- **Final EditMode: 1053/1053**, covering the implementer suites and QA U4c + U4d. 0 `error CS`.
- **Ruling:** QA's `KillerHudQaTests.ThereIsExactlyOneMarkedAdapterPointForTheFutureSelfStatePacket` was superseded, because U4d removes the stub on purpose. QA replaced it with a test that no stub remains and that a single pump path feeds the holder.
- **Build:** `BuildWindows` Success, on the final product code. Only QA tests changed after it.
- **Offline (implementer run):** 0 TCP / 0 UDP at +20 s and +35 s.
- **Online, against this branch's U2c server, 0 exceptions:**
  - Survivor:
    - SelfState received;
    - the cooldown counted 15 → 0.94 locally;
    - `skill_ready` played.
  - Killer:
    - `blips tracked max=11` (6 Survivors plus phantoms);
    - `execution prompt=Grace grace=2.00`, then `StepBack`.
- **Screenshots** in `docs/qa/U4d-screens/`, all 4 × {720, 1080}, all natural captures. The orchestrator checked them by eye:
  - crowd tracking with no avatar collapse;
  - Grace prompt copy;
  - cooldown ring;
  - rescuer row.

## QA (`docs/qa/U4d-qa-a1.md`)
- **Verdict:** QA-PASS. Spec bullets §1–§6 PASS.
- **Deviation:** the §4.8 "Not used" state is not built; it was not in the brief.
- **SLOP §9:** Survivor HUD 92, Killer cluster 91.
- **Information leak:** PASS.
  - The Killer never reads `RescueProgress` or `InvulnerableLeft`.
  - The Survivor never reads `ExecutionGraceLeft`.
  - The Killer path still reads no Survivor `Id`, `Yaw`, timers or forbidden flags.

## Follow-ups (minor, not blocking)
1. **F-01:** a terminal tag can draw over an in-view Survivor at execution range. The body rect is not registered when the projection leaves the viewport (`KillerWorldLayer.cs:177`, `:226-231`).
2. **F-02:** the `Lagging` tag is anchored at the untilted head height, so it floats above the 25° stutter pose.
3. **F-03:** `skill_ready` plays while the Survivor is Lagging (skill disabled) or spectating. It needs a status gate.
4. **F-04:** two collinear edge chevrons merge.
5. **F-05:** `KillerCues.OnCooldown` is dead code.
6. The §4.8 "Not used" state.

## Needs a human
- The audibility and timing of `skill_ready`.
- How the rescue and Protected readouts feel in a real rescue.
- How the execution prompt feels at close range.
