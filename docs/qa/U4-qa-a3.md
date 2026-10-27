# U4 QA — attempt 3 (tests + screenshot review)

Reviewer: Claude Sonnet (implementer of a2/a3 was Claude Opus subagent). Unity was NOT launched. Specs: `docs/briefs/U4-a1.md`, `U4-a2.md`, `U4-a3.md`, `docs/ui/SLOP_DEFINITION.md`. Every PNG in `docs/qa/U4-screens/` was opened with the Read tool.

## 1. Tests

Folder `Client/Assets/Tests/EditMode/QA/` (asmdef unchanged).

| File | Status | Cases | Covers |
|---|---|---:|---|
| `MovementRateQaTests.cs` | new | 36 | 1 s hold at 240/144/90/60/45/30 fps = 29..31 sends; one send per frame, gap within one frame of 1/30 s; 5 s hold, every interior 1 s window 29..31; fresh press after idle sends in the same frame; 250 ms hitch (no burst, hold stays under 0.15 s fresh); release at 144/60/30 fps = zero in the release frame, exactly one backup zero one slot later, then 3 s of silence; release after a 3 s hold at 6 frame phases is still immediate; tapping (7 patterns) and 3 seeded random key/jitter/hitch runs: <= 31 sends in any sliding second, Seq strictly increasing, last packet is a zero; Seq across hold-release-hold (4 zeros); quick re-press inside the backup window; a new match on the same connection (`ResetKeys`) does not restart Seq; two connections have independent counters; NaN/negative/infinite dt never bursts |
| `DevArgsQaTests.cs` | new | 58 | inert (null, empty, ordinary player command line, non-`-ptk` flags); every flag valid (join, bots 0/3/7, role x4 incl. case, start, hold-w, quit-after, shots with spaces); 19 invalid values (named in the error, not applied); 6 flags missing at the end; 6 flags followed by another flag (never swallowed, later flag still applies); blank value; unknown `-ptk-*` flag, near-miss prefixes, independence of two parses |
| `BeepSeamQaTests.cs` | new | 30 | `BuildBeepSamples`: length = round(rate x 0.06) at 6 rates, zero crossings 103..108 (about 105.6) at 3 rates, Goertzel check that 880 Hz dominates 440/660/1100/1320/1760, bounded peak 0.05..1, near-silent first/last sample and short attack, deterministic; `SetLevel` before `Awake` does not throw (EditMode, needs Unity); `NextBeepAfterChange`: rising always now, falling never immediate and equals min(scheduledNext, lastBeep + interval) with literal numbers, falling before any beep keeps the schedule, falling to 0 = no beep, GDD 0.6/1.2 s intervals |
| `InputQaTests.cs` | edited | 26 (was 28) | the two per-frame tests `OneSendPerHeldFrameThenExactlyOneZeroSendThenSilence` and `SeqIsStrictlyIncreasingAcrossHeldFramesZeroSendAndALaterPress` removed (superseded by orchestrator ruling, U4-a2 item 1; replaced by the new file, not weakened). `HeldWithOpposingKeysStillSendsEveryFrame` renamed `...WhenASlotIsDue` (legacy overload, one call = one slot); rotation and packet tests unchanged |
| Unchanged | | 65 | `InterpolationQaTests` 19, `EndpointQaTests` 24, `ThreatQaTests` 9, `NetClientQaTests` 13 |

How verified without Unity: compiled `Shared/Runtime/**`, `MovementInput`, `ClientMath`, `ThreatAudio`, `EndpointParser`, `DevArgs`, `SnapshotBuffer` plus the QA test files against Unity's own `UnityEngine.CoreModule/AudioModule/SharedInternalsModule.dll` and the package `nunit.framework.dll` in a scratch .NET 9 project outside the repo (LangVersion 9, zero compile errors), and ran every case with a small reflection runner: **201 passed, 0 failed, 1 skipped** (the skipped case, `SetLevelBeforeAwakeDoesNotThrow`, needs a native `GameObject`; `NetClientQaTests` was not part of this run, it passed 13/13 in attempt 1 and `NetClient.cs` only changed in a2 for RTT/IsServerPacket). Mutation check: removing the "drop missed slots" line and the backup-zero count from a scratch copy of `MovementInput` made 6 of the new tests fail (hitch burst, freshness, release backup x3, Seq/zero count), so the tests can fail. One run of my own beep tests failed on tolerance (`BeepInterval` is a `float`, so 1.2 s is 1.20000005 as a double); fixed the test tolerance to 1e-6, no product assertion was loosened. This is not a Unity result: the orchestrator's batch run is authoritative.

## 2. Findings

No blocker, no major. Severity per AGENTS.md section 5.

| ID | Sev | Finding |
|---|---|---|
| QA3-01 | minor (copy, SLOP 4) | Connection-error line appends the raw OS-localized exception text. Repro: join `127.0.0.1:7799` with no server on a Korean Windows. Expected: one language, actionable sentence. Actual (`a3-menu-error-1280.png`): `Server not reachable at 127.0.0.1:7799 or connection lost. Check that the host is running. 대상 컴퓨터에서 연결을 거부했으므로 연결하지 못했습니다.` The English part says what and what to do (passes SLOP 4); the tail is untranslated platform text, mixes languages and says the same thing again, and "or connection lost" is wrong for a refused connect. Evidence `NetClient.cs:103` (`exception.GetBaseException().Message`), `:126`. Fix: map `SocketError` (refused, timeout, closed) to fixed English phrases or drop the tail. Layout of the two-line message is fine (wraps at 960 px width, Join moves down, no overlap). Hangul renders here via an OS fallback font because `Inter-Regular` has no Hangul; not verified on a machine without one. |
| QA3-02 | minor (carried from QA-12) | User-visible internal unit id: `Offline mode arrives in U4b` (`ClientScreens.cs:100`, visible in every menu shot). Spec-mandated for now; must change before any store screenshot (SLOP 7). |
| QA3-03 | minor (carried from QA-11) | Lobby row `7   Player · player · host · you` (`a3-lobby-1280.png`): every client is called `Player` (`NetClient.cs:81`), and the role word repeats the nickname. Names also start at slightly different x (57 vs 60 px at 1280) because the index is a space-padded string (`ClientScreens.cs:160`) rather than a fixed column, against SLOP 8 "tabular numerals". |
| QA3-04 | minor (layout) | Dead gap between the IP field and Join in the menu (about 48 px at 1280, 58 px at 1920): the empty `.message` keeps `min-height: 22px` plus 12 px and 24 px margins (`ClientUI.uss:25`). It reads as a missing element. Collapse the message when empty. |
| QA3-05 | minor | Survivor HUD carries the debug line (`RTT: 0 ms · Esc: release cursor`, `a3-play-survivor-1280.png`) although SLOP 8 says the survivor HUD is the meter, skill/cooldown and frozen-ally markers and "Nothing else". RTT is brief-mandated developer info; hide it outside a dev flag before store builds. The hint is correctly absent for the killer (`ClientApp.cs:135-143`, `ClientScreens.cs:198`). |
| QA3-06 | minor (cosmetic) | Killer view: the label `Exit · Locked` starts at the ring centre (`ClientScreens.cs:210-211`), so it overlaps the right half of the ring and runs past the map edge at 1280 px (not clipped). Offset it to the side of the ring. |
| QA3-07 | minor (evidence) | `a3-play-survivor-1280.png` is a close-up of a wall face; it proves the HUD but not the camera. The Silent shot does (wall top 66 px above and base 75 px below the horizon, consistent with a 1.6 m eye and a 3 m wall). No other player is visible in either survivor shot, so "survivor never sees the killer" is verified by code only (`WorldView.cs:46`). |
| QA3-08 | info | At 1280x720 the panel scales to 0.8 (`ClientScreens.cs:17,38`): 14 px text renders at about 11 px. Legible in the screenshots and above 4.5:1; needs a human check on a real monitor. The killer map is pillar-boxed (718 of 1280 px wide) because the 35 m camera frames the height; the whole 60x60 map is visible. |

Carried from attempt 1 and now resolved: QA-01 (USS specificity, buttons render flat with 1 px lines and an inverted primary), QA-02 (killer Esc), QA-03 (spacing), QA-04 (beep seam), QA-07 (Seq per connection), QA-08 (RTT from arrival time), QA-13 (falling-threat beep). Still open by design: QA-05 (documented), QA-06 (TODO U4b), QA-10 (no result screen), QA-14 (only regular weight shipped; bold is synthesised, looks acceptable in the title).

## 3. Spec checklist

| Spec | Result | Evidence |
|---|---|---|
| a1 s1 Shared import compiles | PASS (orchestrator gate) | impl report: 0 `error CS`; my scratch compile of `Shared/Runtime/**` plus client code: 0 errors |
| a1 s2 NetClient: background thread, queue drained in Update, Ping 1 s, RTT | PASS | `NetClient.cs:40,56-68,118-122`; RTT from `Received.Time` `:62-64`; shown `ClientScreens.cs:201`; QA `NetClientQaTests` |
| a1 s3 Menu: host line, IP:port + validation, disabled Offline | PASS | `ClientScreens.cs:83-101`, `ClientApp.cs:81`; `a3-menu-1280.png` shows all four, Offline visibly disabled |
| a1 s3 Lobby: 8 fixed slots, host-only bots/role/Start, one primary | PASS | `ClientScreens.cs:102-123,152-179`, `ClientApp.cs:62-64`; `a3-lobby-1280/1920.png` (8 rows, `8   Empty` muted, Start inverted and dominant) |
| a1 s4 World from primitives, killer red / survivor teal, local highlight | PASS | `WorldView.cs:26-27,32-38,51-52`; `a3-play-killer-1280.png` red capsule with white ring, teal capsules, grey terminals, exit ring; map mirrors U2a FixedMap (`MapSource.cs:5`) |
| a1 s5 Interpolation 100 ms, fade 150 ms | PASS | `SnapshotBuffer.cs`, `ClientMath.cs:15-28`; 19 QA tests pass |
| a1 s6 Killer top-down 35 m whole map; survivor first person 1.6 m, yaw only | PASS | `RoleCamera.cs:37-47`; killer shot shows the full 60x60 map; survivor Silent shot geometry matches 1.6 m |
| a1 s7 / a2 item 1 Input at 30 Hz, release zero now + one backup, <= 31/s, Seq monotonic per connection | PASS | `MovementInput.cs:25-48`, `ClientApp.cs:84,110,151`; 36 new QA tests |
| a1 s8 Beep 880 Hz / 60 ms, GDD intervals | PASS in code, audibility needs a human | `ThreatAudio.cs:14-24`, `ClientMath.cs:27`; seam tests pass |
| a2 item 2 USS selectors win over the theme | PASS | all button states visible in screenshots (inverted primary, flat grey buttons, disabled Offline) |
| a2 item 3 spacing scale | PASS | `ClientUI.uss` margins/paddings/offsets all in 4/8/12/16/24/32 (heights/widths/borders are sizes) |
| a2 item 4 killer Esc, no cursor hint | PASS | `ClientApp.cs:135-143`; killer HUD shows only `RTT: 0 ms` |
| a2 items 5, 6 beep seam and level-change rule | PASS | `ThreatAudio.cs:14-34,46-51`; QA tests |
| a2 item 8 no per-packet allocation / reused sets | PASS | `NetClient.cs:129-130`, `WorldView.cs:21-22,56` |
| a2 item 9 BuildScript has no `InitializeOnLoad` | PASS | grep: no `InitializeOnLoad`/`delayCall` in `Client/Assets` |
| a2 item 10 QA-05 comment, prediction TODO | PASS | `SnapshotBuffer.cs:31`, `ClientApp.cs:153` |
| a3 DevArgs pure parser, inert without flags | PASS | `DevArgs.cs`; 58 QA tests |
| a3 layout: no wrapped/overlapping/clipped single-line text | PASS | `a3-menu-1280.png`, `a3-menu-1920.png`, `a3-lobby-1280.png`, `a3-lobby-1920.png`: title, host line, field label all on one line; compared with `a2-menu-1280x720.png` (title wrapped over the host line, `Server IP:port` clipped) the defect is gone |
| a3 screenshots delivered (6 required + 2 extra) | PASS | all 8 files present |
| a3 survivor shows Signal/Silent HUD bottom-centre | PASS | `a3-play-survivor-1280.png`: amber `Signal` with amber underline; `a3-play-survivor-silent-1280.png`: grey `Silent` |

## 4. Screenshot verdicts (what I saw)

- `a2-menu-1280x720.png` (old failure): `Ping the` / `Killer` on two lines over `Run the server on this PC...`; `Server` / `IP:port` clipped by the field. Confirms the a2 reject.
- `a3-menu-1280.png`, `a3-menu-1920.png`: `Ping the Killer` (28 px bold), `Run the server on this PC: dotnet run --project Server` (muted), `Server IP:port`, field `127.0.0.1:7777`, `Join` (inverted, thick focus ring), `Offline` (dark, grey text, thin dark border = disabled), `Offline mode arrives in U4b`. Left aligned, nothing wraps or clips, same layout at 1080p. Findings QA3-02, QA3-04.
- `a3-menu-error-1280.png`: see QA3-01. No overlap.
- `a3-lobby-1280.png`, `a3-lobby-1920.png`: `Lobby`, rows `1   Bot 1 · bot` ... `7   Player · player · host · you`, `8   Empty` (muted); right column `Host controls`, `Bots: 6`, `− bot`, `+ bot`, `Host role: Killer`, `Survivor`/`Killer`/`Random` (Killer has a left bar); `Choose a role, then start the match.`; `Start` (primary), `Leave`. No overlap or clipping; the first row sits tight under the title (about 8 px) but does not touch. Findings QA3-03.
- `a3-play-killer-1280.png`: top-down, entire map and all four walls visible, red local capsule with white ring, four teal capsules, grey terminal discs, exit ring with `Exit · Locked`, `RTT: 0 ms` bottom-left only. No survivor counters, no cursor hint, no information beyond Signal blips. QA3-06, QA3-08.
- `a3-play-survivor-1280.png`: first-person wall face, bottom-centre panel `Signal` (amber) and `RTT: 0 ms · Esc: release cursor`. QA3-05, QA3-07.
- `a3-play-survivor-silent-1280.png`: horizon, wall, floor; panel `Silent` (neutral underline). Shows the role-specific state change without colour alone (word + underline colour).

## 5. SLOP section 9

| Check | Max | Score | Basis |
|---|---:|---:|---|
| V1-V12 visual tells (gates V1 V2 V4 V8 V10) | 20 | 19 | Flat fills, 1 px lines, 2 px radius, palette only from section 8, Inter, no emoji, no gradients/glow/shadow/particles in any shot. Gates PASS, V10 now confirmed by pixels (custom buttons, not the default Unity look). -1 bold is synthesised (QA-14) |
| L1-L8, one spacing scale | 15 | 14 | Left aligned, one dominant primary, 28 px title, no tutorial, no modal, scale respected. -1 dead gap (QA3-04) |
| Copy and fixed vocabulary | 15 | 12 | Signal/Silent/Exit/Locked/Killer/Survivor consistent, no exclamation marks, no hype. -1 `U4b` (QA3-02), -1 mixed-language raw error tail (QA3-01), -1 `Player · player` (QA3-03) |
| Every element answers "what state, what action" (gate) | 15 | 14 | Gate PASS. -1 RTT line is developer info |
| Role-specific HUDs, no information leak (gate) | 20 | 16 | Gate PASS: killer map-as-HUD with exit state on the exit, survivor bottom-centre Signal/Silent, no killer avatar for survivors, no counters. -2 survivor HUD carries extra debug/hint (QA3-05), -2 no skill/cooldown or frozen-ally markers yet (outside U4 scope) |
| States normal/hover/pressed/disabled/focused; keyboard + gamepad (gate) | 10 | 8 | Gate provisional PASS: normal, disabled, focused and chosen verified by pixels; USS has hover/pressed rules (`ClientUI.uss:35-37,49-50`). Hover feel, pressed feel, Tab/arrow/gamepad traversal NEED A HUMAN, not scored from stills |
| Contrast >= 4.5:1, readable at 1280x720 | 5 | 4 | `#9A9890` on `#1A1C1F` about 5.9:1; 11 px muted text legible but small (QA3-08) |
| **Total** | **100** | **87** | Threshold 85, no gate failed |

Needs a human (not guessed): beep audibility and level changes, hover/pressed/focus animation feel, keyboard and gamepad navigation order, 11 px text on a real 720p monitor, Hangul fallback on machines without a Korean OS font, and the `SetLevelBeforeAwakeDoesNotThrow` test (needs Unity).

## 6. Verdict

All spec bullets of U4 a1 to a3 pass with evidence, the new and kept QA tests pass in my out-of-Unity run (201/201 executed), the layout defect that failed attempt 2 is fixed in every screenshot, and SLOP scores 87 with no gate failure. Remaining findings are minor copy/cosmetic items (QA3-01 to QA3-06) and should be fixed before any store screenshot; none blocks U4. The orchestrator's Unity batch run must still confirm the QA assembly compiles and `SetLevelBeforeAwakeDoesNotThrow` passes.

QA-PASS
