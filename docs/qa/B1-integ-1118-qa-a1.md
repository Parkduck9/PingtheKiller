# B1-balance + M1-minors a2 integration QA a1 (Fable)

Reviewed: `claude/integ-1118` at 327d35f (main 7b23663 + B1 c52ad14 + merge of `claude/m1-minors` 6a5d6c4). Implementers: Claude Opus (B1), Claude Sonnet (M1). Reviewer: Fable. No product code edited, nothing committed. Evidence: `docs/qa/B1-integ-1118-qa-a1/`.

**Verdict: PASS**, with 1 major finding about the acceptance criterion and its test (not about the rule change), and 6 minor findings. No blocker.

- The Killer is clearly stronger at 1v7: Survivor win 50.6 % → 41.7 % over 9480 fresh seeds per tree (difference −8.9 points, standard error 0.7).
- One spec bullet fails **as written** on the merged tree: the two fixed 40-seed sets give 1v7 48 % and 53 %, not 38–46 %. B1 alone gives 45 % and 43 %. The M1 one-tick swing gap reshuffles the matches; the real mean does not move (B1 alone 42.5 %, merged 42.1 % on the same 2000 seeds). See F1. I recommend fixing the criterion and the test, not the constants.

## 1. Balance

Tool: the implementer's sweep (`docs/qa/B1-balance/sweep`), built against four source snapshots exported with `git archive` into my scratchpad (main 7b23663, B1 c52ad14, M1 6a5d6c4, HEAD 327d35f). `git worktree add` failed in the scratchpad and left nothing behind; main was never touched. Outputs: `B1-integ-1118-qa-a1/sweep/`.

### Survivor win %

| Setup | Seeds | main | B1 only | M1 only | **HEAD** |
|---|---|---|---|---|---|
| 1v7 | 1–40 (fixed) | 50.0 | 45.0 | 57.5 | **47.5** |
| 1v7 | 1001–1040 (fixed) | 52.5 | 42.5 | 50.0 | **52.5** |
| 1v7 | 7001–7240 (mine) | 52.9 | 47.9 | 49.6 | **47.9** |
| 1v7 | 30001–32000 (mine) | 50.5 | 42.5 | 50.7 | **42.1** |
| 1v7 | 40001–46000 (mine) | 50.7 | – | – | **41.6** |
| 1v7 | 5001–5240 (implementer's) | 49.6 | 40.0 (reported) | – | **40.8** |
| 1v7 | 20001–21000 (implementer's) | 49.7 | 39.7 (reported) | – | **40.5** |
| 1v7 | **all 9480 non-fixed** | **50.6** | – | – | **41.7** |
| 1v3 | 1–40 / 1001–1040 | 52.5 / 47.5 | 47.5 / 47.5 | 55.0 / 50.0 | **50.0 / 47.5** |
| 1v3 | 7001–7240 | 56.3 | 52.1 | 55.4 | **49.6** |
| 1v3 | 30001–32000 | 51.6 | 45.7 | 51.8 | **46.0** |
| 1v3 | 40001–46000 | 53.5 | – | – | **48.0** |
| 1v3 | **all 9480 non-fixed** | **53.0** | – | – | **47.9** |
| 1v1 | 1–40 / 1001–1040 | 32.5 / 42.5 | 37.5 / 35.0 | 32.5 / 45.0 | **37.5 / 37.5** |
| 1v1 | 7001–7240 | 34.6 | 33.8 | 34.6 | **33.8** |
| 1v1 | 30001–32000 | 31.5 | 30.8 | 31.7 | **30.8** |

The implementer's B1-only numbers reproduce exactly (45 / 42.5 on the fixed sets).

### 1v7 detail (40001–46000, 6000 seeds per tree)

| | main | HEAD |
|---|---|---|
| Median length | 596 s | 602 s |
| Ended before 4 min | 0.0 % | 0.0 % |
| Time-ups | 2 % | 2 % |
| Captures per match | 12.01 | 12.89 (+7 %) |
| Eliminated by third capture | 2.04 | 2.38 (+17 %) |
| Escaped per match | 3.40 | 2.98 |

### Targets

| Target | Result |
|---|---|
| 1v7 at 38–46 % on both fixed sets | **FAIL as written on HEAD** (47.5 / 52.5). PASS on B1 alone. F1 |
| 1v7 240-seed mean about 42 % | 7001–7240: 47.9; 5001–5240: 40.8. Large mean 41.7 %: PASS in substance |
| Nothing on a band edge | 1001–1040 is at 52.5 % against the test's 55 % limit: one more Survivor win is still inside, two fail. F1 |
| Killer clearly stronger than main | PASS: −8.9 points, about 12 standard errors |
| Captures rise visibly | Weak: +7 % per match (12.0 → 12.9); the gain is mostly in third captures |
| 1v3 at 40–55 % | PASS: 50.0 / 47.5, large mean 47.9 % |
| 1v1 inside its band (15–85 %) | PASS: 37.5 / 37.5, 2000-seed mean 30.8 % |
| 1v7 median ≥ 7 min | PASS: 593 s, 582 s, 602 s |
| Under 10 % of 1v7 matches end before 4 min | PASS: 0 % everywhere |

### Overfitting

Confirmed. A 40-seed set has a standard error of about 8 points, so a real 42 % lands in the 38–46 % window only about 37 % of the time per set, and about 14 % for both sets. The implementer's grid found constants that hit both; any later change that alters swing timing by one tick moves both sets again, which is what the merge did. The real balance is stable across every large sample (40.5–42.5 %).

## 2. Side effects of the rule changes

| Place | Result |
|---|---|
| Server hit check (`GameServer.Combat.cs` `SwingTarget`) | reads `SwingRange` |
| `BlipTracker.SwingCaptureRadius` | `SwingRange + .2f` = 3.15 m, follows |
| Switch progress on the wire (`GameServer.Exits.cs`, `Visibility.cs`) | reads `ExitSwitchSeconds` |
| Client hold prediction (`V2Cues.Rate`) | reads `ExitSwitchSeconds` |
| Survivor bot planning (`SurvivorBrain`) | reads `ExitSwitchSeconds` |
| Harness H6 (`ScenarioCheck.cs` 578–583), Harness.Tests fake servers | read the constant; comments still say 15 s (F5) |
| Intro card, How to play, HUD copy | no range or switch number in any string; nothing to update |
| Attack prompt (`KillerHudCopy.AttackPromptRange = 3f`) | hard-coded, does not follow the constant (F3) |
| `RulesV2Preview.ExitSwitch` (`t / 15f`) | hard-coded 15, dev preview only (F5) |
| HUD_SPEC 6.1f, GDD v2 (switch, swing table, §11) | updated to 2.95 m, 17 s, 3.15 m |

Live: Exit 2's switch went 10 % → 92 % in 13.99 s (5.86 % per second, 17.1 s for the full hold), and the own hold read 0.06 after 1.00 s (1/17). Because Harness.Tests compiles the constant into its fake servers, I ran it although `Tools/` is untouched: 641/641.

## 3. M1 a2 on the merged tree

| Item | Result | Notes |
|---|---|---|
| F1: stale `Missed` cleared | PASS | `ClearMissed` on `SwingEvent.Started` and on the capture-confirm hit. Live: miss shown at t=76.56, next swing started at 76.86, hit at 77.06; the shot at 77.27 (`u8-killer-capture-3-strike.png`) has the impact and the `Lagging` tag and no `Missed` (it would have lasted to 77.56 before) |
| F2: one clear tick between chained swings | PASS | See below |
| F3: impact only for a held verdict | PASS | `LastHitWasHeld` is set from `held` at the fall; `held` only rises on a vanish inside this pending swing, which also sets `VanishedPoint` |
| 18-tick early miss | PASS | A hit's flag is exactly 16 ticks; `Observe` runs on every WorldState in order (TCP, `ClientApp.cs:293`), and a late-seen rise only shortens the measured length. A hit cannot reach 18. Live: a held miss read at 0.93 s after the press |
| F4: docs | PASS | HUD_SPEC says 17 ticks, the "cannot be made sooner" sentence is gone, the brief says 17 |
| Interaction with B1 | PASS | The vanish radius is derived (3.15 m) and HUD_SPEC says so. A wider radius holds more verdicts for Silent vanishes, so those misses read at 0.9 s instead of 0.55 s; consistent with the rule |

**F2 detail.** `Swing()` clears `AttackRequested`, and when a recovery ends in this tick it puts the press back and refuses to start, so the swing starts on the next tick. A swing still starts only from `SwingNone` through `CanStartSwing`; the carried press has already passed the Seq and throttle checks and is consumed once.

Scratch xUnit run against a copy of HEAD (`sweep/chain-scratch-test.cs.txt`, `sweep/chain-test.txt`), 400 ticks per case, press every 1, 2, 3, 4, 5, 7, 16, 17, 20 and 21 ticks, first swing a hit or a miss: 14/14 passed.
- Every flag span is exactly 16 (hit) or 20 (miss) ticks; every gap is at least 1 tick; the wire flag equals the server phase in every tick.
- At 5 presses per second (every 4 ticks) the gaps are 1, 3, 1, 3 …: no press on an idle Killer is dropped (18 swings in 400 ticks).
- A press in any of swing ticks 1–19 starts nothing, then or later.
- Presses faster than `MaxAttacksPerSecond` (every 1 or 2 ticks) show occasional gaps of 11–15 ticks. That is the existing throttle, not the new gap.

The implementer's test `ChainedSwingsEachGetTheirOwnFlagSpan` presses once per 20 ticks, which is 1 per second, not the "5 per second" its comment says. It lands on the end tick every second press and asserts spans of 20, so it does catch the old merge (40-tick spans). It is enough to pin the bug, but it does not cover the real 5-per-second pattern (F7).

## 4. Masking and protocol

Unchanged. `Shared/` differs from main only in two `GameConstants` values and comments; no packet, field or serializer change; `PROTOCOL.md` untouched. The server diff is the swing start gate and a `KillerBrain` comment. The Killer receives nothing new: the one-tick gap changes only the timing of the own `Swinging` flag it already gets. Harness H3 and H6 leak scans pass in all three runs.

## 5. Gates (re-run on 327d35f)

| Gate | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror --no-incremental` | 0 warnings, 0 errors |
| Server.Tests, `Category!=BalanceSweep` | 1149 passed, 0 failed |
| Server.Tests, `Category=BalanceSweep` | 18 passed. 1v7: 48 % (seeds 1–40, time-ups 8 %) and 53 % (1001–1040) |
| Harness ×3, fresh servers, 7795 sandbox / 7796 H6 | 100/100, gatesPassed True, three times |
| Harness.Tests (extra) | 641 passed, 0 failed |
| Unity EditMode, absolute results path | 1618 total: 1616 passed, 0 failed, 2 skipped |
| BuildWindows (`BuildScript.BuildWindows`) | Success; `PingTheKiller.Client.dll` and `ServerCore.dll` rebuilt 02:59 |
| Live Killer, 1280x720, 7 bots, 300 s | 22 swings. Server: 10 hit, 12 miss, 0 interrupted. Client: 22 wind-ups, 10 hit (1 eliminating, read from the flag), 12 Missed. Same order, one to one. 0 exceptions. 3 Survivors eliminated, no result at 300 s |
| Live Survivor, 1280x720, 7 bots, 300 s | Own Survivor was rescued twice and rescued an ally once; held a switch (0.06 after 1 s), then was eliminated by a third capture at the switch; a bot opened Exit 2 at 294.5 s. 0 exceptions |

After the Unity runs 19 files differed in line endings only (`DOTweenSettings.asset`, character assets, two `ProjectSettings` files); all reverted. The tree is clean apart from this report and its folder. Every server, test host, editor and player I started has exited.

## 6. Shots looked at

- `live-killer/u8-killer-capture-3-strike.png`: hit 0.5 s after a `Missed`; impact, `Lagging` tag, no `Missed`. Correct (M1 F1).
- `live-killer/r1-killer-miss.png`: taken at the `Missed` event; the 6.1c slot shows `Slowed by traffic overload · 1.0 s`, not `Missed` (F4).
- `live-killer/r1-killer-elim-hit.png`: taken at the eliminating hit; the slot shows `Slowed by traffic overload · 1.9 s`, not `Hit` (F4).
- `live-killer/play-1s.png`: Killer intro card, bottom centre, four lines, no range or switch number.
- `live-survivor/play-1s.png`: Survivor intro card, `Hack 5 terminals, then hold an exit switch and escape.`, no switch time.
- `live-survivor/survivor-rescue.png`: `Lagging`, `Being rescued — stay close`, progress bar about a third.
- `live-survivor/exit-open.png`: `Exit 1 9%`, `Exit 2 Open`, `Shutdown 2:00`, `Eliminated`, `Disconnected: caught 3 times.`

## 7. Findings

### F1 (major, acceptance and test; no rule defect): the fixed-set 1v7 target is not met on the merged tree, and BalanceSweep cannot tell B1 from main
- Repro: `dotnet test Server.Tests --filter "Category=BalanceSweep"` on 327d35f, or the sweep tool on seeds 1–40 and 1001–1040.
- Expected (brief): 1v7 at 38–46 % on both fixed sets, nothing on a band edge.
- Actual: 47.5 % and 52.5 %. Seeds 1001–1040 give the same 52.5 % as main. The test passes because its band is 30–55 %.
- The comment in `BotBalanceTests.Configs` says the band will "fail a return to the pre-B1 1v7 balance". It does not: main scores 50.0 / 52.5 on these sets and passes 30–55 %.
- Cause: 40 seeds carry about ±8 points of noise; the constants were picked partly to land both sets, and the M1 one-tick gap reshuffled them. The real mean is unchanged at about 42 %.
- Risk if left: any later change that touches swing or bot timing can push a 40-seed set past 55 % or under 30 % with no real balance change (roughly 5–7 % per set per change).
- Evidence: `B1-integ-1118-qa-a1/gate-balance.log`, `sweep/sweep-head.txt`, `sweep/sweep-b1.txt`, `sweep/sweep-main.txt`, `sweep/sweep-big.txt`.
- Fix (tests and docs only; keep 2.95 m and 17 s):
  1. Add a large-seed 1v7 check to the BalanceSweep category: 2000 all-bot seeds run in about 45 s on this machine; band 38–46 % (standard error 1.1 points). Main fails it at 50.5 %.
  2. Keep the 40-seed tests as smoke checks and correct the comment.
  3. Record the merged-tree fixed-set numbers (48 / 53) in GDD v2 §11 and the report, and state the target as the large-seed mean.

### F2 (minor, docs): numbers in GDD v2 §11 and the impl report are B1-only
- GDD v2 §11 and `B1-balance-impl-a1.md` give 1v7 45 % · 43 % and 1v3 48 % · 48 %; HEAD gives 48 · 53 and 50 · 48.
- `GameConstants.cs` and GDD say the range gain is "1v7 −7, 1v3 −1". For both changes together I measure 1v7 −8.9 and 1v3 −5.1 over 9480 seeds; the implementer's 1000-seed set (20001–21000) shows 1v3 −1.2 and is the outlier. 1v3 is still inside 40–55 %.

### F3 (minor, pre-existing, made tighter by B1): the attack prompt range is hard-coded at 3 m
- `KillerHudCopy.AttackPromptRange = 3f`; the reach is now 2.95 m.
- The prompt appears 0.05 m outside the reach, on blip positions that are 100 ms old. In the live Killer run the scripted driver pressed 13 times at 2.97–3.00 m: 4 hit, 9 missed.
- Expected: the prompt follows `SwingRange` (the brief's check), and a press on the prompt usually connects.
- Fix: derive it from `GameConstants.SwingRange` (for example equal to it, or slightly inside), and update HUD_SPEC 6.1p ("within 3 m") and `KillerAttackR1QaTests`.

### F4 (minor, pre-existing): `Missed` and `Hit` are hidden while the Killer is slowed
- Repro: a Survivor's Traffic overload slows the Killer (2 s); swing during it.
- Expected: the swing's verdict is readable.
- Actual: `KillerFxState.Line` returns the Slowed line first, so neither `Missed` nor `Hit` shows. Seen twice in one 300 s run: `r1-killer-miss.png`, `r1-killer-elim-hit.png`.
- HUD_SPEC line 948 gives this order on purpose (Slowed > own skill > reason), written before the swing lines existed. Needs a decision: a second line, or the verdict above Slowed.

### F5 (minor): leftover 15 s
- `Client/Assets/Scripts/UI/RulesV2/RulesV2Preview.cs:161` uses `t / 15f` (dev preview shots only).
- Comments: `Tools/Harness/ScenarioCheck.cs` 9, 14, 546; `Tools/Harness.Tests/ScenarioServer.cs` 32, 399; `QA/U3b/QaRulesServer.cs` 72; `U3bBrokenFakeTests.cs` 22; `U3dRound3Tests.cs` 342. The code in all of them reads the constant.

### F6 (minor, M1 behaviour to note): a chained swing cuts `Missed` short
- With F1's fix a new swing clears `Missed`. Pressing again at once leaves `Missed` up for 0.5 s after a plain miss (shown at 0.55 s, next rise at 1.05 s) and for 0.15 s after a held miss (shown at 0.9 s).
- Acceptable (the new swing's own verdict follows), but the held case is close to unreadable. Not seen as a problem live.

### F7 (minor, test): the chained-swing test's comment and coverage
- `ChainedSwingsEachGetTheirOwnFlagSpan` says "(5 per second)" but presses once per 20 ticks. Add a case pressing every 4 ticks with the 1, 3, 1, 3 gap pattern, and one asserting that a press in swing ticks 1–19 starts nothing.

## 8. Unverified

- No human play: every balance number is bot against bot. The Killer bot still swings from 2.75 m, so a human using the full 2.95 m may gain more than the bots show.
- `LateHit` (a hit on a never-seen Survivor) did not occur live; unit tests and code read only.
- F3's stale-point case (frame stall over 0.25 s) not reproduced; code read and the unit test only.
- Gamepad input on hardware.
- The Survivor run did not complete a full own switch hold (eliminated after 1.4 s); the 17 s time is confirmed on a bot's hold and by tests.
