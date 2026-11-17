# HUD and screen spec — D3 UI pass

Status: camera revision (GDD §2, 2026-10-27) SPEC-PASS (86) with review fixes applied · N1–N9 decided 2026-10-27 (§11.1), positional sound added (§6.5.1), re-review below · earlier: attempt 3 SPEC-PASS (89), decisions recorded 2026-10-27 (§11) · author: Claude Opus (orchestrator) · reviewer: Claude Sonnet, `docs/qa/UI-spec-review.md`

> **한국어 요약:** 메뉴·로비·매치 인트로·생존자 HUD(3인칭)·살인마 HUD(1인칭)·결과·재접속 화면의 구현 명세. 모든 요소는 그 역할의 클라이언트가 실제로 받는 패킷 필드, 정적 데이터, 또는 그 둘에서 계산한 값(명시)으로만 만든다. 생존자는 살인마의 위치·방향을 절대 알 수 없고(살인마 캐릭터는 그려지지 않음), 살인마 화면은 Id를 전혀 쓰지 않는 익명 인스턴스(시야 안: 공통 캐릭터, 벽 뒤: Signal 마커)로 그려 가짜 Signal(디코이)과 진짜를 구별할 수 없게 한다. 카메라 변경으로 생긴 새 질문은 §11.1에 있다.

**Changes in U7b (11/04):** new §14 Skill feedback: a world effect, a result line and a sound per skill and viewer, with the decoy-safety and no-Killer-coordinates rules; a new Survivor result line (14.1a). Pointers added in §4.8, §6.1, §6.5, §10 and §12.

**Changes in the camera revision (GDD §2 changed 2026-10-27: Survivors third-person orbit, Killer first person):**
- **Survivor (§4):** new §4.10 camera rig, movement relative to camera yaw, wall pull-in, a framing check that keeps the character above every bottom-center block, readability rules (no state tint on the own character, tags clear the own silhouette, the prompted terminal's label is emphasised). Spectate (§4.5) is now an orbit around the spectated Survivor.
- **Killer (§6, rewritten):** map-as-HUD replaced by a first-person view with a bottom-left cluster (intro, execution prompt, info, effect line, skill), in-view characters with a `signal` outline, through-wall Signal markers, edge indicators for off-screen Signals (6 nearest), Terminal and Exit world markers, and execution-grace guidance (GDD §3.3). The anonymous-blip rules (§6.3) now also govern facing and animation.
- **Global:** input map (look/orbit), palette meanings, Killer intro copy, Packet afterimage effect line, connection band and data index updated. New questions N1–N9, decided in §11.1.
- **N1–N9 decided (§11.1):** positional Survivor footsteps and breathing for the Killer (§6.5.1, N3) with a diegetic-sound rule in §0.7; execution guidance reads `S2C_SelfState.ExecutionGraceLeft` (§6.4, N5); Killer terminal `Progress` is 0 or 100 only (N9); SLOP §6/§8 camera lines amended (N4).
- **Fixes after the camera review (86/100):** the Killer terminal marker drops its percentage (M1, §11.1 N9); one derived edge-indicator placement rule with cluster keep-out and band offset, shared by both roles (M2); execution guidance mirrors the server's re-arm rule, where an in-grace touch is spent (M3); Killer cluster heights include borders (472 u worst case); marker keylines; occluded characters are not drawn; distance tags on in-view Survivors beyond 20 m; facing threshold 0.03 m; framing range corrected; Survivor camera row in §10.

**Changes in attempt 3 (review of attempt 2: 84/100):**
- **Killer map:** the different approach for this attempt is to **reduce what the Killer map derives** instead of patching each derivation.
  - The captured-once tick (N2) is removed: it was not decoy-stable, and it does not change what the Killer does.
  - The last-seen ring (N4) is removed.
  - The wire order of entries is neutralised (N2b).
  - Capture cues cover ambush captures (N3).
- **Geometry (G1):** every block is recomputed with worst-case content.
  - S text is budgeted at 15 u with an 18 u line (its 720p floor).
  - The cluster is now 672 u; the meter, info block, skill ring, intros and play menus have new heights.
- **Copy and focus (N5–N10):**
  - The menu tagline is removed.
  - Name comes before Join.
  - Address-history popup rules are added.
  - Nicknames are ellipsized.
  - An empty name is sent as-is.
  - The Killer intro shows the skill effect.
  - Time pressure uses a `text` bar, not `danger`.

**Changes from attempt 1 (review 70/100):**
- **Killer map:** rebuilt on anonymous blips (F1, F3). The client never reads `Id`, ignores the `Afterimage` flag (attempt 2 also drew a last-seen ring; removed in attempt 3).
- **Killer capture cues:** only for transitions the Killer can observe (F2).
- **Readability:**
  - `danger` is never used for text.
  - True contrast ratios replace the claimed ones.
  - Strokes snap to whole pixels, and S text has a 12 px floor (F4, F5).
- **Threat cue:** one screen effect per level (§4.2).
- **Skill blocks:** carry the skill name and a distinct active arc (F6).
- **Text fields:** have states and a gamepad entry method (F7).
- **Main menu:** adds a Host action (§1).
- **Geometry:** intro, meter, connection band and play menu recomputed.
- **Copy:**
  - Join error for `Result = 1`.
  - Terminal numbering.
  - Result lines.
  - No "ally" in player-facing copy.

Binding inputs: `docs/ui/SLOP_DEFINITION.md`, `docs/GDD.md` v0.2, U2b rules brief `docs/briefs/U2b-a1.md` (statuses, flags, decoys rule 9, SkillEffect visibility rule 10, masking rule 11), Shared protocol v0.2 (`Shared/Runtime/Packets/*`, `Shared/Runtime/Game/*`), current screens `docs/qa/U4-screens/`. Conflicts are listed in §11; until decided, this spec follows the GDD and the protocol.

---

## 0. Global rules

### 0.1 Data rule
Every element names its source. Allowed sources:
1. **Wire** — a field the viewing client receives, written `Packet.Field`. Protocol v0.2, plus `S2C_SelfState` from the planned protocol v0.3 (U2c, §11 Q5); every element that uses it says so.
2. **Static** — compiled into the client: the map (`Server.Core/Runtime/Map/FixedMap`), `GameConstants`, `SkillIds` durations, key bindings, the PC's own network interfaces.
3. **Derived** — computed only from (1) and (2); the formula is given and the element is marked *derived*.

A Survivor client has no Killer position, direction, speed or skill state beyond `S2C_WorldState.ThreatLevel`, the Survivor-bound `S2C_SkillEffect`s (U2b rule 10) and, **(10/30)** per the U2e contract (GDD §2, survivors see the Killer in line of sight), the Killer entry of its own `S2C_WorldState.Players`: `Role = Killer`, `X`, `Z`, `Yaw` (its `Flags`, `StatusTimer` and `CaptureCount` are 0), present only in snapshots where that Survivor has a clear line of sight to the Killer. Spectators (Eliminated, Escaped) never receive it. The client uses that entry only to draw the Killer model (§4.10); it is never a marker, an edge indicator, a sound source or an input to any other element. A Killer client never uses `PlayerState.Id`, `Yaw`, `StatusTimer` or `CaptureCount` of Survivor entries and never uses the `Afterimage` flag (§6.3).

### 0.2 Canvas, scale and strokes
- Panel scale = `min(w/1920, h/1080)` clamped to `[0.8, 1.0]` (U4, `ClientScreens.cs`). Logical canvas = screen ÷ scale: **1920×1080 u** at 1920×1080, **1600×900 u** at 1280×720. Every layout below is given in logical units (u) with an anchor, and is checked against the 1600×900 canvas.
- **Stroke snapping:** a line of `n` u renders at `max(1, round(n × scale))` physical px. A 1 u line is 1 px at both resolutions; 2 u is 2 px.
- Spacing scale: 4 / 8 / 12 / 16 / 24 / 32 / 48 u. Layouts are written as stacks with gaps (in brackets) from this scale; absolute positions are only sums of these.
- Corner radius 0. No shadows, blur, gradients, or alpha blends of palette colors.

### 0.3 Type
One family with Hangul coverage and tabular figures: Pretendard (Latin fallback Inter). Three sizes: **S 14 u**, **M 18 u**, **L 28 u** (SLOP §8). **720p floor:** S renders at `max(14 u × scale, 12 px)`, i.e. 12 px at 1280×720 instead of 11.2 px (§11 Q13). M = 14.4 px and L = 22.4 px at 720p. Load-bearing numbers (time, terminals, countdowns, cooldown seconds) use M or L, never S. Weights: 400 body, 600 for the one key line of a block.
**Layout budget:** because of the 720p floor, S occupies 15 u at 720p. Every height and wrap below therefore budgets **S as 15 u with an 18 u line**, M with a 23 u line, L with a 34 u line, and average advance widths of 0.55 × size (S 8.3 u, M 9.9 u per character).
**Names** (`Slot.Nickname`, up to 48 UTF-8 bytes) are ellipsized with `…` at the end when they exceed their container, never splitting a surrogate pair; prompts budget a name at 12 characters before ellipsizing.

### 0.4 Palette and contrast
| Token | Hex | Meaning (only use) |
|---|---|---|
| `bg` | `#101113` | screen background |
| `panel` | `#1A1C1F` | panels; world-tag fills |
| `line` | `#2C2F33` | dividers and secondary-button borders (decorative; the label identifies the button) |
| `text` | `#E8E6E1` | text, primary button fill, neutral world markers (incl. every Killer-view skill signature and the White noise static, §14.6) |
| `muted` | `#9A9890` | secondary text, disabled text, past/inactive markers, **text-field and meter outlines** |
| `signal` | `#FFB020` | transmitting now: Signal state; in the Killer view, Signal Survivor outlines, markers and edge indicators |
| `danger` | `#E5484D` | Killer-caused threat to a Survivor (threat noise, Lagging bar, Exit locked by the Killer); on the Killer HUD only the Traffic-overload slow bar. **Non-text only** (bars, segments, rings, noise mask) |
| `ally` | `#3FB8AF` | Survivor-team information, **shown to Survivors only** |

Contrast (WCAG 2.x, computed): on `panel` — text 13.69, muted 5.91, signal 9.34, ally 7.06, danger 4.36; on `bg` — text 15.15, muted 6.54, signal 10.33, ally 7.81, danger 4.83. Because `danger` is 4.36 on `panel`, it never carries text; every danger state pairs a `danger` mark with words in `text`. Non-text: `muted` vs `panel` 5.91 (≥ 3:1, used for every meaningful outline); `line` vs `panel` 1.27 (decorative only).

### 0.5 Control states
| State | Secondary button / option | Primary button | Text field |
|---|---|---|---|
| normal | fill `panel`, 1 u border `line`, label `text` | fill `text`, label `bg` | fill `bg`, 1 u border `muted`, value `text`, placeholder `muted` |
| hover | border `muted` | 1 u inner border `muted` | border `text` |
| focused (not editing) | 2 u outline `text`, 2 u outside the border | same | same outline + hint under the field (S `muted`): `Press Enter to edit` / `Press A to edit` |
| editing | — | — | 2 u outline `text` + steady caret `text`; keyboard types directly; gamepad opens the character pad (§0.6) |
| pressed | fill `line` | fill `muted` | — |
| invalid | — | — | 2 u border `text` + reason line (S `text`) under the field |
| disabled | label `muted`, border `line`, not focusable; reason line (S `muted`) always visible beside it | fill `line`, label `muted`, same reason line | value `muted`, border `line`, not focusable |
| selected (option) | 4 u bar `text` inside the left edge, label weight 600 | — | — |

State changes are instant (≤ 1 frame); only the focus outline fades in, ≤ 100 ms linear. Hover never scales and never plays a sound (SLOP §5).

### 0.6 Input map
| Action | Keyboard / mouse | Gamepad (Xbox labels) |
|---|---|---|
| Move focus | Tab / Shift+Tab; arrows move spatially (left/right between columns, up/down within one) | D-pad / left stick, same spatial rule |
| Confirm / start editing a field | Enter, Space | A |
| Back / stop editing | Esc | B |
| Move | W A S D | left stick |
| Look | Survivor: mouse orbits the third-person camera (360° yaw); Killer: mouse look (first person) | right stick, same |
| Interact (hold) | E | X |
| Skill | Q | RB |
| Spectate next / previous (Eliminated) | E / Q (mouse orbits the spectate camera) | RB / LB (right stick orbits) |
| Play menu | Esc | Start |

**Gamepad text entry (character pad):** A on a focused field opens a pad anchored directly under the field (never screen center): 12 keys per row, 32×32 u keys, 4 u gaps. Address pad: `0–9 . :` + `Delete` + `Done`. Name pad: `A–Z a–z 0–9 space - _` in 6 rows + `Delete` + `Done`. D-pad moves, A types, X deletes, B or `Done` closes. Pad keys use the §0.5 option states (normal / hover / focused / pressed). Fields remember the last value, so a gamepad player rarely types.

**Address history popup:** while the Address field is focused (not editing) and history exists, a list of up to 3 previous addresses (options, 560×40 u each, 4 u gaps) opens directly under the field and **overlays** the items below it (reason line, Join); it is drawn on top with `panel` fill and a 1 u `muted` border. Down moves into the list, A / Enter fills the field and closes it, B / Esc or moving focus away closes it.

Prompts show the glyph of the last-used device (`[E]` or `X`), switching on the first input from the other device.

### 0.7 Sound rules
One sound per discrete state change. Continuous states are silent, except the threat beep, which *is* the threat indicator (GDD §3.2). Signal ↔ Silent toggles have no sound (§11 Q3). Priority: threat beep > capture/eliminated > exit opened/locked > terminal done > skill > UI > world sound.

These rules govern **UI cues**. **Diegetic world sound** — positional sound emitted by a character in the 3D scene, today only the Killer-side Survivor footsteps and breathing (§6.5.1) — is part of the scene, like the walk animation, and is not a UI cue. It may be continuous, but only while its source is on the wire and visible; it never encodes anything the visuals do not show (position, motion, Lagging), and it never sounds for a Survivor who is not in the Killer view.

---

## 1. Main menu

Purpose: choose how to play. Primary action: **Join** (the one dominant button). Anchor top-left, margin 48 u; one left-aligned column, width 560 u. No art, no animation (SLOP V11, L8).

Stack, top to bottom (gap after each item in brackets):

| # | Element | Source | Content and states | Size |
|---|---|---|---|---|
| 1.1 | Title | Static | `Ping the Killer` | L 600 `text` [32] |
| 1.2 | Name field | Local input, persisted → `C2S_Join.Nickname`, previewed with `GameServer.SanitizeNickname` | Label `Name` (S) [4]; max 48 UTF-8 bytes; placeholder `Player`; an empty field is sent empty and the server names the player `Player N` | 280×40 [24] |
| 1.3 | Address field | Local input, persisted | Label `Server address` (S `text`) [4]; field, placeholder `192.168.0.10:7777`; history popup (§0.6) | 560×40 [4] |
| 1.4 | Address reason | Derived: `host:port`, port 1–65535 | Hidden when valid; invalid: `Enter an address like 192.168.0.10:7777.` | S `text` [16] |
| 1.5 | **Join** (primary) | TCP connect, then `C2S_Join{Nickname, ReconnectToken = 0}` | All §0.5 states; disabled while 1.4 is invalid (reason = 1.4) | 280×48 [8] |
| 1.6 | Status line | Socket state; `S2C_JoinResult.Result` | `Connecting to 192.168.0.10:7777…` (Join disabled meanwhile; no spinner) · refused: `Server not reachable at 192.168.0.10:7777 — check that the host is running.` · 5 s timeout: `No answer from 192.168.0.10:7777 after 5 s — check the address and the host's firewall.` · `Result = 1`: `No free slot — the lobby or match is full.` · other socket error: `Connection to 192.168.0.10:7777 failed — check the address.` OS error text is never shown. | S `text`, max 2 lines [32] |
| 1.7 | Host a game | Starts `GameServer` + `TcpTransport` on port 7777 in-process (the same `Server.Core` offline mode uses), then joins `127.0.0.1:7777` | Secondary. Line under it (S `muted`): `Players on your network join at 192.168.0.10:7777. Windows may ask to allow network access.` (first private IPv4 of this PC; none: `No network address found — only this PC can join.`). Port busy: `Port 7777 is in use — close the other server and try again.` Decided (§11 Q14): the first host triggers the Windows Firewall prompt; the Store package declares `privateNetworkClientServer`. | 200×40 [4] + line [16] |
| 1.8 | Play offline | `ServerOptions.IsOffline = true`, `InMemoryTransport` | Secondary. Line: `Play against bots on this PC. No network needed.` (S `muted`) | 200×40 [4] + line [32] |
| 1.9 | Quit | App quit | Secondary | 120×40 |

Column height: 34+32 + 18+4+40+24 + 18+4+40+4 + 18+16 + 48+8 + 36+32 + 40+4+36+16 + 40+4+18+32 + 40 = 606 u ≤ 900 − 2 × 48 (status and host lines budgeted at 2 lines × 18 u).
Focus order: Name → Address → Join → Host a game → Play offline → Quit (Tab and Down follow it; Up reverses). Initial focus: Join if the stored address is valid, else Address. Esc does nothing on this screen.
A Join that lands in a running match (`S2C_MatchStart` arrives before any `S2C_LobbyState`) goes straight to §3.
Sounds: none.

---

### 1.7 How to play **(11/17, R3)**
A "How to play" button sits under the Offline note in the main menu (class `set-open`), disabled while connecting. It opens the `howto` panel with two tabs, Survivor and Killer, and a Back button; Esc closes it. Each page has the headings `How to win`, `Controls`, `Status` (three lines: `Normal:`, `Lagging:`, `Rescue:`), `Signal and Silent` and `Skills` (names and effects from the existing skill tables). Copy lives in `HelpCopy.Page(role)`.

## 2. Lobby

Purpose: fill 8 slots and start. The slot list *is* the data (SLOP §8).

Sources: `S2C_LobbyState{IsOffline, HostRoleChoice, Slots[8]}`; own id = `S2C_JoinResult.PlayerId`. A slot is empty when `PlayerId == 0`.

Layout: anchor top-left, margin 48 u. Left column x 48, width 560 (slots). Right column x 48 + 560 + 32 = 640, width 400 (controls). Total width 1040 ≤ 1600.

Left column stack:
| # | Element | Content | Size |
|---|---|---|---|
| 2.1 | Title | `Lobby` / `Offline lobby` (`IsOffline = 1`) | L 600 [8] |
| 2.2 | Join line (online only) | Hosting PC: `Players join at 192.168.0.10:7777` (local interfaces); others: the address they joined (S `muted`) | [24] |
| 2.3 | Slots 1–8 | Row: slot number (S `muted`, tabular) · nickname (M `text`) · tags (S `muted`): `Host` if `IsHost = 1`, `You` if own id. Bots need no tag (their name is `Bot N`). Empty: `Empty` (M `muted`). Roles are never shown (secret until match start). Rows are not focusable. | 8 × 40 + 7 × 4 = 348 [12] |
| 2.4 | Count | `5 of 8 slots filled` (S `muted`) | [24] |
| 2.5 | **Start** (host, primary) | `C2S_StartMatch`. Enabled when filled ≥ 2. Disabled reason: `Add a bot or wait for a player — a match needs 2.` Non-host: replaced by `Waiting for the host to start.` (M `text`) | 280×48 |
| 2.6 | Leave | Close the connection (offline or hosting: stop the in-process server; when hosting, reason line `Leaving closes the game for everyone.`) → Main menu | secondary 160×40, right of Start, gap 16 |

Left column height: 34 + 8 + 18 + 24 + 348 + 12 + 18 + 24 + 48 = 534 u. Nicknames in rows are ellipsized at 400 u (number and tags keep their space).

Right column stack (host only; others see read-only S `muted` text `Bots: 6` and `Host plays as Killer`):
| # | Element | Source / action | Content and states | Size |
|---|---|---|---|---|
| 2.7 | Bots | bots = count of `IsBot = 1`; `C2S_SetBots{Count = bots ∓ 1}` | Label `Bots: 6` (M, tabular) [8]; `−` and `+`. `−` disabled at the minimum (filled = 2) with `A match needs 2 players.`; `+` disabled at 8 filled with `All 8 slots are filled.` | 64×40 each, gap 8 [24] |
| 2.8 | Role | `C2S_SetRole{RoleChoice}`; selected = `HostRoleChoice` | Label: online `Host plays as`, offline `You play as` (S) [8]; options `Survivor`, `Killer`, `Random` in a row, §0.5 *selected* state | 3 × 120×40, gaps 8 |

**2.9 Skill picker (11/17, R1).** A third column right of the controls: x 640 + 400 + 32 = 1072, width 512; the lobby screen grows to 1536 u (≤ 1600 at 720p). Stack: title `Your skill` (M 600) [16]; chosen line `Chosen: Packet trace (Killer)` / `Chosen: random. A skill of the other role also gives a random one.` (S `muted`, wraps) [12]; one group per listed role: group title `Killer skills · 12 s cooldown` / `Survivor skills · 15 s cooldown` (S `muted`) [6], then one row per skill: a 192×36 button with the name (16 u, §0.5 *selected* = the own slot's `SkillChoice`) and, 12 u to its right, the effect line from `PlayMenuCopy.KillerEffect` / `SurvivorEffect` (S `muted`, 308 u, wraps to 2 lines) [6]; **(a2)** names and effect lines are 18 u in `text`; the effect line does not wrap by itself: `LobbyCopy.PickerEffect` breaks the same copy into the fewest lines of ≤ 30 characters, as even as possible (at most 3 lines, no lone word; INTEG-R: a number keeps its unit, so no line starts with `s` or `m`), rows [8]; last a `Random skill` button (192×36, selected when the choice is `NoChoice`).
- Which groups: both while `HostRoleChoice` is Random; the host sees only its chosen role; a guest of a Killer host sees only Survivor skills; a guest of a Survivor host sees both (it may be the Killer).
- A press sends `C2S_ChooseSkill{SkillId = SkillIds.Choice(role, id)}` (`0xFF` for Random). The lobby state echoes it in the own slot; the own roster row adds the skill name (`Ana · you · Overclock`). Other rows never show a choice. **(a2)** The server sends each client only its own slot's choice (every other slot `0xFF`) and accepts at most 4 choices per player per second.
- The server keeps a valid choice for the role dealt at match start; a choice for the other role or none gets a random skill (bots always random).
- Height: 34 + 16 + 22 + 12 + 2 × (18 + 6) + 9 × 42 + 42 = 552 u with both groups (fits beside the 534 u slot column).

Behaviour: re-render on each `S2C_LobbyState`, no animation. If own `IsHost` becomes 1, host controls appear and `You are now the host.` (S `text`) shows under 2.4 until the next lobby change. `S2C_MatchStart` → §3.
Focus (host): Start → Leave → (Right) `−` → `+` → (Down) Survivor → Killer → Random. Left from the right column returns to Start. Initial: Start if enabled, else `+`. Non-host: Leave only. Esc / B = Leave.
Sounds: `ui_join` (short neutral click, −18 dB) when a new human slot appears.

---

## 3. Match intro (7 s card, non-modal) **(11/17, R3)**

**(11/17, R3)** The 3 s block below is superseded by a 7 s intro card (`IntroCard`, class `introcard`), dismissed by Esc, Enter, Space or a mouse click (**INTEG-R minors:** Esc on the card only dismisses it; the play menu stays closed for that press. **INTEG-R:** movement keys, E and Q no longer dismiss it) or after 7 s, and removed in one frame; under Reduced motion it only appears and disappears. Width 672 u, one height slot of 134 u, bottom 104 u for the Survivor (directly above the cluster; the prompt row is suppressed while it shows) and bottom 24 u for the Killer (bottom-center; it never covers the character's feet, §4.10). Content (**INTEG-R**, as built; there is no separate role line): the goal line, which starts with the role (M 600: `Survivor · Hack 3 terminals, then hold an exit switch and escape.` / `Killer · Capture every Survivor before they escape.`), the skill line (M: name, `[Q]`, cooldown, e.g. `Dummy packet [Q] · Cooldown 15 s`), the effect line (S; text only from `PlayMenuCopy.SurvivorEffect` and `PlayMenuCopy.KillerEffect`), and the controls legend (S `muted`; exempt from Title Case checks): `WASD Move · Hold E Hack / Rescue / Exit switch / Shutter · While hacking: WASD routes packets · Q Skill · Esc Menu` / `WASD Move · Left click Attack · Hold E Kick terminal / Break shutter / Close hatch · Q Skill · Esc Menu`. `TerminalsRequired` comes from the first `S2C_WorldState`. The Killer's old 3 s block is no longer shown.

### 3.0 Original 3 s block (superseded)

Source: `S2C_MatchStart{Role, SkillId}`; skill name and effect from §8; `TerminalsRequired` from the first `S2C_WorldState`.

The server simulates from MatchStart (no pre-match freeze — decided against for now, §11 Q4), so the intro **never blocks the view or input** (SLOP L5). It shows for 3.0 s and is removed in one frame. No countdown numerals.

- **Survivor:** a block 672×160 u directly above the Survivor cluster (§4.0), same width, `panel` fill, 1 u `line` border, padding 12 (inner width 648 u). The prompt row (§4.3) is suppressed while it shows.
  - L 600 `text`: `You are a Survivor` [4]
  - M `text`: skill name and effect, e.g. `Dummy packet — a fake Signal where you stand, 3 s`; up to 2 lines (the longest, Stealth relay, is 86 characters ≈ 851 u at M → 2 lines in 648 u) [4]
  - S `muted`, 2 lines: `Moving or hacking makes you Signal. Stand still to stay Silent.` / `Hack 3 terminals, then reach the Exit.` (number = `TerminalsRequired`; the second line is omitted until it is known)
  - Worst-case height: 12 + 34 + 4 + 2 × 23 + 4 + 2 × 18 + 12 = 148 ≤ 160.
- **Killer:** block 6.1e, 320×208 u, at the top of the bottom-left cluster (§6.1); the info and skill blocks stay visible. Inner width 296 u (≈ 35 S characters per line).
  - L 600 `You are the Killer` [4]; M skill name (1 line; longest `Packet afterimage`, 17 characters ≈ 168 u) [4]; S `text` effect line from §8, max 3 lines [4]; S `muted`, max 3 lines: `Only Signal Survivors show, even through walls. Eliminate more than half of the Survivors to win.` (97 characters → 3 lines at 35 per line; **U6c-wire:** rules v2 wording)
  - Worst-case height: 12 + 34 + 4 + 23 + 4 + 3 × 18 + 4 + 3 × 18 + 12 = 201 ≤ 208. (The longest Killer effect line, Packet afterimage, is 60 characters → 2 lines.)

Sound: `match_start` (one low tone, 400 ms).

---

## 4. Survivor HUD (third person)

Zone rule (SLOP §8): one **bottom-center cluster**, plus screen-edge markers and world-anchored labels. No top bar, no corners. The character stands above the cluster (camera framing, §4.10).

### 4.0 Cluster layout
Anchor bottom-center, 24 u above the bottom edge. Width 672 u, height 72 u, fill `panel`, 1 u border `line`. The prompt row sits 8 u above it (672×40 u), only when a prompt applies.

```
 ┌──────────────────────┬──────────────────────────────────┬──────────────────────┐
 │ SKILL 168×72         │ TRANSMIT METER 304×72            │ INFO 168×72          │
 │ (ring)  Dummy        │ Signal            Captured once  │ 9:42                 │
 │  56u    packet [Q]   │ Visible while you move.          │ Terminals 1/3        │
 │                      │ [████████████████████████████]   │ ▌Locked 4 s          │
 └──────────────────────┴──────────────────────────────────┴──────────────────────┘
 8 + 168 + 8 + 304 + 8 + 168 + 8 = 672 u   (42 % of 1600, 35 % of 1920)
```
Info block (168×72, padding 8 left/right, 0 top/bottom): two M lines (the v1 exit line 3 is gone, §13.1); inner width 152 u. Widest line at M (9.9 u per character): `Terminals 1/3` 129 u ≤ 152.
**(10/30) Block widths changed (U4e Q-02):** skill **176** and info **160** (8 + 176 + 8 + 304 + 8 + 160 + 8 = 672 u, unchanged overall). The skill name column is 100 u (padding 8 + ring area 64 + gap 4 + 100, no right padding), so the longest skill word, `Emergency` at S 600 (≈ 84 u at 14 u, ≈ 90 u at the 15 u floor, Inter with synthesized bold), fits and names wrap only at spaces. The info block's inner width becomes 144 u; its widest line, `Terminals 4/4` at M 600, is ≈ 135 u.
At 720p the cluster is 538×58 px; at 1080p 672×72 px. Nothing else is in screen space except the §4.2 level-3 band, §4.6 markers and the play menu (§5).

### 4.1 Transmit meter — core element
Source: own entry in `S2C_WorldState.Players` (Survivors receive every Survivor including themselves): `Status`, `Flags`, `StatusTimer`, `CaptureCount`. Forced-exposure reasons come from Survivor-bound `S2C_SkillEffect`: PingScanner (sent to the exposed Survivor, X/Z = 0,0) and own TrafficOverload.

Internal layout (304×72, padding 8 left/right and 4 top/bottom inside the cluster's 1 u border; inner 288×62): row A 24 u — state word (M 600) left, capture tag (S `muted`) right; row B 18 u — reason (S, one line, max 34 characters ≈ 282 u ≤ 288; every reason below is within that budget and carries no name); gap 4 u; row C 16 u — bar 288×16, 1 u outline `muted`. 24 + 18 + 4 + 16 = 62.

**Precedence** — the first matching row wins:

| # | State | Trigger (own entry) | Word | Reason (S) | Bar |
|---|---|---|---|---|---|
| 1 | Eliminated | `Status = Eliminated` | meter replaced by the spectate block (§4.5) | | |
| 2 | Escaped | `Status = Escaped` | `Escaped` (`text`) | `Waiting for the result.` (`muted`) | empty |
| 3 | Lagging, being rescued | `Status = Lagging` and `S2C_SelfState.RescueProgress > 0` | `Lagging` (`text`) | `Being rescued — stay close` | fill `ally`, width = `RescueProgress` (§4.4) |
| 4 | Lagging | `Status = Lagging` | `Lagging` (`text`) | `Always visible · 42 s left` (`ceil(StatusTimer)`, tabular) | fill `danger`, width = `StatusTimer / 60` |
| 5 | Signal, scanned | `Signal` set and a PingScanner effect for you arrived less than its `Duration` ago | `Signal` (`signal`) | `Scanned by the Killer · 1.2 s` | fill `signal` |
| 6 | Signal, overload | `Signal` set and own TrafficOverload active (< `Duration`) | `Signal` | `Traffic overload · 1.4 s` | fill `signal` |
| 7 | Signal, hacking | `Signal` + `Hacking` | `Signal` | `Visible while you hack.` | fill `signal` |
| 8 | Signal, rescuing | `Signal` + `Rescuing` | `Signal` | `Visible while you rescue.` | fill `signal` |
| 9 | Signal | `Signal` set | `Signal` | `Visible while you move.` | fill `signal` |
| 10 | Silent, relay | `Signal` clear and `Stealthed` set | `Silent` (`text`) | `Relay: hidden while moving` | empty, outline `ally` |
| 11 | Silent | `Signal` clear | `Silent` (`text`) | `Not visible to the Killer.` (`muted`) | empty |

Capture tag (row A, right), first match wins:
- `Protected 1.4 s` (S `text`, tabular) while `S2C_SelfState.InvulnerableLeft > 0`. This is the 2 s after a rescue during which you cannot be captured.
- `Captured once` (S `muted`) when `CaptureCount = 1` and `Status = Normal`. The play menu (§5) explains that the next capture eliminates you.
- **(11/08, Q1)** `Next capture eliminates` (S `muted`) when `CaptureCount ≥ 2` and `Status = Normal`. `CaptureCount` counts Lagging stages, not captures: a first stage that runs out raises it to 2 without a second capture, so the tag never says `Captured twice`.

The tags show only while `Status = Normal`, so the word beside them is `Signal` or `Silent` (≈ 60 u at M); `Next capture eliminates` (23 characters, measured in `PlaycheckPolishTests`) plus the 8 u gap fits 288 u. `Protected 1.4 s` is shorter.
Fills are steady (no pulse). Sounds: none on Signal/Silent; `stealth_on` (soft, 150 ms) when `Stealthed` sets; `captured` (low hit, 250 ms) on entering Lagging.

**(10/30) Capture banner.** On the own `Status` going Normal → Lagging (never on the first snapshot of a match, so a reconnect into Lagging shows nothing), the prompt row (§4.3) shows for 3 s, ahead of every other prompt, M `text` with a 4×18 u `danger` bar before it: `Lagging. A Survivor holding [E] within 1.5 m for 3 s rescues you · 60 s left` (numbers from `RescueRadius`, `RescueTimeSeconds`, `LaggingTimeoutSeconds`; 77 characters ≈ 640 u, fits the 656 u row; **(11/17, R3)** the rescue is an explicit E-hold, not automatic). It is shown once per capture and removed early when the status leaves Lagging. **(U6c-wire a2)** The seconds follow the Lagging stage (the meter's `LaggingTotal`): on the second capture, and when the first stage runs out and the server escalates (`CaptureCount` 1 → 2 while Lagging, which shows the banner once more), it reads `Lagging. A Survivor holding [E] within 1.5 m for 3 s rescues you · 30 s left`. The brief's draft wording (`Caught … ally …`) was changed to SLOP §6 vocabulary (`Survivor`). **(10/30, U4e attempt 2)** The banner starts with the meter's state word, `Lagging.` (it was `Captured.` in attempt 1), so the banner and the meter name the same state; the `captured` sound name is unchanged.

**(10/30) Elimination reason** (*derived* from the own entry in the snapshot before `Status = Eliminated`):

| Previous own status | Condition | Reason line |
|---|---|---|
| Lagging | last `StatusTimer ≤ 0.2` | `Disconnected: no rescue in the second Lagging stage (30 s).` |
| Normal | `CaptureCount ≥ 2` | **(11/08, Q1)** names the captures the client saw: `Disconnected: caught 3 times.`, or after a first stage ran out `Disconnected: caught 2 times, no Lagging stage left.` When the client did not see the whole match (first snapshot not Normal with `CaptureCount` 0) or its count disagrees with the server's: `Disconnected: caught with no Lagging stage left.` (was `caught a third time.`, wrong after an escalation) |
| anything else, a collapse clock running | — | `Disconnected: the shutdown reached you.` |
| anything else | — | none (the meter keeps `Watching until the match ends.`) |

**(U6c-wire)** Rules v2 has no execution: the two `Executed: …` lines are gone. A Survivor is eliminated by the third capture, by the second Lagging stage running out (`SecondLaggingTimeoutSeconds` 30 s), or by the collapse clock. The Lagging meter's full scale follows the stage: 60 s on the first capture, 30 s on the second; from `CaptureCount` 2 the tag reads `Next capture eliminates` (11/08, Q1; it read `Captured twice`).

While Eliminated the meter shows `Eliminated` (row A) and the reason in S `text` in rows B–C, wrapped to at most two lines in 288×38 u, with no bar. The same reason follows the §7.2 outcome line.

### 4.2 Threat cue (GDD §3.2)
Source: `S2C_WorldState.ThreatLevel` (0–3). Distance only; the UI adds no direction (no audio panning, no edge bias).

Each level has its sound and **exactly one** screen effect:

| Level | Trigger | Sound (centered, non-positional) | Screen effect |
|---|---|---|---|
| 0 | `ThreatLevel = 0` | — | — |
| 1 | `= 1` (12–18 m) | `threat_beep` every 1.2 s | **meter-edge noise:** a 1-bit noise strip (pixels `danger` or transparent, ~~30 %~~ **45 % density (10/30)**) along the meter's top edge, ~~4 u~~ **8 u (10/30)** tall, covering 1/3 of the meter width from the right |
| 2 | `= 2` (6–12 m) | `threat_beep` every 0.6 s + `threat_scratch` (low, −12 dB) | the same strip at 2/3 width |
| 3 | `= 3` (≤ 6 m) | `threat_beep` every 0.25 s | **screen-edge noise** replaces the strip: the same kind of 1-bit `danger` mask (30 % density) in a 24 u band on all four screen edges |

The noise mask is static (never animated); it is re-rolled once on each level change (§11 Q2). Beep timers restart on change. The cue is hidden while Eliminated or Escaped. Level 2's visual is an addition (the GDD lists only sounds for level 2), recorded in §11 Q2.

**(10/30) Strip size (orchestrator ruling, U4e Q-01):** the meter-edge strip at levels 1 and 2 is **8 u tall at 45 % density** (it was 4 u at 30 %, which read too faint). It still covers 1/3 (level 1) and 2/3 (level 2) of the meter width from the right, lies inside the meter's top padding and the blank top of row A (it reaches no glyph), stays static and is re-rolled once per level change. `danger` stays a non-text color only. The level-3 band is unchanged: 24 u at 30 %.

**(10/30) Build notes.** One mask texel is 2×2 u in both the 8 u strip (four rows of noise) and the 24 u band, point-filtered, so the noise stays crisp at both canvas sizes. **(U6c-wire B1)** In the 8 u strip each column of the mask is stacked from the bottom (same cells per column, same 45 % density): a flat 2D mask pressed against the cluster's top border read as a clipped line of red text at 1080p; the stacked columns read as a ragged bar rising off the meter. `threat_scratch` plays once on entering level 2 (from any other level). The hidden rule applies to the beep too: while Eliminated or Escaped the beep is fed level 0. A `ThreatLevel` outside 0–3 reads as 0.

### 4.3 Terminals, hacking, "hold E"
Sources: `S2C_WorldState.Terminals[]{Id, X, Z, Progress}`, `TerminalsRequired`; own `Flags.Hacking` and position; static `TerminalRadius` 1.5 m. Display number = `Id + 1`.

- **World label** on each terminal within 25 m and on screen, 1.6 m above it: `Terminal 2 · 40%` (S `text`, `panel` tag, 4 u padding); complete: `Terminal 2 · Done` (S `muted`).
- **Info block line 2:** `Terminals 1/3` (M, tabular): done = count of `Progress = 100`, needed = `TerminalsRequired`. Weight 600 once done ≥ needed.
- **Prompt row** (672×40 above the cluster, M `text`, `panel` fill, 1 u `line` border):

| State | Trigger | Prompt |
|---|---|---|
| Can hack | own `Status = Normal`, an incomplete terminal within 1.5 m (derived), not `Hacking` | `Hold [E] + WASD to hack Terminal 2 · 40% — you will be Signal` **(11/17, R2: names the route keys, §13.2)** |
| Hacking | own `Hacking` | `Hacking Terminal 2 · 47%` + a 656×4 u bar under the text, fill `text`, width = `Progress` |
| Co-hacking | `Hacking` and at least one other Survivor within 1.5 m of the same terminal with `Hacking` (derived) | `Hacking Terminal 2 · 47% · 2 hacking` |
| Lagging at a terminal | `Status = Lagging`, within 1.5 m | `Lagging Survivors cannot hack.` (`muted`) |

Input: E down → `C2S_Interact{Active = 1}`. E up, opening the play menu, or losing window focus → `C2S_Interact{Active = 0}`. The server decides range; the bar shows only the server's `Progress`.
Sounds: `hack_start` (soft click) when own `Hacking` sets; `terminal_done` (two notes, 300 ms) when any `Progress` reaches 100.

### 4.3a First-time hints **(11/17, R3)**
One short line in the prompt row, once per match each, 6 s, never while the intro card shows and never over another prompt. Survivor: a terminal within 6 m, the first Lagging ally (`HelpCopy.HintLagging`), the first Signal. Killer: a blip within 6 m (shown in the Killer prompt slot when it is free). `HintTracker.Reset()` re-arms them at MatchStart.

### 4.4 Rescue
Sources: Survivors' `Status`, `Flags.Rescuing`, `X/Z`; own `S2C_SelfState.RescueProgress` (protocol v0.3, U2c — §11 Q5); static `RescueRadius` 1.5 m.

Rescue progress comes from the server, own-only: for a Lagging Survivor it is the progress of their own rescue; for a rescuer it is the progress of the rescue they are taking part in; otherwise 0. Assumed range 0–1 (the U2c brief fixes the encoding). It is drawn as a bar, never as seconds. No client estimate is used.

| Viewer | Trigger | Display |
|---|---|---|
| Normal Survivor approaching **(11/17, R3)** | a Survivor with `Status = Lagging` within 6 m but beyond 1.5 m (derived from positions), own `Rescuing` clear | prompt row: `Mina is Lagging · 4 m — move next to them, hold [E] to rescue` |
| Helper in reach **(11/17, R3)** | a Lagging Survivor within 1.5 m, own `Rescuing` clear | prompt row: `Hold [E] to rescue Mina — you will be Signal` |
| Rescuer | own `Rescuing` set (`ActionKind = Rescue`) | prompt row: `Rescuing Mina — you are Signal` + 656×4 bar, fill `ally`, width = own `RescueProgress` |
| Lagging Survivor | meter row 3 (§4.1) | — |
| Done | a watched Survivor's `Status` goes Lagging → Normal | prompt and meter return to normal; `rescued` (rising two notes) for the rescued Survivor and the rescuers |

Names = lobby `Slot.Nickname` for that `PlayerId` (stored from the last `S2C_LobbyState`).

**(11/17, R3) Getting up.** A Survivor who is rescued (E-hold or Emergency restore) gets up for 1.5 s: the server holds speed at 0 (MoveInput direction ignored, look yaw kept), refuses E and the skill (`NotNormal`), and the existing 2 s protection covers it; `PlayerFlags.GettingUp (32)` goes to Survivor recipients only (never the Killer: a flag that decoys lack would reveal real Survivors). The rescued player's prompt row shows `Getting up…` (ahead of every other prompt, no error sound). The Survivor view plays the Mixamo `GettingUp` clip from the flag, sped up from 2.7 s to 1.5 s. The Killer view shows no get-up.

**(11/17, R3) Explicit rescue.** The rescue is no longer automatic. A Normal Survivor must hold E within 1.5 m of a Lagging Survivor for 3 s (`RescueTimeSeconds`); releasing E or leaving range resets the count. The server owns it: `Flags.Rescuing` and `ActionKind.Rescue (7)` with `ActionProgress` are set only while E is held in range. **E priority** (one E, one action): rescue, then chase prop, then exit switch, then terminal. While a Lagging Survivor is in reach, E never starts a chase prop, switch or hack. Bots hold E next to a Lagging Survivor. Emergency restore is unchanged. Vocabulary **(INTEG-R)**: one term, SLOP §8 `Rescue` (noun and verb: `Hold [E] to rescue`, `Rescuing`, `Being rescued — stay close`, `rescues you`). Only the skill keeps its name, `Emergency restore`, and its own feedback lines (§14.2.8).

### 4.5 Own status: Lagging, Escaped, Eliminated
- **Lagging:** meter rows 3–4.
  - ~~Frame-stutter treatment (GDD §3.3): the world camera image is presented at 8 Hz (each frame held 125 ms) and blended with the previous frame at 30 %. HUD and audio stay at full rate. No shake, no flash.~~
  - **(11/17, U8) Capture impact + Lagging look, replacing the frame stutter** (players read the 8 Hz hold as real lag). The world always renders at full frame rate.
    - **Hit-stop:** at the swipe's contact (0.27 s after the own capture, §4.10) the world view (character poses, the Killer model, the camera follow) holds for 100 ms, then eases back to real time over 150 ms (no jump). Input, networking, audio and the HUD keep running; `Time.timeScale` is not used.
    - **Glitch burst** (0.4 s, from the capture): from the contact: a Killer-red (`danger`) flash, 20 % at its start, fading out over 150 ms (one flash per capture, well under 3 per second), and three horizontal scanline tear bars (`danger` / neutral grey tints) that jump every 50 ms and slide back to 0 as the burst fades.
    - **Steady look**, exactly while the own `Status = Lagging`, faded in over 250 ms: a dark veil (20 %), a dark vignette (60 % at the corners) and faint scanlines (one dark line in three, 16 %). The meter is still the one indicator of the state (rows 3–4); the look carries no text.
    - All three are UI layers under every screen and the HUD; nothing touches the world camera.
  - Reduced-motion alternative **(11/17, U8)**: no burst, no tear and no flash; the steady look only (the hit-stop stays: it is a 100 ms hold, not motion). The Reduced motion setting (§12.5) or the launch option `-ptk-reduce-stutter` turns it on.
  - ~~**(10/30) Build:** the world camera renders into an off-screen texture; at each 8 Hz slot (every frame when reduced) the newest presented frame is kept and the one before it is drawn on top at 30 % opacity, both under the HUD. It runs exactly while the own `Status = Lagging`.~~ Removed by U8 (11/17).
  - The skill is disabled (§4.8), and edge markers show Normal Survivors (§4.6).
- **Escaped:** lasts until `S2C_GameResult`, which normally arrives in the same tick. The meter shows row 2.
- **Eliminated:** plays the sound `eliminated` (falling tone, 600 ms). The meter is replaced by a spectate block of the same size.
  - Line 1, M: `Eliminated — spectating Mina`. Line 2, S `muted`: `[E] next · [Q] previous`.
  - Camera: the §4.10 orbit rig around the spectated Survivor's `X, Z`; their character faces their `Yaw`. The spectator orbits it with the mouse / right stick. Switching target cuts instantly and keeps the current camera yaw and pitch.
  - Order: slot order, skipping Survivors whose `Status` is Eliminated or Escaped. If none is left, line 1 reads `Eliminated — no Survivor left to spectate` and the camera stays.
  - The threat cue, skill and prompts are hidden. The info block stays.

### 4.6 Screen-edge markers
**(11/17, R3) Built as:** every Lagging ally (nearest first, at most 7) gets a tag `Mina · Lagging · 38 m` on screen (above the body at 1.2 m) or an edge chevron plus tag off screen, reusing `MarkerLayer`, `EdgePlacement` and `EdgeSpread`. Decision: the chevron is the amber Signal chevron with a Lagging ring (matching the Killer HUD's Lagging marker and the amber Lagging body tint) rather than the `ally` teal in the table. The Lagging viewer's markers for the nearest 2 Normal Survivors are not built; the downed line below replaces them: `Lagging · an ally can rescue you with [E] · nearest ally 18 m · 54 s` (nearest Normal ally distance; an ally bar while `RescueProgress > 0`).
Sources: Survivors' `X/Z`, `Status`; static Exit position; `ExitOpen`, `ExitLocked`.

| Marker | Shown to | Off-screen | On-screen |
|---|---|---|---|
| Lagging Survivor | Normal Survivors | 16×16 u chevron `ally` at the screen edge toward them + S label `Mina · 38 m` | world tag above them: `Mina · Lagging · 38 m` (S `text`, `panel` tag, 2 u left bar `ally`) |
| Normal Survivor (nearest 2) | a Lagging Survivor | chevron `ally` + `Mina · 12 m` | world tag `Mina · 12 m` |
| Exit | Normal Survivors, once `ExitOpen = 1` | chevron `text` + `Exit · 34 m` (`Exit Locked · 34 m` while `ExitLocked = 1`) | world tag on the exit |

Placement and pointing follow the §6.2 edge-indicator rule (safe rectangle inset 24 u; keep-out = cluster, prompt row and, while shown, intro, expanded by 8 u). At most 7 markers. No marker or element ever concerns the Killer.

### 4.7 Exit (info block line 3) — removed in v2
**(U6c-wire)** The v1 single-exit line (`Exit Locked` / `Exit Open` / `Locked 4 s`) is removed. Both exits are read on the objective row (§13.1, §13.4); the switch and hatch prompts are in §13.3.

### 4.8 Skill block (left block, 168×72)
Sources: `S2C_MatchStart.SkillId`; own `S2C_SelfState.SkillCooldown` (seconds left, protocol v0.3, U2c — §11 Q5); own `S2C_SkillEffect` (`CasterId` = own id) for the active duration; static `SurvivorSkillCooldownSeconds` 15 s (the ring's full scale) and the §8 durations; own `Status`; Survivor positions.

The cooldown is the server's value: the ring and seconds follow `SkillCooldown` exactly, including after a reconnect. A key press alone changes nothing on screen.

Layout (168×72, padding 8): at left a 64×64 u area holding a 56 u ring (3 u track `line`) with the 32×32 u icon centered in it (1 u `text` line drawing, §8); the Active arc (below) is drawn at 64 u diameter, so it stays inside the 64 u area and 4 u inside the block's top and bottom edges. To the right (168 − 8 − 64 − 8 − 8 = 80 u, ≈ 9 S characters per line): the skill name, S 600, max 2 lines (e.g. `Traffic` / `overload`; `Emergency` / `restore`), then the key glyph `[Q]` / `RB` (S `muted`). 2 × 18 + 18 = 54 ≤ 56 inner height. **(10/30, U4e Q-02)** The block is now 176×72 with the text column 176 − 8 − 64 − 4 = **100 u** (no right padding), because `Emergency` at S 600 (≈ 90 u at the 15 u floor) broke mid-word in 80 u. A name may wrap only at a space.

| State | Trigger | Ring | Center | Sound |
|---|---|---|---|---|
| Ready | `SkillCooldown = 0`, `Status = Normal`, precondition holds | full ring `text` | icon | `skill_ready` (soft tick) when `SkillCooldown` reaches 0 |
| Active | own effect running; `Duration` from the effect | an **outer** 2 u arc (64 u diameter, outside the 56 u ring) shrinking clockwise in `text` (`signal` for TrafficOverload, which makes you Signal) | icon | `skill_use` on the own effect |
| Cooldown | `SkillCooldown > 0` | remaining fraction `SkillCooldown / 15 s` in `muted`, clockwise from 12 o'clock | `ceil(SkillCooldown)` (M tabular `text`) over the icon | — |
| Disabled — Lagging | `Status = Lagging` | track only | icon `muted`; reason in the prompt row slot (S `muted`; the prompt row shows it only when no other prompt applies): `Skills are off while Lagging.` | — |
| Disabled — no target | EmergencyRestore and no Survivor with `Status = Lagging` within 1.5 m (derived) | track only | icon `muted`; reason: `Emergency restore needs a Lagging Survivor next to you.` | — |
| Not used | key pressed while Ready, and within 0.5 s neither an own effect arrives nor `SkillCooldown` becomes > 0 | unchanged | reason for 2 s: `Skill not used — the server did not accept it.` | — |
| Not used (server reason) **(11/10, Q3)** | key pressed while Ready, and an `S2C_SkillRejected` for the press arrives first | unchanged | the reason line for that reason (§14.3); the 0.5 s row above is only the fallback | — |

**(U7b)** A press in a state the client consumes itself shows its reason on the §14.1a result line for 2 s (§14.3), unless the prompt row already shows the same steady Disabled reason. The `Not used` reason moves to the result line. The `skill_use` sound is replaced by each skill's own cue (§14.2), and every own effect also gets a result line and a world effect.

Distinctness (SLOP §6): each skill has its own icon depicting its effect, its name in the block, and its own active-arc duration (1.5–3 s); passive skills have no ring (§6.1).

### 4.9 Time left (info block line 1)
Source: `S2C_WorldState.TimeLeft`. Format `m:ss`, M 600 tabular `text`. In the last 60 s a 4×18 u `text` bar appears left of the number (time pressure is not Killer-caused threat, so not `danger`). Sound `time_60` (single tone) once at 60 s.

### 4.10 Camera and framing (third person, GDD §2 changed 2026-10-27)
Sources: own `PlayerState.{X, Z, Yaw}`; static `FixedMap.Walls`; input. Everything here is client-side presentation; the server sees only `C2S_MoveInput`.

- **Rig:** an orbit camera around a look target 0.7 m above the own character's position. Boom 4.0 m. Yaw free 360°, driven by mouse / right stick only. Pitch −10° (looking up) to +60° (looking down), default +20°. Vertical FOV 50° (Hor+ on other aspect ratios). These values are the starting point (🔶, §11.1 N6).
- **Movement:** the move vector is relative to camera yaw: W = away from the camera. The client rotates it to world space and sends it as `C2S_MoveInput.{DirX, DirZ}`. The character turns toward the movement direction at 720°/s; `C2S_MoveInput.Yaw` = the character's facing, not the camera's. Standing still keeps the last facing, so orbiting the camera never turns the character or makes it Signal. While own `Hacking` is set, the character faces the terminal (derived).
- **Wall collision:** a 0.2 m sphere cast from the look target toward the camera against static walls. The boom shortens to the hit distance at once and eases back out over 150 ms when the wall clears. When the boom is shorter than 1.0 m, the own character is drawn with a 40 % screen-door dither so it never blocks the view.
- **Framing check** (default pitch, full boom): camera height 0.7 + 4.0 × sin 20° = 2.07 m, horizontal distance 3.76 m. The feet project at 8.8° below center, which is **≈ 67 % of screen height** (600 u at 720p); the head (1.8 m) is at ≈ 19 %. The character therefore stands above every bottom-center block: the 3 s intro's top edge is 636 u (71 %), the prompt row's 756 u (84 %), the cluster's 804 u (89 %). Across the pitch range (−10° … +60°) the feet stay between ≈ 58 % and 69 % (523–622 u), still above the intro, prompt row and cluster; a shortened boom enlarges the character around the screen center.
- **No reticle** and no aim marker: hacking and rescuing are range-based (1.5 m), and the prompt row (§4.3, §4.4) is chosen from the character's position, not from where the camera points.
- **Readability against the character:**
  - The own character has no outline, glow or tint for Signal / Silent. The meter is the only indicator of that state (one indicator per state, SLOP §8).
  - Other Survivors are drawn as characters with no outline; the only on-character elements are the §4.6 world tags (Lagging, or the nearest Normal Survivors while you are Lagging).
  - The terminal the prompt row refers to gets its world label (§4.3) at weight 600, so the prompt and the object it names are linked.
  - World tags never draw over the own character's silhouette: a tag whose screen rectangle overlaps the own character's screen bounds moves up until it clears them.
- ~~**The Killer is never drawn.**~~ **(10/30) The Killer model is drawn only from the Killer entry (§0.1).** While snapshots carry the entry, a Killer-red body (the Killer's own color, one per match) stands at its interpolated `X, Z` on the same 100 ms render delay as Survivors, facing its `Yaw`. When the entry stops arriving, the model stays at its last position (no extrapolation) and fades out over 0.3 s. A gap of at most 0.3 s between two entries is bridged by interpolation; an entry that reappears after a longer gap snaps to its new position. There is no marker, outline, edge indicator, shadow, footstep or through-wall cue: the server only sends the entry when the Survivor's line of sight is clear, and the client adds nothing beyond it. The camera can still see around a corner the character cannot; that shows the Killer only if the character's own line of sight is also clear.
- **(11/03, U4j) Character motion (Survivor view, full state).** Clips are Mixamo, retargeted to the shared characters; they come only from fields this client receives (`Status`, `Flags.Hacking`, `CaptureCount`, positions):
  - A Survivor's `Status` Normal → Lagging: a short hit flinch blending into a knock-down, then the crawl while Lagging. The crawl's rate follows the measured speed, so a still Lagging Survivor holds the last crawl pose. Lagging → Normal: getting up (2.7 s), then locomotion.
  - → Eliminated: the fall-back death once; the body keeps its last pose for 6 s after the fall began, then despawns. A Survivor first seen already Eliminated is not drawn.
  - `Hacking` set and speed about 0: the button-pushing loop. `CaptureCount` ≥ 1 and Normal: the injured idle/limp when still or slow.
  - The Killer model swipes once per capture (`Status` Normal → Lagging, or Normal → Eliminated; ~~1.2 s~~ **(11/17, U8 a2) 1.15 s, keys chosen from every imported frame: a fast 0.15 s wind-up from the cocked arm, the strike reaching contact 0.27 s after the capture, the strike pose held 0.12 s, then a slow follow-through (0.4x)**), and only if the model is drawn at that moment. The swipe is on the upper body; the legs keep their locomotion.
  - **(11/17, U8) Capture impact:** at every capture of a drawn Survivor (any Survivor, Survivor view), a 0.6 s Killer-red burst of streaks and fragments at chest height (1 m) over its position, starting on the swipe's contact (0.27 s after the capture). The Killer client plays the same burst, at first-person sizes (capped at 0.04 m per particle), at the tracker instance of its own confirmed capture on the view kick's strike (§6.3; never at a position it does not receive). Pooled at match start. Reduced motion does not remove it (it is not camera motion).
  - No other cue is added: the motion never reveals anything the HUD does not already show.
- **(11/17, U10) Interaction motions and holo props (Survivor client only).** Built on the U4j rules above: driven only by received fields, and an interaction plays only while the character is Normal, upright and still (measured speed < 0.3 m/s), except Restore, which also plays while the rescuer follows a crawling ally (measured speed < 1.5 m/s; the feet are then free, the lean and the link stay).
  - **Hack** (`Flags.Hacking`, own and mates): the button-pushing loop plus a two-hand IK pose. Both hands type on a floating panel 1.4 m high and 0.5 m in front, on alternating 0.17 s beats; the head looks at the panel. The prop is a translucent `ally` key-grid panel (0.9 × 0.5 m, wide and high enough to show past the shoulders of the own character from the camera behind; tilted toward the eyes, double-sided so the camera behind sees it). It pulses +7 % on each beat **(11/17, R2: the own Survivor's panel pulses on each of its real route keys instead, while it routes, §13.2; every other Survivor's panel keeps the 0.17 s beat)**, and a thin stream of six small `ally` packet squares flows from the terminal top into it. *(S1: the old one-hand press was lowered by IK to the 0.72 m terminal top, at hip height, so from the camera behind the character it read as standing idle.)*
  - **Restore** (`Flags.Rescuing`, the §4.4 source): the hips drop 0.36 m, the torso leans 30° over the nearest Lagging Survivor within 2.1 m, and both hands reach down to it. The prop is an `ally` link line from the hands to the ally, with packets travelling down it.
  - **Exit switch hold** (own `S2C_SelfState.ActionKind = 1` only; other Survivors' holds are not exposed per player, so mates show none): the right palm on the switch at 1.15 m, the body leaning in, and packets streaming from the palm into the switch.
  - **Killer jam** (Killer model, Survivor view): one two-hand slam (0.7 s) when a terminal within 2.0 m of the drawn model turns `Regressing` (the end of a kick, a flag Survivors receive and the HUD tags). A `danger` glitch burst of squares appears at the terminal top on the impact. Nothing plays while the model is not drawn. The kick hold itself is not exposed to Survivors, so the slam is not repeated during it.
  - **Colours and shapes:** `ally` (Survivor-team information, Survivor clients only) and `danger` (Killer-caused), one meaning each. Shapes are panels, lines and squares, never rings (rings belong to the §14.1 skill effects).
  - **Killer view:** no interaction pose and no prop, for any instance. §6.3 rule 1 forbids reading `Hacking` and `Rescuing`, and the Killer client builds no Survivor character from an entry. The Killer's own kick has no first-person effect (§6.0: no body or hands).
  - **Pooling:** each character builds its props once, on first use (root, panel, link, 6 packets, 10 burst squares). After that they are only moved and toggled.
- **(11/17, U10) Locomotion speeds.** One speed-driven blend, shared by both views: idle below 0.49 m/s, walk from 0.5 to 1.6 m/s, run at 4.0 m/s (`SurvivorSpeed`: a moving Survivor runs), and the same run sped up for the Killer (4.6 m/s, and 6.9 m/s with Overclock). Each child's rate is its speed ÷ the clip's own ground speed, so the planted foot moves at the travel speed (±10 %). The injured blend keeps the limp up to 1.6 m/s, then blends into the same run at 4.0 m/s. The Killer view still reads only `Status` and the matched speed.

---

## 5. Survivor play menu
Esc / Start replaces the prompt row with a 672×152 u block (same anchor; it grows upward):
- Line 1 (M 600): the skill name. Line 2 (S): its effect (§8; the longest, 68 characters ≈ 564 u, fits one line in the 648 u inner width).
- Line 3 (S `muted`), when applicable. **(Q6, GDD v2)** The third capture eliminates (`EliminatingCaptureCount` 3), so the v1 line `Captured once: the next capture eliminates you.` is no longer true after one capture. While `Status = Normal`: `CaptureCount = 1` → `Captured once: the next capture leaves 30 s for a rescue.`; `CaptureCount ≥ 2` → **(11/10)** `Next capture eliminates you.` (count-free: `CaptureCount` counts Lagging stages, so a first stage that runs out reaches 2 without a second capture; it read `Captured twice: the next capture eliminates you.`); otherwise no line.
- Then `Resume` (primary, focused), `Settings` (opens §12) and `Leave match`, three 160×40 u buttons in one row, gap 8: 160 × 3 + 16 = 496 ≤ 648 u. The row is pinned 12 u above the block's bottom edge, so it does not move when line 3 is absent.
- Height with padding 12: 12 + 23 + 4 + 18 + 4 + 18 + 8 + 40 + 12 = 139 ≤ 152.
- While the menu is open the prompt row and the hold row (§13.3) stay hidden; the meter cluster, the objective row, the result line (§14.1a) stay as they are; the route panel hides, because E counts as released **(11/17, R2)**.

Opening the menu sends `C2S_Interact{Active = 0}` (only after a sent press) and one zero `C2S_MoveInput`, and it releases the cursor; while it is open there is no mouse look and no W A S D, E, Q or Space. Closing it captures the cursor again (when the window has focus). The match keeps running.

Esc no longer only releases the cursor: Esc / Start opens and closes this menu. With Settings open, Esc closes Settings first and focus returns to the menu's `Settings` button; a second Esc closes the menu. Losing window focus still releases the cursor and shows `Click to resume mouse look`, but a click never captures the cursor while the menu is open.

`Leave match` closes the connection and returns to the Main menu with the cursor free (offline: no notice). The server keeps the slot for 10 s, then a bot takes it. The menu is closed on leave, disconnect, the game result and the next match start.

Focus: Resume → Settings → Leave match (Tab wraps inside the menu; arrows stop at the ends). Enter / Space / A presses the focused button; Esc / B = Resume. Buttons use the §0.5 states, like the Killer's.

**(Q6) One component.** The Killer menu (§6.6) and this one are the same `PlayMenu` in two layouts: Stacked (320×256, buttons 296×40 stacked) for the Killer and Row (672×152, buttons 160×40 in a row) for the Survivor. The input gate (`PlayMenuGate`) is shared too.

---

## 6. Killer HUD (first person)

GDD §2 (changed 2026-10-27): the Killer plays in first person with mouse look. Signal Survivors appear as characters when in line of sight and as through-wall Signal markers when occluded. Terminals and the Exit have world markers. A Silent Survivor is never sent to the Killer (masking rule 11), so nothing is drawn for them, even when they stand in front of the camera.

### 6.0 Camera and zones
- **Camera:** eye height 1.7 m. Yaw is free; pitch is −70° to +70°; horizontal FOV is 90° at 16:9, i.e. vertical 59°, Hor+ on other aspect ratios (🔶, §11.1 N6). Movement input is relative to the view yaw, and `C2S_MoveInput.Yaw` = view yaw. There is no Killer body or hands model in view (art decision, §11.1 N6). There is no crosshair: capture is proximity-based (1.2 m), so nothing needs aiming.
- **Zones:**
  1. One **bottom-left cluster** (SLOP §8: skill and cooldown bottom-left).
  2. **World markers** anchored to world positions.
  3. **Screen-edge indicators** for off-screen Signals.

  There is no top bar, no right side and no center panel. Edge indicators never enter the cluster rectangle (they clamp to its top edge and its right edge).

### 6.1 Bottom-left cluster
Anchor bottom-left, margin 24 u, width 320 u. Each block has a `panel` fill and a 1 u `line` border, because the cluster sits over the 3D view. Stack, from the top. Info (6.1a) and Skill (6.1d) are always shown; the other blocks only when they apply. Every height below includes the 1 u border on both sides.

| # | Block | Source | Content | Size |
|---|---|---|---|---|
| 6.1e | Intro | §3 | §3 Killer intro | 320×208 [8] |
| 6.1p | Prompt | §13.3 | interact prompt (jam, Emergency port close), 2 S lines; replaced by the 13.3 hold block while holding. **(11/17, R1)** With no interact prompt in reach, `Click  Attack` (gamepad only: `RT  Attack`) while the nearest Normal tracked blip is within 3 m; **(INTEG-R minors)** not shown when a wall or a lowered (Down) shutter blocks the line to that blip (`KillerHudCopy.SwingClear`; advisory only, the press is sent either way) | 320×56, padding 8; 1 + 8 + 2 × 18 + 8 + 1 = 54 ≤ 56 [8] |
| 6.1a | Info | `TimeLeft`, `Terminals[].Progress`, `TerminalsRequired` | line 1 `9:42` (M 600 tabular `text`; last 60 s: 4×18 u `text` bar left of it); line 2 `Terminals 1/3` (M, the Survivor format, §4.3); line 3, only when done = needed − 1: `One more powers the Exits.` (S `text`) | 320×72, padding 8 left/right, 0 top/bottom; 1 + 23 + 23 + 23 + 1 = 71 ≤ 72 (line 3 budgeted at the M line height) [8] |
| 6.1c | Effect line | own Overclock effect; Killer-bound TrafficOverload effect | `Overclock · 2.1 s`, then `Recovering · 0.8 s` (static 1 s after `Duration`); `Slowed by traffic overload · 1.6 s` with a 4×18 u `danger` bar. Hidden when none. | 320×32 (S, padding 4); 1 + 4 + 18 + 4 + 1 = 28 ≤ 32 [8] |
| 6.1d | Skill block | `MatchStart.SkillId`, own `S2C_SelfState.SkillCooldown` (v0.3), own `S2C_SkillEffect`, static `KillerSkillCooldownSeconds` 12 s (ring scale), `ExitOpen` | Ring and icon as in §4.8 (64 u area, 56 u ring, 64 u active arc); name M 600 to the right; **(11/17, R1)** under it the one-line effect `PlayMenuCopy.KillerSkillLine` (S `text`, ellipsized: `Nearest Survivor shown 3 s`, `1.5× speed for 3 s`, `Signal within 12 m, 2.5 s`, `Locks a switch or terminal`); state line S `muted`: `Ready` / `Cooldown 7 s` (`ceil(SkillCooldown)`) / `Stunned`; `[Q]`. R1 removed `Passive — always on` and `Needs an Exit switch in progress`: every skill is active, and Firewall block can always target a terminal (the Killer cannot see progress; a refusal says why). | 320×88 (R1: was 72); 3 + 23 + 18 + 18 + 18 + 3 = 83 ≤ 88 |

Width checks:
- Skill block text area: 320 − 8 − 64 − 8 − 8 = 232 u. The longest name, `Packet afterimage`, is ≈ 168 u, and the longest state line, `Passive — always on`, is ≈ 158 u.
- Effect line: 282 + 4 + 4 = 290 ≤ 312.

Heights at 720p:
- Worst case, with everything shown: 208 + 8 + 56 + 8 + 72 + 8 + 32 + 8 + 88 = 488 u ≤ 900 − 48 (11/17, R1: the skill block is 88 u).
- The cluster's top edge sits at 900 − 24 − 488 = 388 u, below the screen center.
- In normal play (intro, prompt and effect line hidden), the cluster is 72 + 8 + 88 = 168 u tall; 208 u with the effect line.
- **(11/17, R1)** Effect-line width: the longest one-line effect, `Nearest Survivor shown 3 s`, is ≈ 190 u at S ≤ 232 u.

**(U7b)** The 6.1c effect line also carries the Ping scanner and Firewall block lines and the reason lines, and its Overclock copy changes (§14.1, §14.2).

**6.1f Attack (11/17, R1).** Left mouse button (gamepad right trigger) sends `C2S_Attack{Seq}`; the play menu, a lost focus or a released cursor blocks it like every other press. The server swings (0.2 s wind-up at 50 % speed, then one hit check: 2.75 m (**INTEG-R**: 2.6 → 2.75 m), ±40°, line of sight; recovery 0.6 s at 0 % after a hit, 0.8 s at 48 % after a miss) and sets the own entry's `ChaseFlags.Swinging = 16`. A hit keeps the U8 kick, impact, hit-stop and flash (from the capture confirm). A miss (no capture confirm 0.55 s after the flag rose): the 6.1c line `Missed`, a 0.26 s procedural whoosh (`V2Cue.SwingMiss`, filtered noise swell), and a first-person sweep of the view (yaw ±3°, one sine period over 0.3 s; reduced motion: none). No impact on a miss. **(INTEG-R, R1 QA M1)** A press while the own entry is Stunned, Pulling, Vaulting or Breaking plays no whoosh and no sweep (the server refuses it). **(INTEG-R, M4)** A flag that lasts at most 17 ticks reads as a hit (16 is the hit length; 18 or more is a miss). The prompt is in 6.1p.
**(a2) Immediate swing.** The press itself (once per swing: not while the own flag is up, not within 0.4 s of the last predicted press) plays the whoosh (`V2Cue.SwingWhoosh`) and the view sweep (yaw ±4° with 1.5° roll, 0.3 s). The result follows from the server: a hit adds the U8 kick and impact; a miss shows `Missed` (`text`, not `muted`, 1 s, above the own skill line). Nothing on a press ever shows a hit. **Eliminating hit:** the third capture removes the entry, so no capture confirm comes. When a Normal blip within 2.8 m vanishes during the pending swing, the verdict waits for the own flag's fall: its length in server ticks is 16 after a hit, 20 after a miss, ≤ 4 after a stun interrupt; ≤ 18 ticks (and over 4) is the hit (kick, impact where the blip was). A Survivor turning Silent also vanishes; its swing's miss-length fall says `Missed`. Without a vanish, a hit-length fall after `Missed` (an unseen Survivor) is only counted and logged.
**Survivor view (11/17, R1).** The Killer model's swipe (§15.3 / U8) starts when the Killer entry's `Swinging` flag rises (the wind-up, ≈ 0.27 s before contact), only while the model is drawn; a capture then adds no second swipe.

### 6.2 World layer
Sources: Killer-bound `S2C_WorldState.Players` (the Killer plus Signal Survivors, including Lagging, forced, afterimage and decoys), `Terminals`, `ExitOpen`, `ExitLocked`, FirewallBlock effect; static map (`FixedMap` walls, terminal and exit positions).

**Survivors.** Each anonymous blip (§6.3) is classified every frame, and the classification is *derived*:
- **in view**: inside the camera frustum, and a ray from the camera to the blip's position at 1.2 m height hits no static wall;
- **occluded**: inside the frustum, but the ray hits a wall;
- **off-screen**: outside the frustum.

The classification must hold for 100 ms before it switches, which prevents flicker at wall edges. Walls are treated as full height.

| Class | Rendering |
|---|---|
| In view, `Status = Normal` | The **Survivor character**: one shared model and one shared neutral material for every Survivor in the Killer view, with no per-player color, cosmetic, name or number (§11.1 N1). The silhouette gets a 1 u `signal` screen-space outline, meaning "transmitting". Facing and animation follow §6.3 rule 5. Beyond 20 m, the distance tag `24 m` is also drawn under the character, so a distant in-view Survivor gets the same guidance as an occluded one. |
| In view, `Status = Lagging` | The same character in the Lagging pose, with its animation stepped at 8 Hz (the GDD §3.3 stutter), plus a world tag above the head: `Lagging` (S `text` on a `panel` tag, with a 2 u `text` left bar). |
| Occluded, Normal | The character model is **not drawn**. Instead a **Signal marker**: a 16 u filled `signal` diamond at the blip position, 1.2 m high, drawn on top of walls (screen-constant size). Under it, a distance tag `24 m` (S `text` on `panel`; distance derived from positions). |
| Occluded, Lagging | The same diamond plus a 1 u `text` ring at 24 u; tag `Lagging · 24 m`. |
| Off-screen | An **edge indicator**: a 16 u `signal` chevron placed and pointed by the rule below, with tag `24 m`. Lagging chevrons get a 1 u `text` ring. Only the 6 nearest are shown, so a White noise burst cannot fill the edges. |

Decoys are classified and rendered by exactly the same rules (§6.3).

**Marker contrast.** Every world-marker shape (diamond, ring, square, chevron) has a 1 u `bg` keyline around it, so it reads against bright and dark scene areas alike; tags have a `panel` fill. No glow (SLOP V3).

**Edge-indicator placement** (*derived*, also used by §4.6):
1. Transform the target point (blip at 1.2 m height) into camera space: `x` right, `y` up, `z` forward.
2. Direction `d = normalize(x, y)` in screen space. This works for points behind the camera too (`z ≤ 0`), because camera-space `x` and `y` keep their sign. If `x = y = 0` (straight behind), use `d = (0, −1)` (bottom edge).
3. The **safe rectangle** is the screen inset 24 u, with its top moved down to 56 u while the connection band (§9) shows.
4. Cast a ray from the screen center along `d` and place the chevron where it meets the safe rectangle; the chevron points along `d`.
5. **Keep-out:** the HUD cluster expanded by 8 u. A chevron that lands inside it slides along the safe rectangle's edge, away from the screen corner, until it clears: on the left edge it moves up, on the bottom edge it moves right (Killer); on the bottom edge it moves left or right, whichever is nearer (Survivor). Tags sit on the screen-center side of their chevron.
6. On-screen markers (occluded diamonds, Terminal and Exit markers) whose projected point falls inside the keep-out are clamped to its nearest outside edge, the same way, so the cluster never hides a marker.

**Terminals.**
- An incomplete terminal gets a through-wall marker 1.5 m above it: a 14 u square outline in `text`, with the tag `Terminal 2` (S `muted` on `panel`; number = `Id + 1`).
- **No progress percentage** in the Killer view. From U2c the Killer-bound `Terminals[].Progress` is only 0 or 100 (§11.1 N9): the client treats 100 as done and any other value as incomplete, and never shows a number. A rising percentage would expose decoys standing at a terminal and Silent hacking. The Killer sees only which terminals are done and the done count (6.1a).
- When the terminal is in view and within 8 m, only the tag is drawn, because its 3D model is visible.
- A completed terminal gets no marker; its 3D model shows a done state (art).

**Exit.** Always shown as a through-wall marker 2.5 m above the exit:
- **(U6c-wire)** One marker per exit, as specified in §13.4 (`Exit 1 · Powered`, `Exit 2 · Locked 3 s`, …). The v1 single `Exit Locked` / `Exit Open` tag is gone.

**(11/10) Tag clearance:** `TagLayout` keeps every placed tag `TagGap` = 4 u (Q2's edge-marker gap) clear of every other placed tag and of every other visible glyph it must avoid, including Survivor edge chevrons for Survivor distance tags; a tag keeps its default offset from its own glyph. Stacked tags step by tag height + 4 u. Exception: a rules v2 Overloaded or Exit tag that finds no slot at 4 u retries at 2 u, then with no clearance (the pre-11/10 rule), before it is hidden, so a crowded edge stays readable; Survivor tags never fall back.

**Draw order**, back to front: terminal and exit markers, Signal markers and edge indicators, then the cluster. Markers never use glow (SLOP V3). Only Survivors get the `signal` outline.

**Shading (U4i, 2026-11-03).** Characters and world art use flat toon bands (`PingTheKiller/Toon`, the Toon Shaders Pro URP shader with its double-albedo multiply fixed): two bands for world art (ground with a soft shadow edge), three for characters, a cool dark shadow tint that is never black. No outline pass, no rim light, no specular; the `signal` hull above stays the only outline. Lit windows and signs stay unlit colours; the Exit marker keeps its particle material.

### 6.3 Anonymous blips (normative; decoy safety)
Decoys are `PlayerState` entries shaped like a Normal Signal Survivor that borrow the Id of an absent Survivor (U2b rule 9). The server sets the `Afterimage` flag only on real Survivors. Per the 2026-10-27 decision (§11 Q8), U2c may replace Ids in the Killer view with per-snapshot indices. These rules work either way:
1. **Read only `Role`, `Status`, `Flags.Signal`, `X`, `Z`** of Survivor entries. Never `Id`, `Yaw`, `StatusTimer`, `CaptureCount`, `Flags.Hacking`, `Flags.Rescuing`, `Flags.Stealthed` or the `Afterimage` flag. Entries with `Afterimage` set render exactly like the others; they get the `signal` outline although they are no longer transmitting, because to the Killer amber means "reported by the server" (§11 Q8).
2. **Match by position, not by Id.** In each snapshot, the Survivor entries form an unordered set.
   - The client shuffles the received list. Distance ties are broken randomly. Nothing (spawn order, sound, draw order) follows the wire order.
   - Match greedily by nearest distance to the previous snapshot's blips, accepting only pairs within 0.4 m (2 × 4.0 m/s × 0.05 s).
   - A matched pair keeps its character or marker instance, which tweens over one tick.
   - An unmatched new entry spawns a new instance in place, with no spawn animation.
   - An unmatched old blip despawns instantly, with no death or fade animation.

   A borrowed Id that jumps across the map therefore looks exactly like one Survivor vanishing and another appearing.
3. Status-change cues use matched pairs or the Killer's own position only (§6.5).
4. No per-instance identity anywhere: no names, colors, numbers, cosmetics, and no per-entry sound identity. The positional sound of §6.5.1 is a function of position, matched speed and `Status` only, the same for every entry.
5. **Facing and animation are derived only from matched position deltas.**
   - Facing = the direction of the most recent matched displacement ≥ 0.03 m (below the slowest per-tick step, a Lagging Survivor's).
   - An instance that has not moved since it spawned faces the nearest terminal (static data). This applies equally to real and fake entries.
   - Animation: walk when the matched speed is > 0.5 m/s, otherwise idle; the Lagging pose when `Status = Lagging`.
   - There is no hacking or rescuing animation, because those flags are unread (§11.1 N2).
   - **(11/03, U4j) Character motion in the Killer view.** The motion reads only `Status` and the matched speed, the same for real entries, afterimages and decoys. Allowed: locomotion; on a matched Normal → Lagging change, the hit flinch blending into the knock-down; while Lagging, the crawl (frozen when still), stepped at 8 Hz. Never: hacking/button pushing, rescuing, getting up, the injured limp or anything from `CaptureCount`. An unmatched new Lagging entry spawns already down (no knock-down). The knock-down falls in place over the entry's position. While the body is down, the `Lagging` tag sits just above it (0.7–0.9 m), not at standing head height. An unmatched old blip still despawns at once; **no death clip plays in the Killer view** (Eliminated entries never reach it).
   - The Killer's own capture (`CaptureConfirm`, §6.5) gives a 0.22 s first-person lunge: vertical FOV −4° and the eye 0.12 m forward, easing out. None with reduced motion (`-ptk-reduce-stutter`, later the settings toggle).

### 6.4 Execution guidance — removed in v2
**(U6c-wire)** GDD v2 has no execution and protocol v0.4 has no `ExecutionGraceLeft`. The 6.1p prompt slot now shows the §13.3 Killer interact prompt (jam, Emergency port close), and the hold block while holding.

### 6.5 Killer sounds
| Cue | Trigger (all observable in the Killer view) |
|---|---|
| `blip_first` (soft ping, −12 dB, non-positional) | The Signal count goes from 0 to ≥ 1 after ≥ 2 s with none. |
| `capture_confirm` (hit) | Either a matched pair changes `Status` Normal → Lagging, or a new unmatched entry with `Status = Lagging` appears within 1.2 m of the Killer (the usual ambush). Decoys are never Lagging. At most once per 0.5 s. |
| `execute_confirm` (low hit) | A Lagging instance within 1.2 m of the Killer has no match in the next snapshot. Lagging Survivors are always visible, so this means Eliminated. |
| `exit_open` | `ExitOpen` 0 → 1 |
| `skill_use` / `skill_ready` | own effect / own `SkillCooldown` reaching 0. **(U7b)** `skill_use` is replaced by each skill's own cue, and Traffic overload's slow gets a cue (§14.2). |
| `time_60` | `TimeLeft` crosses 60 |

There is no cue for eliminating a Normal Survivor whose `CaptureCount` is already ≥ 1, because that Survivor leaves the view in the same tick (§11 Q7, decided: not now).

### 6.5.1 Positional Survivor sound (footsteps and breathing; §11.1 N3)
Decided 2026-10-27: the Killer hears Signal Survivors in the style of Dead by Daylight. This is **diegetic world sound** (§0.7): it belongs to the character or marker instance in the 3D scene, like its animation, and says nothing the visuals do not already show.

**Sources** (*derived*, per anonymous instance, §6.3): the instance's rendered (tweened) position; its matched speed `v`; `Status`. Nothing else: never `Id`, `Yaw`, flags, `StatusTimer` or `CaptureCount`, and never the wire order.
- `v` = mean length of the instance's last 4 matched displacements ÷ 0.05 s (fewer if it has fewer). A new instance has `v = 0` until its first match.
- Silent Survivors are not on the wire, so they make no sound. When an instance despawns, its sounds stop with a 50 ms fade (no tail, no echo), exactly as its model or marker disappears.
- Decoy safety: every parameter below is a function of position, `v` and `Status` only. From U2c, decoys walk at Survivor speed (§11.1 N7), so a decoy and a real Survivor moving the same way sound the same. Random choices (sample variant, first-step phase, pitch) come from a client RNG that is never seeded from wire data.

**Footsteps:**

| Variant | Applies when | Samples | Step interval | Audible range |
|---|---|---|---|---|
| Normal | `Status = Normal`, `v > 0.5 m/s` | `step_survivor` (4 variants, random each step, pitch ± 5 %) | `1.2 m ÷ v`, clamped 0.25–0.9 s (0.30 s at 4.0 m/s) | full to 2 m, log rolloff, silent beyond 20 m |
| Lagging | `Status = Lagging`, `v > 0.2 m/s` | `step_lagging` (dragging scuff, 4 variants) | `0.8 m ÷ v`, clamped 0.5–1.5 s (0.8 s at the Lagging 1.0 m/s) | full to 2 m, silent beyond 20 m |

The first step of a newly moving instance plays after a random delay in `[0, interval)`, so step phase carries nothing. Below the speed threshold there are no steps.

**Breathing** (one steady loop per voiced instance, cross-faded over 300 ms when its variant changes):

| Variant | Applies when | Level at 2 m | Audible range |
|---|---|---|---|
| Calm | `Status = Normal`, `v ≤ 0.5 m/s` | −30 dB | silent beyond 6 m |
| Heavy | `Status = Normal`, `v > 0.5 m/s` | −24 dB | silent beyond 8 m |
| Ragged | `Status = Lagging` | −24 dB | silent beyond 10 m |

**Spatial and mix:**
- **Levels** are relative to the UI-cue reference level (0 dB = `capture_confirm`), so world sound always sits below the cues.
- **Attenuation** (every row above): the stated level up to 2 m, then linear in dB down to −40 dB at the row's range limit, then a hard mute beyond it. Sources: footsteps at 0.1 m height, breathing at 1.5 m.
- **Step timer**, per instance: it runs while `v` is above the row's threshold. Thresholds have hysteresis: footsteps start above 0.5 m/s and stop below 0.4 m/s (Lagging: 0.2 / 0.15 m/s); breathing switches Calm ↔ Heavy at the same points. Each time stepping starts, the timer restarts with the random first-step delay.
- 3D panning by azimuth relative to the camera yaw, no Doppler.
- Occlusion uses the §6.2 wall ray (cast even for off-screen instances): occluded instances get a 1.5 kHz low-pass and −4 dB, cross-faded over 100 ms and switching with the same 100 ms hysteresis as §6.2. Footsteps peak at −20 dB at 2 m.
- **Voice cap:** only the 4 nearest instances are voiced (one footstep voice and one breathing voice each). A voiced instance keeps its voice until another instance is at least 1 m nearer, so voices do not flicker. With a White noise burst, the extra instances stay silent but keep their markers. A voiced instance is always drawn: the 6-nearest edge-indicator selection (§6.2) takes voiced instances first.
- Priority (§0.7): world sound is lowest. It ducks by 6 dB for 300 ms under every UI cue (§6.5 table).

The Killer itself makes no sound on Survivor clients. **(10/30)** Its entry reaches a Survivor only in line of sight (§0.1) and is used only for the model (§4.10), never as a sound source.

**(10/30)** The Killer-bound view (§6.2–§6.5) is unchanged by the U2e contract, and the anonymity rules of §6.3 stand.

### 6.6 Killer play menu
Esc / Start replaces the intro/prompt area at the top of the cluster with a 320×256 block **(U7a: was 320×208)**, padding 12, inner width 296 u. It holds the skill name (M 600), the effect (§8; S, max 3 lines), then `Resume` (primary, focused), `Settings` (opens §12) and `Leave match`, stacked as 296×40 u buttons with an 8 u gap.
- Height: 12 + 23 + 4 + 3 × 18 + 12 + 40 + 8 + 40 + 8 + 40 + 12 = 253 ≤ 256.
- Behaviour is the same as §5: one zero MoveInput, the cursor is released, and the match keeps running.
- Focus: Resume → Settings → Leave match. Esc / B = Resume.
- The HUD itself has no focusable elements.

---

## 7. Result screen and return to lobby

Sources: `S2C_GameResult{WinnerRole, Reason, EscapedIds[]}`; own role (`MatchStart`); names from the stored LobbyState; the last `S2C_WorldState` (`Terminals`, `TerminalsRequired`, `TimeLeft`). The server returns to the lobby right after GameResult and broadcasts `S2C_LobbyState`, which the client stores.

Layout: menu style, left column, margin 48 u, full `bg` behind (the 3D view is not shown).

| # | Element | Content | Style |
|---|---|---|---|
| 7.1 | Headline | `Survivors win` / `Killer wins` | L 600 `text` [8] |
| 7.2 | Your outcome (v1, replaced by §13.7) | Survivor, in `EscapedIds`: `You reached the Exit.` · Survivor, not escaped, Survivors win: `Your team escaped.` · Survivor, Killer wins: `You did not escape.` · Killer, `AllSurvivorsDown`: `You stopped every Survivor.` · Killer, `TimeUp`: `Time ran out before the Survivors escaped.` · Killer, `SurvivorEscaped`: `A Survivor reached the Exit.` · **(10/30)** Survivor, eliminated with a §4.1 reason: `You were eliminated. ` + the reason line (e.g. `You were eliminated. Disconnected: caught a third time.`) | M `text` [8] |
| 7.3 | Reason (v1, replaced by §13.7) | `SurvivorEscaped`: `Mina reached the Exit with 4:12 left.` (names from `EscapedIds`, joined by `, `) · `AllSurvivorsDown`: `Every Survivor was Lagging or eliminated.` · `TimeUp`: `Time ran out with the Exit closed.` / `Time ran out with the Exit open.` | S `muted` [24] |
| 7.4 | Fact | `Terminals hacked: 4 (3 needed)` — the done count and `TerminalsRequired`, both known to both roles. Captures and rescues are not in the protocol (§11 Q7). | M `text` [32] |
| 7.5 | **Back to lobby** (primary) | → Lobby with the stored state | 280×48 |
| 7.6 | Leave | disconnect → Main menu (offline or hosting: stops the in-process server) | secondary 160×40, right of 7.5, gap 16 |

No banner, confetti or score (SLOP §6). One sound: `result_win` or `result_lose` (800 ms chord, from the viewer's side).
If `S2C_MatchStart` arrives while this screen is open → §3.
Focus: 7.5 → 7.6. Initial 7.5. Esc / B = 7.5.

---

## 8. Skill table (static: `SkillIds` + GDD §4)

| Role | Id | Name | Effect line (intro, play menu) | Icon (1 u line, 40×40 u; depicts the effect) |
|---|---|---|---|---|
| Killer | 0 | Ping scanner | `Shows the nearest Survivor for 3 s.` (11/17, R1: was 1.5 s) | three concentric arcs from a point toward a small dot |
| Killer | 1 | Overclock | `1.5× speed for 3 s, then 0.3 s slowed.` (11/17, R1: was a 1 s freeze; the server runs the tail at 60 % speed, the Killer copy shows no percent sign) | two right chevrons followed by a vertical stop bar |
| Killer | 2 | Packet trace (11/17, R1: was the passive Packet afterimage) | `Survivors within 12 m of you turn Signal for 2.5 s.` | a centre dot in a dashed ring with four short rays bursting out |
| Killer | 3 | Firewall block | `Locks the Exit switch in progress for 5 s, or else the most hacked terminal for 8 s.` (11/17, R1) | a ring with a horizontal bar through it |

**(11/17, R1)** One copy source: `PlayMenuCopy.KillerEffect(skill)` / `PlayMenuCopy.SurvivorEffect(skill)` feed the intro, the play menu and the lobby picker (§2.9); `PlayMenuCopy.KillerSkillLine(skill)` is the short 6.1d line.
| Survivor | 0 | Dummy packet | `A fake Signal where you stand, 3 s.` | a dashed-outline dot |
| Survivor | 1 | Stealth relay | `You and Survivors within 5 m stay Silent for 2 s, even while moving.` | a dot inside a circle, a wave line crossed out |
| Survivor | 2 | Traffic overload | `You are Signal for 2 s; the Killer moves at half speed for 2 s.` | a dot emitting two arcs, a down chevron beside it |
| Survivor | 3 | Emergency restore | `Instantly rescues a Lagging Survivor next to you.` | two dots joined by a solid line |
| Survivor | 4 | White noise | `Five fake Signals across the map, 1.5 s.` | five scattered dots |

---

## 9. Connection lost / reconnecting

Sources: socket state; time since the last packet; `S2C_Pong` (the client sends `C2S_Ping` every 1 s on every screen); the stored `S2C_JoinResult.ReconnectToken`; `S2C_JoinResult.Result`. Not shown in offline mode (the in-memory transport cannot drop), and not shown to the hosting player for their own in-process server.

Trigger: the socket closes, or no packet arrives for 3 s.

Display:
- A full-width band at the top, 48 u tall, `panel` fill, 1 u `line` bottom border, M `text` at x 24.
- The band never overlaps a HUD block: the Survivor cluster and the Killer cluster are both bottom-anchored (worst-case Killer cluster top edge 404 u, below the band's 48 u).
- Gameplay input is ignored while the band shows.
- The 3D view keeps its last frame. There is no spinner; the text states the reason (SLOP §5).

| State | Trigger | Band text | Action |
|---|---|---|---|
| Reconnecting | the trigger above; reconnect + `C2S_Join{ReconnectToken}` every 1 s | `Connection lost — reconnecting. Your slot is kept for 7 s.` (seconds = 10 − elapsed, derived from the server's 10 s window) | — |
| Rejoined | `Result = 0`, same `PlayerId` | band removed; the server re-sends `S2C_MatchStart`, which re-enters play **without** the intro | `reconnected` (soft) |
| Slot lost | `Result = 2`, or 10 s elapsed with the server reachable | `Your slot went to a bot after 10 s offline.` + **Back to menu** (primary, focused) | `disconnected` |
| Server gone | 10 s of failed connects | `Server not reachable at 192.168.0.10:7777 — the host may have stopped.` + **Back to menu** | `disconnected` |

Focus: Back to menu only, when shown. Esc / B = Back to menu.

---

## 10. Element-to-data index

| Element | Source |
|---|---|
| Lobby slots, tags, bots, role, offline | `S2C_LobbyState.Slots[].{PlayerId, Nickname, IsBot, IsHost}`, `.HostRoleChoice`, `.IsOffline` |
| Own id, reconnect, join errors | `S2C_JoinResult.{PlayerId, ReconnectToken, Result}` |
| Intro | `S2C_MatchStart.{Role, SkillId}`, `S2C_WorldState.TerminalsRequired` |
| Transmit meter | own `PlayerState.{Status, Flags, StatusTimer, CaptureCount}` (Survivor view); PingScanner and own TrafficOverload `S2C_SkillEffect.Duration` |
| Threat cue | `S2C_WorldState.ThreatLevel` |
| Terminals, prompts | `S2C_WorldState.Terminals[].{Id, X, Z, Progress}`, `.TerminalsRequired`; own `Flags.Hacking`; static radius |
| Rescue progress, protection | own `S2C_SelfState.{RescueProgress, InvulnerableLeft}` (v0.3) |
| Exit | `S2C_WorldState.{ExitOpen, ExitLocked}`; FirewallBlock `S2C_SkillEffect.Duration` |
| Skill state | own `S2C_SelfState.SkillCooldown` (v0.3) + own `S2C_SkillEffect.{CasterId, SkillId, Duration}` + static cooldowns as ring scale |
| Time | `S2C_WorldState.TimeLeft` |
| Killer characters, Signal markers, edge indicators | Killer-bound `PlayerState.{Role, Status, Flags.Signal, X, Z}` as a shuffled anonymous set; occlusion derived from static walls; facing and animation from matched position deltas |
| Killer effect line | own Overclock / Killer-bound TrafficOverload `S2C_SkillEffect.Duration` |
| Skill feedback (U7b): lines, world effects, cues | `S2C_SkillEffect.{CasterId, SkillId, X, Z, Duration}` resolved per §14.0; Survivor side also own and Survivors' `PlayerState.{Status, Flags.Stealthed, X, Z}`, own `SelfState.ActionKind`, lobby names; static exit switch points |
| Spectate | Survivors' `PlayerState.{X, Z, Yaw, Status}` (orbit pivot and character facing) |
| Survivor camera and movement | own `PlayerState.{X, Z, Yaw}`; static walls (boom collision); input → `C2S_MoveInput.{DirX, DirZ, Yaw}` (§4.10) |
| Killer model (Survivor view) **(10/30)** | the Killer entry `PlayerState.{Role = Killer, X, Z, Yaw}`, present only in line of sight (§0.1, §4.10) |
| Capture banner, elimination reason **(10/30)** | own `PlayerState.{Status, StatusTimer, CaptureCount}` transitions (§4.1) |
| Terminal and Exit markers (Killer) | `Terminals[].{Id, X, Z, Progress = 100}` (done only, no percentage), `ExitOpen`, `ExitLocked`, FirewallBlock `Duration`; static positions |
| Killer prompt slot | own position, `Terminals[]` (kick within `TerminalRadius`), `HatchVisible/X/Z` (close within `HatchRadius`); own `SelfState.ActionKind/ActionProgress` for the hold block (§13.3) |
| Positional Survivor sound (Killer) | per anonymous instance: rendered position, matched speed, `Status`; static walls for occlusion (§6.5.1) |
| Result | `S2C_GameResult.{WinnerRole, Reason, EscapedIds}` + the last WorldState |
| Connection band | socket, `S2C_Pong`, `S2C_JoinResult.Result` |

Never used:
- The Killer's position or direction, on a Survivor client, **(10/30)** except the line-of-sight Killer entry, for the model only (§0.1).
- `Id`, `Yaw`, `StatusTimer`, `CaptureCount` and `Afterimage` of Survivor entries, on the Killer client.
- Counts of hidden Survivors.
- Another player's `S2C_SelfState` (it is never sent; the Killer never receives Survivor cooldowns, rescue progress or protection).

---

## 11. Decisions (2026-10-27)

The user decided every open question from the review loop. "Final" means the spec's choice stands as written.

| # | Question | Decision | Effect on this spec |
|---|---|---|---|
| Q1 | Vocabulary: SLOP §8 said `Frozen`; the GDD and protocol say `Lagging`. | **Final.** SLOP §8 is amended to the GDD/protocol terms: Lagging (replacing Frozen), Eliminated, Escaped, Terminal, hack, spectate. | All copy uses these terms. |
| Q2 | Threat visuals vs GDD §3.2. | **Final.** One static visual per level: meter-edge noise at 1/3 and 2/3 width; screen-edge noise at level 3; re-rolled once per level change. | §4.2 |
| Q3 | No sound on Signal ↔ Silent. | **Final** (a deliberate exception to SLOP §8 feedback). | §0.7, §4.1 |
| Q4 | 3 s pre-match freeze. | **No, not now.** | The intro stays non-blocking (§3). |
| Q5 | Skill cooldown on the wire. | **Yes.** A new own-only packet `S2C_SelfState { SkillCooldown, RescueProgress, InvulnerableLeft }`, planned for **U2c / protocol v0.3**. It goes only to the player it describes. It is never a `PlayerState` field, and the Killer never receives it about anyone else. | §4.8 and §6.1d read `SkillCooldown`; §4.1 shows `Protected` from `InvulnerableLeft`. The derived cooldown timer is removed. These elements depend on U2c; until it lands they cannot be built as specified. |
| Q6 | Rescue progress on the wire. | **Yes**, the `RescueProgress` field of the Q5 packet. Semantics assumed here: own rescue (Lagging) or the rescue you are taking part in (rescuer), 0–1; U2c fixes the encoding. | §4.1 row 3 and §4.4 read `RescueProgress`; the derived estimate is removed. |
| Q7 | Result stats and a Killer capture event. | **No, not now.** | The result shows terminals only (§7.4). There is no cue for eliminating a previously captured Normal Survivor (§6.5). |
| Q8 | Decoy detectability outside the UI. | **Under design, settled in U2c.** The user chose to evaluate shuffled per-snapshot indices instead of Ids in the Killer view, plus afterimage applied to decoys. | §6.3 keeps position-only matching. The Killer view **may stop carrying stable Ids**, so no Killer-side code may depend on `PlayerState.Id` of Survivor entries. §6.3 already forbids it. |
| Q9 | Host role choice online. | **Final.** Shown online as `Host plays as` (host only). | §2.8 |
| Q10 | Localization. | **Final.** English first with a string table; a Korean layout pass at 720p before Store screenshots. | All copy. |
| Q11 | Exit-open color. | **Final.** Survivors see an `ally` bar and ring; the Killer sees a `text` ring. | §4.7, §6.2 |
| Q12 | Lagging stutter and photosensitivity. | **Final.** 8 Hz hold + 30 % blend by default. The reduced alternative is blend only, with no border. It is a launch option until a settings screen exists, and a settings toggle is required before Store submission. | §4.5 |
| Q13 | S text at 720p. | **Final.** S renders at ≥ 12 px physical; the panel-scale minimum stays 0.8. | §0.3 |
| Q14 | Hosting from the game. | **Yes.** In-process `GameServer` + `TcpTransport` from the client. Expect a Windows Firewall prompt on the first host, and the Store package must declare the network-server capability (`privateNetworkClientServer`). | §1.7 |
| Q15 | Panel scale above 1080p. | **Final.** The scale stays clamped at 1.0 for now. | §0.2 |

### 11.1 Decisions on the camera-change questions (2026-10-27)
GDD §2 gives Survivors a third-person orbit camera and the Killer a first-person view. The user decided every question this raised.

| # | Question | Decision | Effect on this spec |
|---|---|---|---|
| N1 | Survivor appearance in the Killer view: any per-player look would let the Killer track a real Survivor against decoys. | **Accepted.** One shared Survivor model and material in the Killer view, no names. | §6.2. It constrains any future cosmetics; Survivors may still see each other's looks. |
| N2 | Hack and rescue animations in the Killer view (decoys never carry `Hacking` / `Rescuing`). | **Accepted.** No hack or rescue animation in the Killer view. | §6.3 rule 5 |
| N3 | Positional Survivor sound for the Killer. | **Yes** (changed from the spec's earlier "none"). Footsteps and breathing per Signal instance, Dead by Daylight style, derived only from on-wire position and motion; Silent Survivors make no sound; decoys sound identical. | New §6.5.1; §0.7 separates diegetic world sound from UI cues. |
| N4 | SLOP §6 and §8 still described a first-person Survivor and a top-down Killer. | **Accepted.** SLOP §6 and §8 "Role-specific HUD" are amended to GDD §2 (user-approved edit, those lines only). | `docs/ui/SLOP_DEFINITION.md` |
| N5 | Execution grace guidance was derived from the observed capture and was lost after a reconnect. | **Server-side, U2c.** The Killer's own `S2C_SelfState` gets `ExecutionGraceLeft`: grace left for the nearest Lagging Survivor within 3 m. | §6.4 reads it; the derived `t0` and its fallback are removed. Depends on U2c. |
| N6 | Camera numbers, Killer hands model, FOV / sensitivity settings. | **Accepted.** The numbers stay 🔶 for playtest tuning. An FOV setting and a mouse-sensitivity setting are **required before Store submission**. | §4.10, §6.0 |
| N7 | Stationary decoys would be told apart from moving Survivors. | **U2c design:** decoys move, walking at Survivor speed, and still appear only at terminals. | §6.3 and §6.5.1 need no change: facing, animation and footsteps already follow position and motion only. |
| N8 | Occlusion source for the Killer's in-view / occluded split. | **Accepted.** Full-height walls only. | §6.2 |
| N9 | Terminal progress in the Killer view. | **Server-side, U2c.** The Killer receives `Terminals[].Progress` as 0 or 100 only. | §6.2 and 6.1a show done / not done and the done count; never a percentage. |

---

## 12. Settings (U7a)

Opened from the main menu (`Settings`, secondary, under `Play against bots on this PC`) and from the Killer play menu (§6.6). **Gap:** the Survivor play menu (§5) is not built yet, so a Survivor has no way to open Settings during a match until it is. Esc outside a match closes it; during a match Esc closes it first and a second Esc closes the play menu.

One panel, 640 u wide, 24 u from the top-left (top-right over a match), `panel` fill, 1 u `line` border, padding 32 u (24 u on top). It replaces the menu; over a match it sits at the top-left, so the centre of the view stays visible while FOV is adjusted. Each row is 48 u high with a 12 u gap: label 240 u, control 240 u, value 80 u (right-aligned, tabular).

| # | Setting | Control | Range | Default | Stored as (`PlayerPrefs`, float) |
|---|---|---|---|---|---|
| 12.1 | Survivor field of view | slider, whole degrees | 60–90° | 79.2° shown `79°` (vertical 50°, as before) | `ptk.settings.survivorFov` |
| 12.2 | Killer field of view | slider, whole degrees | 60–100° | 90° (vertical 58.7°, §6.0) | `ptk.settings.killerFov` |
| 12.3 | Mouse sensitivity | slider, 0.05 steps, shown `1.00×` | 0.25×–3× | 1× | `ptk.settings.sensitivity` |
| 12.4 | Invert Y | toggle button `On` / `Off` | | Off | `ptk.settings.invertY` |
| 12.5 | Reduced motion (**U7b:** also skill effects, §14.1) | toggle button `On` / `Off` | | Off | `ptk.settings.reducedMotion` |
| 12.6 | Master volume | slider, 5 % steps, shown `100 %` | 0–100 % | 100 % | `ptk.settings.volume` |

- **FOV is the horizontal angle at a 16:9 reference** (§6.0 convention). The rig converts it to Unity's vertical FOV, so other aspect ratios stay Hor+. The 60° lower bound is a horizontal angle, so today's Survivor (vertical 50° = 79.2° horizontal) and Killer (90°) defaults are inside their ranges.
- **Mouse sensitivity** multiplies `RoleCamera.Sensitivity` (0.12° per count) for both axes. **Invert Y** flips only the pitch sign. Both roles.
- **Reduced motion** is the Lagging look's reduced mode (§4.5, **(11/17, U8)**: the steady look only, no burst, tear or flash; also no Killer view kick or shake). `-ptk-reduce-stutter` still forces it on whatever the setting says.
- **Master volume** sets `AudioListener.volume`.
- A stored value that is missing, NaN or out of range loads as that setting's default; the others are kept. Setters clamp.
- Every control applies at once; `Done` (primary, 320×48) and Esc outside a match save. `Reset to defaults` (secondary) restores all six. The values are also saved when the app quits.
- Focus: Survivor FOV slider first; Tab / Up / Down move through the controls in the order above, then `Done` → `Reset to defaults`. Left / Right change a slider; Enter or Space presses a button. Focused slider: brighter track and a wider handle. Hover: lighter track.
- Notes in S `muted`: under the FOV rows `Horizontal angle at 16:9. Applies at once.`, under Reduced motion `Lagging shows every frame instead of holding the picture at 8 Hz.`
- Presentation only: nothing here is sent to the server.

---

## 13. Rules v2 (GDD v2, protocol v0.4)

Sources are the protocol v0.4 fields from the U6a brief. The widgets live in `Client/Assets/Scripts/UI/RulesV2/` and read only plain view models (`RulesV2Views.cs`), so the wiring copies packet fields in. Style is in `Resources/RulesV2.uss`. All global rules in §0 apply: logical units, spacing scale, radius 0, S/M/L type, palette meanings, `danger` never on text.

**New fixed terms** (added to the SLOP §8 vocabulary): route **(11/17, R2; Sync lock and Great / Good / Fail are retired with the sync check)**, Powered, No power, switch, Emergency port, Shutdown, Rolling back, Overloaded, jam (verb, Killer). **(11/17, IP)** renamed from Sync check, Hatch, Collapse, Regressing and kick, which matched another game's terms.

### 13.1 Placement
- **Survivor:** one bottom-center stack sits directly on the cluster (§4.0). Its bottom edge is at 96 u (the cluster top), and every child keeps an 8 u gap below it. From top to bottom:
  1. the route panel (13.2), only while E is held on a route **(11/17, R2)**;
  2. the prompt row: the §4.3/§4.4 prompts or the 13.3 hold row, 672×40;
  3. the **objective row** (672×48), shown once any exit is Powered or Open, or a collapse clock runs.
  With every child shown, the stack's top is at 96 + 48 + 8 + 40 + 8 + 80 + 8 = 288 u **(11/17, IP: the bar is 80 u tall, the old ring 144 u; R2: the route panel keeps the same 320×80 u)**. The panel only shows while hacking, which is in act 1, when the objective row is normally hidden; then the panel spans 152–232 u from the bottom, so its top edge is at 668 u of 900 at 720p (74.2 %) and 856 u of 1080 at 1080p (79.3 %). That is below the feet at default pitch (67 %, §4.10), so it never covers the body. If a route shows while the objective row shows (hacking after power), the panel sits 56 u higher, its top edge at 620 u (68.9 % at 720p), still below the feet.
- **v2 info block:** line 3 of the info block (§4.7, `Exit …`) is removed in v2. The objective row replaces it. Line 1 stays the match time and line 2 stays `Terminals 1/5`.
- **Killer:** two blocks go into the 6.1 cluster between the prompt slot and the info block. In order: the hold block (13.3, in the 6.1p prompt slot) and the collapse block (13.5). Exits stay on world markers (13.4), never in a panel (SLOP §8).

### 13.2 Route panel (Survivor) **(11/17, R2)**
Replaces the sync lock bar (and the sync check behind it: `S2C_SyncCheck`, `S2C_SyncResult`, `C2S_SyncPress` and their ids 0x11–0x13 are retired). Hacking is now an active minigame: the server deals the hacker a route of 4 directions; each right key moves along it; a full route adds a burst of progress.
Source: own `S2C_SelfState {RouteId, RouteLength, RouteDirs, RouteIndex}` (appended fields; `RouteId` 0 = no route; `RouteDirs` packs 2 bits per cell, the first cell in the low bits: Up 0, Down 1, Left 2, Right 3). Only the hacker receives its route; the Killer sees only the terminal's Overloaded flash (§13.6).
- **Keys (attempt 2):** only while E is held, the own entry is `Hacking` and a route is present (the server sends a route only while its owner hacks that terminal), W A S D and the arrow keys, the gamepad d-pad or a left-stick flick (fires past 0.6 of the radius on its dominant axis, re-arms inside 0.3) send `C2S_HackInput {RouteId, Direction}` on the press edge, and movement sends zero. Releasing E gives the keys back to movement at once. The client sends at most 12 keys per second (the server's limit).
- **Shape (320×80 u):** `panel` fill, 1 u `line` edge. Four 56×36 u cells at y 8, x 24 / 96 / 168 / 240, each with a drawn arrow (12 u, no glyph font):
  - done cells: `muted` fill, `bg` arrow;
  - the current cell: 2 u `text` edge, `text` arrow;
  - later cells: 1 u `line` edge, `muted` arrow.
- **Right key:** the cell it completed pulses (+3 u, 0.12 s), a soft tick plays, and the own holo panel pulses (§4.10).
- **Panel:** shown while E is held and the own entry is `Hacking` (so the flash and the short gap stay on screen), hidden otherwise.
- **Full route:** the label shows the burst, `+3.4 %` (M 600 `text`), for 0.6 s and a rising chord plays. Then `Next route` (S `muted`) with a thin `muted` bar that fills over the gap (0.7 s) until the next route arrives. During the gap W A S D move.
- **Wrong key:** the route is dropped (the server takes 2 points and deals a new route; the terminal is Overloaded for 1.5 s, which the Killer sees). The panel shakes ±4 u for 0.3 s, its edge turns 2 u `danger` and the label reads `Route dropped · the Killer was alerted` (S `muted`; `danger` stays off text) for 1.5 s; a glitch sound plays. Nothing more is sent for the dropped route.
- **Label row (y 52–80 u), otherwise:** `Route · WASD` (or `· D-pad` on a gamepad), S `muted`. The first route of the match shows `Press the arrows in order` instead, until it completes or drops.
- **Prediction:** the client knows its route, so it moves the current cell, the pulse, the flash and the error at once, then reconciles with the next SelfState: for the same `RouteId` within 0.3 s of the last key the shown index never falls behind the server's; later than that the server's index wins and a predicted error or completion is undone (a dropped key, attempt 2); a new `RouteId` starts clean; `RouteId` 0 clears the cells.
- **Reduced motion (§14.1):** no pulse growth and no shake; the colours, label and sounds stay.
- **Wiring:** the route clears at once when the own status leaves Normal and fully resets (the first-route hint again) on MatchStart, GameResult and disconnect. No "skill check" wording anywhere.
- **Numbers (GDD v2 §2, R2, attempt 2):** passive progress 100 points per 90 s solo; +3.4 per completed route (each of n co-hackers adds (CoHackRate(n)/n)², 0.81 for two), then a 0.7 s gap counted only while hacking, and at least 1.3 s between two routes being dealt; −2 and a new route on a wrong key. Solo: 0.15 s per key ≈ 29 s, 0.5 s ≈ 44 s, 1 s ≈ 56 s, idle 90 s; every fifth key wrong ≈ 50 s; two fast co-hackers ≈ 16 s; the bots ≈ 41 s.

### 13.3 Hold action (Survivor exit switch, Killer jam and Emergency port close)
Source: `S2C_SelfState {ActionKind, ActionProgress}`. `Progress` is clamped to 0–1, and the percentage is `floor(progress × 100)`, so the row never shows 100 % before the server finishes.

| Kind | Role | Text | Block |
|---|---|---|---|
| 1 switch | Survivor | `Opening the Exit · 62% — you are Signal` (M) | prompt row 672×40, 656×4 bar `text` |
| 2 jam | Killer | `Jamming the Terminal · 55%` (M 600) + `Hold [E] · moving cancels the jam` (S `muted`) | 320×72: padding 8/7; 23 + 4 + 4 bar + 4 + 18 |
| 3 Emergency port close | Killer | `Closing the Emergency port · 70%` + `Hold [E] · releasing cancels` | same |

`ActionKind = 0` hides the row or block.

**Interact prompts (U6c-wire).** Before a hold starts, the same row or block shows what E would do. The hold row or block replaces the prompt while `ActionKind ≠ 0`. Survivor prompts apply only when no §4.3/§4.4 prompt (other than a muted one) or banner is showing and the own `Status = Normal`; the Killer's are hidden while the play menu is open.

| Role | Condition (first match wins) | Line 1 | Line 2 (Killer, S `muted`) |
|---|---|---|---|
| Survivor | `HatchVisible` and within `HatchRadius` 1.5 m | `Press [E] to escape through the Emergency port` | — |
| Survivor | nearest exit switch within `ExitSwitchRadius` 1.5 m, exit Powered, not Open, not Locked | `Hold [E] to open Exit 1 · 62% — you will be Signal` | — |
| Survivor | the same switch while Locked | `Exit 1 is Locked 3 s by the Killer` (`muted`; seconds from the own FirewallBlock knowledge, else `Exit 1 is Locked by the Killer`) | — |
| Killer | `HatchVisible` and within `HatchRadius` | `Hold [E] to close the Emergency port` | `Stand still · starts the shutdown` |
| Killer | an unfinished terminal within `TerminalRadius` 1.5 m (Killer `Progress` is 0/100 only, N9) | `Hold [E] to jam Terminal 3` | `Stand still · hacked Terminals only` |

Every Killer line is at most 38 characters and fits the 302 u inner width at S. The server decides every action; the prompt is guidance.

### 13.4 Exit status
Source: `WorldState` exits `{Id, Flags (Powered 1, Open 2, Locked 4), SwitchProgress}`; FirewallBlock `S2C_SkillEffect.Duration` gives the lock seconds. The name is `Exit N`, with N = `Id + 1`.

**Precedence:** Open > Locked > Switching (progress > 0) > Powered > No power.

**Survivor objective row cells**, 192×46 each:
- Line 1: `Exit 1` (M 600) on the left and the state (M) on the right: `No power` / `Powered` / `62%` / `Locked` or `Locked 3 s` / `Open`.
- Line 2: a 176×6 track with a 1 u `muted` outline and a 4 u fill:
  - Switching: `text` up to the progress.
  - Locked: `danger` up to the progress (Killer-caused).
  - Open: full `ally` (Q11).
  - No power: text `muted`, no fill.

**Killer world marker** at 2.5 m, with the tag above it (S on `panel`, `muted` when No power), for example `Exit 1 · Powered`. The marker is a 16 u ring with a 1 u `bg` keyline:
- No power: dashed `muted`.
- Powered: solid `text`.
- Switching: solid ring plus a 2 u `text` progress arc at radius 11.
- Locked: ring plus a bar across it.
- Open: filled `text` disc.

Off-screen exits get no indicator. Every tag is clamped inside the 24 u screen inset.

### 13.5 Shutdown clock
Source: `WorldState.CollapseLeft` (global) and `SelfState.CollapseLeft` (personal); −1 means not running. The format is `m:ss` of `ceil`.
- **Survivor cell** (272×46, the third cell of the objective row):
  - Line 1: a 4×18 bar, then `Shutdown 1:52` (M 600 `text`) showing the global clock.
  - Line 2 (S `text`), only when the personal `m:ss` differs from the global one: `Lagging: 2× faster · you 0:48` while the own `Status = Lagging`, otherwise `You 0:48`.
- **Killer block** (320×32): the bar and `Shutdown 1:52`. There is no personal clock.
- **Urgent:** the clock that eliminates the viewer is checked. That is the personal clock when it runs, otherwise the global one. When it shows `0:30` or less, only the bar colour changes: `muted` → `danger` for Survivors, `muted` → `text` for the Killer (the Killer HUD keeps `danger` for the overload slow). The text keeps its colour, nothing flashes, and nothing animates.
- **Sound** (wiring): `collapse_start` once (exit opened or hatch closed) and one tone at 30 s.

### 13.6 Terminal flags
Source: `TerminalState.Flags` (Regressing 1, Overloaded 2).
- **Rolling back** (both roles; the terminal's `Regressing` flag) replaces the terminal's world tag:
  - Survivor: `Terminal 3 · 40% · Rolling back`, with a 2 u `danger` left bar. **(U6c-wire a2)** Survivors have no other terminal tag, so this one is drawn by the Survivor HUD's own v2 marker layer: above the terminal's 1.5 m point, only while that point is on screen (no edge indicator), clamped out of the cluster and stack keep-out.
  - Killer: `Terminal 3 · Rolling back`, with no percentage (N9) and a 2 u `text` left bar.
- **Overloaded** (Killer alert, shown while the flag is set, about 2 s):
  - On screen: a 16 u filled `signal` square at the terminal (1.5 m), drawn through walls, with a 1 u `bg` keyline.
  - Off screen: the same square at the §6.2 edge-indicator position (`EdgePlacement.Place`, Killer slide rule, cluster keep-out including the v2 blocks), with an 8 u `signal` pointer toward the terminal.
  - Tag below it: `Terminal 3 overloaded · 20 m`.
  - It is a square so that it is never read as a Survivor: Survivors are diamonds and chevrons.
  - Survivors do not get this marker; their own Fail shows on the ring.

### 13.7 Result screen v2
Source: `S2C_GameResult {WinnerRole, Reason, EscapedIds, EliminatedIds}`, lobby names, own id and role. The layout replaces §7.1–7.4; §7.5–7.6 are unchanged and go into `ResultV2Panel.Actions`.

| # | Element | Content | Style |
|---|---|---|---|
| 1 | Headline | `Survivors win` / `Killer wins` (from `WinnerRole`) | L 600 [8] |
| 2 | Own outcome | `You escaped.` / `You were eliminated.` / Killer: `You eliminated 4 of 7 Survivors.` | M [8] |
| 3 | Rule | `3 of 7 escaped · Survivors win when half or more escape (4 of 7)` | S `muted` [4] |
| 4 | Reason | AllResolved `Every Survivor escaped or was eliminated.` · AllSurvivorsDown `Every Survivor was eliminated before anyone escaped.` · TimeUp `Time ran out. Survivors still inside were eliminated.` · Collapse `The shutdown eliminated the Survivors still inside.` | S `muted` [24] |
| 5 | Rows | One row per starting Survivor, in slot order, 560×32 [4]: name (M, ellipsized at 24 characters, ` (you)` on the own row) on the left; `Escaped` (M `text`) or `Eliminated` (M `muted`) on the right. The own row has a 4 u `text` bar inside its left edge and a 600-weight name. | [32] |

At 720p this is 48 + 34 + 8 + 23 + 8 + 18 + 4 + 18 + 24 + 7 × 36 + 32 + 48 = 523 u ≤ 900. There is no colour per outcome (no ally/danger), because both roles see the same list. The winner is the server's; `ResultV2Copy.Consistent` checks it against `escaped × 2 ≥ starting` in development only.

---

## 14. Skill feedback (U7b)

Playtest finding (11/04): after a skill press the player cannot tell what happened. This section gives each of the nine skills (§8) a world effect, a result line and a sound per viewer, built only from packets the viewer already receives. It amends §4.8 (reason lines, `skill_use`), §6.1c (copy), §6.5 (`skill_use`) and §12.5 (Reduced motion); those sections point here. All §0 rules apply.

### 14.0 Source: who receives which effect
One packet, `S2C_SkillEffect {CasterId, SkillId, X, Z, Duration}`, sent as in `Server.Core/Runtime/GameServer.Skills.cs` (unchanged). Skill ids are role-local (Killer 0 and Survivor 0 are different skills), so the receiver resolves the skill first.

| Skill | Sent to | `CasterId` | `X, Z` | `Duration` |
|---|---|---|---|---|
| Ping scanner (K0) | the Killer | Killer | the Killer's position | 1.5 |
| Ping scanner (K0) | the scanned Survivor only (Normal or Lagging) | Killer | **0, 0** | 1.5 |
| Overclock (K1) | the Killer only | Killer | the Killer's position | 3 |
| Packet afterimage (K2) | nobody (passive) | — | — | — |
| Firewall block (K3) | every player | Killer | the locked exit's position (a static map point) | 5 |
| Dummy packet (S0), Stealth relay (S1), White noise (S4) | every Survivor; never the Killer | caster | the caster's position | 3 / 2 / 1.5 |
| Traffic overload (S2) | every Survivor | caster | the caster's position | 2 |
| Traffic overload (S2) | the Killer | **0** | **0, 0** | 2 |
| Emergency restore (S3) | every Survivor | caster | the rescued Survivor's position | 0 |

**Resolution rules (normative):**
- **Killer client.** It acts on an effect only when `CasterId` = own id (own skill), or when `CasterId = 0` and `SkillId = TrafficOverload`. It ignores everything else. A Survivor skill never reaches the Killer; if one did, this rule would drop it, so no Killer-side output can depend on a decoy skill.
- **Survivor client.** `CasterId` = own id → own skill. `CasterId` = a Survivor of the last WorldState → that Survivor's skill. Otherwise → the Killer's skill (only K0 and K3 arrive). The client never looks up the Killer's `CasterId`, never matches it to the Killer entry (§0.1) and never places anything from it.
- **No Killer coordinates on a Survivor client.**
  - The Survivor-bound Ping scanner carries `0, 0`. Nothing is ever placed from it.
  - The Survivor-bound Firewall block carries a static exit position. It is used only to pick the exit (the static exit within 1 m of `X, Z`). Everything is drawn at that exit's static switch point (`FixedMap.ExitSwitches[i]`). If no exit is within 1 m, nothing is placed and the line says `An Exit switch` instead of `Exit 2 switch`.
  - Nothing is ever attached to the drawn Killer model.

### 14.1 Global rules

**Viewers.** For each effect, the client is exactly one of these:
- **caster**: its own skill;
- **affected**: the skill changed this player's own state (the rule is given per skill);
- **ally**: any other Survivor client that received it;
- **Killer**.

Spectators (own `Status` Eliminated or Escaped) see world effects and hear positional sound only: no line and no 2D sound.

**Result line, Survivor (new element 14.1a).**
- **Block:** 672×40 u, `panel` fill, 1 u `line` border, padding 8. It sits in the §13.1 bottom-center stack, **directly above the prompt row**. When the prompt row is hidden, it takes the prompt row's place, and the stack closes up as before.
- **Content:** a 4×18 u bar in the line's colour, an 8 u gap, then the text in M 400 `text`. One line, at most 64 characters (≈ 634 u ≤ 640 u). Names are `Slot.Nickname`, ellipsized at 12 characters (§0.3).
- **Bar colour by meaning:**
  - `danger`: the Killer acted against you;
  - `signal`: you are made visible;
  - `ally`: a Survivor-team outcome;
  - no bar and `muted` text: a reason line (14.3).
- **Time:** shown for **2.5 s** (reason lines 2.0 s), then removed in one frame. Numbers in the line are the skill's static duration, not a countdown. The live countdowns stay where they are: the meter (§4.1), the active arc (§4.8) and the objective row (§13.4).
- **One line at a time, never stacked (SLOP §5).** Priority: affected (`danger` first) > caster > ally > reason. A new line replaces the shown one when its priority is equal or higher. A lower one is dropped, not queued.
- **Stack geometry:** with every child shown, the stack top is 96 + 48 + 8 + 40 + 8 + 40 + 8 + 80 + 8 = 336 u. While hacking (bar shown, objective row normally hidden), a line lifts the bar by 48 u. The bar top is then 280 u from the bottom, at 68.9 % of the 720p height, still below the feet (67 %, §4.10) **(11/17, IP)**. This happens only while a line shows during routing (≤ 2.5 s) **(11/17, R2)**.
- Hidden while the play menu is open (§5); its timer keeps running.

**Result line, Killer.** The §6.1c effect line (320×32, S, with the 4×18 u bar slot) carries the Killer's lines.
- Own-skill lines tick (`· 1.2 s`, `Seconds1`) for exactly the effect's duration. Reason lines show for 2.0 s.
- One line at a time: Slowed (`danger` bar) > own skill > reason (`muted`, no bar).
- At most 34 characters (282 u, §6.1).

**World effects (VFX).**
- **Two kinds only:**
  - a **Hovl one-shot**: a non-`Loop` prefab from `Client/Assets/ThirdParty/Hovl Studio/Map track markers VFX/Prefabs/`;
  - a **procedural ring**: a flat unlit ring 0.02 m above the ground, line width 0.08 m, one palette colour, depth-tested, no texture, no glow.
- **Every effect:**
  - encodes a live event;
  - is depth-tested (never drawn through walls);
  - lasts ≤ 2 s, or exactly as long as the effect;
  - is cleared (`StopEmittingAndClear`) at its stated end at the latest.
- **Tint:** the particle `startColor` is set to the palette colour, keeping the prefab's alpha, as the Exit marker does (`WorldProps.SetExitLook`). Meanings:
  - `signal`: a Signal (real or fake) is transmitted here;
  - `ally`: a Survivor-team effect;
  - `danger`: Killer-caused, against Survivors (Survivor view only);
  - `text`: neutral; used for every Killer-view effect.
- **No bloom:**
  - The marker material is one runtime copy of the vendor material, with `_Emission` 0.8. The vendor value of 18 burns tints to white, and the Exit marker's 1.4 is above the URP bloom threshold of 0.9 (`DefaultVolumeProfile`).
  - The `Flash` and `EndSparks` children (pure glow puffs) are disabled in every pooled instance.
  - If a 1080p capture still shows bloom on an effect, that effect falls back to its procedural ring.
- **Size:** the stated outer diameter in metres. The prefab uses `scalingMode = Hierarchy`, and the root is scaled from the prefab's own size.
- **Reduced motion** (the §12.5 setting or `-ptk-reduce-stutter`):
  - every Hovl one-shot is replaced by its procedural ring at the final diameter, still, with the same colour and duration;
  - rings that would grow appear at full size;
  - the Overclock FOV change and the Ping scanner sweep are off;
  - lines and sounds are unchanged.
- **Pooling:**
  - Everything is created at warm-up (the first match start): 3 instances per Hovl prefab used, 6 procedural rings, 2 2D audio sources and 4 positional ones.
  - After that, no effect calls `Instantiate` or `new GameObject`. When a pool is empty, its oldest instance is reused.
  - Everything is cleared on MatchStart, GameResult and disconnect.

**Sound.**
- One cue per event per viewer. A skill's cue **replaces** the generic `skill_use` (§4.8, §6.5) for that skill. `skill_ready` is unchanged.
- **Clips** come from `Client/Assets/ThirdParty/Sci-Fi Weapons-Bullet Hell Sound Effects Pack/AUDIO/`; the vendor files stay untouched.
- **Gain:** `AudioSource.volume = (0.4 / clip peak) × 10^(dB/20)`. Each clip's peak therefore sits at the 0 dB cue reference (`capture_confirm`, amplitude 0.4) before the stated dB is applied. The peaks are in the 14.4 table.
- **Maximum length:** a clip is stopped with a 50 ms fade at the stated cut, which is never longer than the effect, so no tail outlives the effect. No loops.
- **2D** means `spatialBlend 0`.
- **Positional** means:
  - `spatialBlend 1`, spread 0, no Doppler;
  - the stated level up to 2 m, then linear in dB down to −40 dB at the stated range, and muted beyond it (the §6.5.1 curve).
- **Priority (§0.7):**
  - Affected cues of Killer skills sit at the "exit opened/locked" tier (Firewall) or just below "capture" (scan).
  - Caster cues sit at the skill tier.
  - Ally positional cues sit at the world-sound tier and duck like §6.5.1 world sound.
  - A new skill-tier cue cuts the previous skill-tier cue (30 ms fade). It never cuts a higher tier.
- Reason lines and refused presses make no sound, because no state changed.

### 14.2 Per-skill feedback
**(11/17, U9)** The World column's Hovl one-shots and procedural rings are replaced by the per-skill signatures of §14.6. Viewers, anchors, tints and lines below are unchanged unless §14.6 says otherwise.

Copy in `code` is final. `N` = a nickname (ellipsized at 12 characters); `E` = the exit number (`Id + 1`).

#### 14.2.1 Ping scanner (Killer 0)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Killer (caster) | A procedural ring, `text`, centred on the own rendered position. Its radius sweeps from 1 to 16 m over 0.6 s, then it is removed. Reduced motion: none. **No blip is marked:** the client cannot know which instance is the scanned Survivor (§6.3). | `Nearest Survivor shown · 2.7 s` (ticking, 3 s; 11/17, R1: was 1.5 s) | `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_2.wav`, −9 dB, 2D, full length (0.40 s) |
| Affected (the scanned Survivor) | Hovl `Marker 5 Circle`, `signal`, at the own character's feet, 2 m, 0.6 s. The position is the own entry's `X, Z`, never the effect's. It is a one-shot for the event; the state itself stays on the meter only (§4.10). | `Scanned: the Killer sees you · 1.5 s` (`danger` bar) | `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_1.wav`, −6 dB, 2D, full length (0.56 s) |
| Ally Survivors | — (not sent) | — | — |

**Safety:**
- The Killer uses `CasterId` (= own), `SkillId` and `Duration`. The ring's centre is its own position.
- The Survivor uses `CasterId` (not a Survivor → the Killer's skill), `SkillId`, `Duration` and its own entry's `X, Z`. It never reads the effect's `X = Z = 0`.
- While the target is Lagging (always visible), the line still shows: it is true, and it says that the scan picked them.

#### 14.2.2 Overclock (Killer 1)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Killer (caster) | No world object (first person has no body). The camera FOV widens by 4°, eased in over 150 ms, held for `Duration`, and eased out over 150 ms when Recovering starts. Reduced motion: none. | `Overclock: 1.5× speed · 2.1 s`, then `Recovering: slowed · 0.2 s` (static 0.3 s after `Duration`, 60 % speed; 11/17, R1: was `cannot move`, 1 s) | Start: `Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun1_Set_to_Charge_Part_2.wav`, −9 dB, 2D, full length (0.68 s). Recovering start: `Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Reload_Magazine_Out_1.wav`, −12 dB, 2D, full length (0.23 s). |
| Survivors | — (not sent; a Survivor with line of sight only sees the model move faster, §4.10) | — | — |

**Safety:** Killer only. It uses its own `CasterId`, `Duration` and the static `OverclockRecoverySeconds`.

#### 14.2.3 Packet trace (Killer 2) **(11/17, R1: replaces the passive Packet afterimage)**
The server forces every active Survivor within 12 m of the Killer Signal for 2.5 s (the Ping scanner exposure). It never fails, so a press tells nothing about a Silent Survivor.

| Viewer | World | Line | Sound |
|---|---|---|---|
| Killer (caster) | §14.6 trace ring, `text`, on the own rendered position: grows 2 → 24 m diameter in 0.35 s, gone at 0.6 s. No glyph (own first-person point). Reduced motion: still, full size. **No blip is marked** (§6.3). | `Survivors within 12 m shown · 1.6 s` (ticking, 2.5 s) | the Ping scanner start clip, −9 dB, 2D |
| Affected (each traced Survivor; `X = Z = 0` on the wire) | the trace ring, `signal`, closing 4 → 2 m on its own feet (its own entry's `X, Z`) | `Traced: the Killer sees you · 2.5 s` (`danger` bar) | the scanned clip, −6 dB, 2D |
| Other Survivors | — (not sent) | — | — |

**Safety:** the Survivor-bound effect never carries the Killer's position; the Killer's ring uses its own position only.

#### 14.2.4 Firewall block (Killer 3)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Killer (caster) | Hovl `Marker 3 Zone`, `text`, at the locked exit's switch point, 3 m (the switch's 1.5 m hold radius), 1.0 s. The §13.4 marker shows `Locked` for the full 5 s. | `Exit E switch locked · 4.2 s` (ticking, 5 s) | `Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Set_Part_1.wav`, −9 dB, 2D, full length (0.50 s) |
| Affected Survivor: when the effect arrives, its own `SelfState.ActionKind = 1`, or its own position is within `ExitSwitchRadius` of that switch | Hovl `Marker 7 Danger zone`, `danger`, at the switch point, 3 m, 1.0 s | `Exit E switch locked by the Killer · 5 s` (`danger` bar) | `Mechanics/SFX_GUNS_MECH_SCIFI_Pistol_or_Rifle_Set_Part_2.wav`, −6 dB, 2D, full length (0.56 s) |
| Ally Survivors | The same `danger` one-shot, seen only if in view | — (the objective row already reads `Locked 5 s`, §13.4) | The same clip, positional at the switch point, −12 dB at 2 m, range 30 m |

**Safety:**
- Every client uses `SkillId`, `Duration`, and `X, Z` only to pick the static exit (14.0).
- The Survivor also uses its own `SelfState.ActionKind` and its own entry's position.
- The Killer's position is never in this packet.

**(11/17, R1) Terminal lock.** With no Exit switch in progress, Firewall block locks the unfinished terminal with the most progress for 8 s (`TerminalFlags.Locked = 4`; hackers on it stop, nobody can hack it). The effect's `X, Z` is that terminal's static map point (`SkillFeedback.TerminalAt`, Terminal N = id + 1). It fails (`NoSwitchInProgress`, line `Not used · nothing to lock`) only when no terminal has progress. **(a2)** A terminal cannot be locked again until 20 s after its lock ended; another terminal can (a press with only such terminals fails the same way).

| Viewer | World | Line | Sound |
|---|---|---|---|
| Killer (caster) | Hovl `Marker 3 Zone`, `text`, at the terminal, 3 m, 1.0 s. Its §6.2 terminal marker tag reads `Terminal N · Locked` (`text`) while the flag is set. | `Terminal N locked · 7.4 s` (ticking, 8 s) | as above |
| Affected Survivor (hacking within 2 m of it) | Hovl `Marker 7 Danger zone`, `danger`, at the terminal, 3 m, 1.0 s | `Terminal N · Locked · 8 s` (`danger` bar) | the Survivor clip, −6 dB, 2D |
| Other Survivors | the same one-shot if in view | `Terminal N · Locked · 8 s` (`danger`, ally priority) | positional at the terminal, −12 dB, 30 m |

#### 14.2.5 Dummy packet (Survivor 0)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | Hovl `Marker 2 Pointer`, `signal`, at the effect's `X, Z`, 1.5 m, 0.8 s. It marks where the fake Signal started. The fake then moves like a Survivor and is not on the Survivor wire, so nothing follows it. | `Dummy packet: fake Signal sent from here · 3 s` (`ally` bar) | `Shoot/Energy/SFX_SCIFI_WEAPON_Energy_Shoot_Big_Gun3_1.wav`, −9 dB, 2D, cut at 0.6 s |
| Ally Survivors | The same one-shot, if in view | — | The same clip, positional at `X, Z`, −15 dB at 2 m, range 20 m |
| Killer | **Nothing beyond §6.2–§6.5.1.** No packet arrives. The fake is an anonymous instance, drawn, voiced and cued exactly like a real Signal Survivor, with no spawn effect (§6.3 rule 2). | — | — |

**Safety:**
- Survivors use `CasterId`, `SkillId`, `X, Z` (the caster's position, which Survivors already have) and `Duration`.
- **Decoy safety:** the Killer client has no code path for this skill (the 14.0 Killer rule). Its output for the fake equals its output for a real Signal Survivor at the same position and motion.

#### 14.2.6 Stealth relay (Survivor 1)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | A procedural ring, `ally`, centred on `X, Z`. Its radius is exactly `StealthRelayRadius` (5 m, the rule's area). It grows from 0 to 5 m over 0.15 s, is held until 2.0 s (`Duration`), then removed. | `Stealth relay: you and 2 Survivors are Silent · 2 s`; with one: `you and 1 Survivor`; with none: `Stealth relay: you are Silent · 2 s` (`ally` bar) | `Skill/SFX_SCIFI_ENERGY_Skill_Whoosh_Electrified_1.wav`, −12 dB, 2D, cut at 2.0 s (300 ms fade) |
| Affected: another Survivor whose own entry has `Stealthed` set, within 5 m of `X, Z`, in the first WorldState after the effect | The same ring | `You are Silent · 2 s · Stealth relay from N` (`ally` bar) | The same clip, −15 dB, 2D, cut at 1.0 s |
| Ally Survivors (not affected) | The same ring, if in view | — | The same clip, positional at `X, Z`, −18 dB at 2 m, range 20 m, cut at 1.0 s |
| Killer | Nothing (not sent). Covered Survivors simply stop being sent (§6.2). | — | — |

**The caster's count:**
- It counts other Survivors with `Status = Normal` and `Stealthed` set, within 5.0 m of `X, Z`, in the first WorldState at or after the effect.
- The line appears with that snapshot (at most 1 tick later).
- A Survivor already covered by an earlier relay is counted too (derived; accepted).
- Lagging Survivors are not covered by the server and are not counted.

**Safety:** `CasterId`, `X, Z` and `Duration`; the Survivors' `Flags.Stealthed`, `Status` and `X, Z` (full state on Survivor clients).

#### 14.2.7 Traffic overload (Survivor 2)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | Hovl `Marker 6 Arrows`, `signal`, at the effect's `X, Z` (the caster's server position when the effect arrives), 2 m, 0.5 s | `Traffic overload: you are Signal, the Killer is slowed · 2 s` (`signal` bar) | `Shoot/Electric/SFX_SCIFI_WEAPON_Electric_Shoot_Medium_Gun_1.wav`, −9 dB, 2D, cut at 0.6 s |
| Ally Survivors | The same one-shot at the caster, if in view | `N slowed the Killer · 2 s` (`ally` bar) | The same clip, positional at the caster, −15 dB at 2 m, range 20 m |
| Killer (affected) | **None.** The effect has no position (`0, 0`), and the client cannot know which instance is the caster, so no instance is marked. | The existing `Slowed by traffic overload · 1.6 s` (`danger` bar) | `Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun_Out_of_Ammo.wav`, −6 dB, 2D, cut at 1.0 s |

**Safety:**
- Survivors use `CasterId` (for the ally's name and live position from their WorldState) and `Duration`.
- The Killer uses only `CasterId = 0`, `SkillId` and `Duration`.

#### 14.2.8 Emergency restore (Survivor 3)
The rescued Survivor (the "target") is derived. It is the Survivor in the WorldState before the effect who:
- is not the caster;
- has `Status = Lagging`;
- is nearest to `X, Z`, within 0.5 m.

With no match, the target is unnamed (`a Survivor`).

| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | Hovl `Marker 4 Pillar`, `ally`, at `X, Z`, 1.5 m, 1.0 s | `Rescued N · no longer Lagging` (`ally` bar) | `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_Start_2.wav`, −9 dB, 2D, cut at 1.0 s |
| Affected: own status Lagging → Normal, and own position within 0.5 m of `X, Z` | The same one-shot | `N rescued you · no longer Lagging` (`ally` bar). The meter's `Protected` tag follows from `InvulnerableLeft`. | The same clip, −6 dB, 2D, cut at 1.0 s |
| Ally Survivors | The same one-shot, if in view | `N rescued N` (`ally` bar), e.g. `Mina rescued Rio` | The same clip, positional at `X, Z`, −15 dB at 2 m, range 20 m |
| Killer | Nothing (not sent). The Lagging instance turns Normal or leaves the view by the §6.3 rules. | — | — |

`Duration = 0`, so the caster's active arc does not show (§4.8). The line and the one-shot use their own times.

**Safety:** `CasterId`, `X, Z` (a Survivor's position), the Survivors' `Status` and `X, Z`, and lobby names.

#### 14.2.9 White noise (Survivor 4)
| Viewer | World | Line | Sound |
|---|---|---|---|
| Caster | Hovl `Marker 3 Zone`, `signal`, at `X, Z`, 3 m, 1.0 s. The fakes' positions are not on the Survivor wire, so nothing is drawn where they are. | `White noise: fake Signals sent across the map · 1.5 s` (`ally` bar) | `Skill/SFX_SCIFI_SKILL_Fire_Rain_1.wav`, −12 dB, 2D, cut at 1.5 s (200 ms fade) |
| Ally Survivors | The same one-shot, if in view | `N sent fake Signals across the map · 1.5 s` (`ally` bar) | The same clip, positional at `X, Z`, −18 dB at 2 m, range 20 m, cut at 1.0 s |
| Killer | **Nothing beyond §6.2–§6.5.1**, as for Dummy packet. The fakes are five more anonymous instances, staggered by the server, with no spawn effect. The 6-nearest edge cap and the 4-voice cap apply to them equally. | — | — |

The copy gives no count: the server may place fewer than `WhiteNoiseSignalCount`, and the entry cap may hide some.

**Safety and decoy safety:** as in 14.2.5.

### 14.3 Presses with no effect
The client sends `C2S_UseSkill` on every press, except while its own `Status` is Lagging, Eliminated or Escaped. Reason lines show at once: on the Survivor line 14.1a with no bar, and on the Killer's 6.1c line in `muted`. They are replaced if an own effect arrives after all.

| Case | Known how | Server reason **(11/10, Q3)** | Survivor line | Killer line |
|---|---|---|---|---|
| Cooldown | `SelfState.SkillCooldown > 0` | `Cooldown` | None: the ring and its number are the feedback (§4.8) | None: the block reads `Cooldown 7 s` |
| Cooldown edge **(11/10, Q3)** | derived Ready, server cooldown not yet over | `Cooldown` | `Skill still on cooldown.` | `Not used · still on cooldown` |
| Lagging | own `Status` | `NotNormal` | `Skills are off while Lagging.` (not repeated while the prompt row shows it) | — |
| Eliminated, Escaped | own `Status` | `NotNormal` | None: the status banner is the feedback | — |
| Emergency restore, no target | derived, §4.8 | `NoTarget` | `Emergency restore needs a Lagging Survivor next to you.` | — |
| Ping scanner, no active Survivor **(11/10, Q3)** | server only | `NoTarget` | — | `Not used · no Survivor to scan` |
| Firewall block, no switch and no terminal in progress **(11/17, R1)** | server only (the Killer cannot see terminal progress) | `NoSwitchInProgress` | — | `Not used · nothing to lock` |
| ~~Packet afterimage~~ **(11/17, R1: no passive skill remains; `Passive` reads as the generic refusal)** | — | `Passive` | — | `Not used · server did not accept` |
| **Killer swing missed (11/17, R1)** | `KillerSwingWatch`: the own `Swinging` flag rose and no capture confirm came within 0.55 s (a2: or a held verdict fell at miss length) | — | — | `Missed` (a2: `text`, 1 s) |
| Not in a match, not the own skill, dev sandbox **(11/10, Q3)** | server only | `NotInMatch`, `NotYourSkill`, `Sandbox` | `Skill not used — the server did not accept it.` | `Not used · server did not accept` |
| No answer (fallback) | Derived Ready, but within 0.5 s of the press neither an own effect, nor an `S2C_SkillRejected`, arrives, nor `SkillCooldown` becomes > 0 (§4.8) | — | `Skill not used — the server did not accept it.` | `Not used · server did not accept` |

**Server reasons (11/10, Q3; replaces the former "Protocol gap").**
- The server answers every refused `C2S_UseSkill` with one `S2C_SkillRejected { SkillId, Reason }` (protocol v0.6, 0x0016), to the caster only, at most one per player per tick. Bots get none.
- Only the press that armed the 0.5 s watch (sent while the client derived Ready, with no local reason) is answered on screen. Its reason line shows at once, with the styling above, and the watch is disarmed. A rejection that arrives after an own effect, after the fallback fired, or for a press the client already explained itself (cooldown shown, Lagging, passive, no target, no switch) is ignored, so a local pre-check and a server reason never show two texts for one case.
- The 0.5 s watch stays only as a fallback for a reply slower than 0.5 s.
- Every reason is derived from state the caster's own stream already carries. For the Killer, `NoSwitchInProgress` matches §6.1d: every `WorldState` carries each exit's `SwitchProgress`, and a successful Firewall block is a visible effect. `NoTarget` for Ping scanner happens only when no Survivor is active, never because of where a Silent Survivor stands.
- Still unknown to the client (out of scope):
  - in advance, which exit Firewall block will lock;
  - how many White noise fakes were placed or shown;
  - whether the Dummy packet fake is currently shown to the Killer.

### 14.4 Sound table
Peaks are measured from the vendor files (int16 peak ÷ 32768). Lengths are the files' own.

| Clip | Length | Peak | Used for |
|---|---|---|---|
| `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_2.wav` | 0.40 s | 0.82 | Ping scanner, Killer |
| `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_End_1.wav` | 0.56 s | 0.75 | Ping scanner, scanned Survivor |
| `Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun1_Set_to_Charge_Part_2.wav` | 0.68 s | 0.78 | Overclock start |
| `Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Reload_Magazine_Out_1.wav` | 0.23 s | 0.63 | Overclock, Recovering start |
| `Mechanics/SFX_GUNS_MECH_SCIFI_Analogue_Set_Part_1.wav` | 0.50 s | 0.69 | Firewall block, Killer |
| `Mechanics/SFX_GUNS_MECH_SCIFI_Pistol_or_Rifle_Set_Part_2.wav` | 0.56 s | 0.85 | Firewall block, Survivors |
| `Shoot/Energy/SFX_SCIFI_WEAPON_Energy_Shoot_Big_Gun3_1.wav` | 1.48 s | 0.84 | Dummy packet |
| `Skill/SFX_SCIFI_ENERGY_Skill_Whoosh_Electrified_1.wav` | 2.60 s | 0.85 | Stealth relay |
| `Shoot/Electric/SFX_SCIFI_WEAPON_Electric_Shoot_Medium_Gun_1.wav` | 1.50 s | 0.84 | Traffic overload, Survivors |
| `Mechanics/SFX_GUNS_MECH_SCIFI_HiTech_Gun_Out_of_Ammo.wav` | 1.01 s | 0.78 | Traffic overload, Killer slowed |
| `Z_Bonus/SFX_SCIFI_ENERGY_Anime_Weapon_Charge_Start_2.wav` | 1.13 s | 0.69 | Emergency restore |
| `Skill/SFX_SCIFI_SKILL_Fire_Rain_1.wav` | 1.71 s | 0.85 | White noise |

The clips were chosen by name, length and envelope (most of the energy early). None has been auditioned, so the levels are 🔶 for playtest tuning. Each clip belongs to one skill, so every skill sounds distinct, and the caster and the target of a Killer skill hear different clips.

### 14.5 Build notes
- **Rules type:** one pure type (no `UnityEngine`) maps (own role, own id, resolved skill, viewer context) to a feedback record. The record holds: line text and colour; VFX kind, prefab, tint, diameter and time; sound clip, dB, 2D or positional, and cut.
- **Player:** a `SkillFx` component under `World/` only plays records from its pools.
- **Tests** drive the rules type: per skill and viewer, decoy safety, the `0, 0` rule and durations. They also count pool instances after warm-up.
- The Killer world layer keeps its §6.3 path unchanged; `SkillFx` adds nothing to blips.
- **§12.5 Reduced motion** now also covers skill effects (14.1). The note under the toggle (S `muted`, at most 2 lines in 576 u) becomes: `Lagging shows every frame instead of holding the picture at 8 Hz. Skill effects show as still rings.`

### 14.6 Skill signatures and glyphs (U9, 11/17)
Playtest (11/17): "even when a skill is used, it is hard to tell which skill it was". Several §14.2 effects were similar Hovl marker rings in similar colours.

**Rule.** Every skill has its own **shape** and its own **motion**; any two skills therefore differ in at least two of shape, motion and colour. Colour is **not** used to tell skills apart: it keeps its §0.4 meaning (SLOP V7, palette max 4 + neutrals; the brief's cyan / blue / yellow / green / grey suggestions were not adopted). All signatures are generated geometry: unlit wire lines (0.045–0.07 m wide, two crossed quads per segment, double-sided), one palette colour, depth-tested, no texture, no glow, no alpha blend.

| Skill | Shape | Motion | Colour (viewer) | Time |
|---|---|---|---|---|
| Ping scanner (K0) | three vertical bands, 0.5–0.85 m above the ground (a band reads in first person; a flat ground ring did not) | **pulse**: Killer — staggered 0.15 s, each sweeps 2 → 16 m in 0.7 s around the own point; scanned Survivor — each closes 3 → 0.35 m onto its own feet | `text` (Killer), `signal` (scanned) | 1.0 s |
| Overclock (K1) | ten short speed streaks around the first-person camera | **rush past**: 14 m/s toward and past the camera, following the own camera | `text` (Killer only) | the effect (3 s) |
| Packet trace (K2) (11/17, R1) | one flat ground ring (wire, the trace radius) | **flash**: grows 2 → 24 m in 0.35 s (ease-out) and holds; on a traced Survivor it closes 4 → 2 m on the own feet | `text` (Killer), `signal` (traced) | 0.6 s |
| Firewall block (K3) | a hex-grid barrier wall (cylinder r = 1.5 m, 2 m high, the switch hold radius) + ground ring, at the static switch point | **rise + one blink**: rises in 0.2 s, one 0.1 s blink at 0.72 s | `text` (Killer), `danger` (Survivors) | 1.0 s |
| Dummy packet (S0) | a dashed wire double of a character (capsule r 0.5 × 1.8 m, 0.035 m lines; hidden while the camera is within 1 m of it, the §4.10 short-boom rule) | **pop-in**: back-ease overshoot to full size in 0.22 s, shrinks out over the last 0.12 s | `signal` | 1.2 s |
| Stealth relay (S1) | a wire dome over the 5 m relay area (12 meridians, 3 parallels, ground ring) | **envelop**: grows in 0.15 s, turns 20°/s | `ally` | the effect (2 s) |
| Traffic overload (S2) | ten jagged spikes from the caster's chest, 1.0 m | **burst**: out in 0.1 s, jitters between two jagged frames at 12 Hz | `signal` | 0.8 s |
| Emergency restore (S3) | a beam of six columns (r 0.35 m, 3.2 m high) on the rescued point | **ascend**: columns rise in 0.15 s, three rings climb the beam | `ally` | 1.0 s |
| White noise (S4) | thirty filled static squares (0.12–0.3 m) in a 1.5 m-radius volume, 0.2–1.7 m high | **flicker**: each square blinks on a random 14 Hz pattern, about half on | **`text`** (was `signal`: the fake Signals are elsewhere on the map; nothing transmits at the caster) | 1.0 s |

**Glyph.** The skill's §8 icon floats above its world effect: a 0.6 m camera-facing quad, 1.9 m above the effect point, rising 0.15 m over 1.0 s (ease-out), lowered when needed so its top stays 24 u inside the top screen edge, in the effect's colour, alpha-clipped (no blend), depth-tested. It is rasterized at warm-up from the same primitives the skill block draws (`SkillGlyph`), so the HUD and the world always match. No glyph above the Killer's own first-person point (Ping scanner, Overclock): the 14.1b flash covers those.

**14.1b Own-cast flash (new element).** When the own effect arrives (the caster's record with its start cue), the skill's glyph (32 u, `text`, 1 u lines) shows for **0.6 s** in a 48 × 48 u `panel` block (1 u `line` border, padding 8; no name: the skill block it touches already names the skill), bottom-aligned with the skill block: **Survivor** — left of the bottom cluster, 8 u outside its border (outside the §13.1 stack; feet at 67 % stay clear, §4.10); **Killer** — right of the bottom-left cluster, 8 u gap. It rises 4 u into place in 0.1 s; with Reduced motion it is still. Removed in one frame. Not shown for the Overclock Recovering cue, the Stealth relay count line, ally or affected records.

**Safety (unchanged rules, restated).** A signature and its glyph are drawn only for a record that already has a §14.2 world effect for this viewer, and only at `SkillFeedback.VisualPoint` (the effect point, the own point, or the static switch point) — or, for Overclock, at the Killer's own camera. The Killer client never receives Survivor skills (14.0), so it never draws their signatures or glyphs; the Killer-bound Traffic overload draws nothing.

**Reduced motion.** Each signature shows still at its final pose for its time (Ping scanner for the scanned Survivor: three still rings at 0.45 / 0.75 / 1.05 m); the Killer's Ping sweep and the Overclock streaks are off; the glyph does not rise.

**Pooling.** At warm-up: 2 rigs per shape (1 for the streaks), 4 glyph quads, 9 glyph textures (64 px), all meshes and materials; every rig and glyph is drawn once, tiny, in front of the camera for 2 frames so its pipeline state exists before the first cast. Nothing is created per effect.

## 15. Shutters and rails (11/17, Q5)

Sources: protocol v0.7 (Q5a S0):
- `WorldChaseProps.Shutters` (10) and `.Rails` (8): static boxes;
- `S2C_WorldState.Shutters`: one `ShutterState` byte per shutter, the same for every recipient;
- `PlayerState.Chase`: `Pulling 1, Vaulting 2, Breaking 4, Stunned 8`;
- `S2C_SelfState.ActionKind`: `Pull 4, Vault 5, Break 6`.

The server decides every outcome. The client only shows the state it received and never predicts a pull, vault, stun or break. Code: `World/ChaseProps*.cs`, `World/ChaseActionPose.cs`, `Audio/ChasePropAudio.cs`, `Audio/ChasePropCues.cs`, `UI/ChasePropCopy.cs`, `Editor/Setup/ChasePropSetup.cs`.

**New fixed terms** (added to the SLOP §8 vocabulary): Shutter, Rail, pull (verb), vault (verb), force (verb, Killer), Stunned.

### 15.1 Visuals
- **Where:** each prop stands at its Shared box centre, turned onto the box's long axis. The client places nothing on its own. Both roles draw the props as world geometry; the Killer sees them like any other prop.
- **Art:**
  - `ChasePropSetup` builds one prefab per box into `Art/ChaseProps/` and the catalog `Resources/ChaseProps.asset`. It is a sibling of the WorldArt setup and never touches `WorldArt`.
  - Each piece records its box. A catalog built for other boxes is never drawn; the same shapes are then built from primitives (`ChasePropShapes` is the one definition for both paths).
  - Toon materials in the WorldArt palette (`Shutter` 92/96/101, `Steel` 66/70/77, night graded). No emission, no glow (SLOP V3), no colliders.
- **Shutter**, spanning the alley gap:
  - frame (always): a guide post against each wall and the roll housing overhead (bottom at 2.6 m), thin and out of the way;
  - `Up`: only the light bottom lip of the rolled panel shows under the housing;
  - `Down`: a slatted steel panel 1.2 m high with light top and bottom bars, the same from both sides. On an Up → Down change after the first snapshot, the panel drops out of the housing in 0.12 s (at once under Reduced motion);
  - `Broken` (a2): wrecked, in the light `Edge` grey: one bent half leaning on the left post (about 1.1 m high, below the Killer eye), a second half on the ground across the gap, a scrap on the right post and a torn strip hanging askew under the housing. It reads as wrecked at 6 m, not as a rolled-up Up.
- **Rail:** 3 m long and 1.0 m high: two 0.12 m posts and a top and a mid tube in the light `Edge` grey (a2: thicker and lighter, so a rail reads as vaultable beside the non-vaultable fences).
  - The vault point is marked by a 0.6 m light band on the top tube and a flat light plate (0.8 × 0.35 m) on each side.
  - The marker colour is `#D6D4CC`: a neutral readability cue like the hatch rim, never a role colour.
- **Sight:** the panel (1.2 m) and the rail (1.0 m) are below the Killer eye (1.7 m) and the Survivor camera, like the existing low obstacles. The housing is overhead. Tags and markers are screen overlays, so the props do not hide them.
- **Late join or reconnect:** the first `WorldState` sets every shutter look at once, with no drop and no sound.

### 15.2 Prompts
Same rows and rules as 13.3. The ranges are the server's (`GameConstants`): measured to the shutter box, and to the rail's vault point (its centre). The nearest target in its own range wins.

| Role | Condition | Line 1 | Line 2 (Killer, S `muted`) |
|---|---|---|---|
| Survivor | `Up` shutter within 1.5 m | `Press [E] to pull the Shutter down — you will be Signal` | — |
| Survivor | `Down` shutter within 1.0 m | `Press [E] to vault the Shutter — you will be Signal` | — |
| Survivor | rail vault point within 1.0 m | `Press [E] to vault the Rail — you will be Signal` | — |
| Killer | `Down` shutter within 1.5 m | `Hold [E] to force the Shutter` | `Stand still · 2 s breaks it` |
| Killer | rail vault point within 1.0 m | `Press [E] to vault the Rail` | `Takes 1.6 s · you cannot move` |

- **Order:**
  - A Shutter or Rail prompt only fills an **empty** prompt slot, or one that shows a muted reason line. Every existing prompt (rescue, hack, hatch, switch, kick, banner) keeps the slot.
  - The props stand away from terminals, exits and spawns, so the two rarely meet.
  - The Survivor prompt needs `Status = Normal`, a captured cursor and no banner.
  - The Killer prompt is hidden while stunned and while the play menu is open.
- **During an action** (`SelfState.ActionKind`):
  - Survivor `Pull`: the row reads `Pulling the Shutter — you are Signal`. Survivor `Vault`: `Vaulting — you are Signal`. There is no bar, because these actions last 0.3–1.0 s.
  - Killer `Break`: the 13.3 hold block reads `Forcing the Shutter · 55%` and `Hold [E] · moving cancels`, with the bar.
  - Killer `Vault`: `Vaulting the Rail` and `You cannot move until you land`, with the bar.
  - Between SelfState packets the progress advances locally at the server's rate, as for the 13.3 holds.
- Every Killer line is at most 38 characters.

### 15.3 Animation (Survivor client: the Survivors and the line-of-sight Killer model)
- A procedural overlay on top of the Animator pose (`ChasePoseRig`, applied in `LateUpdate`), driven only by `PlayerState.Chase`.
- A pose starts at the snapshot that first carries its flag, and is timed to the server durations.
- The parallel unit U8 owns the Motion controller, so no controller state is added.

| Flag | Pose | Length |
|---|---|---|
| Pulling | both arms reach up and pull down | 0.3 s |
| Vaulting | a hop with tucked legs and a forward lean; the server moves the body across | Survivor: 0.8 s rail, 1.0 s shutter. Killer: 1.6 s rail |
| Breaking | the right arm hammers twice a second, the upper body leaning in | while held (2.0 s to break) |
| Stunned | a stagger: leaning back, head down, arms out, swaying at 1.4 Hz (no sway under Reduced motion) | 2.0 s |

- If flags ever combine, the precedence is Stunned > Breaking > Vaulting > Pulling.
- The capsule fallback gets only the hop and a whole-body lean.
- The Killer client draws no bodies (6.0), so it shows no poses.

### 15.4 Stun feedback
- **Survivors**, while the Killer model is drawn:
  - a halo ring over its head, 1.1 m across, tinted `danger` (the Killer colour), turned to face the camera (a flat ring is edge-on from the Survivor camera and was invisible in a1);
  - unless Reduced motion is on, the Hovl `Marker 5 Circle` particles are added inside it, with the low-emission marker material and the glow children off;
  - under Reduced motion, the still ring alone;
  - it lasts while `Stunned` is set, at most 2.0 + 0.2 s.
- **The Killer player:**
  - a 0.25 s view impulse: the vertical FOV narrows by up to 5° and eases back;
  - the 6.1c effect line reads `Stunned` while the flag is set, at most 2.2 s. The line has no bar;
  - Reduced motion removes the impulse; the line stays.

### 15.5 Sounds
The cues are procedural (`ChasePropSynth`: fixed seeds, the V2CueSynth reference level) and positional (3D, linear roll-off from 2 m). None plays on the first snapshot.

| Cue | When | Where | Range |
|---|---|---|---|
| `ShutterSlam`, 0.7 s: a sheet-metal knock, a ringing plate and a slat rattle | a shutter goes Up → Down | the shutter centre, for every recipient | 30 m |
| `ShutterCrunch`, 0.45 s: bending-metal bursts over a falling groan | a shutter becomes Broken | the shutter centre, for every recipient | 30 m |
| `VaultThud`, 0.16 s: a soft low step | an entry the viewer receives starts Vaulting | that entry | 14 m |

- Vault edges are matched by position (within 1.5 m), not by Id.
- The Killer's stream carries `Chase = 0` on Survivor entries. So the Killer gets no Survivor vault thud; it hears the shutter slam and crunch like everyone else.

### 15.6 Safety
- Nothing here reveals a Silent Survivor:
  - Shutter states are public.
  - A Survivor who pulls or vaults is forced Signal by the server during the action and for 1 s after, so its entry is already in the Killer's stream.
  - The Killer client still shows no pose for it: those entries carry `Chase = 0`, and the Killer client draws no bodies.
- The slam and crunch are heard by everyone in range. That is the design: a pull is a loud action.
- The stun burst is drawn only over the Killer model, which a Survivor sees only with a clear line (4.10). There is no through-wall stun cue.
- The client sends nothing new: E is the existing `C2S_Interact`.
