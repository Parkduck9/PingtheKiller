# Bot freeze next to an idle killer: implementation report

Implementer: Claude Opus subagent (not Codex). Nothing committed.

## Changed
- `Server.Core/Runtime/SurvivorBrain.cs`
  - :36-41: new `CampSeconds = 20` and `KillerShiftDistance = 0.5`.
  - :154-158: new `_killerMovedAt`. It is set when the killer is seen moving, seen again after more than 3 s out of sight, or seen more than 0.5 m from where it was last seen.
  - :233-254: new `acute` flag: ThreatLevel rose, or `_killerMovedAt` is within 20 s. At ThreatLevel 2 or more, danger, the blind flee and the in-sight "killer within 12 m" pause now need `acute`. ThreatLevel 1 still uses its 8 s rule. The first time the threat goes stale, the bot drops a terminal within 18 m and avoids it for 20 s (`_campNoted`).
  - :494-496: `ChooseTerminal` fallback pass: a terminal near a killer that has been seen but is camping is no longer blocked.
- `Server.Tests/Core/SurvivorCampTests.cs` (new, 10 cases): the brain is driven only through `SurvivorBotView`.
  - Idle unseen killer near the last terminal, 5 seeds: the bot hacks for at least 20 s out of 180 s.
  - Idle killer in sight, same setup: the bot reaches the terminal and hacks.
  - Rising ThreatLevel: the bot stops hacking within 1 s and flees more than 3 m.
  - Killer seen coming: the bot stops hacking.
  - With the old brain, the freeze tests fail. The rise and coming tests fail too, because the bot never starts hacking.

## Verified
- `dotnet build -warnaserror`: 0 warnings.
- `dotnet test Server.Tests` (without BalanceSweep): 948 of 949 pass.
  - MatchLength Killer case passes: 15 of 15 survivor wins, 0 TimeUp, median 630.8 s.
  - MatchLength Survivor case still fails: 7 of 15 time-outs, the same count as before.
- U2c gated AUC: 0.855 (best single feature 0.759).
- Indistinguishability KS: median speed p = 0.0166, still-away p = 0.9997, tail p = 1.
- `Tools/Harness.Tests`: 635 of 635 pass (18 min).
- BalanceSweep (7 s), old brain vs new:
  - All-bot 1v7: identical. Survivor win 0 % / 3 %, TimeUp 28 % / 30 %.
  - Idle killer + 7: win 85 % → 98-100 %, TimeUp 15-20 % → 0 %.
  - The 4 U2d QA sweep failures (U2d win bands) happen with the old brain too.

## Survivor case cause (not a freeze)
I logged seeds 2, 3, 5 and 15 with a temporary diagnostic, since deleted. The killer bot keeps moving, and the remaining bots keep moving and hacking. The real problem is slow progress:
- Bots hack only about 3-9 s in every 30 s (bursts of 2-4 s, pauses of 8-16 s).
- Progress is spread over many terminals (for example 97 %, 85 % and 81 % left unfinished).

## Unverified / risks
- Unity compilation of Server.Core was not run (plain C# only).
- A killer that is unseen, keeps a constant distance band and does not approach for 20 s is treated as camping, so bots take more risk near a patient camper.

## Attempt 2 (QA majors 1 and 2)

Implementer: Claude Opus subagent. Nothing committed.

### Changed
- `SurvivorBrain.cs`
  - :248: `if (acute) _campNoted = false;`. Every new stale phase now notes the camp once. The reset on a ThreatLevel rise in `Remember` (:136) is removed because `acute` covers it.
  - :43, :487, :502: on the fallback pass, a terminal stays blocked while the remembered killer position is within `KillerAtTerminalRadius` (CaptureRadius + HackStopDistance + 1 = 3.6 m) of it. This applies only once the killer was ever seen. In MatchLength, T4 is 7 m from the killer, so it is still workable.
- `SurvivorCampTests.cs`
  - :181 `ASecondCampPhaseLeavesTheTerminalAgain` (3 seeds). After the first camp phase at T4, T1 reopens. The killer then appears 11 m off: still ThreatLevel 2, more than 10 m from T4, watched but not coming. It shuffles, then idles. The bot must hack T1 within 90 s.
  - :217 `AKillerRememberedAtTheTerminalIsNeverWalkedInto` (3 seeds). The killer stands 1 m in front of T4, in sight and idle. Over 120 s the bot must never hack T4 and never come within 2.2 m of the killer.
  - On the attempt-1 brain, all 6 new cases fail: the bot stays at T4, or hacks T4 at t = 55.7, 57.8 and 59.2 s. On attempt 2 they pass.

### Verified
- `dotnet build PingTheKiller.sln -warnaserror --no-incremental`: 0 warnings, 0 errors.
- `dotnet test Server.Tests --filter "Category!=BalanceSweep"`: 955 of 955 pass.
  - MatchLength Killer case: 0 time-outs, median 630.8 s.
  - MatchLength Survivor case: 7 time-outs, within the new bound of 7.
- U2c gated AUC: 0.855 (best single feature 0.759).
- Indistinguishability median-speed KS: p = 0.0166, D = 0.068.
- `dotnet test Tools/Harness.Tests`: 635 of 635 pass (17 min). It was a full run, because most Harness fixtures start servers with bots.
- BalanceSweep (report only), the same as attempt 1:
  - 1v7: survivor win 0 % / 3 %, TimeUp 28 % / 30 %.
  - Idle killer + 7: win 100 % / 98 %, TimeUp 0 %.

### Unverified / risks
- Unity compilation of Server.Core was not run.
- A killer that was last seen at a terminal keeps that terminal blocked on the fallback pass until it is seen elsewhere. The normal pass is not affected once the 12 s danger memory expires.
