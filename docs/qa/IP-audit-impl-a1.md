# IP-audit implementation report, attempt 1 (Claude Sonnet)

**Findings** (`docs/qa/IP-audit-findings.md`): 1 high, 4 medium, 7 low (kept), plus listed-only internal references and third-party assets. No other game's character, logo, map, sound or text was found. Store listing already names no other game; no edit.

**Changed (old -> new)**

| Old | New |
|---|---|
| Sync check ring with needle (DbD skill check look) | Signal lock bar (`SignalLockBar.cs`) |
| Collapse (clock, reason lines) | Shutdown |
| Regressing | Rolling back |
| kick (Terminal) | jam |
| Hatch (prompts, hold label) | Emergency port |

Protocol, identifiers, dev flag names and `Server.Core` logs are unchanged.

**Signal lock bar:** 320x72 u (ring was 144 u tall). Waveform track, a pulse travelling left to right as a bump in the wave with a crest bar. Lock window: Good = `muted` block, Great = taller `text` block, as the first `GreatSize` of the window exactly as the judge reads it (so Great is the leading part, not centred). Pulse x = `NeedleFraction` mapped onto the track; server judge, windows, packets and `-ptk-auto-sync` untouched. The ring had no Reduced-motion branch; none added. Label: `Signal lock · Space`, then Great/Good/Fail. HUD_SPEC 13.1, 13.2, 14 and terms updated (11/17, IP): the bar top is at 75% of 720p height (69.8% with a result line), below the feet (67%).

**Unchanged and why:** Killer/Survivor, Great/Good/Fail, skill names, Exit/Terminal/Rescue (generic or original); sounds are synthesized tones; no third-party asset deleted (none of the 10 folders has a license file; listed). Internal design notes and server logs only listed. Q5 names Shutter/Rail not in this worktree; re-check at merge.

**Tests:** 4 new bar tests (shape budget, pulse at window centre = old needle at zone middle, linear round trip, label); 1 ring test removed; ~20 copy asserts updated. EditMode: 1489/1489 pass (main 1486).

**Gates:** BuildWindows success; `dotnet build PingTheKiller.sln -warnaserror` 0 warnings, 0 errors. Unity-touched unrelated files reverted.

**Shots:** `docs/qa/IP-screens/v2-sync-check.png`, `v2-sync-result.png` (1280x720, `-ptk-dev-hack 120 -ptk-auto-sync good`; result Good). Logs in `docs/qa/IP-runs/`.

**Risks:** the bar is small at 720p (256 px wide); only Good was captured live, Great/Fail are covered by the unchanged judge tests; localized Korean store text does not use the renamed terms; renaming "Collapse" etc. is a judgement call about similarity, not legal advice.

## Attempt 2

Fixes for QA a1 F1-F4. F1: HUD_SPEC result reason now says "shutdown". F2: lock bar is 80 u tall, label row at 52 u (`LabelTop`), test asserts the Great block bottom is above `LabelTop`; HUD_SPEC geometry updated (top 74.2 % of 720p, below the feet). F3: player label is "Sync lock"; `SignalLockBar` renamed `SyncLockBar` (file, .meta, tests, spec). F4: findings section D lists Mixamo, DOTween, Inter (OFL), world FBX, plus an order-history recommendation; `docs/store/THIRD_PARTY.md` template added.
Gates: EditMode 1489/1489; BuildWindows success; live `-ptk-auto-sync great` run exit 0, shot `docs/qa/IP-screens/a2/v2-sync-check.png` (block clears the label). Logs in `docs/qa/IP-runs/a2/`. Unity-only line-ending changes reverted.
