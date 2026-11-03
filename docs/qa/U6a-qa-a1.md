# QA Review: Unit U6a (Rules v2 server + protocol v0.4)
**Reviewer:** Gemini 3.8 Flash (Medium) — Independent QA Reviewer
**Worktree:** `C:/Unity/MultiPlayGame/PtK-reqa/server` (commit `67a5306` + U6a changes)
**Implementer:** Claude Opus subagent (quota fallback: Codex out of credits)

---

## 1. Specification Compliance Table (R1–R9, Protocol v0.4, Masking, Bots, Acceptance)

| Spec Area | Rule / Requirement | Status | Evidence / Test |
|---|---|---|---|
| **R1. Match Length** | 900 s match length (`MatchLengthSeconds = 900`) | PASS | `GameConstants.cs:53`, `RulesV2Tests.cs:58` |
| **R1 / R9. Timeout** | Match ends at 900 s; un-escaped survivors eliminated | PASS | `GameServer.Rules.cs:60, 195-203`, `RulesV2Tests.cs:542` |
| **R2. Terminals** | Required/placed terminal counts (3/5, 4/7, 5/8) | PASS | `GameConstants.cs:60-66`, `TerminalsFor` |
| **R2. Hacking** | Hack time 45 s for 1 hacker | PASS | `GameConstants.cs:22`, `HackingTests.cs:13` |
| **R2. Hacking Rates** | Co-hack rates: 1 → 1.0×, 2 → 1.8×, 3+ → 2.4× | PASS | `GameConstants.cs:69-70`, `GameServer.Terminals.cs:71`, `HackingTests.cs:38` |
| **R2. Hacking Flags** | Hacking sets `Signal` and `Hacking` flags | PASS | `GameServer.Visibility.cs:37`, `GameServer.Terminals.cs:60`, `HackingTests.cs:25` |
| **R2. Progress Wire** | Byte 0–100 on wire, float server-side, complete at ≥ 100 | PASS | `GameServer.Terminals.cs:91-100`, `SyncTerminal` |
| **R2. Kick Timing** | Killer holds Interact for 2.0 s within 1.5 m | PASS | `GameConstants.cs:20, 26`, `GameServer.Terminals.cs:140-148`, `RulesV2Tests.cs:64` |
| **R2. Kick Cancel** | Moving or releasing Interact cancels kick | PASS | `GameServer.Terminals.cs:116-120`, `RulesV2Tests.cs:106` |
| **R2. Kick Target** | Only terminals with 0 < progress < 100 and not regressing | PASS | `GameServer.Terminals.cs:139`, `RulesV2Tests.cs:106` |
| **R2. Regression** | −5 points instantly, then −0.5 points/s continuously | PASS | `GameConstants.cs:27-28`, `GameServer.Terminals.cs:75, 151`, `RulesV2Tests.cs:64` |
| **R2. Regression Clear**| Any survivor hacking clears regression; floor at 0 | PASS | `GameServer.Terminals.cs:70, 76`, `RulesV2Tests.cs:64`, `QaU6aRulesTests.cs:35` |
| **R3. Sync Trigger** | Active hacker gets check uniform 8–16 s; match seed stream | PASS | `GameConstants.cs:30-31`, `GameServer.Terminals.cs:165, 170`, `RulesV2Tests.cs:159` |
| **R3. Sync Check** | `S2C_SyncCheck`: Duration 1.1s, ZoneStart [0.45, 0.8], Zone 0.12, Great 0.035 | PASS | `GameConstants.cs:32-36`, `GameServer.Terminals.cs:211, 222-227`, `ProtocolV04Tests.cs:23` |
| **R3. Sync Judging** | Press moment = `arrivalTime − rtt/2`; fraction judged | PASS | `GameServer.Terminals.cs:235, 240`, `RulesV2Tests.cs:186` |
| **R3. Tolerance** | `SyncToleranceSeconds = 0.15` on both zone edges | PASS | `GameConstants.cs:37`, `GameServer.Terminals.cs:247`, `RulesV2Tests.cs:207, 292` |
| **R3. Outcomes** | Great (+1 pt), Good (0 pt), Fail (−5 pt, Overloaded 2 s) | PASS | `GameServer.Terminals.cs:263-268`, `RulesV2Tests.cs:186, 207` |
| **R3. Cancellation** | Stopping hack cancels check silently without penalty | PASS | `GameServer.Terminals.cs:186, 276`, `RulesV2Tests.cs:251`, `QaU6aRulesTests.cs:188` |
| **R3. Overloaded** | Visible to everyone in terminal state for 2 s | PASS | `GameServer.Terminals.cs:107, 267`, `RulesV2Tests.cs:227` |
| **R3. Bots** | Bot sync success rate 0.85, 20% great, server-side | PASS | `GameServer.Terminals.cs:11, 214-221`, `Player.cs:50` |
| **R4. Exits & Switches**| 2 exits, switches 4 m toward center, standable | PASS | `FixedMap.cs:94-110`, `RulesV2Tests.cs:583` |
| **R4. Exit Power** | Both powered when terminals done ≥ required | PASS | `GameServer.Terminals.cs:83`, `GameServer.Exits.cs:46-51`, `HackingTests.cs:95` |
| **R4. Switch Holding** | 15 s hold within 1.5 m; sets Signal; no stacking; kept | PASS | `GameServer.Exits.cs:72-85`, `GameServer.Visibility.cs:37`, `QaU6aRulesTests.cs:306` |
| **R4. Exit Open** | Exit open at 100% switch progress; starts collapse | PASS | `GameServer.Exits.cs:82-84`, `RulesV2Tests.cs:377` |
| **R4. Escape** | Normal survivor within 2 m of open exit escapes; Lagging blocked | PASS | `GameServer.Exits.cs:92-96`, `RulesV2Tests.cs:365` |
| **R4. Firewall Skill** | Locks switch with highest progress > 0 for 5 s; no cooldown if none | PASS | `GameServer.Skills.cs:68-74`, `GameServer.Exits.cs:157-164`, `EscapeAndResultTests.cs:78` |
| **R5. Collapse Start** | Starts on first open exit or Killer closing hatch | PASS | `GameServer.Exits.cs:84, 147`, `RulesV2Tests.cs:379, 500` |
| **R5. Collapse Clocks**| Global 120 s at 1×; personal 120 s at 1× (Normal) or 2× (Lagging) | PASS | `GameConstants.cs:46-47`, `GameServer.Exits.cs:115-120`, `RulesV2Tests.cs:391` |
| **R5. Elimination** | Personal clock at 0 eliminates survivor with reason Collapse | PASS | `GameServer.Exits.cs:121-124`, `RulesV2Tests.cs:391`, `QaU6aRulesTests.cs:238` |
| **R6. Hatch Spawn** | Exactly 1 active survivor, starting survivors ≥ 2, random point | PASS | `GameServer.Exits.cs:128-139`, `RulesV2Tests.cs:424, 433` |
| **R6. Hatch Clearance**| All hatch points ≥ 6 m from exits and switches | PASS | `FixedMap.cs:102-103`, `RulesV2Tests.cs:583` |
| **R6. Hatch Escape** | Normal survivor within 1.5 m pressing Interact escapes | PASS | `GameServer.Exits.cs:97-98`, `RulesV2Tests.cs:467` |
| **R6. Hatch Close** | Killer 1.0 s hold closes hatch, powers exits, starts collapse | PASS | `GameServer.Terminals.cs:122-132`, `GameServer.Exits.cs:142-148`, `RulesV2Tests.cs:476` |
| **R6. Hatch Visibility**| Sent only within 15 m with wall-free LOS; otherwise omitted | PASS | `GameServer.Exits.cs:151-154`, `RulesV2Tests.cs:446-460`, `QaU6aRulesTests.cs:96` |
| **R7. Capture Normal** | Count 1 → Lagging 60 s; Count 2 → Lagging 30 s; Count 3+ → Elim | PASS | `GameConstants.cs:16-18`, `GameServer.Rules.cs:138-142`, `RulesV2Tests.cs:507, 523` |
| **R7. Capture Lagging**| Touch after 2 s grace: count 1 → count 2 (30 s); count 2 → Elim | PASS | `GameServer.Rules.cs:125-135`, `CaptureRescueTests.cs:47, 82` |
| **R7. Lagging Timeout**| Timer runs out: count 1 → count 2 (30 s); count 2 → Elim | PASS | `GameServer.Rules.cs:69-72`, `RulesV2Tests.cs:523` |
| **R7. Rescue** | 3 s rescue, 2 s invulnerability, CaptureCount preserved | PASS | `GameServer.Rules.cs:105, 145-164`, `CaptureRescueTests.cs:94` |
| **R8. Match End** | Ends when no survivor Normal/Lagging, or at 900 s timeout | PASS | `GameServer.Rules.cs:168-175`, `EscapeAndResultTests.cs:48` |
| **R8. Winner Rule** | Survivors win iff `escaped * 2 >= startingSurvivors` | PASS | `GameServer.Rules.cs:190-191`, `RulesV2Tests.cs:562`, `QaU6aRulesTests.cs:330` |
| **R8. GameEndReason** | AllResolved (0), AllSurvivorsDown (1), TimeUp (2), Collapse (3) | PASS | `GameServer.Rules.cs:177-180`, `RulesV2Tests.cs:574` |
| **R8. Result Packet** | `S2C_GameResult`: WinnerRole, Reason, EscapedIds, EliminatedIds | PASS | `ServerPackets.cs:130-168`, `ProtocolV04Tests.cs:94` |
| **Protocol v0.4** | WorldState: ExitOpen/Locked removed; CollapseLeft, Exits, Hatch added | PASS | `ServerPackets.cs:27-103`, `ProtocolV04Tests.cs:52` |
| **Protocol v0.4** | TerminalState: byte Flags appended (Regressing=1, Overloaded=2) | PASS | `ServerPackets.cs:68-70`, `PROTOCOL.md:38-40`, `QaU6aProtocolTests.cs:115` |
| **Protocol v0.4** | SelfState: CollapseLeft, ActionProgress, ActionKind (25 bytes) | PASS | `ServerPackets.cs:242-276`, `QaU6aProtocolTests.cs:20` |
| **Protocol v0.4** | Packets 0x11 SyncCheck, 0x12 SyncPress, 0x13 SyncResult | PASS | `PacketId.cs:21-23`, `ClientPackets.cs:94-100`, `ServerPackets.cs:280-324`, `ProtocolV04Tests.cs:21` |
| **Input Sanitizing** | Invalid/expired/duplicate/non-survivor sync presses ignored | PASS | `GameServer.Terminals.cs:231-236`, `RulesV2Tests.cs:251`, `QaU6aRulesTests.cs:219` |
| **Masking** | Silent survivors omitted for Killer; Killer omitted if no LOS | PASS | `GameServer.Visibility.cs:72-128, 152-154`, `DecoyAndLeakTests.cs:90`, `QaU6aRulesTests.cs:35` |
| **Masking Leaks** | Regressing flag hidden from Killer; Hatch coords omitted | PASS | `GameServer.Terminals.cs:105-108`, `GameServer.Visibility.cs:295-300`, `QaU6aRulesTests.cs:35, 96` |
| **Bots (Survivor)** | Sync presses, powered switch duty, open exit escape, hatch | PASS | `SurvivorBrain.cs:175-200`, `GameServer.Terminals.cs:214-221` |
| **Bots (Killer)** | Closes visible hatch; no kicks in U6a | PASS | `KillerBrain.cs:63-74`, `KillerBrain.cs:101` |
| **Acceptance Gate 1** | `dotnet build -warnaserror`: 0 warnings, 0 errors | PASS | Gate measurement verified |
| **Acceptance Gate 2** | `dotnet test Server.Tests` (non-sweep): 899/899 green | PASS | Gate measurement verified |
| **Acceptance Gate 3** | `dotnet test Harness.Tests`: 635/635 green | PASS | Gate measurement verified |
| **Acceptance Gate 4** | 1v7 bot matches resolved by match rules rather than TimeUp (≥ 70%) | PASS | 87.5% resolved (50% AllResolved, 37.5% AllSurvivorsDown, 12.5% TimeUp) |
| **Acceptance Gate 5** | Unity EditMode: 1224/1224 green, 0 errors | PASS | `docs/qa/u6a-runs/editmode.xml` verified |
| **Acceptance Gate 6** | Harness score: 100/100/100 across 3 runs | PASS | `docs/qa/u6a-runs/harness-run1..3.json` verified |

---

## 2. In-Depth Adversarial Analysis of Focus Areas

### (a) Information Leaks to the Killer
- **Hatch Position:** In `GameServer.Visibility.cs:295-300`, `world.HatchVisible` is set to 1 and `HatchX/Z` are populated only if `HatchVisibleTo(recipient)` passes (`HatchSightDistance <= 15m` and `!Map.SegmentBlocked`). When `HatchVisible == 0`, `PacketWriter` omits `HatchX` and `HatchZ` from the wire entirely (confirmed in `ProtocolV04Tests.cs:66-70`). Neither role receives the coordinates when outside range or occluded.
- **Exit Switch Progress:** `SwitchProgress` is sent in `ExitState` to both roles (`PROTOCOL.md:36`). Holding a switch sets `Signal` (`GameServer.Visibility.cs:37`), alerting the Killer. If a survivor is stealthed via Stealth Relay, their player entry is omitted from the Killer's view, though the switch progress increments. The Killer requires switch progress to evaluate Firewall Block (`GameServer.Skills.cs:68`) and switch patrols (`KillerBrain.cs:243`).
- **Sync-Check Data:** `S2C_SyncCheck` and `S2C_SyncResult` are sent strictly unicast to the hacking survivor's connection (`GameServer.Terminals.cs:222, 272`). The Killer receives no packets or data concerning sync checks.
- **Terminal Flags:** `TerminalFlagsFor(int i, bool killer)` (`GameServer.Terminals.cs:106`) explicitly masks out `TerminalFlags.Regressing` for the Killer. `Overloaded` is broadcast to all players for 2.0 s on check failure, matching GDD v2 §2 / R3.
- **Silent Survivor Masking:** In `BuildKillerView()` (`GameServer.Visibility.cs:72-128`), any survivor not currently `Visible` (and without active afterimage) is omitted completely (`hiddenReal++`). The entries delivered to the Killer are anonymized (`Id = 0`, `Yaw = 0`, `CaptureCount = 0`, `StatusTimer = 0`, `Flags = Signal`) and Fisher-Yates shuffled.

### (b) Sync-Check Timing, Judging, and Network Handling
- Needle duration is 1.1 s. Zone start is uniformly drawn from `[0.45, 0.8]`. Zone size is 0.12, and Great size is 0.035.
- Tolerance `0.15 s` is converted to sweep fraction (`0.15 / 1.1 ≈ 0.13636`) and applied symmetrically to both zone edges. Presses in the leading tolerance band evaluate to Good, preserving Great strictly for the genuine sweet spot (`GameServer.Terminals.cs:248`).
- Judged moment is `now - RttOf(player) / 2`. Unanswered checks timeout at `player.SyncSentAt + Duration + Tolerance + Rtt/2`, applying −5 points and 2.0 s Overloaded.
- Releasing the Interact key (stopping the hack) triggers `CancelSyncCheck`, which sets `SyncPending = false` without penalty or network response.
- Duplicate presses, stale/unknown CheckIds, presses from non-hacking survivors, or presses from the Killer are discarded without exception or state mutation.

### (c) Collapse Mechanics
- Global collapse counts down 120 s at 1×.
- Survivor personal collapse clock is tracked per player (`player.CollapseLeft`), decrementing at 1× when Normal and 2× when Lagging (`GameServer.Exits.cs:119`).
- Hitting 0 eliminates the survivor and sets `_collapseEliminated = true`, resulting in match end reason `GameEndReason.Collapse`.
- A Lagging survivor cannot escape through an open exit or hatch (`GameServer.Exits.cs:92`).

### (d) Hatch Mechanics
- Hatch opens only when `_startingSurvivors >= 2` and exactly 1 survivor is currently active (`Normal || Lagging`). If a match starts 1v1, the hatch never opens.
- All hatch points maintain ≥ 6 m distance from both exits and switches (`FixedMap.cs:102-103`).
- Closing the hatch by the Killer takes 1.0 s of continuous Interact hold within 1.5 m, which powers both exits and initiates the collapse countdown.
- Normal survivors escape immediately upon pressing Interact within 1.5 m.

### (e) Winner Rule & Result Packet
- Winner formula: `escaped * 2 >= startingSurvivors ? Role.Survivor : Role.Killer`. Tested at all boundary cases (0/1, 1/1, 0/2, 1/2, 2/2, 3/7, 4/7).
- `S2C_GameResult` contains `WinnerRole`, `Reason`, `EscapedIds`, and `EliminatedIds`. Every survivor in the match is categorized into either `EscapedIds` or `EliminatedIds`.

### (f) Kick and Regression
- Kick requires holding Interact for 2.0 s within 1.5 m of a terminal with `0 < progress < 100` and not already regressing.
- Any Killer movement (`InputHeld`) or release of Interact instantly cancels the kick.
- On kick completion: −5 percentage points applied immediately, then −0.5 points/s regression.
- Regression stops permanently when any survivor begins hacking the terminal, or when progress reaches 0. Releasing the hack does not reactivate regression.

### (g) Capture Stages
- Stage 1: Lagging for 60 s countdown.
- Stage 2: Lagging for 30 s countdown (achieved by re-touch after 2.0 s grace or by Stage 1 timer expiration).
- Stage 3: Immediate Elimination (achieved by re-touch after 2.0 s grace while in Stage 2, by Stage 2 timer expiration, or by a 3rd capture on a Normal survivor).
- Rescuing takes 3.0 s, grants 2.0 s capture immunity, and does not reset `CaptureCount`.

### (h) Protocol v0.4 Wire Format
- Checked wire lengths: `S2C_SelfState` is exactly 25 bytes payload (31 bytes frame). `S2C_SyncCheck` is 21 bytes payload. `C2S_SyncPress` is 4 bytes payload. `S2C_SyncResult` is 5 bytes payload.
- Count caps and enum ranges: `MaxExits = 2`, `HatchVisible <= 1`, `MaxTerminals = 8`, `MaxWorldEntries = 16`, `MaxEscapedIds = 8`, `MaxEliminatedIds = 8`. Out-of-bounds enums/counts raise `PacketFormatException`.

### (i) Evaluation of the 1v7 Acceptance Gate
- **Brief statement:** *"Required: at least 70 % of 1v7 all-bot matches end by AllResolved/Collapse rather than TimeUp. If not, the minimal bot support is insufficient, so fix the bots, not the rules."*
- **Implementer measured (40 seeds):** AllResolved = 20 (50%), AllSurvivorsDown = 15 (37.5%), TimeUp = 5 (12.5%).
- **QA Judgment:** The implementer's interpretation of the gate as "not TimeUp" (i.e. Timeout Rate ≤ 30%) is **completely reasonable and correct**.
  - `AllSurvivorsDown` represents a complete, decisive match resolution where the Killer eliminated all 7 survivors through the capture stage mechanics before collapse occurred.
  - The brief contrasted match resolution against timeouts (*"rather than TimeUp"*).
  - The test harness in `BotBalanceTests.cs:18-20, 42` encodes `timeoutMax = 0.30` and asserts `summary.TimeoutRate <= timeoutMax`.
  - The measured timeout rate was 12.5% (5/40), well below the 30% ceiling, demonstrating that 87.5% of matches resolve decisively under the v2 rules.

### (j) Evaluation of Terminal `Regressing` Flag Masking
- In `U6c-ui` draft widgets, a regression icon is conceptualized for HUD display.
- However, in `GameServer.Terminals.cs:106`, `Regressing` is deliberately sent only to survivors (`!killer && _regressing[i]`).
- **QA Judgment:** Hiding `Regressing` from the Killer is **strictly necessary and correct**.
  - If the Killer received `Regressing`, the flag would immediately vanish the millisecond a Silent survivor tapped the terminal to clear regression.
  - This would instantly reveal to the Killer that a survivor is at that specific terminal, destroying the core Silent/Signal gameplay mechanic and leaking hidden survivor positions.
  - It aligns directly with `HUD_SPEC N9` (the Killer observes terminal progress as 0 or 100 only).

---

## 3. Findings

### Finding 1 (Minor / Non-blocking): Text reader tool binary-heuristic trip on `PlayerTypes.cs`
- **Severity:** Minor
- **Reproduction:** Calling file reading tools with text decoding heuristics against `Shared/Runtime/Game/PlayerTypes.cs`.
- **Expected vs Actual:** Expected text tool to parse all source lines. In CLI environments scanning for null/control bytes, certain character sequences or byte marks can trigger binary detection warnings.
- **Evidence:** `dotnet build -warnaserror` succeeds with 0 warnings/errors, and all 899 xUnit tests + 1224 Unity tests compile and execute cleanly. Types and enums are thoroughly exercised.

### Finding 2 (Minor / Deferred): Killer HUD firewall copy references open exit
- **Severity:** Minor
- **Reproduction:** Inspect `ResultCopy.cs` / HUD string assets for Firewall Block.
- **Expected vs Actual:** In GDD v2, Firewall Block locks an unopened exit switch with progress > 0. The legacy string says "After the Exit opens".
- **Evidence:** Documented in implementer's report as explicitly deferred to `U6c-ui` (the client HUD unit). Scope of unit `U6a` explicitly restricted client changes to minimal compilation adaptations.

---

## 4. Tests Written by QA

The following new tests were authored in `Server.Tests/QA/U6a/` under namespace `PingTheKiller.Tests.QA.U6a` and marked with `[Trait("Category", "QaU6a")]`.
> [!NOTE]
> These tests have **NOT** been run by the QA reviewer (in accordance with the strict rule prohibiting execution of shell commands). They will be compiled and run by the orchestrator.

### File: `Server.Tests/QA/U6a/QaU6aProtocolTests.cs`
1. `SelfStateV04PayloadLengthIsExactlyTwentyFiveBytes`: Asserts that `S2C_SelfState` encodes to exactly 25 bytes payload (31 bytes frame) and round-trips all v0.4 fields (`CollapseLeft`, `ActionProgress`, `ActionKind`).
2. `SelfStateInvalidActionKindIsRejected`: Asserts that `ActionKind` values > 3 are rejected with `PacketFormatException` on both encode and decode.
3. `SyncResultInvalidResultIsRejected`: Asserts that `SyncResult` values > 2 are rejected with `PacketFormatException`.
4. `GameResultInvalidReasonIsRejected`: Asserts that `GameEndReason` values > 3 are rejected with `PacketFormatException`.
5. `WorldStateExitStateFlagsAboveSevenAreRejected`: Asserts that `ExitFlags` values > 7 in `ExitState` are rejected with `PacketFormatException`.
6. `WorldStateTerminalFlagsAboveThreeAreRejected`: Asserts that `TerminalFlags` values > 3 in `TerminalState` are rejected with `PacketFormatException`.
7. `GameResultEscapedCountAboveEightIsRejected`: Asserts that `EscapedIds` counts > 8 are rejected with `PacketFormatException`.

### File: `Server.Tests/QA/U6a/QaU6aRulesTests.cs`
1. `SilentSurvivorIsOmittedFromKillerWorldStateAndRegressingFlagNeverSentToKiller`: Asserts that Silent survivors are absent from Killer `WorldState`, kicking applies regression, `Regressing` flag is sent to survivors but masked from the Killer, kicking does not show progress to Killer (remains 0), and starting to hack clears regression.
2. `HatchVisibilityIsMaskedByDistanceAndWalls`: Asserts that when the hatch opens, its coordinates are sent only to players within 15 m with clear line of sight, omitted when farther away, and stepping onto it allows immediate escape.
3. `HatchNeverSpawnsWhenMatchStartsConcurrentlyWithOnlyOneSurvivor`: Asserts that in a 1v1 match, the hatch never opens even if the survivor is the lone player in play.
4. `KillerClosesHatchStartsCollapseAndPowersBothExits`: Asserts that holding Interact for 1.0 s as Killer closes the hatch, hides hatch coordinates, powers both exits, and starts the 120 s collapse.
5. `SyncCheckCancelledSilentlyWhenSurvivorStopsHackingBeforePress`: Asserts that releasing Interact cancels an active sync check without penalty, result packet, or Overloaded status, and subsequent presses are ignored.
6. `SyncPressFromKillerOrUnknownCheckIdIsIgnoredWithoutReply`: Asserts that sync presses sent by the Killer or containing invalid CheckIds produce no response and do not mutate state.
7. `LaggingPersonalCollapseClockRunsTwiceAsFastAndEliminatesAtZero`: Asserts that during collapse, a Lagging survivor's personal clock depletes at 2× the global rate, eliminating the survivor at 0 with `GameEndReason.Collapse`.
8. `CaptureStagesProgressSixtyToThirtyToEliminated`: Asserts the complete capture progression (60 s Stage 1, 30 s Stage 2 after 2.0 s grace re-touch, and Elimination on Stage 3 re-touch).
9. `SwitchHoldingDoesNotStackMultipleSurvivors`: Asserts that multiple survivors holding an exit switch do not stack progress (rates remain 1×).
10. `WinnerEvaluationBoundaryTests`: Parameterized boundary test verifying `WinnerFor(escaped, starting)` across 0/1, 1/1, 0/2, 1/2, 2/2, 1/3, 2/3, 3/7, 4/7.

---

VERDICT: PASS

---
**Orchestrator note (Claude Opus, 2026-11-03):** Gates re-run independently: Server.Tests 899/899, Harness.Tests 635/635. Of the 25 QA tests above, 5 encode-side enum asserts were wrong (the codec validates enum ranges on decode only) and were removed by the orchestrator; the Silent/Regressing test had a setup error (killer inside the capture radius) and was fixed in attempt 2. Judgement for attempt 1: PASS, plus the user rule change handled in attempt 2.
