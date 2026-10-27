# U4b QA — round 2 (a2)

QA: Claude Sonnet (implementer: Claude Opus subagent, a different model). Date 2026-10-27. Branch `claude/u4b-offline`, nothing committed. Scope: SkillIds, lighting, lobby order, real-rules verification, DevArgs, readouts.

## Verdict
- **QA-PASS.** No blocker, no major. Three minor findings and two info items (below). No client leak found.
- SLOP section 9 for the changed screens: **92 / 100**, no gate failure (threshold 85).
- I did not launch Unity, the game or a server. Screenshots were read with the Read tool; pure-black pixels were counted with System.Drawing on the PNGs (read only).

## What I added (only under `Client/Assets/Tests/EditMode/QA/` and this file)
| File | Cases | Covers |
|---|---:|---|
| `HudCopyQaTests.cs` (edited) | 32 (was 31) | **Orchestrator ruling:** `TheSkillTableKeepsItsTodoForTheSharedSkillIds` removed and replaced by `TheNameAndPassiveTablesAreKeyedByTheSharedSkillIdsForEveryId` (every `SkillIds` constant to its GDD name, `IsPassive` true only for (Killer, `PacketAfterimage`) over both roles and all 256 ids) and `TheSkillTableTodoIsGoneAndTheSourceNamesEverySkillIdsConstant` |
| `LobbyOrderQaTests.cs` (new) | 17 | host, other humans, bots, empty last; stable inside a group (ids deliberately not ascending); non-host online view; display only (input and `S2C_LobbyState.Slots` untouched); permutation and length; 500 random lobbies; row numbers 1..8 by code read of `ClientScreens` |
| `RulesReadoutQaTests.cs` (new) | 15 | Lagging countdown from `StatusTimer` (full 20 Hz simulation 60 s to 1 s), Escaped / Eliminated text, `Terminals k/N` with the real `FixedMap.CreateTerminals` N, exit open / locked / firewall-locked derivation in `ClientApp`, `WorldView` and `DevCapture`, result copy per end reason, no killer data on survivor readouts |
| `DevArgsRulesQaTests.cs` (new) | 26 | `-ptk-wander` valid, invalid (0, negative, NaN, Infinity, 1e999, text, comma decimal), missing, next-flag-not-swallowed; `-ptk-rematch` takes no value; inert when absent; the brief's run (a), (b) and online command lines |
| `LightingQaTests.cs` (new) | 12 | URP-only shaders (code and `.mat` guids), background `#101113`, no skybox or fog, ambient / key / fill light set in code, terminals square, muted, never amber, value-separated from floor and walls |
| `WanderQaTests.cs` (new) | 16 | spec of `-ptk-wander` (slow turn, reverse before a wall within 2 m, corners, bad `dt`), 600 s simulations from every spawn on the real `FixedMap`, and the camera-isolation claim |
| 5 new `.cs.meta` files | - | minimal metas, Unity completes them |

New or changed cases: **88** (86 in new files plus the two replacement tests). NUnit categories are `U4bQaA2` only (no forbidden characters).

### How they were run
Unity was not touched. All six files compile under C# 9 against the real `Shared`, `Server.Core`, the client sources they test (`HudCopy`, `ResultCopy`, `LobbyCopy`, `DevArgs`, `Wander`, `SceneLighting`, `SharedMap`) and the real `UnityEngine.CoreModule.dll`, and run in a throw-away net10 console runner with Unity's own `nunit.framework.dll` (scratchpad `qa2\`). Result: **118 cases run (my 6 files), 117 pass, 1 cannot run outside Unity**: `LightingQaTests.TheAmbientProbeIsDarkNeverBlackAndBrightestFromAbove` needs the native `SphericalHarmonicsL2` (ECall). It compiles; it runs in the EditMode gate. Expected values by hand: equator 0.28 everywhere plus a lobe of about 0.07 from above, so every direction sums above 0.1, grayscale stays under 0.35, and up beats forward. The rest of the QA suite was not re-run by me (the implementer's gate covers it).

## Spec bullets of U4b-a2
| Bullet | Result | Evidence |
|---|---|---|
| 1. SkillIds: tables keyed by constants, TODO removed, `IsPassive` uses `PacketAfterimage`, tests green | PASS | `HudCopy.cs` `KillerTable()` / `SurvivorTable()` index by `SkillIds.*`; no `TODO` in the file; the two replacement tests |
| 2. Lighting: URP-compatible, ambient and fill by script, dark palette, no skybox, shaded faces readable | PASS | `SceneLighting.cs:28-37` (skybox null, fog off, Trilight ambient, `ambientProbe`, key 1.2 plus fill 0.35, no shadows); `ClientApp.cs:55` calls `Apply`; `ClientApp.cs:52` background `Color32(16,17,19)` = `#101113`; only `Shader.Find` is `"Universal Render Pipeline/Lit"` (`WorldView.cs:135`) and `WorldLit*.mat` use the URP Lit guid |
| 2. Wall tops lighter than sides | PASS | `a2-light-after-killer.png`: top rim of the boundary walls is lighter than the side faces; probe test (up > side) |
| 2. Terminals do not read like capsules | PASS | square plinth plus panel (`WorldView.MakeTerminal`, `WorldView.cs:120`); `a2-light-after-killer.png` shows six squares versus elongated capsules; colours muted (`Plinth` 66,70,77 and `PanelIdle` 154,152,144), done = team teal, never amber (`WorldView.cs:85`) |
| 2. Before/after from the same camera, four files | PASS | `a2-light-{before,after}-{survivor,killer}.png` exist, same camera pose (same wall corner at x = 496, same boundary). Skill line differs (`Q · Overclock` vs `Passive · Packet Afterimage`, `Traffic Overload` vs `Stealth Relay`) because the skill is random per match; irrelevant to lighting |
| 3. Lobby order: host, humans, bots, empty; stable; display only; rows 1..8 | PASS | `LobbyCopy.DisplayOrder`; 17 tests; `a2-a-lobby-return.png`: `1 Player · host · you`, `2 Bot 1 · bot` ... `7 Bot 7 · bot`, `8 Empty`. `ShowLobby` fills rows from `DisplayOrder` only and row numbers are created once as `(i + 1)` |
| 4. DevArgs `-ptk-wander`, `-ptk-rematch`, inert when absent | PASS | 26 tests |
| 4. Event shots, `[DEV]` lines, wander, rematch | PASS with notes | shots below; I could not re-read the player log (no `Player.log` is kept for this project), so the quoted `[DEV]` lines are the impl report's, checked against the screenshots |
| 5. F-03 to F-07 | PASS | F-04 fixed (`Enter the IP and port of the host PC to join its lobby.`, `ClientScreens.cs`); F-03, F-05, F-06, F-07 skipped as the impl report says (server order, info, protocol). F-03 is visible in `a2-a-lobby-return.png`: bot numbers skip 4 (the player's id), now harmless because the host sits in row 1 |
| Owned files | PASS | `git diff HEAD` shows no change in `Server.Core/`, `Server/`, `Shared/`, `Tools/`, `PingTheKiller.sln` |

## Lighting: before / after (all four screenshots read)
- **Pure black faces:** I counted pixels with every channel below 8 on every second pixel. `a2-light-before-survivor.png` 850, **after 0**. `a2-light-before-killer.png` 10 698, **after 0**. The before killer view has the whole east wall side and the shadow face of the south-east block pure black; after, they are a dark blue-grey.
- **Palette kept:** background pixels exactly `#101113` count 101 492 versus 101 505 (survivor) and 101 705 versus 101 575 (killer): unchanged.
- **Distinguishable:** survivor after shows floor, wall side, far wall and a terminal with a dark plinth side and light panel top. Killer after shows floor, wall tops, wall sides, four blocks, six square terminals, teal survivors and the red killer.
- **Terminals from above:** before = grey blobs with black halves, hard to tell from capsules; after = squares. A survivor standing on a terminal still hides it (capsule over square), which is fine.
- **Same shaded-face rule in real play:** all `a2-a-*`, `a2-b-*`, `a2-c-*` shots contain 0 near-black pixels.

## Real-rules screenshots (all `a2-*.png` read)
Exact strings quoted from the images.

| Screenshot | What it shows | Check |
|---|---|---|
| `a2-a-hacking.png` | `Silent`, `Terminals 0/4 · Exit locked`, `Q · Stealth Relay` | OK for the state. The event is "any terminal at 1..99 %" (a bot), so the local survivor is not hacking and there is **no `Hacking · N%` line in any a2 shot** (see F-A2-04) |
| `a2-a-lagging.png` | `Signal`, `Lagging · 59 s`, `Terminals 1/4 · Exit locked`, `Q · Stealth Relay` | OK; matches the impl log (`Lagging · 60 s` then `59 s`). Lagging is Signal as the GDD says. Wall fills the view, so it is a poor picture but a correct HUD |
| `a2-a-lagging-run1.png` | amber capsules ahead, `Lagging · 59 s`, `Terminals 1/4 · Exit locked` | OK. Amber capsules are Lagging allies (`WorldView.cs:103-104`), not the killer. Old lighting (run 1) |
| `a2-a-result.png` | `Match result`, `Killer wins. No survivor is left in play.`, `You were still Lagging when the match ended.`, `The lobby is open again.` | correct for `AllSurvivorsDown` with a Lagging local survivor. **Back to lobby is already enabled in this frame**, so the "disabled until lobby reopens" rule is shown by the `enabled=False` log line and the code only (`ClientApp.cs:194` disables, `ClientApp.cs:137` enables on `S2C_LobbyState`), not by a picture |
| `a2-a-lobby-return.png` | `Lobby · offline`, host in row 1, `Bots: 6`, `Your role: Survivor`, `Choose a role, then start the match.` | OK |
| `a2-a-rematch-play.png` | `Signal`, `Terminals 0/4 · Exit locked`, `Q · Traffic Overload` | OK. View matches the same 4 s hold-W view of `a2-light-after-survivor.png` (left boundary wall, terminal 7 m east), consistent with the moved 11.62 m in the log |
| `a2-b-hacking.png` | killer view: four teal Signal survivors, one on a terminal, `Q · Ping Scanner`, `Exit · Locked` | OK, no Silent survivor drawn |
| `a2-b-exit-open.png` | four teal terminals, two grey, teal ring, `Exit · Open` | OK, four done terminals matches `Terminals 4/4` of the log |
| `a2-b-result.png` | `Survivors win. Bot 6 and Bot 7 reached the Exit.`, `You lost as the Killer.` | matches `SurvivorEscaped [6,7]` |
| `a2-c-hacking.png` | `Silent`, `Terminals 0/4 · Exit locked`, `Q · White Noise` | OK |
| `a2-c-result.png` | `Killer wins. Time ran out.`, `You did not escape.` | matches `TimeUp` |
| `a2-c-lobby-return.png` | online `Lobby`, `Host controls`, `Host role: Survivor`, host row 1 | OK |
| `a2-c-rematch-play.png` | `Signal`, `Terminals 0/4 · Exit locked`, a wall filling the screen | HUD OK; the picture carries no information (camera against a wall), so the 9.60 m move rests on the log |
| `a2-c-exit-open-run1.png` | `Terminals 4/4 · Exit open`, `Exit · Open`, `Q · White Noise`, teal dashed ring | OK (old lighting) |

### No information leak
- **Survivor never sees the killer:** no red capsule on any survivor screen (`a2-a-*`, `a2-c-*`, `a2-light-*-survivor`); `WorldView.Accept` drops the killer entry before any avatar exists for a survivor client (test `ASurvivorClientDropsTheKillerEntryBeforeCreatingAnyAvatar`); the survivor HUD carries no coordinates.
- **Killer sees only Signal survivors:** `a2-b-hacking.png` shows four of six survivors, the ones on terminals plus movers; `a2-b-exit-open.png` shows two. Masking is the server's (U2b tests); the client draws only what arrives.
- **No killer coordinates on survivor screens or logs:** the dev log prints positions only after the `if (player.Id != localId) continue;` guard (test `TheDevLogOnlyEverPrintsTheLocalPlayersPosition`).

## Camera-dependent new code is isolated (U4c redesign)
PASS. The whole camera-facing surface is: `RoleCamera.Turn` (one line, `RoleCamera.cs`), one `rig.Turn(steered)` call in `ClientApp`, and `DevCapture.Steer` (returns a yaw, names no camera). `Wander.cs` is pure (no Camera, Input, Time, MovementInput). `DevCapture` sends no movement packet. Tests: `WanderIsPureAndNamesNoCameraInputOrNetworkType`, `DevCaptureNeverMentionsTheCameraRigAndSendsNoMovementPackets`, `TheOnlyDevHooksInTheCameraPathAreTheYawTurnAndTheLocalPositionRead`, `TheDevHooksInClientAppAreGuardedSoAnOrdinaryPlayerRunsNone`.

## Findings
| ID | Severity | Title |
|---|---|---|
| F-A2-01 | minor | `Terminals k/N` can read above N (for example `5/4`) |
| F-A2-02 | minor | Firewall-locked exit reads the same as the not-yet-open exit |
| F-A2-03 | minor | `hacking.png` is taken for any survivor, so the local `Hacking · N%` line is never shown in a real-rules shot |
| F-A2-04 | info | Wander stays in a pocket of the map |
| F-A2-05 | info | Two run shots carry no information; Back-to-lobby disabled state is not visible in the result shot |

### F-A2-01 minor: count above N
- Server `Hack()` keeps accepting hackers after the exit opens (`GameServer.Rules.cs`, `completed >= _terminalsRequired` only flips `_exitOpen`), and the map has spare terminals (6 placed for 4 required). A fifth finished terminal gives `Terminals 5/4 · Exit open`.
- Expected: a readout that never exceeds N, or an explicit decision to show the extra. Actual: `HudCopy.TerminalsLine` prints the raw count. The test `MoreFinishedTerminalsThanRequiredStillReadAsKOverTheServersN` only pins no-throw and the `/4` suffix. Suggest `Math.Min(done, required)`.

### F-A2-02 minor: firewall lock wording
- Server: `ExitLocked = !_exitOpen || firewall active`, so (`ExitOpen 1`, `ExitLocked 1`) is "open but blocked for 5 s". Client rule `ExitOpen != 0 && ExitLocked == 0` shows `Exit locked` / `Exit · Locked` for both (tests prove it never says open while blocked, which is the important part).
- A survivor who has finished all terminals and sees `Terminals 4/4 · Exit locked` has no hint that the Firewall Block is the reason. Not a spec bullet of a2 (the brief only requires `Exit open` when it opens); consider `Exit blocked` in a later UI pass.

### F-A2-03 minor: no local hacking line in a real-rules shot
- Brief: "HUD lines readable and correct against the dev log". `hacking.png` fires on the first WorldState with any terminal at 1..99 %, which is a bot in every run, so `a2-a-hacking`, `a2-c-hacking` show `Silent` and no `Hacking · N%`. That line is covered by unit tests (`HackingLine`, nearest terminal) but never seen in real play. A human check is needed (the hold-W run does not press E at a terminal).

### F-A2-04 info: wander is bounded
- Simulating the real `FixedMap` for 600 s from every spawn: it never enters a wall and walks more than 10 m in every 5 s window, so the survivor stays Signal as the brief wants. It does not explore: from (5,5) it bounces around x 21 to 25, z 2 to 12 for the first 190 s; from (10,26) it visits 6 of 36 ten-metre cells. Fine for the purpose (the killer bot comes to it); do not rely on it to touch terminals.

### F-A2-05 info: picture quality
- `a2-c-rematch-play.png` and `a2-a-lagging.png` are camera-against-wall frames. `a2-a-result.png`, `a2-b-result.png`, `a2-c-result.png` already say `The lobby is open again.` and an enabled `Back to lobby` (offline and online lobby reopen within the same frame), so the disabled interval is only in the log. `lagging.png` for run c was skipped by design (the capture ended the match the same tick, logged).

## Out of U4b scope (server and balance observations from the impl report; not client findings)
- Offline matches end very fast: run a ended `AllSurvivorsDown` at 18.1 s (the killer bot downed all six survivors; no exit-open), run b opened the exit at 16.29 s and `SurvivorEscaped [6,7]` came at 18.4 s.
- Online, survivor bots were stuck at 3/4 terminals from about 115 s until `TimeUp` at 599.93 s (run c, final build); in run c1 the exit opened at 66.7 s. Bot behaviour in `GameServer.Bots.cs` is for U2b follow-up.
- Server `Bot N` ids are not contiguous after the human joins (F-03).

## SLOP section 9 (changed screens: play HUDs, lobby, result)
| Check | Max | Score | Note |
|---|---:|---:|---|
| No V1-V12 visual tells (gates V1, V2, V4, V8, V10) | 20 | 18 | flat dark palette, amber only for Signal and Lagging allies, no glow or gradient chrome; -2 for a 3D scene with a soft floor shading band and result/lobby screens that leave about 70 % of the frame empty |
| No L1-L8 layout tells, one spacing scale | 15 | 13 | left-top clustered lobby and result, otherwise a consistent 8 px scale and a fixed-width number column |
| Copy and fixed vocabulary | 15 | 15 | `Lagging · 59 s`, `Terminals 1/4 · Exit locked`, `Killer wins. No survivor is left in play.`; plain sentences, no banner words |
| Each element answers "what state, what action" | 15 | 14 | result and lobby buttons say what they do; HUD lines name state; -1 for the firewall lock ambiguity (F-A2-02) |
| Role HUDs, no forbidden leak (gate) | 20 | 20 | see leak section |
| Interactive states, keyboard and gamepad (gate) | 10 | 8 | no new interactive elements in a2; focus ring on `Start` and `Back to lobby` visible; hover and pressed not provable from static shots |
| Contrast and 1280x720 legibility | 5 | 4 | HUD text is small (about 11 px) but readable; dimmed `Empty` row is intentional |
| **Total** | **100** | **92** | no gate failure, threshold 85 |

## Human checks still open
Audio, E/Q feel, gamepad, F3 line (as the impl report), plus: stand at a terminal and hold E to see `Hacking · N%`, and see the `Exit` text while the Firewall Block is active.

QA-PASS
