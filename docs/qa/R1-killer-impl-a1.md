# R1-killer impl a1 (Claude Opus)

**Built.** `C2S_Attack` 0x0017, `C2S_ChooseSkill` 0x0018, `ChaseFlags.Swinging`=16, `TerminalFlags.Locked`=4; no version bump. `GameServer.Combat.cs` replaces proximity capture: 0.2 s wind-up at 0.5× speed, one check (2.6 m, ±40°, walls/Down shutters block, nearest Normal Survivor); hit → 0.6 s recovery, miss → 0.8 s at 0.48×; stale Seq or busy swings dropped. Survivors see `Swinging` only with LOS. Skills: Ping 3 s; Overclock tail 0.3 s at 60 %; Packet trace (active): Survivors ≤12 m Signal 2.5 s; Firewall: switch 5 s, else most-hacked terminal 8 s. Lobby skill choice (wrong role/none/bots → random). Bot swings at 2.2 m.
Client: LMB/RT; `Click  Attack` ≤3 m; miss line + whoosh + 3° sweep; Survivor swipe on flag edge; 88 u skill block with effect line; lobby picker (one copy source); trace ring/glyph; terminal Locked tag.

**Deviations.** Swing numbers tuned within ±20 %. Shared beyond allocations: `GameConstants` swing values, `SkillIds.Choice`. Test hook `ContactSwingForTests` (skips wind-up/LOS) for legacy capture tests. Killer copy says "slowed" (no-% rule). No Survivor HUD effect line (no room). `SkillChoice` is on the wire for every lobby slot; UI shows only own. Bot Firewall terminal use limited (≥20 m away, <2 done).

**Balance** (Survivor win %, seeds 1–40 · 1001–1040): before 1v1 40·?, 1v3 55·60, 1v7 63·53; after 1v1 33·25, 1v3 68·58, 1v7 48·60 (median 614/622 s). Captures/match unchanged (1v3 3.2, 1v7 ~9.9).

**Gates.**
- build -warnaserror: 0 warnings, 0 errors
- Server.Tests 1142/1143 (flaky `RttProbeTests` RTT under load; passes isolated); BalanceSweep 18/18
- Harness.Tests 641/641
- Harness 3× fresh 7781/7782: 100/100 each; H6 "swung 1x". Two runs spoiled by a stray client from an aborted script: `R1-killer/harness-spoiled/`
- Unity EditMode 1562 passed, 0 failed, 2 skipped
- BuildWindows: Success
- Live 1280×720: Killer 245 s: 8 swings, 3 hits, 5 misses; Survivor 200 s: 3 wind-ups seen; 0 exceptions

**Shots** (`docs/qa/R1-killer/`): `killer/r1-killer-windup.png`, `killer/r1-killer-hit.png`, `killer/r1-killer-miss.png`, `survivor/r1-survivor-windup.png`, `killer/lobby.png`.

**Unverified / risks.** Gamepad RT not run live. Picker line orphans "s." (Packet trace). 1v1 Survivor win rate fell. Merge overlap with R2/R3: `ClientApp.cs`, `GameServer.Rules.cs`, `Terminals.cs`.

## Attempt 2 (QA a1 fixes)

- **F1** `KillerAttackKick` terms are 0 outside their windows and NaN-guarded; test: sweep without a kick.
- **F2** A sent press (`KillerSwingWatch.Predict`, once per swing) plays the whoosh and view sweep at once. A hit still comes only from the server.
- **F3** If a Normal blip within 2.8 m vanishes mid-swing, the own `Swinging` fall decides: 16 ticks = hit, 20 = miss (server test pins both). Not reached live; client tests cover it.
- **F4** Lobby `SkillChoice` masked per recipient; 4 choices/s.
- **F5** Picker 18 u in `text`, evenly broken lines; `Missed` in `text` for 1 s.
- **Firewall**: a terminal cannot be relocked for 20 s after its lock ends.
- **Balance** (survivor win %, seeds 1–40 · 1001–1040): 1v1 35·25, 1v3 62·68, 1v7 50·55.

**Gates:** build 0/0; Server.Tests 1148/1148; BalanceSweep 18/18; harness 100/100 ×3; EditMode 1567/0/2; BuildWindows fresh (17:33). Live Killer, 7 bots, 300 s: 7 presses, 2 hits, 5 misses, 0 exceptions. Shots: `R1-killer/a2/killer/`.

**Incident:** the shared scratchpad `harness.sh` belonged to another session (integ-1117b, 7793/7794). I ran it once by mistake; its client likely spoiled that session's `harness-pre` H6. Stopped; my 4 stray files removed.
