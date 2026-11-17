# Brief: M1-minors, attempt 1 (saved as given, condensed header only)

Implementer unit for "Ping the Killer". Worktree `PtK-work/m1-minors`, branch `claude/m1-minors` from main 7b23663. Client only; no protocol change. Parallel unit B1-balance owns server rules, bots and balance tests (do not touch). Ports 7783 (sandbox) / 7784 (H6). Never push or merge; no desktop input; no subagents.

Fix list:
1. R1 M2: an eliminating hit on a never-seen Survivor reads `Missed`. Use the flag-length verdict (16 ticks or fewer = hit, 20 = miss, 4 or fewer = interrupted) for every swing; the `LateHit` path must give the normal allowed hit feedback (at least the hit line; kick/impact only if already the hit path). Never show a hit the server did not make. Tests.
2. R1 M3: `Missed` arrives about 1.0 s after the press when a nearby blip went Silent. Make it sooner where safe (e.g. no confirm and no vanish by 0.55 s: `Missed`; vanish: wait for the flag). Keep it correct; if not both, keep correct and say so. Tests.
3. Gamepad Start does not dismiss the intro card (mirror `IntroCardState.ConsumeEscape`); Start and gamepad A/B dismiss it. Tests.
4. `KillerWorldLayer.NearestSwingableBlip` / `NearestNormalBlip`: indexed `for` instead of `foreach` over `IReadOnlyList`.
5. HUD_SPEC 6.1f and the intro card section updated, marked `(11/18, M1)`.

Gates: `dotnet build PingTheKiller.sln -warnaserror`; Server.Tests `Category!=BalanceSweep`; Harness once on fresh servers (`--threshold 100 --expect-los`); Unity EditMode (absolute results path); BuildWindows + live offline Killer run at 1280x720, 7 bots, ~240 s (`-ptk-offline -ptk-bots 7 -ptk-start -ptk-role killer -ptk-chase -ptk-shots <dir> -ptk-quit-after 240 -logFile <path>`): report swings, hits and misses shown vs server log; look at shots. Evidence in `docs/qa/M1-minors/`. Report `docs/qa/M1-minors-impl-a1.md` (English, 250 words max). Commit with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
