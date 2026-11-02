# Re-verification Report: Harness (U3c, U3d) — Area B

- **Model:** Gemini 3.8 Flash (Medium) / Antigravity Gemini Flash
- **Role:** Independent QA Reviewer
- **Target Units:** U3c (Anonymous killer view and unknown packets) & U3d (Line-of-sight killer entry and leak detection)
- **Branch:** `claude/reqa-harness-flash` (worktree from `91e8891`, base `48abe4a`)
- **Date:** 2026-11-02

---

## 1. Specification Verification Matrix

| Unit | Specification Section | Description | Status |
|:---|:---|:---|:---:|
| **U3c** | §1 (Ground Truth) | Survivor ground truth taken from survivor stream per tick, never by survivor Id in killer stream | **PASS** |
| **U3c** | §1 (Attribution) | Killer-view survivor entries attributed to nearest true position within epsilon (0.05 m); unexplained entries fail | **PASS** |
| **U3c** | §1 (Moving Baseline) | Controlled moving survivor produces at least one attributed Signal entry | **PASS** |
| **U3c** | §1 (Silent Masking) | Stopped (Silent ≥ 3.1 s) survivor attribution fails H3; byte-level X/Z raw scan fails on matches | **PASS** |
| **U3c** | §1 (Bot Robustness) | Bots walking past (within 0.5 m) do not fail H3; exact float ties handled as evidence, not failure | **PASS** |
| **U3c** | §1 (Tick Alignment) | Killer and survivor streams joined by Tick; missing survivor ticks skipped; ≥ 50 joined ticks required | **PASS** |
| **U3c** | §2 (H6 Rules) | Survivor ground truth from survivor streams; killer progress quantised (0 or 100); ExitOpen requires 100% terminals | **PASS** |
| **U3c** | §2 (SelfState Seam) | Reflection/name resolution for `S2C_SelfState` packet; float timers treated as coincidence when compiled | **PASS** |
| **U3c** | §3 (Unknown Packets) | Unknown PacketIds skipped and counted; malformed lengths and known-Id decode errors still fault | **PASS** |
| **U3c** | §3 (Unknown Packet Scan)| Unknown-Id frames on survivor stream raw-scanned for killer X/Z coordinates at all alignments | **PASS** |
| **U3d** | Contract | Killer entry in survivor world only when recipient Normal/Lagging and 2D segment intersects no map walls | **PASS** |
| **U3d** | Contract Fields | Killer entry has real Id, Role=Killer, real X/Z/Yaw, Flags=0, StatusTimer=0, CaptureCount=0 | **PASS** |
| **U3d** | §1 (Line of Sight) | Independent Liang-Barsky line-of-sight test on closed, uninflated map walls (no server helper called) | **PASS** |
| **U3d** | §1 (Ambiguous Band) | Margins within ±0.05 m of wall boundary not decided (classified as Ambiguous, not pass/fail) | **PASS** |
| **U3d** | §2 (H6 LoS Scan) | Killer entry legal only when line of sight is clear; blocked entry fails H6; entry offsets allowed in byte scan | **PASS** |
| **U3d** | §2 (Byte Leak Scan) | Killer X/Z bytes elsewhere in frame or outside allowed entry offsets fail H6 | **PASS** |
| **U3d** | §3 (H3 Gate) | Silent survivor leak caught beside legal line-of-sight entry | **PASS** |
| **U3d** | §4 (Evidence Lines) | Counts of visible, blocked-absent, ambiguous reported; `--expect-los` requires ≥ 1 visible and ≥ 1 blocked-absent | **PASS** |
| **U3d** | §5 (Tick Coverage) | No edge tolerance; per-stream tick integrity (gap ≤ 2); every survivor world in window must join killer tick | **PASS** |

---

## 2. Broken Fake Attack Verification Matrix

| Attack Category / Broken Fake Variant | Target Check | Expected Result | Actual Result | Status |
|:---|:---:|:---|:---|:---:|
| **Anonymous View:** Stopped survivor emitted anonymously at exact true position | H3 | FAIL (gate) | FAIL (0/20 pts: `Silent survivor 2 leaked in the killer view at tick=...: entry (...) is attributed to it by tick+position (0.0000 m...)`) | **PASS** |
| **Anonymous View:** Stopped survivor emitted anonymously with Signal=0 flag | H3 | FAIL (gate) | FAIL (0/20 pts: `Silent survivor 2 leaked in the killer view at tick=...: entry (...) is attributed to it by tick+position (0.0000 m...)`) | **PASS** |
| **Anonymous View:** Stopped survivor emitted anonymously offset inside epsilon (0.036 m) | H3 | FAIL (gate) | FAIL (0/20 pts: `Silent survivor 2 leaked in the killer view at tick=...: is attributed to it by tick+position (0.0361 m...)`) | **PASS** |
| **Anonymous View:** Stopped survivor emitted anonymously offset outside epsilon (0.064 m) | H3 | FAIL (gate) | FAIL (0/20 pts: `unexplained killer-view entry at tick=... is 0.064 m from the nearest...`) | **PASS** |
| **Anonymous View (Control):** Correct server with anonymous killer view and no leak | H3 | PASS | PASS (20/20 pts: `0 unexplained, 0 to stopped survivor 2`) | **PASS** |
| **Line of Sight:** Killer entry sent to survivor while line of sight blocked by wall | H3 | FAIL (gate) | FAIL (0/20 pts: `Killer 1 leaked to survivor 2 at tick=...: line of sight ... is blocked`) | **PASS** |
| **Line of Sight (Control):** Contract line of sight (no entry when blocked) | H3 | PASS | PASS (20/20 pts: `blocked-and-absent` observed and counted) | **PASS** |
| **Line of Sight:** Killer entry sent to survivor B while blocked behind wall in sight step | H6 | FAIL | FAIL (0/10 pts: `Killer 1 leaked to survivor 3 at tick=...: line of sight ... is blocked`) | **PASS** |
| **Line of Sight:** Killer entry with stale (previous-tick) position while killer moves | H6 | FAIL | FAIL (0/10 pts: `Killer 1 leaked to survivor 2 at tick=...: ... is 0.275 m from the killer's own position ... at that tick (epsilon 0.05 m); it equals the killer's position at tick=... (stale)`) | **PASS** |
| **Line of Sight (Control):** Contract-correct line of sight server | H6 | PASS | PASS (10/10 pts: legal visible entries and blocked-absent observed) | **PASS** |
| **Packet Leak:** Killer X/Z inside survivor-bound `S2C_SkillEffect` (killer caster) | H6 | FAIL | FAIL (0/10 pts: `killer coordinate ... bytes leaked: 1 occurrence(s) in survivor-bound S2C_SkillEffect ... but only 0 legitimate field(s) hold that value`) | **PASS** |
| **Packet Leak:** Killer X/Z bytes inside unknown-Id packet (0x7E) to survivor | H6 | FAIL | FAIL (0/10 pts: `killer coordinate bytes in unknown packet 0x7E (update Shared)`) | **PASS** |
| **Packet Leak:** Stopped survivor coordinates in unknown-Id packet (0x7E) to killer | H3 | FAIL (gate) | FAIL (0/20 pts: `unknown packet 0x7E: ... coordinate byte leak`) | **PASS** |
| **Packet Leak (Control):** Clean unknown-Id frames without killer coordinates | H6 | PASS | PASS (10/10 pts: skipped unknown packets logged in evidence) | **PASS** |
| **Ambiguous Band:** Server shows killer ONLY when segment is within 0.05 m of wall (`--expect-los`) | H6 | FAIL | FAIL (0/10 pts: `--expect-los needs at least one legal line-of-sight killer entry and one blocked-and-absent tick in the survivor worlds; got 0 legal line-of-sight killer entries`) | **PASS** |
| **Ambiguous Band:** Server shows killer ONLY when segment is within 0.05 m of wall (no flag) | H6 | PASS (by design) | PASS (10/10 pts: pre-U2e backward compatibility; ambiguous entries tolerated) | **PASS** |
| **Ambiguous Band:** Killer penetrates wall corner beyond band (0.07 m) | H3 | FAIL (gate) | FAIL (0/20 pts: `line of sight ... is blocked (penetrates a wall by 0.070 m)`) | **PASS** |
| **Ambiguous Band:** Killer penetrates wall corner inside band (0.03125 m) | H3 | PASS (by design) | PASS (20/20 pts: classified Ambiguous, accepted limit U3d-1 / F2) | **PASS** |
| **Strict Contract (Control):** Strictly correct line of sight with `--expect-los` | H6 | PASS | PASS (10/10 pts: `--expect-los: ... legal visible entries and ... blocked-and-absent ticks observed`) | **PASS** |

---

## 3. Findings and Analysis

### Finding 1: Abuse of the ±0.05 m Ambiguous Band
- **Severity:** Accepted Limit / By Design (Documented in README under U3d-1 / F2)
- **Attack Evaluated:**
  1. A server that leaks the killer entry through a wall when penetration depth is ≤ 0.05 m (e.g. 0.03125 m).
  2. A server that shows the killer to survivors *only* when the line-of-sight segment is within 0.05 m of a wall boundary.
- **Analysis & Judgment:**
  - *How much of a leak can hide there?* Exactly up to 0.05 m (5 cm) of wall penetration. Under U3d Spec item 1, the harness is explicitly forbidden from deciding on line-of-sight for pairs within 0.05 m of a wall boundary (`|margin| <= 0.05 m`). Any entry within this band is classified as `SightOutcome.Ambiguous` and granted byte allowance. Thus, an adversarial server can leak the killer through walls up to 5 cm deep without triggering a check failure.
  - *Penetration beyond the band (> 0.05 m):* When wall penetration exceeds 0.05 m (tested at 0.07 m), `margin < -LineOfSight.Band` triggers immediately, and the check fails with: `line of sight from the recipient ... is blocked (penetrates a wall by 0.070 m)`.
  - *Server showing killer only near walls:* When tested with `--expect-los`, this server fails H6 because `Visible == 0` (all entries near walls are ambiguous; none are legal visible entries). Without `--expect-los`, it passes because pre-U2e servers that emit no killer entries are accepted for backward compatibility.
- **Conclusion:** The 5 cm exposure is an intentional and bounded specification decision, not an implementation defect. The harness strictly rejects any penetration > 5 cm.

### Finding 2: Robustness of Anonymous Killer View Attribution (H3)
- **Severity:** No Defect (Robust)
- **Analysis:** The harness attributes non-killer entries in killer-bound frames by Euclidean distance against survivor ground truth. When the stopped survivor is emitted anonymously (Id 0, shuffled, Signal-only or zero flag):
  - At true position: Caught by attribution (`a.ExactTie` is false) and caught by raw X/Z byte scanning.
  - At offset ≤ 0.05 m (inside epsilon): Nearest survivor is the stopped survivor; fails as a Silent leak.
  - At offset > 0.05 m (outside epsilon): Nearest distance exceeds epsilon; fails as an "unexplained killer-view entry".
- **Conclusion:** No anonymous survivor leak can bypass H3 without an exact bit-tie with a bot.

### Finding 3: Robustness of Line-of-Sight and Packet Leak Scanning (H6)
- **Severity:** No Defect (Robust)
- **Analysis:**
  - Blocked entries in survivor worlds are caught in real-time during streaming inspection (step 0 sight step and subsequent steps).
  - Stale positions are detected as soon as the killer moves, triggering an immediate failure with stale position evidence.
  - Unknown-Id frames on survivor streams are scanned at all alignments across all 4-byte windows during the end-of-match leak scan.
  - `S2C_SkillEffect` fields allow caster X/Z only when the caster is an identified survivor; killer coordinates from killer-cast skill effects are caught immediately.
- **Conclusion:** All packet leak vectors and line-of-sight contract violations are detected.

---

## 4. Gate Output Counts

1. **Solution Incremental Build Check:**
   ```powershell
   dotnet build PingTheKiller.sln -warnaserror --no-incremental
   ```
   - **Result:** Build succeeded. 0 Warnings, 0 Errors. (Elapsed: 00:00:02.36)

2. **Re-verification Test Suite:**
   ```powershell
   dotnet test Tools/Harness.Tests/Harness.Tests.csproj --filter FullyQualifiedName~QA.Reverify
   ```
   - **Result:** Passed: 19, Failed: 0, Skipped: 0, Total: 19. (Elapsed: 1 m 54 s)

3. **Full Solution Test Suite:**
   ```powershell
   dotnet test PingTheKiller.sln
   ```
   - **Server.Tests.dll:** Passed: 793, Failed: 0, Skipped: 0, Total: 793. (Elapsed: 21 s)
   - **Harness.Tests.dll:** Passed: 635, Failed: 0, Skipped: 0, Total: 635. (Elapsed: 5 m 56 s)
   - **Total Tests Passed:** 1,428 / 1,428 (100% green across entire solution)

---

VERDICT: PASS
