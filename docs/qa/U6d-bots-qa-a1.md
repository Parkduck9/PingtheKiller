# U6d-bots QA Review (Attempt 1) — Gemini Flash

**Reviewer:** Gemini Flash (Independent QA)  
**Unit:** U6d-bots (Bot AI v2), Attempt 1  
**Implementer:** Codex `gpt-6.1-sol`  
**Working Directory:** `C:/Unity/MultiPlayGame/PtK-reqa/client`  

---

## 1. Spec Conformance

| Spec Bullet | Status | Evidence (File & Line) | Notes |
|---|---|---|---|
| **1. Survivors: spread** | **PASS** | [`SurvivorBrain.cs:48`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L48), [`SurvivorBrain.cs:114-118`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L114-L118), [`SurvivorBrain.cs:455-462`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L455-L462), [`SurvivorBrain.cs:483`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L483), [`SurvivorBrain.cs:16`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L16) | `TerminalCap = 2`. `Occupants` counts lower-Id teammates within 3 m. Removed 1-worker cap & 210/360 s ramp. Pacing retuned (`HackBurstMin=1.5, HackBurstMax=2, HackPauseMin=0.5, HackPauseMax=1.5`). Progress prioritized (`-terminal.Progress * 200f + Distance`). |
| **2. Survivors: hide** | **PASS** | [`SurvivorBrain.cs:232`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L232), [`SurvivorBrain.cs:253-270`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L253-L270), [`SurvivorBrain.cs:377-400`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L377-L400), [`SurvivorBrain.cs:234-246`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L234-L246) | Triggers when `KillerVisible` or acute `Threat >= 2`. `PickRefuge` scores line-of-sight blockage (+1000f). Resumes when unseen and calm timer expires. Camp rule retains 20 s motionless check (`CampSeconds = 20`) to avoid camped terminal. |
| **3. Survivors: rescue split** | **PASS** | [`SurvivorBrain.cs:428-453`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L428-L453), [`SurvivorBrain.cs:248-252`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L248-L252), [`SurvivorBrain.cs:498-503`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L498-L503) | Only nearest eligible Normal ally commits (`theirs < mine || ... other.Id < view.SelfId`). Evaluates `arrival` time against `ally.StatusTimer`. Relaxes quiet/threat conditions when `ally.StatusTimer < arrival + 12`. Urgent rescue takes precedence. |
| **4. Survivors: end game** | **PASS** | [`SurvivorBrain.cs:164-175`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L164-L175), [`SurvivorBrain.cs:184-196`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L184-L196), [`SurvivorBrain.cs:502-520`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L502-L520), [`SurvivorBrain.cs:612-620`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L612-L620), [`SurvivorBrain.cs:177-182`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L177-L182) | Switch duty prioritizes exit with highest `Progress`. Open exits make all Normal bots escape. `CanRescueBeforeEscape` models Lagging ally 2x collapse rate (`CollapseLaggingRate`). Hatch logic preserved for last survivor in play. |
| **5. Survivors: skills** | **PASS** | [`SurvivorBrain.cs:561-563`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L561-L563), [`SurvivorBrain.cs:577-580`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L577-L580) | `TrafficOverload` triggers when Killer visible within 10 m or Threat >= 3 while moving. `StealthRelay` triggers during hack when Threat >= 1. |
| **6. Killer: predictive chase** | **PASS** | [`KillerBrain.cs:146-155`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L146-L155), [`KillerBrain.cs:72-78`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L72-L78), [`KillerBrain.cs:91-103`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L91-L103) | Anonymous blip velocity computed (`Distance(bx, bz, _observedX, _observedZ) <= 6 * elapsed + 1`). Predicted target extrapolated up to 1.5 s (`Math.Min(1.5, ...)`), followed by searching around `_searchX, _searchZ`. |
| **7. Killer: patrol and kick** | **PASS** | [`KillerBrain.cs:67-69`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L67-L69), [`KillerBrain.cs:254`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L254), [`KillerBrain.cs:203-221`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L203-L221), [`KillerBrain.cs:133`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L133) | Terminal rank boosts recent signals (< 60 s, <= 6 m) by -200 score. 2 s kick hold executed on arrival if no unblocked normal blip in sight. Lagging blips skipped. |
| **8. Killer: end game** | **PASS** | [`KillerBrain.cs:240-242`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L240-L242), [`KillerBrain.cs:252`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L252), [`KillerBrain.cs:257`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L257), [`KillerBrain.cs:272-280`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L272-L280), [`KillerBrain.cs:80-90`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L80-L90) | Identifies exit with highest `Progress`. Alternates patrol between leading switch and nearby terminals (<= 18 m). Firewall block and hatch close behaviors maintained. |
| **9. Killer: skills** | **PASS** | [`KillerBrain.cs:262-271`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L262-L271) | PingScanner uses 1 s delay from last blip. Overclock triggers during chase (`_hasTarget`) within 3-10 m. |
| **10. Phantoms** | **PASS** | [`GameServer.Bots.cs:20-24`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/GameServer.Bots.cs.txt#L20-L24), [`GameServer.Phantoms.cs:56`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/code.diff.txt#L56) | Survivor hack/walk pacing synchronized to `GameServer.Bots.cs`. Decoy terminal points varied (`WhiteNoiseMinTerminalPoints + Next(WhiteNoiseTerminalPointChoices)`). |
| **11. Information fairness** | **PASS** | [`BotViews.cs:34`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/BotViews.cs.txt#L34), [`BotViews.cs:49-53`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/BotViews.cs.txt#L49-L53), [`GameServer.Bots.cs:100`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/GameServer.Bots.cs.txt#L100) | `CollapseLeft` matches SelfState personal timer. Killer view exposes only boolean `Done` on terminals (zero progress visibility). Switch progress is public to both roles. |
| **12. Restored bounds** | **PASS** | [`tests.diff.txt:115`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/tests.diff.txt#L115), [`tests.diff.txt:187`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/tests.diff.txt#L187), [`tests.diff.txt:96`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/tests.diff.txt#L96) | `MatchLengthTests`: bot timeouts <= 4 (measured 3/15). `U2cAdversarialTests`: AUC < 0.85 (measured 0.773). `IndistinguishabilityTests`: KS gate p >= 0.02 (measured p=1.0). |
| **13. Balance targets** | **PASS** | [`BotBalanceTests.cs:37-40`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/tests.diff.txt#L37-L40), [`U2dQaSweepTests.cs:220-223`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/tests.diff.txt#L220-L223), [`balance-sweep.log.txt`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/balance-sweep.log.txt) | All 4 configs gated on win rate, median duration, and timeout rate across Seeds 1-40 and 1001-1040. Idle killer exit escapes asserted and met. |
| **14. Scope constraints** | **PASS** | [`code.diff.txt`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/code.diff.txt) | `Shared/`, client code, packet protocol, rules constants, and sync judge untouched. `GameServer.Phantoms.cs` edit justified by phantom mix migration. |

---

## 2. Fairness Analysis

### Fields Read by SurvivorBrain
- `view.Time`, `view.SelfId`, `view.X`, `view.Z`, `view.Speed`, `view.Status`, `view.Skill`, `view.SkillReady`
- `view.Threat` (server distance band 0-3)
- `view.CollapseLeft` (received by client in `SelfState`)
- `view.KillerVisible`, `view.KillerX`, `view.KillerZ`, `view.KillerYaw` (only populated when server LoS check `KillerInSightOf` passes)
- `view.Team` (`Id`, `Status`, `Flags`, `X`, `Z`, `StatusTimer`) — carried in `WorldState` to all survivor clients
- `view.Terminals` (`Progress`, `X`, `Z`) — carried in `WorldState`
- `view.Exits` (`Powered`, `Open`, `Progress`, `X`, `Z`, `SwitchX`, `SwitchZ`) — carried in `WorldState`
- `view.HatchVisible`, `view.HatchX`, `view.HatchZ`

**Human Client Parity:** Fully identical. No private server state leaked.

### Fields Read by KillerBrain
- `view.Time`, `view.X`, `view.Z`, `view.Yaw`, `view.Speed`, `view.Skill`, `view.SkillReady`
- `view.Blips` (`X`, `Z`, `Lagging`) — anonymous entries without player IDs, corresponding to active Signals/Lagging targets
- `view.Terminals` (`X`, `Z`, `Done`) — boolean `Done` only
- `view.Exits` (`Powered`, `Open`, `Progress`, `SwitchX`, `SwitchZ`)
- `view.HatchVisible`, `view.HatchX`, `view.HatchZ`

**Terminal Progress Parity:** `KillerBotView.Terminal` does not contain a `Progress` field. `KillerBrain` ranks terminals strictly using `terminal.Done`, distance, and internal history of recent Signal blips observed near terminals (`_signalAt`). It has zero access to partial terminal progress.

---

## 3. Findings

### Finding 1 (Major): Killer Patrol Lockout Causes Prolonged Idle Freezes
- **Severity:** Major
- **Evidence:** [`KillerBrain.cs:249-265`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/KillerBrain.cs.txt#L249-L265):
  ```csharp
  double revisit = view.TerminalCount <= SmallLobbyTerminals ? 150
      : view.TerminalCount <= MidLobbyTerminals ? 110 : 90;
  if (terminal.Done || t - _visitedAt[i] < revisit) continue;
  ...
  if (_patrol < 0)
  {
      Stop(ref intent, view.X, view.Z);
      _lookUntil = t + 2;
      _searchX = view.X; _searchZ = view.Z;
      return;
  }
  ```
  `balance-sweep.log.txt:96`:
  `seed  26:  900.0s Killer   TimeUp           terminals 2/3 cap 0 (2nd 0) res 0 exe 0 lagTimeout 0 escaped 0`
- **Expected:** When all incomplete terminals have been visited within the `revisit` window, the Killer should fall back to patrolling the least-recently visited terminal or actively searching the map.
- **Actual:** If all incomplete terminals were visited within the last 90–150 seconds, `_patrol` evaluates to `-1`. The Killer comes to a dead halt, spins in place for 2 seconds, and repeats this idle cycle indefinitely until the 90–150 s window elapses or a survivor generates a visible Signal blip. In 1v1 (where only 1 incomplete terminal remains), this causes the Killer to freeze in place for up to 2.5 minutes, leading to degenerate 900 s timeouts with 0 survivor interactions (e.g., Seed 26).

---

### Finding 2 (Minor): Unchecked Division by Speed in CanRescueBeforeEscape
- **Severity:** Minor
- **Evidence:** [`SurvivorBrain.cs:615-616`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L615-L616):
  ```csharp
  double arrive = Distance(view.X, view.Z, ally.X, ally.Z) * 1.4 / view.Speed;
  double leave = Distance(ally.X, ally.Z, view.Exits[exit].X, view.Exits[exit].Z) * 1.4 / view.Speed;
  ```
  Compared to [`SurvivorBrain.cs:437`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L437) and [`SurvivorBrain.cs:500`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L500):
  ```csharp
  double arrival = mine * 1.4 / Math.Max(0.1f, view.Speed) + GameConstants.RescueTimeSeconds + 1;
  ```
- **Expected:** Defensive clamping against zero or negative speed (`Math.Max(0.1f, view.Speed)`), matching the implementation in `RescueTarget` and `RescueUrgent`.
- **Actual:** `view.Speed` is used directly as a divisor. If a status effect or edge case sets speed to 0, `arrive` evaluates to `double.PositiveInfinity` or `NaN`.

---

### Finding 3 (Minor): Balance Fragility on Seeds 1–40 (1v7 and 1v1 Win Rates)
- **Severity:** Minor
- **Evidence:** [`balance-sweep.log.txt:167`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/balance-sweep.log.txt#L167) and [`balance-sweep.log.txt:113`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/balance-sweep.log.txt#L113):
  - **1v7 (Seeds 1–40):** Survivor win rate **38%** (GDD band: 35%–65%). Margin to lower threshold is **3%** (1–2 matches from failure).
  - **1v1 (Seeds 1–40):** Survivor win rate **18%** (GDD band: 15%–85%). Margin to lower threshold is **3%** (1 match from failure).
- **Expected:** Win rates comfortably within the target band (> 5% safety margin from band edges).
- **Actual:** Both 1v7 and 1v1 configurations sit within 3% of the lower failure boundary on fixed seeds 1–40, making them fragile against minor tuning adjustments.

---

### Finding 4 (Minor): Terminal Choice Hysteresis Removed
- **Severity:** Minor
- **Evidence:** [`SurvivorBrain.cs:469-487`](file:///C:/Unity/MultiPlayGame/PtK-reqa/client/docs/qa/U6d-review/src/SurvivorBrain.cs.txt#L469-L487):
  `_terminal = -1;` unconditionally resets the choice, eliminating the previous retention check (`if (_terminal >= 0 && ... view.Terminals[_terminal].Progress < 100) return _terminal;`).
- **Expected:** Small stickiness/hysteresis bias toward the currently active terminal to prevent mid-hack abandonment when remote progress fluctuates.
- **Actual:** Any time a teammate vacates a slot at a terminal with higher progress, a hacking bot immediately stops hacking and travels to the other terminal. While progress weighting (-200/%) provides natural stickiness during active hacking, mid-hack retargeting can occur upon remote slot vacancies.

---

VERDICT: FAIL (0 blockers, 1 majors)