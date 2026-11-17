# IP-audit QA report, attempt 1

- **Unit:** IP-audit (branch `claude/ip-audit`, uncommitted on main 3675877). **Implementer:** Claude Sonnet. **QA:** Fable.
- **Verdict: PASS**, with 4 minor findings and no blocker or major.
- QA changed no product code. Files that Unity touched only in line endings were reverted; `git status` matches the state before the QA runs.

## Gates

| Gate | Result | Evidence |
|---|---|---|
| Unity EditMode | **1489 / 1489 passed**, 0 failed, 0 skipped | `docs/qa/IP-runs/qa/editmode.xml`, `editmode.log` |
| BuildWindows | **Success** | `docs/qa/IP-runs/qa/build.log` (`Build Finished, Result: Success.`) |
| `dotnet build PingTheKiller.sln -warnaserror` | **0 warnings, 0 errors** | console |
| Live run, `-ptk-auto-sync great` (200 s, 1280x720) | exit 0, no exception; 19 checks judged: 16 Great, 3 Good | `docs/qa/IP-runs/qa/live-great.log`, shots in `docs/qa/IP-screens/qa/great/` |
| Live run, `-ptk-auto-sync miss` (90 s) | exit 0, no exception; every check Fail | `docs/qa/IP-runs/qa/live-miss.log`, shots in `docs/qa/IP-screens/qa/miss/` |

`dotnet test` and the harness were not run: the diff touches no `Shared/`, `Server*` or `Tools/` file.

## Spec table

| # | Brief item | Result | Notes |
|---|---|---|---|
| 1 | Audit written with where / resembles / risk / change | PASS | 1 high, 4 medium, 7 low; third-party list incomplete (F4) |
| 2 | Every high and medium item changed | PASS | all five renames are in code, tests and HUD_SPEC |
| 3 | Terms consistent across HUD, lobby, results, prompts, tests, HUD_SPEC | PASS with F1 | no old term left in any player-facing string in `Client/Assets/Scripts`; one stale line in HUD_SPEC §14 |
| 4 | Sync check no longer a ring with a needle | PASS | horizontal bar, waveform, travelling pulse; does not read as the Dead by Daylight dial |
| 5 | Pulse uses the old time-to-position function | PASS | `SyncCheckState.Needle` and `TryPress` are byte-identical to the old file; `PulseX = 16 + clamp01(fraction) x 288`; tested |
| 6 | Judge, windows, packets, `-ptk-auto-sync` unchanged | PASS | `git diff` is empty for `Shared`, `Server`, `Server.Core`, `Server.Tests`, `Tools`, `Client/Assets/Scripts/Dev`, `ClientApp.cs`, `World`, `Net` |
| 7 | Great placement matches the server judge | PASS | server: Great is `[zoneStart, zoneStart + SyncGreatSize]` (`GameServer.Terminals.cs:274`); packet comment says "its first GreatSize is the great part"; the bar draws the same span |
| 8 | HUD_SPEC placement budget (feet at 67 %) | PASS | measured bar top y = 542 of 720 (75.3 %) with the hacking row shown |
| 9 | 720p floor sizes | PASS | label uses the `s-floor` 15 u rule (12 px); strokes go through `Palette.Stroke` |
| 10 | Reduced motion kept | PASS | the ring had no Reduced-motion branch; none was removed |
| 11 | Store listing names no other game | PASS | grep of `docs/store/*` for the game names and the old terms: no hit |
| 12 | Out-of-scope files untouched | PASS | see row 6; `ClientApp.cs`, `LaggingStutter.cs`, `CharacterMotion.cs`, `KillerAttackKick.cs`, `SkillFx.cs` have no diff |
| 13 | Tests: copy tests and bar math test | PASS with F2 | 4 new bar tests; one assertion is weaker than its message |
| 14 | HUD_SPEC updated and marked `(11/17, IP)` | PASS with F1 | 13.1, 13.2, 13.3, 13.5, 13.6, terms list updated |
| 15 | Report and commit message written | PASS | `IP-audit-impl-a1.md`, `IP-audit-commit-message.txt` |

## Rename review

| Old → new | Consistent | SLOP | Comment |
|---|---|---|---|
| Collapse → Shutdown | yes in code and tests; F1 in HUD_SPEC §14 | ok | fits the network theme |
| Regressing → Rolling back | yes | ok | |
| kick → jam | yes | ok | matches the GDD's own word |
| Hatch → Emergency port | yes | ok | already the GDD and store name |
| Sync check ring → Signal lock bar | yes | F3 | "Signal" is already the fixed term for "visible to the Killer" |

Internal identifiers keep the old words (`HoldKind.KickTerminal`, `CollapseCopy`, `HatchView`, dev flags, the `SyncRing` property). None is shown to players.

## Findings

### F1 (minor) HUD_SPEC §14 still pins the old result reason
- **Repro:** `docs/ui/HUD_SPEC.md` line 849, row 4 "Reason".
- **Expected:** `The shutdown eliminated the Survivors still inside.`, as `ResultV2Copy.Reason` now returns.
- **Actual:** the spec still reads `` Collapse `The collapse eliminated the Survivors still inside.` ``.
- **Fix:** change the quoted copy in that row (the enum name `Collapse` can stay).

### F2 (minor) The Great block reaches into the label row, and the test does not check what it says
- **Repro:** `SignalLockBar.cs`: the Great block spans y 2–50 u (`TrackTop - GreatExtra` to `TrackTop + TrackHeight + GreatExtra`); the label row starts at y 44 u (`RulesV2.uss` `.v2-lock-label { top: 44px }`).
- **Expected:** the block ends above the label row, as the comment and the test message ("and above the label row") say.
- **Actual:** the block overlaps the row by 6 u. At 1280x720 its bottom edge touches the top of the result word when the window sits near the bar centre. The test asserts only `< SignalLockBar.Height` (72), so it passes for any overlap.
- **Evidence:** `docs/qa/IP-screens/qa/great-v2-sync-result-zoom4x.png` (the block ends on the cap height of "Great"); `RulesV2WiringTests.TheSignalLockBarIsAShortWideBarWithItsTrackInsideAndAGreatPartCutFromTheGoodPart`.
- **Fix:** start the label row at 52 u (bar 80 u tall, still below the feet: 74.2 %), or shorten the track; assert against the label top.

### F3 (minor) "Signal lock" reuses the fixed term Signal for a second concept
- **Where:** `SignalLockBar.WaitingText` → `Signal lock · Space`; HUD_SPEC terms list.
- **Expected (SLOP §4, §8):** one term per concept. Signal means "visible to the Killer", and amber is reserved for it.
- **Actual:** the same HUD shows `Signal` (amber, "Visible while you hack.") 70 px below a grey `Signal lock` that means a timing press. A new player can read it as "lock my Signal state".
- **Evidence:** `docs/qa/IP-screens/qa/great/v2-sync-check.png`.
- **Fix (orchestrator's call):** a name without "Signal", for example `Link lock`, `Packet lock` or `Sync lock`.

### F4 (minor) The third-party list misses content outside `Client/Assets/ThirdParty`
- **Where:** `docs/qa/IP-audit-findings.md` section D.
- **Missed:**
  - `Client/Assets/Art/Characters/Mixamo/` — 9 Adobe Mixamo animation FBX files, no license note;
  - `Client/Assets/Plugins/Demigiant/DOTween` — free DOTween, `readme.txt` present;
  - `Client/Assets/Resources/Fonts/Inter-Regular.ttf` — `OFL.txt` is present, which is what the licence requires;
  - `Client/Assets/Art/World/Models/*.fbx` — models that look copied out of a pack (House, Gray Car, Dumpster); the source pack is not recorded.
- **Fix:** add these four rows to the findings and to the proposed `docs/store/THIRD_PARTY.md`.

### Info (no action required in this unit)
- **I1 Third-party assets without license files.** Not a real risk by itself. Asset Store packages are licensed by the Asset Store EULA attached to the buyer's account, not by a file in the folder, and the standard EULA allows shipping them inside a built game without attribution. Mixamo animations are licensed by Adobe's terms for use inside a game. The real risk is proof of origin. Recommendation: keep the order history or invoices, record publisher / package / order ID / licence in `docs/store/THIRD_PARTY.md`, and confirm no pack was obtained outside the Asset Store. The raw files must not be redistributed (the repository must stay private or keep them in LFS that is not public).
- **I2 Residual similarity of the timing check.** The presentation is now distinct: linear bar, waveform, pulse. What remains shared with Dead by Daylight is structural: a narrow bonus part at the leading edge of the success window, one Space press, Great/Good words. That comes from the server judge and is a mechanic, which the brief keeps out of scope. This is a similarity judgement, not legal advice.
- **I3 Legibility at 1280x720.** The bar is 256 x 58 px and reads clearly in both live shots. The Great target is 8 px wide and 39 px tall; the pulse and the Great block are both light vertical bars, told apart by the waveform bump, the dark keyline and motion. Acceptable. The steady wave is `line` on `panel`, nearly invisible, and carries no state.
- **I4 `-ptk-auto-sync great` landed Good on 3 of 19 checks** (`live-great.log`, for example id 11). The Great part is 38 ms long. Dev and server code are unchanged by this unit, so this is existing behaviour.
- **I5 Store listing is behind rules v2** (it describes a top-down Killer, a first-person Survivor and a single Exit). It names no other game. Not an IP item; listed for the next store pass. `ProjectSettings` still has `companyName: DefaultCompany`, `productName: Client`.
- **I6 Q5 names "Shutter" and "Rail"** are not in this worktree; re-check at merge, as the implementer says.

## Audit completeness

Sampled every string literal in `HudCopy`, `LobbyCopy`, `ResultCopy`, `KillerHudCopy`, `RulesV2Copy`, `RulesV2Wiring`, `SkillFeedback`, `SurvivorFeedback`, `SurvivorReadout`, `PlayMenu`, `SettingsScreen`, `ClientScreens`, `ConnectionMessages`, the store documents and 20 live screenshots. Grepped for other games' terms (hook, generator, totem, pallet, vault, sacrifice, trial, perk, terror radius, decode, cipher, impostor and others): no player-facing hit. No code, USS or store file names another game. The audit missed no player-facing or store-facing item; its only gap is the third-party list (F4).

## SLOP §9 score (signal lock bar and renamed copy)

| Check | Points | Score | Notes |
|---|---:|---:|---|
| No V1–V12 visual tells | 20 | 19 | flat fills, palette colours only, no amber; the steady wave is faint decoration |
| No L1–L8; one spacing scale | 15 | 13 | 6 u, 9 u and 13 u values are off the 4 u scale; F2 overlap |
| Copy follows §4 and the vocabulary | 15 | 13 | F3 |
| Every element answers "what state, what action" | 15 | 14 | gate passed |
| Role-specific HUDs; no leak | 20 | 20 | gate passed; Survivor only |
| Interactive states; keyboard and gamepad | 10 | 10 | gate passed; `Space` / `A` glyph kept |
| Contrast and 720p readability | 5 | 4 | 8 px Great target |
| **Total** | 100 | **93** | no gate failure; threshold 85 |

## Evidence paths

- Shots: `docs/qa/IP-screens/qa/great/` (waiting and Great), `docs/qa/IP-screens/qa/miss/` (waiting and Fail), zooms `great-v2-sync-check-zoom4x.png`, `great-v2-sync-result-zoom4x.png`, `miss-v2-sync-result-zoom4x.png`.
- Logs: `docs/qa/IP-runs/qa/editmode.xml`, `editmode.log`, `build.log`, `live-great.log`, `live-miss.log`.

## Orchestrator judgement (Claude Opus, 2026-11-17)

**PASS.** Attempt 1 passed Fable QA. Attempt 2 fixed all 4 minors, and I chose the name `Sync lock` for F3 so "Signal" keeps its one meaning. I reviewed the a2 bar shot (`docs/qa/IP-screens/a2/v2-sync-check.png`): the Great block clears the label, and the bar reads as its own design. Attempt 2 gates: EditMode 1489/1489, BuildWindows OK.
