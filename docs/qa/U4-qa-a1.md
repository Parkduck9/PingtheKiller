# U4 QA — attempt 1 (independent tests + SLOP review)

Reviewer: Claude Sonnet (implementer was Codex). Unity was NOT launched. Spec: `docs/briefs/U4-a1.md`, `docs/briefs/U4-qa-a1.md`, `docs/GDD.md` 3.2, `docs/ui/SLOP_DEFINITION.md`.

## 1. Tests written

Folder `Client/Assets/Tests/EditMode/QA/` (asmdef `PingTheKiller.Client.QA.Tests`, Editor-only, references PingTheKiller.Client + PingTheKiller.Shared, `optionalUnityReferences: TestAssemblies`, same pattern as the implementer's test asmdef).

| File | Cases | Covers |
|---|---:|---|
| `InterpolationQaTests.cs` | 19 | render time = now-100 ms; 0/50 ms snapshots at now=125 ms give x=5; hold (no extrapolation) past newest; yaw 350->10 and 10->350 short way; before-first-snapshot; fade alpha 0.75/0.5/0.25 at 37.5/75/112.5 ms, monotonic, removed after 150 ms; present entity stays alpha 1; reappear during fade returns to alpha 1 |
| `InputQaTests.cs` | 29 | InputDirection at yaw 0/45/90/180/270, W/A/S/D/diagonals, length 1, no keys; MovementInput: one send per held frame, exactly one zero send on release, silence after, opposing keys still send, Seq strictly increasing across held/zero/later press |
| `EndpointQaTests.cs` | 24 | valid `127.0.0.1:7777`, `192.168.0.10:1`, `10.0.0.1:65535`; invalid null/empty/whitespace/no port/`:7777`/port 0/65536/-1/abc/empty/overflow/space inside/`256.1.1.1`/`1.2.3`/extra octet/two ports; surrounding whitespace; message quality (no `!`, names what to fix, port errors name `65535`) |
| `ThreatQaTests.cs` | 9 | BeepInterval 0 = none (infinity), 1 = 1.2, 2 = 0.6, 3 = 0.25, ordering, out-of-range 4/5/127/255 no crash and interval > 0 |
| `NetClientQaTests.cs` | 13 | loopback fake server: no packet dispatched before `Pump()` (500 ms wait), dispatch only inside Pump on the caller thread, order 1..50; split and coalesced frames; Disconnected only via Pump, once, caller thread; refused connection notice names `127.0.0.1:<port>`; frame lengths 0, 1, -5, 16385, int.MaxValue disconnect; Ping about every 1 s and Pong sets RTT; 4 threads x 50 `Send` lose nothing and keep per-thread order; Connect/Pump/Dispose do not block; Send before connect returns false |
| Total | 93 | |

How verified without Unity: compiled `Shared/Runtime/**`, `Scripts/Net/*`, `ClientMath/MovementInput/SnapshotBuffer` and the QA tests with Unity's own `UnityEngine.CoreModule.dll` and the package's `nunit.framework.dll` in a scratch .NET 8 project (`<scratchpad>/qarun`, outside the repo), LangVersion 9, zero errors/warnings, and ran all cases with a small reflection runner: **93 passed, 0 failed**. One failure in my first run was a bug in my own test (the "in Pump" flag was never set) and was fixed in the test, not by weakening an assertion. This is not a Unity result; the orchestrator's batch run is authoritative (Mono/Unity-hosted, and `RoleCamera`, `ThreatAudio`, `WorldView`, UI were not part of my compile).

Test limitations:
- The incoming queue is private and has a single producer (receive thread). "4 threads concurrently lose nothing" is therefore exercised on the outgoing queue via `Send` (the only multi-producer queue reachable publicly). Incoming drain-only-on-main-thread is tested end to end over TCP.
- The ping-interval test takes about 1 s; refused-connect test waits up to 9 s worst case. All waits have timeouts and all clients/sockets are disposed.

## 2. Spec checklist

| Spec item | Result | Evidence |
|---|---|---|
| Interpolation: render time now-100 ms, x=5 at 125 ms | PASS | `SnapshotBuffer.cs:29`; QA test passes |
| Yaw short way 350->10 | PASS | `ClientMath.cs:21-25` (LerpAngle) |
| Before first snapshot "clamps to the first" | DEVIATION (ambiguous, not failed) | `SnapshotBuffer.cs:32` returns false (entity hidden) rather than clamping. Comment says deliberate: do not reveal newly visible entity early. Test accepts both. See QA-05 |
| Fade 1->0 linear over 150 ms, removed after | PASS | `ClientMath.cs:28`, `SnapshotBuffer.cs:39-43` |
| Reappear during fade returns to full alpha | PASS | `SnapshotBuffer.cs:34-38` |
| Input -> world direction | PASS | `ClientMath.cs:7-13` |
| WASD -> (right, forward) mapping, cursor/focus gating | NOT TESTABLE | inside `ClientApp.Update` `ClientApp.cs:131-133` (MonoBehaviour); finding QA-09 |
| Zero send on release, one per held frame, no idle sends | PASS | `MovementInput.cs:11-19` |
| Seq strictly increasing | PASS within one `MovementInput`; see QA-07 for reset per match | `MovementInput.cs:16`, `ClientApp.cs:98` |
| IP:port validation | PASS | `EndpointParser.cs`; behaviour below |
| Beep intervals 0/1.2/0.6/0.25 | PASS | `ClientMath.cs:27` |
| Clip 880 Hz / 60 ms | NOT TESTABLE, code read only | `ThreatAudio.cs:13-21`: 44100 Hz, `RoundToInt(44100*.06)` = 2646 samples, `sin(2*pi*880*i/rate)`, 5 ms envelope. Reads correct. QA-04 |
| Packet queue drained only on main thread | PASS | `NetClient.cs:56-67`; QA TCP test |
| Ping every 1 s, RTT | PASS | `NetClient.cs:117-121`; QA test |
| Frame length < 2 or > 16384 rejected | PASS (via Shared assembler) | `NetClient.cs:94`; QA test |
| Killer camera ~35 m above center, whole map | PASS by code read, needs human eye | `RoleCamera.cs:39-40`: y=35, rot (90,0,0), FOV chosen so half-extent 34 m is visible at 35 m, also on aspect < 1 |
| Survivor camera 1.6 m, yaw-only sent, cursor locked | PASS by code read, needs human eye | `RoleCamera.cs:22-34,45`, `ClientApp.cs:134` |
| Killer sees no hidden survivors | PASS (client side) | WorldView only creates avatars for ids present in received states; vanished ids fade out |
| Survivor never gets killer avatar | PASS | `WorldView.cs:93` skips Role.Killer when local role is Survivor (defence in depth; server must also not send, GDD 2) |

IP:port behaviour chosen by the implementation: input is `Trim()`med then parsed, so `"127.0.0.1:7777 "` and `" 127.0.0.1:7777"` are accepted (`EndpointParser.cs:13`); whitespace inside the string is rejected. Four decimal octets are required, so `1.2.3:7777` and hex/abbreviated forms are rejected; `localhost:7777` is rejected (message says "IP address", consistent). `[::1]:7777` and `0.0.0.0:7777` are accepted. Messages: format/host errors say `Enter an IP address and port, for example 127.0.0.1:7777.`; port errors say `Enter a port between 1 and 65535.` Both state what to fix (SLOP 4). Minor gap: `256.1.1.1:7777` gets the generic message, not "octets must be 0-255".

## 3. Findings

Severity legend per AGENTS.md 5. No blockers.

| ID | Sev | Finding |
|---|---|---|
| QA-01 | major (unverified, gate risk) | USS selector specificity may defeat the custom look. `ClientUI.uss:13-27` styles buttons with the type selector `Button`, `Button:hover/:active/:focus/:disabled` and `Label`. `ClientTheme.tss` imports `unity-theme://default`, whose rules are class selectors (`.unity-button`, `.unity-label`). In USS a class selector outranks a type selector, so background, border, radius, margin, padding and colour of the default theme can win over the custom rules, leaving the default grey Unity button (SLOP V10, a gate) and no custom hover/pressed/focus/disabled states. `Button.primary` (class + type) is safe. I cannot confirm without pixels. Repro: build, open menu, compare Join/Offline/Cancel to the USS palette; hover/press/Tab each. Expected: flat `#1A1C1F` fill, 1 px `#9A9890` border, 2 px radius, 48 px min height. Fix if confirmed: use `.unity-button` (or add a class to every Button) as the selector. |
| QA-02 | minor | Killer: pressing Esc silently stops all movement and the HUD does not say how to resume. `ClientApp.cs:120-128` sets `releasedCursor` for every role, so `active` is false; `ClientApp.cs:137` passes `releasedCursor && role==Survivor`, so for the killer `ClientScreens.cs:177-178` always prints `Esc: release cursor` and never `Click to resume mouse look`. The hint is also false for the killer (cursor is never locked). Expected: killer Esc does nothing, or the HUD names the state. |
| QA-03 | minor | Spacing scale (4/8/12/16/24/32) broken by 48 and 64 px: `ClientUI.uss:8` (`margin-left: 64px; margin-top: 48px`), `:12` min-height 48, `:16` min-height 48, `:29` height 48, `:38` slot height 48. SLOP L6 / section 8. Card widths (1040/560/400/320) are sizes, not spacing. |
| QA-04 | major (testability) | The 880 Hz / 60 ms clip is built inside `ThreatAudio.Awake` into a private field (`ThreatAudio.cs:11-26`); `Awake` does not run in EditMode and `SetLevel(0)` would NRE (`source` null). No pure seam, so the brief's clip test could not be written. Reviewed by reading only. Suggest extracting `static float[] ThreatAudio.BuildBeep(int rate)` (or into `ClientMath`) so it can be asserted (sample count, zero crossings). |
| QA-05 | minor | Entities are hidden until render time reaches their first snapshot (`SnapshotBuffer.cs:31-32`), spec wording says clamp to first. Effect: every newly visible entity appears 100 ms late and the whole world is invisible for the first 100 ms after match start. Defensible; orchestrator should decide. |
| QA-06 | minor (needs playtest) | The local player is positioned from the same 100 ms-delayed buffer (`WorldView.cs:139-142`, `ClientApp.cs:136`). The brief says only remote entities render in the past, so the survivor camera lags server position by 100 ms plus network latency, with no prediction. |
| QA-07 | minor (server-dependent, unverified) | `ClientApp.cs:98` creates a new `MovementInput` at every `S2C_MatchStart`, so `Seq` restarts at 1. If the server keeps `lastSeq` per session across matches and drops "older" inputs (AGENTS.md 1), the first inputs of a second match would be dropped. Server source is not in this worktree; U2b should confirm it resets per match. |
| QA-08 | minor | RTT is computed at `Pump` time, not arrival time (`NetClient.cs:62-63` ignores `item.Time`), so frame stalls and editor hitches inflate the displayed RTT. Use `item.Time`. |
| QA-09 | minor | Not testable without product edits: WASD axis mapping, Esc/focus gating, cursor locking, camera placement are inside `ClientApp.Update`/`RoleCamera` (needs Mouse/Keyboard devices). Needs a human or an extracted pure function. |
| QA-10 | minor (out of brief scope) | `S2C_GameResult`, `S2C_SkillEffect` are ignored (`ClientApp.cs:82-111`); `playing` stays true so further `S2C_LobbyState` is dropped (`:92`). After a match there is no result screen and no way back to the lobby/menu except closing the app. |
| QA-11 | minor | All clients send nickname `Player` (`NetClient.cs:80`); lobby rows differ only by `· you`. |
| QA-12 | minor (spec-mandated) | User-visible copy contains the internal unit id: `Offline mode arrives in U4b` (`ClientScreens.cs:83`). Brief requires the sentence; it must change before any store screenshot (SLOP 7). |
| QA-13 | minor | `ThreatAudio.SetLevel` resets the timer on every level change (`ThreatAudio.cs:29`), so a survivor oscillating around 12 m or 6 m gets an immediate beep per change. Needs playtest. |
| QA-14 | minor | Only `Inter-Regular.ttf` is shipped; `-unity-font-style: bold` (`ClientUI.uss:9,23`) is synthesised. SLOP 8 asks for weight hierarchy; needs human eye. |

## 4. SLOP review (SLOP_DEFINITION.md section 9)

Evidence is from code and USS only; I could not see rendered pixels.

| Check | Max | Score | Basis |
|---|---:|---:|---|
| V1-V12 visual tells (gates V1 V2 V4 V8 V10) | 20 | 18 | Flat fills only, no gradients/glass/glow/shadows/particles (`ClientUI.uss` whole file); radius 2 px; palette is exactly the section 8 palette; Inter font embedded (`ClientScreens.cs:44-47`); no emoji. V10 not confirmed: see QA-01. Gates V1/V2/V4/V8 pass; **V10 UNVERIFIED** |
| L1-L8 + one spacing scale | 15 | 12 | Left-aligned, one dominant primary button (320 px bold inverted vs 160 px), small 28 px title (`ClientUI.uss:9`), no tutorial wall, no modal. -3: 48/64 spacing outside scale (QA-03) |
| Copy and vocabulary | 15 | 13 | Quoted strings: `Run the server on this PC: dotnet run --project Server`, `Server IP:port`, `Join`, `Cancel connection`, `Offline`, `Offline mode arrives in U4b`, `Lobby`, `Host controls`, `Bots: N`, `- bot`/`+ bot`, `Host role: Random`, `Survivor`/`Killer`/`Random`, `Start`, `Leave`, `Choose a role, then start the match.`, `Add a bot or wait for another player.`, `Waiting for the host to start.`, `Connecting to 127.0.0.1:7777...`, `Signal`/`Silent`, `Exit`/`Exit · Locked`, `RTT: waiting`, errors `Server not reachable at <ip:port> or connection lost. Check that the host is running. <reason>`, `The server did not send a lobby. Check that the server uses protocol v0.2, then rejoin.`, `The server rejected the join (code N). Check available slots and rejoin.`. No exclamation marks (grep clean), no hype, vocabulary matches (Signal/Silent/Exit/Locked). -2: `U4b` (QA-12), false `Esc: release cursor` for killer (QA-02) |
| Every element answers "what state, what action" (gate) | 15 | 13 | Gate PASS. Exit label sits on the exit (`ClientScreens.cs:180-187`), per section 8. Debug RTT line is brief-mandated developer info; `Host controls` label is a pure heading. -2 |
| Role-specific HUDs, no information leak (gate) | 20 | 17 | Gate PASS. `survivor-hud` bottom-centre with Signal/Silent meter; `killer-hud` bottom-left with the signal label hidden (`ClientScreens.cs:161-169`, `ClientUI.uss:43-44`). No survivor counters anywhere; survivor view omits killer avatar. Missing (outside U4 scope): skill + cooldown ring, frozen-ally markers. -3: survivor HUD also carries the debug line while section 8 says "nothing else"; transmit meter is text + 4 px underline, not a true meter |
| States normal/hover/pressed/disabled/focused; keyboard and gamepad (gate) | 10 | 8 | Rules exist for all five states on Button, primary Button and text field focus (`ClientUI.uss:19-27,34-35`); Join/Start focused on screen entry (`ClientScreens.cs:125,159`); Enter submits Join (`:77-80`); navigation relies on `InputSystemUIInputModule.AssignDefaultActions` (`ClientApp.cs:51-53`). Gate **UNVERIFIED** pending QA-01 and a hands-on Tab/arrow/gamepad pass. `chosen` role is a 4 px left border only (weak) |
| Contrast >= 4.5:1; readable at 1280x720 | 5 | 3 | Computed: `#9A9890` on `#1A1C1F` about 5.9:1, on `#101113` about 6.5:1; `#E8E6E1` on `#101113` well above 12:1; `#101113` on `#E8E6E1` same. Pass. Panel scales with 1920x1080 reference and match 0.5 (`ClientScreens.cs:33-36`), so at 1280x720 the 14 px muted/debug text renders at about 9 px. Needs human eye |
| **Total** | **100** | **84** | Pass threshold is 85 with no gate failure. 84 < 85 and V10 and the states gate are unverified |

Needs a human eye (not guessable from code): actual button rendering vs QA-01; bold synthesis; 14 px text at 720p; focus ring visibility on `Button.primary` (`border-width: 4px` in `#9A9890` over `#E8E6E1`); killer top-down framing, local-player ring and exit label placement; terminal/exit colours; beep audibility; keyboard/gamepad traversal order on lobby (bots, role, Start, Leave).

## 5. Verdict reasoning

Functional spec items under test pass (93/93 in my out-of-Unity run), no blocker. The verdict is FAIL because the provisional SLOP score is 84 (below 85) and the V10 and interaction-state gates cannot be cleared without seeing the rendered menu (QA-01). The cheap fixes are: switch USS selectors to `.unity-button`/`.unity-label` (QA-01), fix spacing to the scale (QA-03), fix killer Esc/hint (QA-02), and extract the beep builder for a real clip test (QA-04). If the orchestrator's screenshot shows the custom button look is applied and the batch tests are green, items QA-02/QA-03 alone bring the score to 85-86 and this should be re-judged as PASS.

QA-FAIL
