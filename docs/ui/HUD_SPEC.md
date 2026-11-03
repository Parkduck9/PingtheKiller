# HUD and screen spec — D3 UI pass

Status: camera revision (GDD §2, 2026-10-27) SPEC-PASS (86) with review fixes applied · N1–N9 decided 2026-10-27 (§11.1), positional sound added (§6.5.1), re-review below · earlier: attempt 3 SPEC-PASS (89), decisions recorded 2026-10-27 (§11) · author: Claude Opus (orchestrator) · reviewer: Claude Sonnet, `docs/qa/UI-spec-review.md`

> **한국어 요약:** 메뉴·로비·매치 인트로·생존자 HUD(3인칭)·살인마 HUD(1인칭)·결과·재접속 화면의 구현 명세. 모든 요소는 그 역할의 클라이언트가 실제로 받는 패킷 필드, 정적 데이터, 또는 그 둘에서 계산한 값(명시)으로만 만든다. 생존자는 살인마의 위치·방향을 절대 알 수 없고(살인마 캐릭터는 그려지지 않음), 살인마 화면은 Id를 전혀 쓰지 않는 익명 인스턴스(시야 안: 공통 캐릭터, 벽 뒤: Signal 마커)로 그려 가짜 Signal(디코이)과 진짜를 구별할 수 없게 한다. 카메라 변경으로 생긴 새 질문은 §11.1에 있다.

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
| `text` | `#E8E6E1` | text, primary button fill, neutral world markers |
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

Behaviour: re-render on each `S2C_LobbyState`, no animation. If own `IsHost` becomes 1, host controls appear and `You are now the host.` (S `text`) shows under 2.4 until the next lobby change. `S2C_MatchStart` → §3.
Focus (host): Start → Leave → (Right) `−` → `+` → (Down) Survivor → Killer → Random. Left from the right column returns to Start. Initial: Start if enabled, else `+`. Non-host: Leave only. Esc / B = Leave.
Sounds: `ui_join` (short neutral click, −18 dB) when a new human slot appears.

---

## 3. Match intro (3 s, non-modal)

Source: `S2C_MatchStart{Role, SkillId}`; skill name and effect from §8; `TerminalsRequired` from the first `S2C_WorldState`.

The server simulates from MatchStart (no pre-match freeze — decided against for now, §11 Q4), so the intro **never blocks the view or input** (SLOP L5). It shows for 3.0 s and is removed in one frame. No countdown numerals.

- **Survivor:** a block 672×160 u directly above the Survivor cluster (§4.0), same width, `panel` fill, 1 u `line` border, padding 12 (inner width 648 u). The prompt row (§4.3) is suppressed while it shows.
  - L 600 `text`: `You are a Survivor` [4]
  - M `text`: skill name and effect, e.g. `Dummy packet — a fake Signal where you stand, 3 s`; up to 2 lines (the longest, Stealth relay, is 86 characters ≈ 851 u at M → 2 lines in 648 u) [4]
  - S `muted`, 2 lines: `Moving or hacking makes you Signal. Stand still to stay Silent.` / `Hack 3 terminals, then reach the Exit.` (number = `TerminalsRequired`; the second line is omitted until it is known)
  - Worst-case height: 12 + 34 + 4 + 2 × 23 + 4 + 2 × 18 + 12 = 148 ≤ 160.
- **Killer:** block 6.1e, 320×208 u, at the top of the bottom-left cluster (§6.1); the info and skill blocks stay visible. Inner width 296 u (≈ 35 S characters per line).
  - L 600 `You are the Killer` [4]; M skill name (1 line; longest `Packet afterimage`, 17 characters ≈ 168 u) [4]; S `text` effect line from §8, max 3 lines [4]; S `muted`, max 3 lines: `Only Signal Survivors show, even through walls. Catch every Survivor before one reaches the Exit.` (97 characters → 3 lines at 35 per line: 32 / 35 / 28)
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
Info block (168×72, padding 8 left/right, 0 top/bottom): three M lines, 3 × 23 = 69 ≤ 70 inner height; inner width 152 u. Widest lines at M (9.9 u per character): `Terminals 1/3` 129 u; line 3 (§4.7) `Exit Locked` 109 u, bar + gap + `Exit Open` 4 + 4 + 89 = 97 u, bar + gap + `Locked 4 s` 4 + 4 + 99 = 107 u. All ≤ 152.
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
| 3 | Lagging, being rescued | `Status = Lagging` and `S2C_SelfState.RescueProgress > 0` | `Lagging` (`text`) | `Rescue in progress — stay close` | fill `ally`, width = `RescueProgress` (§4.4) |
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
- `Captured once` (S `muted`) when `CaptureCount ≥ 1` and `Status = Normal`. The play menu (§5) explains that the next capture eliminates you.

Either tag is at most 15 characters (≈ 125 u) next to the longest word, `Lagging` (≈ 70 u at M). That fits 288 u.
Fills are steady (no pulse). Sounds: none on Signal/Silent; `stealth_on` (soft, 150 ms) when `Stealthed` sets; `captured` (low hit, 250 ms) on entering Lagging.

**(10/30) Capture banner.** On the own `Status` going Normal → Lagging (never on the first snapshot of a match, so a reconnect into Lagging shows nothing), the prompt row (§4.3) shows for 3 s, ahead of every other prompt, M `text` with a 4×18 u `danger` bar before it: `Lagging. A Survivor within 1.5 m for 3 s rescues you · 60 s left` (numbers from `RescueRadius`, `RescueTimeSeconds`, `LaggingTimeoutSeconds`; 66 characters ≈ 540 u, fits the 656 u row). It is shown once per capture and removed early when the status leaves Lagging. The brief's draft wording (`Caught … ally …`) was changed to SLOP §6 vocabulary (`Survivor`). **(10/30, U4e attempt 2)** The banner starts with the meter's state word, `Lagging.` (it was `Captured.` in attempt 1), so the banner and the meter name the same state; the `captured` sound name is unchanged.

**(10/30) Elimination reason** (*derived* from the own entry in the snapshot before `Status = Eliminated`):

| Previous own status | Condition | Reason line |
|---|---|---|
| Lagging | last `StatusTimer ≤ 0.2` | `Disconnected: no rescue within 60 s.` |
| Lagging | last `StatusTimer > 0.2` | `Executed: the Killer touched you while Lagging.` |
| Normal | `CaptureCount ≥ 1` | `Executed: caught a second time.` |
| anything else | — | none (the meter keeps `Watching until the match ends.`) |

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

**(10/30) Build notes.** One mask texel is 2×2 u in both the 8 u strip (four rows of noise) and the 24 u band, point-filtered, so the noise stays crisp at both canvas sizes. `threat_scratch` plays once on entering level 2 (from any other level). The hidden rule applies to the beep too: while Eliminated or Escaped the beep is fed level 0. A `ThreatLevel` outside 0–3 reads as 0.

### 4.3 Terminals, hacking, "hold E"
Sources: `S2C_WorldState.Terminals[]{Id, X, Z, Progress}`, `TerminalsRequired`; own `Flags.Hacking` and position; static `TerminalRadius` 1.5 m. Display number = `Id + 1`.

- **World label** on each terminal within 25 m and on screen, 1.6 m above it: `Terminal 2 · 40%` (S `text`, `panel` tag, 4 u padding); complete: `Terminal 2 · Done` (S `muted`).
- **Info block line 2:** `Terminals 1/3` (M, tabular): done = count of `Progress = 100`, needed = `TerminalsRequired`. Weight 600 once done ≥ needed.
- **Prompt row** (672×40 above the cluster, M `text`, `panel` fill, 1 u `line` border):

| State | Trigger | Prompt |
|---|---|---|
| Can hack | own `Status = Normal`, an incomplete terminal within 1.5 m (derived), not `Hacking` | `Hold [E] to hack Terminal 2 · 40% — you will be Signal` |
| Hacking | own `Hacking` | `Hacking Terminal 2 · 47%` + a 656×4 u bar under the text, fill `text`, width = `Progress` |
| Co-hacking | `Hacking` and at least one other Survivor within 1.5 m of the same terminal with `Hacking` (derived) | `Hacking Terminal 2 · 47% · 2 hacking` |
| Lagging at a terminal | `Status = Lagging`, within 1.5 m | `Lagging Survivors cannot hack.` (`muted`) |

Input: E down → `C2S_Interact{Active = 1}`. E up, opening the play menu, or losing window focus → `C2S_Interact{Active = 0}`. The server decides range; the bar shows only the server's `Progress`.
Sounds: `hack_start` (soft click) when own `Hacking` sets; `terminal_done` (two notes, 300 ms) when any `Progress` reaches 100.

### 4.4 Rescue
Sources: Survivors' `Status`, `Flags.Rescuing`, `X/Z`; own `S2C_SelfState.RescueProgress` (protocol v0.3, U2c — §11 Q5); static `RescueRadius` 1.5 m.

Rescue progress comes from the server, own-only: for a Lagging Survivor it is the progress of their own rescue; for a rescuer it is the progress of the rescue they are taking part in; otherwise 0. Assumed range 0–1 (the U2c brief fixes the encoding). It is drawn as a bar, never as seconds. No client estimate is used.

| Viewer | Trigger | Display |
|---|---|---|
| Normal Survivor approaching | a Survivor with `Status = Lagging` within 6 m (derived from positions), own `Rescuing` clear | prompt row: `Mina is Lagging · 4 m — stay within 1.5 m to rescue them` |
| Rescuer | own `Rescuing` set | prompt row: `Rescuing Mina — you are Signal` + 656×4 bar, fill `ally`, width = own `RescueProgress` |
| Lagging Survivor | meter row 3 (§4.1) | — |
| Done | a watched Survivor's `Status` goes Lagging → Normal | prompt and meter return to normal; `rescued` (rising two notes) for the rescued Survivor and the rescuers |

Names = lobby `Slot.Nickname` for that `PlayerId` (stored from the last `S2C_LobbyState`).

### 4.5 Own status: Lagging, Escaped, Eliminated
- **Lagging:** meter rows 3–4.
  - Frame-stutter treatment (GDD §3.3): the world camera image is presented at 8 Hz (each frame held 125 ms) and blended with the previous frame at 30 %. HUD and audio stay at full rate. No shake, no flash.
  - Reduced-motion alternative: no frame hold, only the 30 % previous-frame blend, and no extra border (so it never competes with the level-3 edge noise). Until a settings screen exists, it is the launch option `-ptk-reduce-stutter`; a settings toggle is required before any Store build (§11 Q12).
  - **(10/30) Build:** the world camera renders into an off-screen texture; at each 8 Hz slot (every frame when reduced) the newest presented frame is kept and the one before it is drawn on top at 30 % opacity, both under the HUD. It runs exactly while the own `Status = Lagging`.
  - The skill is disabled (§4.8), and edge markers show Normal Survivors (§4.6).
- **Escaped:** lasts until `S2C_GameResult`, which normally arrives in the same tick. The meter shows row 2.
- **Eliminated:** plays the sound `eliminated` (falling tone, 600 ms). The meter is replaced by a spectate block of the same size.
  - Line 1, M: `Eliminated — spectating Mina`. Line 2, S `muted`: `[E] next · [Q] previous`.
  - Camera: the §4.10 orbit rig around the spectated Survivor's `X, Z`; their character faces their `Yaw`. The spectator orbits it with the mouse / right stick. Switching target cuts instantly and keeps the current camera yaw and pitch.
  - Order: slot order, skipping Survivors whose `Status` is Eliminated or Escaped. If none is left, line 1 reads `Eliminated — no Survivor left to spectate` and the camera stays.
  - The threat cue, skill and prompts are hidden. The info block stays.

### 4.6 Screen-edge markers
Sources: Survivors' `X/Z`, `Status`; static Exit position; `ExitOpen`, `ExitLocked`.

| Marker | Shown to | Off-screen | On-screen |
|---|---|---|---|
| Lagging Survivor | Normal Survivors | 16×16 u chevron `ally` at the screen edge toward them + S label `Mina · 38 m` | world tag above them: `Mina · Lagging · 38 m` (S `text`, `panel` tag, 2 u left bar `ally`) |
| Normal Survivor (nearest 2) | a Lagging Survivor | chevron `ally` + `Mina · 12 m` | world tag `Mina · 12 m` |
| Exit | Normal Survivors, once `ExitOpen = 1` | chevron `text` + `Exit · 34 m` (`Exit Locked · 34 m` while `ExitLocked = 1`) | world tag on the exit |

Placement and pointing follow the §6.2 edge-indicator rule (safe rectangle inset 24 u; keep-out = cluster, prompt row and, while shown, intro, expanded by 8 u). At most 7 markers. No marker or element ever concerns the Killer.

### 4.7 Exit (info block line 3)
Sources: `ExitOpen`, `ExitLocked`; FirewallBlock `S2C_SkillEffect.Duration` (sent to everyone, X/Z = exit).

| State | Trigger | Text (M) | Mark (non-text) | Sound |
|---|---|---|---|---|
| Locked | `ExitOpen = 0` | `Exit Locked` (`muted`) | world ring 1 u dashed `muted` | — |
| Open | `ExitOpen = 1`, `ExitLocked = 0` | `Exit Open` (`text`) | 4×18 u bar `ally` before the text; world ring solid `ally` | `exit_open` (three notes, 600 ms) on 0 → 1; the Exit edge marker appears |
| Locked by the Killer | `ExitOpen = 1`, `ExitLocked = 1` | `Locked 4 s` (`text`; the bar marks it as the Exit line; seconds derived from FirewallBlock `Duration`; with no effect received, e.g. after a reconnect: `Exit Locked`) | 4×18 u bar `danger`; world ring solid `danger` with a bar across | `exit_locked` (short buzz) on `ExitLocked` 0 → 1 |

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
  - The Killer model swipes once per capture (`Status` Normal → Lagging, or Normal → Eliminated; 1.2 s), and only if the model is drawn at that moment. The swipe is on the upper body; the legs keep their locomotion.
  - No other cue is added: the motion never reveals anything the HUD does not already show.

---

## 5. Survivor play menu
Esc / Start replaces the prompt row with a 672×152 u block (same anchor; it grows upward):
- Line 1 (M 600): the skill name. Line 2 (S): its effect (§8; the longest, 68 characters ≈ 564 u, fits one line in the 648 u inner width).
- Line 3 (S `muted`), when applicable: `Captured once: the next capture eliminates you.`
- Then `Resume` (primary 160×40, focused) and `Leave match` (secondary 160×40) in a row, gap 8.
- Height with padding 12: 12 + 23 + 4 + 18 + 4 + 18 + 8 + 40 + 12 = 139 ≤ 152.

Opening the menu sends `C2S_Interact{Active = 0}` and one zero `C2S_MoveInput`, and it releases the cursor. The match keeps running.

`Leave match` closes the connection and returns to the Main menu. The server keeps the slot for 10 s, then a bot takes it.

Focus: Resume → Leave match. Esc / B = Resume.

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
| 6.1p | Prompt | §6.4 | execution guidance, 2 S lines | 320×56, padding 8; 1 + 8 + 2 × 18 + 8 + 1 = 54 ≤ 56 [8] |
| 6.1a | Info | `TimeLeft`, `Terminals[].Progress`, `TerminalsRequired` | line 1 `9:42` (M 600 tabular `text`; last 60 s: 4×18 u `text` bar left of it); line 2 `Terminals 1 of 3` (M); line 3, only when done = needed − 1: `One more opens the Exit.` (S `text`) | 320×72, padding 8 left/right, 0 top/bottom; 1 + 23 + 23 + 23 + 1 = 71 ≤ 72 (line 3 budgeted at the M line height) [8] |
| 6.1c | Effect line | own Overclock effect; Killer-bound TrafficOverload effect | `Overclock · 2.1 s`, then `Recovering · 0.8 s` (static 1 s after `Duration`); `Slowed by traffic overload · 1.6 s` with a 4×18 u `danger` bar. Hidden when none. | 320×32 (S, padding 4); 1 + 4 + 18 + 4 + 1 = 28 ≤ 32 [8] |
| 6.1d | Skill block | `MatchStart.SkillId`, own `S2C_SelfState.SkillCooldown` (v0.3), own `S2C_SkillEffect`, static `KillerSkillCooldownSeconds` 12 s (ring scale), `ExitOpen` | Ring and icon as in §4.8 (64 u area, 56 u ring, 64 u active arc); name M 600 to the right; state line S `muted`: `Ready` / `Cooldown 7 s` (`ceil(SkillCooldown)`) / `Passive — always on` (PacketAfterimage: no ring, no key glyph) / `After the Exit opens` (FirewallBlock while `ExitOpen = 0`, disabled) | 320×72 |

Width checks:
- Skill block text area: 320 − 8 − 64 − 8 − 8 = 232 u. The longest name, `Packet afterimage`, is ≈ 168 u, and the longest state line, `Passive — always on`, is ≈ 158 u.
- Effect line: 282 + 4 + 4 = 290 ≤ 312.

Heights at 720p:
- Worst case, with everything shown: 208 + 8 + 56 + 8 + 72 + 8 + 32 + 8 + 72 = 472 u ≤ 900 − 48.
- The cluster's top edge sits at 900 − 24 − 472 = 404 u, below the screen center.
- In normal play (intro, prompt and effect line hidden), the cluster is 72 + 8 + 72 = 152 u tall; 192 u with the effect line.

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
- Not open: 16 u ring, 1 u dashed `muted`, tag `Exit Locked` (`muted`).
- Open: solid `text` ring, tag `Exit Open` (`text`).
- Locked by the Killer: solid ring with a bar across, tag `Exit Locked 4 s` (`text`; seconds from the Killer's own FirewallBlock `Duration`; with no effect received, e.g. after a reconnect: `Exit Locked`).

**Draw order**, back to front: terminal and exit markers, Signal markers and edge indicators, then the cluster. Markers never use glow (SLOP V3). Only Survivors get the `signal` outline.

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

### 6.4 Execution guidance (GDD §3.3, execution grace added 2026-10-27)
Rule (server, `GameServer.Rules.cs`): after a capture, the Lagging Survivor cannot be eliminated for 2 s; a touch during those 2 s is spent and does nothing; elimination needs the Killer to be out of contact (> `CaptureRadius` 1.2 m) at some tick and then touch them after the grace.

Sources: own `S2C_SelfState.ExecutionGraceLeft` (Killer only, protocol v0.3, U2c — §11.1 N5): seconds of grace left for the nearest Lagging Survivor within 3 m of the Killer, 0 when none or when its grace is over. Killer-bound Lagging instances (§6.3) and the Killer's own position.

The **target** is the nearest Lagging instance within 3 m (the same rule the server uses for `ExecutionGraceLeft`). The client keeps one derived flag for it, mirroring the server's re-arm rule:
- `armed = false` when an instance becomes the target (a new capture, a reconnect, or a different instance becoming nearest). This is the safe default: at worst it asks for one extra step back.
- Each snapshot: if the Killer is more than 1.2 m from the target, `armed = true`; otherwise, if `ExecutionGraceLeft > 0`, `armed = false` (the in-grace touch is spent).

Prompt block 6.1p (two S lines; line 1 S 600) is shown while a target exists:

| State | Trigger | Line 1 | Line 2 |
|---|---|---|---|
| Grace | `ExecutionGraceLeft > 0` | `Lagging Survivor` | `Wait 1.6 s, step back, touch again.` (seconds = `ExecutionGraceLeft`, one decimal, tabular) |
| Step back | `ExecutionGraceLeft = 0`, `armed` false | `Lagging Survivor` | `Step back 1.2 m, then touch again.` |
| Ready | `ExecutionGraceLeft = 0`, `armed` true | `Lagging Survivor` | `Touch again to eliminate.` |

Line 2 is at most 35 characters (≈ 290 u), which fits the 302 u inner width (320 − 2 × 1 border − 2 × 8 padding). Decoys are never Lagging, so the prompt cannot expose one, and `ExecutionGraceLeft` only concerns Lagging Survivors, who are always visible. The server decides; the prompt is guidance. Until U2c lands, this element cannot be built as specified.

### 6.5 Killer sounds
| Cue | Trigger (all observable in the Killer view) |
|---|---|
| `blip_first` (soft ping, −12 dB, non-positional) | The Signal count goes from 0 to ≥ 1 after ≥ 2 s with none. |
| `capture_confirm` (hit) | Either a matched pair changes `Status` Normal → Lagging, or a new unmatched entry with `Status = Lagging` appears within 1.2 m of the Killer (the usual ambush). Decoys are never Lagging. At most once per 0.5 s. |
| `execute_confirm` (low hit) | A Lagging instance within 1.2 m of the Killer has no match in the next snapshot. Lagging Survivors are always visible, so this means Eliminated. |
| `exit_open` | `ExitOpen` 0 → 1 |
| `skill_use` / `skill_ready` | own effect / own `SkillCooldown` reaching 0 |
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
Esc / Start replaces the intro/prompt area at the top of the cluster with a 320×208 block, padding 12, inner width 296 u. It holds the skill name (M 600), the effect (§8; S, max 3 lines), then `Resume` (primary, focused) and `Leave match`, stacked as 296×40 u buttons with an 8 u gap.
- Height: 12 + 23 + 4 + 3 × 18 + 12 + 40 + 8 + 40 + 12 = 205 ≤ 208.
- Behaviour is the same as §5: one zero MoveInput, the cursor is released, and the match keeps running.
- Focus: Resume → Leave match. Esc / B = Resume.
- The HUD itself has no focusable elements.

---

## 7. Result screen and return to lobby

Sources: `S2C_GameResult{WinnerRole, Reason, EscapedIds[]}`; own role (`MatchStart`); names from the stored LobbyState; the last `S2C_WorldState` (`Terminals`, `TerminalsRequired`, `TimeLeft`). The server returns to the lobby right after GameResult and broadcasts `S2C_LobbyState`, which the client stores.

Layout: menu style, left column, margin 48 u, full `bg` behind (the 3D view is not shown).

| # | Element | Content | Style |
|---|---|---|---|
| 7.1 | Headline | `Survivors win` / `Killer wins` | L 600 `text` [8] |
| 7.2 | Your outcome | Survivor, in `EscapedIds`: `You reached the Exit.` · Survivor, not escaped, Survivors win: `Your team escaped.` · Survivor, Killer wins: `You did not escape.` · Killer, `AllSurvivorsDown`: `You stopped every Survivor.` · Killer, `TimeUp`: `Time ran out before the Survivors escaped.` · Killer, `SurvivorEscaped`: `A Survivor reached the Exit.` · **(10/30)** Survivor, eliminated with a §4.1 reason: `You were eliminated. ` + the reason line (e.g. `You were eliminated. Executed: caught a second time.`) | M `text` [8] |
| 7.3 | Reason | `SurvivorEscaped`: `Mina reached the Exit with 4:12 left.` (names from `EscapedIds`, joined by `, `) · `AllSurvivorsDown`: `Every Survivor was Lagging or eliminated.` · `TimeUp`: `Time ran out with the Exit closed.` / `Time ran out with the Exit open.` | S `muted` [24] |
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
| Killer | 0 | Ping scanner | `Shows the nearest Survivor for 1.5 s.` | three concentric arcs from a point toward a small dot |
| Killer | 1 | Overclock | `1.5× speed for 3 s, then 1 s unable to move.` | two right chevrons followed by a vertical stop bar |
| Killer | 2 | Packet afterimage | `Passive: Survivors stay visible 1 s after their Signal ends.` | a solid dot with two hollow dots trailing behind |
| Killer | 3 | Firewall block | `Locks the open Exit for 5 s.` | a ring with a horizontal bar through it |
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
| Spectate | Survivors' `PlayerState.{X, Z, Yaw, Status}` (orbit pivot and character facing) |
| Survivor camera and movement | own `PlayerState.{X, Z, Yaw}`; static walls (boom collision); input → `C2S_MoveInput.{DirX, DirZ, Yaw}` (§4.10) |
| Killer model (Survivor view) **(10/30)** | the Killer entry `PlayerState.{Role = Killer, X, Z, Yaw}`, present only in line of sight (§0.1, §4.10) |
| Capture banner, elimination reason **(10/30)** | own `PlayerState.{Status, StatusTimer, CaptureCount}` transitions (§4.1) |
| Terminal and Exit markers (Killer) | `Terminals[].{Id, X, Z, Progress = 100}` (done only, no percentage), `ExitOpen`, `ExitLocked`, FirewallBlock `Duration`; static positions |
| Execution guidance | own `S2C_SelfState.ExecutionGraceLeft` (Killer, v0.3) + derived `armed` from own position and the Lagging target (§6.4) |
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
