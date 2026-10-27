# Brief U2b — attempt 1: game rules (hacking, capture/Lagging/rescue, skills, win/lose, GameResult, Interact, input validation)

Implementer: Claude Opus (quota fallback — Codex workspace is out of credits; this is not a failed attempt). QA: Claude Sonnet (different model). Read `AGENTS.md`, `docs/GDD.md` v0.2 (§3, §4, §6.3 are the rules source of truth), `docs/PLAN.md` §2 and §5, `Shared/Runtime/**` (protocol v0.2), and all of `Server.Core/Runtime/**` (U2a). Work on branch `claude/u2b-rules` in the main checkout. Do not commit; the orchestrator commits after PASS.

## Owned files
- `Server.Core/Runtime/**` (extend; split into new partial files such as `GameServer.Rules.cs`, `GameServer.Skills.cs` rather than growing existing files)
- `Server/Program.cs` (only the `--bind` option below)
- `Server.Tests/Core/**` (new tests; existing tests must keep passing — if an existing test asserts a U2a placeholder that U2b replaces, e.g. "exit always closed", update it and list it in the report)
- `Shared/Runtime/Game/SkillIds.cs` (**new file only**, additive, no wire change). Do not edit any other `Shared/` file.
- `docs/qa/U2b-impl-a1.md`

Do not touch `Client/`, `Tools/`, `Server.Tests/QA/**` (QA-owned; if a QA test asserts old U2a placeholder behaviour, list it in the report — do not edit it).

## Skill ids (`Shared/Runtime/Game/SkillIds.cs`)
`public static class SkillIds` with byte constants, matching the U2a assignment `Random.Next(4|5)` in GDD §4 order:
- Killer: `PingScanner = 0`, `Overclock = 1`, `PacketAfterimage = 2` (passive), `FirewallBlock = 3`
- Survivor: `DummyPacket = 0`, `StealthRelay = 1`, `TrafficOverload = 2`, `EmergencyRestore = 3`, `WhiteNoise = 4`
Add the GDD §4 durations/radii as constants in the same file (not in `GameConstants.cs`).

## Rules (all server-authoritative, evaluated on the game-loop thread in `Tick`)
Order inside one tick: inputs → movement → statuses/timers → capture → rescue → hacking → exit/escape → win check → broadcast. Status changes take effect in the same tick's WorldState.

1. **Interact (`C2S_Interact { Active }`)**: `Active` 0 = released, 1 = held; any other byte → ignore the packet. Held state persists until released, disconnect, or status change away from Normal. Only Normal survivors can interact; killer Interact is ignored.
2. **Hacking (GDD §3.4)**: a Normal survivor holding Interact within `TerminalRadius` (1.5 m) of an incomplete terminal hacks the nearest such terminal. Progress accumulates 10 s total; rate × (number of survivors hacking that terminal, max `MaxCoHackMultiplier` = 3). Progress is kept when interrupted. `TerminalState.Progress` = 0..100 (floor of percent; 100 only when complete). Hacking survivors are Signal (flag `Hacking`) unless Stealthed. When completed terminals ≥ `TerminalsRequired` → exit opens (`ExitOpen = 1`, `ExitLocked = 0`) for everyone.
3. **Capture (GDD §3.3)**: killer within `CaptureRadius` 1.2 m of a survivor:
   - Normal, not invulnerable, `CaptureCount == 0` → `Lagging`, `CaptureCount = 1`, timer 60 s.
   - Normal, not invulnerable, `CaptureCount ≥ 1` → `Eliminated`.
   - Lagging → `Eliminated` (execution).
   - Escaped/Eliminated are never affected.
4. **Lagging**: speed × 0.25, always Signal (overrides Stealth), cannot hack/escape/rescue/use skills. `PlayerState.StatusTimer` = seconds left before timeout (0 for other statuses). Timeout → `Eliminated`.
5. **Rescue**: any Normal survivor within `RescueRadius` 1.5 m of a Lagging survivor accumulates contact time for that target, automatically (no key). Contact must be continuous: if no rescuer is in range for a tick, that target's progress resets to 0. Several rescuers do not speed it up. At 3 s → target `Normal`, 2 s invulnerability (`PostRescueInvulnerabilitySeconds`), `CaptureCount` stays 1. Rescuers are Signal with flag `Rescuing` while in range.
6. **Escape and win (GDD §3.5)**: a Normal survivor within `ExitRadius` 2 m of the exit while open and not firewall-locked → `Escaped` (removed from play; see masking in rule 11). Any escape → survivors win (`SurvivorEscaped`, `EscapedIds` = everyone who escaped this tick). Else if no survivor is Normal → killer wins (`AllSurvivorsDown`). Else when `TimeLeft` reaches 0 → killer wins (`TimeUp`). Check in that order within a tick.
7. **Match end**: send `S2C_GameResult` once to every connected human, stop the simulation, then return the server to the lobby: same slots/host/bot count, `LobbyState` broadcast, a new `StartMatch` works. Reconnect tokens issued in the lobby stay valid.
8. **Skills (`C2S_UseSkill { SkillId }`)**: valid only during a match, only if `SkillId` equals the player's assigned skill, off cooldown (killer 12 s, survivor 15 s), and status Normal. Anything else → ignored silently (no cooldown consumed). Passive `PacketAfterimage` via UseSkill → ignored. A skill whose precondition fails (e.g. FirewallBlock before the exit opens, EmergencyRestore with no Lagging ally within 1.5 m) → ignored, no cooldown consumed.
   - **PingScanner**: nearest Normal/Lagging survivor is forced Signal for 1.5 s.
   - **Overclock**: killer speed × 1.5 for 3 s, then cannot move for 1 s.
   - **PacketAfterimage** (passive, killer owns it): every survivor stays visible to that killer for 1 s after its Signal ends, with flag `Afterimage` set during that extra second.
   - **FirewallBlock**: open exit is locked for 5 s (`ExitLocked = 1`, escape impossible while locked).
   - **DummyPacket**: a fake Signal at the caster's current position for 3 s (see decoys).
   - **StealthRelay**: caster and allies within 5 m at cast time are Stealthed for 2 s: not Signal even while moving or hacking or rescuing (flag `Stealthed`); does not override Lagging or forced exposure (PingScanner / TrafficOverload).
   - **TrafficOverload**: caster forced Signal for 2 s; killer speed × 0.5 for 2 s (multiplies with Overclock).
   - **EmergencyRestore**: instantly rescues the nearest Lagging ally within 1.5 m (same result as a 3 s rescue).
   - **WhiteNoise**: 5 fake Signals at random walkable points (not inside walls, seeded RNG) for 1.5 s.
9. **Decoys (orchestrator decision, keep exactly):** fake Signals appear only in the killer's WorldState, as `PlayerState` entries that are byte-for-byte shaped like a real Signal survivor (Role Survivor, Status Normal, flag Signal, plausible Yaw, StatusTimer 0, `CaptureCount` copied from the survivor whose Id is borrowed). To be indistinguishable they **borrow the Id of a survivor who is currently absent from that killer's view** (alive, Silent, not Escaped/Eliminated); DummyPacket borrows the caster's Id when the caster is absent, otherwise it is not shown that tick. Each WhiteNoise point keeps one borrowed Id for its lifetime when possible; points without an available Id are not shown. A Player Id never appears twice in one WorldState.
10. **SkillEffect visibility (anti-leak):** `S2C_SkillEffect` goes only to recipients who may know it. Survivor skills → survivors only, except TrafficOverload → also the killer. Killer skills → killer only, except FirewallBlock → everyone (X/Z = exit position) and PingScanner → also the exposed survivor. **No survivor-bound packet may ever contain the killer's coordinates** (survivor-bound SkillEffect X/Z for killer skills = exit position or 0,0). DummyPacket/WhiteNoise SkillEffects are never sent to the killer.
11. **Masking (unchanged core rule, extended):** killer view = killer + Signal survivors (including Lagging, forced, afterimage) + decoys. Escaped and Eliminated survivors are omitted from the killer view. Survivor view = all survivors (any status), never the killer, `ThreatLevel` from the true killer distance.
12. **Bots (GDD §6.3):** survivor bot: go to the nearest incomplete terminal and hold Interact; if `ThreatLevel ≥ 2` stop (Silent) until it drops; if a Lagging ally is within 10 m go to it; if Lagging itself, walk to the nearest Normal ally; when the exit is open, go to the exit. Killer bot: chase the last-seen Signal (as U2a), else patrol terminals. Bots use their skill when off cooldown and the precondition holds, except the killer bot uses PingScanner only when no survivor is visible. Bots must not read state a human in that role could not see (killer bot sees only its masked view; survivor bots get only `ThreatLevel`, not the killer position).
13. **Input validation (carried from U1 QA):** every client byte enum is validated (`Interact.Active`, `UseSkill.SkillId`, `SetRole`, `SetBots`) and every float is checked for NaN/Infinity before use (MoveInput already done in U2a). Invalid → ignore that packet, never throw, never disconnect the sender for a semantic error.
14. **`--bind <ip>` option** for `Server` (default `0.0.0.0`; `127.0.0.1` lets local testing run without the Windows Firewall prompt). Invalid IP → exit code 2 with a message.

## Acceptance tests (`Server.Tests/Core/`, deterministic `Tick(dt)` + `InMemoryTransport`, no sleeps)
1. Hacking: one survivor 10 s → terminal 100 and complete; two survivors → 5 s; four survivors → 10/3 s (cap 3); interrupt at 4 s keeps 40 %; out of range or Lagging → no progress; hacking makes the survivor Signal; required count reached → `ExitOpen = 1`.
2. Capture: first contact → Lagging, CaptureCount 1, StatusTimer ≈ 60 and decreasing; Lagging speed 1.0 m/s; second capture after rescue → Eliminated; killer touching Lagging → Eliminated; invulnerability 2 s after rescue blocks capture, 2.05 s later capture works.
3. Rescue: 3 s continuous contact → Normal; contact broken at 2.9 s resets; Lagging timeout 60 s → Eliminated.
4. Escape: open exit + Normal survivor in 2 m → GameResult SurvivorEscaped with that id; locked exit (FirewallBlock) → no escape for 5 s; Lagging survivor on exit → no escape.
5. Killer wins: all survivors Lagging/Eliminated → AllSurvivorsDown; 600 s → TimeUp. GameResult sent exactly once per human; afterwards LobbyState is broadcast and a second match can start.
6. Each skill: effect, duration, cooldown, ignored-when-invalid (wrong id, on cooldown, Lagging, precondition fails → no cooldown consumed).
7. **Decoy indistinguishability:** for DummyPacket and WhiteNoise, the killer-bound decoy entry has the same field layout/flag/role/status pattern as a real Signal survivor; no duplicate Ids in any WorldState; no SkillEffect for them reaches the killer.
8. **Leak sweep:** over a 3-minute seeded bot match with all skills in play (several seeds), encode every survivor-bound frame and assert the killer's X/Z float bytes never appear; encode every killer-bound frame and assert absent (Silent, non-decoy) survivors' X/Z bytes never appear.
9. Bot match smoke: 1 human + 7 bots for up to 600 s simulated time with several seeds → always ends with exactly one GameResult, no exceptions, no NaN positions.
10. Input validation: Interact Active = 2, UseSkill id 200, NaN in every float field of every client packet → ignored, no throw, connection stays open.
11. `--bind 127.0.0.1` listens only on loopback (test via `TcpTransport` options, not the console host).

Commands: `dotnet build PingTheKiller.sln -warnaserror`, `dotnet test PingTheKiller.sln` (all green), then the real-server harness exactly as `docs/PLAN.md` §5 (fresh `--bots 0` server per run, `--bind 127.0.0.1`) must still score 90/100 with all gates — H6 stays skipped until the harness adds it (separate unit).

## Report
`docs/qa/U2b-impl-a1.md` (English, ≤ 300 words): header noting the quota fallback; files changed; commands and results; every place you made a judgement call the brief did not cover; anything stubbed; existing tests you changed and why.
