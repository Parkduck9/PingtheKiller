# HUD and screen spec — D3 UI pass

Status: attempt 3, SPEC-PASS (89) · decisions recorded 2026-10-27 (§11) · author: Claude Opus (orchestrator) · reviewer: Claude Sonnet, `docs/qa/UI-spec-review.md`

> **한국어 요약:** 메뉴·로비·매치 인트로·생존자 HUD·살인마 HUD·결과·재접속 화면의 구현 명세. 모든 요소는 그 역할의 클라이언트가 실제로 받는 패킷 필드, 정적 데이터, 또는 그 둘에서 계산한 값(명시)으로만 만든다. 생존자는 살인마의 위치·방향을 절대 알 수 없고, 살인마 화면은 Id를 전혀 쓰지 않는 익명 블립으로 그려 가짜 Signal(디코이)과 진짜를 구별할 수 없게 한다. 프로토콜에 없는 정보는 "열린 질문"(§11)으로 분리했다.

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

A Survivor client has no Killer position, direction, speed or skill state beyond `S2C_WorldState.ThreatLevel` and the Survivor-bound `S2C_SkillEffect`s (U2b rule 10). A Killer client never uses `PlayerState.Id`, `Yaw`, `StatusTimer` or `CaptureCount` of Survivor entries and never uses the `Afterimage` flag (§6.3).

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
| `panel` | `#1A1C1F` | panels; floor of the Killer map |
| `line` | `#2C2F33` | dividers and secondary-button borders (decorative; the label identifies the button) |
| `text` | `#E8E6E1` | text, primary button fill, neutral world markers |
| `muted` | `#9A9890` | secondary text, disabled text, past/inactive markers, **text-field and meter outlines** |
| `signal` | `#FFB020` | transmitting now: Signal state, Signal blips |
| `danger` | `#E5484D` | threat the Killer poses to the viewer, and the Killer's own marker. **Non-text only** (bars, segments, rings, noise mask) |
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
| Look (Survivor) | mouse | right stick |
| Interact (hold) | E | X |
| Skill | Q | RB |
| Spectate next / previous (Eliminated) | E / Q | RB / LB |
| Play menu | Esc | Start |

**Gamepad text entry (character pad):** A on a focused field opens a pad anchored directly under the field (never screen center): 12 keys per row, 32×32 u keys, 4 u gaps. Address pad: `0–9 . :` + `Delete` + `Done`. Name pad: `A–Z a–z 0–9 space - _` in 6 rows + `Delete` + `Done`. D-pad moves, A types, X deletes, B or `Done` closes. Pad keys use the §0.5 option states (normal / hover / focused / pressed). Fields remember the last value, so a gamepad player rarely types.

**Address history popup:** while the Address field is focused (not editing) and history exists, a list of up to 3 previous addresses (options, 560×40 u each, 4 u gaps) opens directly under the field and **overlays** the items below it (reason line, Join); it is drawn on top with `panel` fill and a 1 u `muted` border. Down moves into the list, A / Enter fills the field and closes it, B / Esc or moving focus away closes it.

Prompts show the glyph of the last-used device (`[E]` or `X`), switching on the first input from the other device.

### 0.7 Sound rules
One sound per discrete state change. Continuous states are silent, except the threat beep, which *is* the threat indicator (GDD §3.2). Signal ↔ Silent toggles have no sound (§11 Q3). Priority: threat beep > capture/eliminated > exit opened/locked > terminal done > skill > UI.

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
- **Killer:** a block 300×208 u in the left column directly above the effect line (§6.1); time stays visible. Inner width 276 u (≈ 33 S characters per line).
  - L 600 `You are the Killer` [4]; M skill name (1 line; longest `Packet afterimage`, 17 characters ≈ 168 u) [4]; S `text` effect line from §8, max 3 lines [4]; S `muted`, max 3 lines: `Only Signal Survivors appear on your map. Catch every Survivor before one reaches the Exit.` (88 characters → 3 lines)
  - Worst-case height: 12 + 34 + 4 + 23 + 4 + 3 × 18 + 4 + 3 × 18 + 12 = 201 ≤ 208. (The longest Killer effect line, Packet afterimage, is 65 characters → 2 lines.)

Sound: `match_start` (one low tone, 400 ms).

---

## 4. Survivor HUD (first person)

Zone rule (SLOP §8): one **bottom-center cluster**, plus screen-edge markers and world-anchored labels. No top bar, no corners.

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

### 4.2 Threat cue (GDD §3.2)
Source: `S2C_WorldState.ThreatLevel` (0–3). Distance only; the UI adds no direction (no audio panning, no edge bias).

Each level has its sound and **exactly one** screen effect:

| Level | Trigger | Sound (centered, non-positional) | Screen effect |
|---|---|---|---|
| 0 | `ThreatLevel = 0` | — | — |
| 1 | `= 1` (12–18 m) | `threat_beep` every 1.2 s | **meter-edge noise:** a 1-bit noise strip (pixels `danger` or transparent, 30 % density) along the meter's top edge, 4 u tall, covering 1/3 of the meter width from the right |
| 2 | `= 2` (6–12 m) | `threat_beep` every 0.6 s + `threat_scratch` (low, −12 dB) | the same strip at 2/3 width |
| 3 | `= 3` (≤ 6 m) | `threat_beep` every 0.25 s | **screen-edge noise** replaces the strip: the same 1-bit `danger` mask in a 24 u band on all four screen edges |

The noise mask is static (never animated); it is re-rolled once on each level change (§11 Q2). Beep timers restart on change. The cue is hidden while Eliminated or Escaped. Level 2's visual is an addition (the GDD lists only sounds for level 2), recorded in §11 Q2.

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
  - The skill is disabled (§4.8), and edge markers show Normal Survivors (§4.6).
- **Escaped:** lasts until `S2C_GameResult`, which normally arrives in the same tick. The meter shows row 2.
- **Eliminated:** plays the sound `eliminated` (falling tone, 600 ms). The meter is replaced by a spectate block of the same size.
  - Line 1, M: `Eliminated — spectating Mina`. Line 2, S `muted`: `[E] next · [Q] previous`.
  - Camera: first person at the spectated Survivor's `X, Z, Yaw`, with a static eye height.
  - Order: slot order, skipping Survivors whose `Status` is Eliminated or Escaped. If none is left, line 1 reads `Eliminated — no Survivor left to spectate` and the camera stays.
  - The threat cue, skill and prompts are hidden. The info block stays.

### 4.6 Screen-edge markers
Sources: Survivors' `X/Z`, `Status`; static Exit position; `ExitOpen`, `ExitLocked`.

| Marker | Shown to | Off-screen | On-screen |
|---|---|---|---|
| Lagging Survivor | Normal Survivors | 16×16 u chevron `ally` at the screen edge toward them + S label `Mina · 38 m` | world tag above them: `Mina · Lagging · 38 m` (S `text`, `panel` tag, 2 u left bar `ally`) |
| Normal Survivor (nearest 2) | a Lagging Survivor | chevron `ally` + `Mina · 12 m` | world tag `Mina · 12 m` |
| Exit | Normal Survivors, once `ExitOpen = 1` | chevron `text` + `Exit · 34 m` (`Exit Locked · 34 m` while `ExitLocked = 1`) | world tag on the exit |

Inset 24 u from the screen edge; markers on the bottom edge stop 8 u above the prompt row. At most 7 markers. No marker or element ever concerns the Killer.

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

Layout (168×72, padding 8): at left a 64×64 u area holding a 56 u ring (3 u track `line`) with the 32×32 u icon centered in it (1 u `text` line drawing, §8); the Active arc (below) is drawn at 64 u diameter, so it stays inside the 64 u area and 4 u inside the block's top and bottom edges. To the right (168 − 8 − 64 − 8 − 8 = 80 u, ≈ 9 S characters per line): the skill name, S 600, max 2 lines (e.g. `Traffic` / `overload`; `Emergency` / `restore`), then the key glyph `[Q]` / `RB` (S `muted`). 2 × 18 + 18 = 54 ≤ 56 inner height.

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

## 6. Killer HUD (top-down)

The map **is** the HUD (SLOP §8). The screen is the square map view (height-fitted, centered) plus one column in the left gutter. The right gutter holds only the play menu when it is open.

Geometry: map square `900 / 1080 u`; gutters `350 / 420 u`. Left column: x 24, width 300, from y 24 to bottom − 24 (shifted down 72 u while the connection band shows, §9).

### 6.1 Left column
From the top:
| # | Element | Source | Content | Size |
|---|---|---|---|---|
| 6.1a | Time left | `TimeLeft` | label `Time left` (S `muted`) [4]; `9:42` (L tabular `text`); in the last 60 s a 4×28 u `text` bar left of it | [24] |
| 6.1b | Terminals | `Terminals[].Progress`, `TerminalsRequired` | `Terminals 1 of 3` (M `text`); when done = needed − 1, a second line `One more opens the Exit.` (S `text`) | — |

From the bottom:
| # | Element | Source | Content | Size |
|---|---|---|---|---|
| 6.1d | Skill block | `MatchStart.SkillId`, the Killer's own `S2C_SelfState.SkillCooldown` (v0.3), own `S2C_SkillEffect`, static `KillerSkillCooldownSeconds` 12 s (ring scale), `ExitOpen` | Ring and icon (64 u) as in §4.8, the name (M 600) to the right, and a state line (S `muted`): `Ready` / `Cooldown 7 s` (`ceil(SkillCooldown)`) / `Passive — always on` (PacketAfterimage: no ring, no key glyph) / `After the Exit opens` (FirewallBlock while `ExitOpen = 0`, disabled) | 300×72 |
| 6.1c | Effect line | own Overclock effect; Killer-bound TrafficOverload effect | `Overclock · 2.1 s`, then `Recovering · 0.8 s` (static 1 s after `Duration`); `Slowed by traffic overload · 1.6 s` with a 4×18 u `danger` bar. Hidden when none. | above 6.1d, gap 8 |
| 6.1e | Intro block | §3 | 300×208, above 6.1c while shown | gap 8 |

Skill block internals (300×72, padding 8, inner height 56): 64 u ring area as §4.8; to the right 300 − 8 − 64 − 8 − 8 = 212 u for the name (M 600, one line; longest `Packet afterimage` ≈ 168 u) and the state line (S, one line; longest `Passive — always on` ≈ 158 u). 23 + 18 = 41 ≤ 56.
Effect line: longest `Slowed by traffic overload · 1.6 s` (34 characters ≈ 282 u at S) + 4 u bar + 4 u gap = 290 ≤ 300; drawn at S (one 18 u line).
Column height check (720p): top stack 18 + 4 + 34 + 24 + 23 + 18 = 121 u; bottom stack 72 + 8 + 18 + 8 + 208 = 314 u; total 435 u ≤ 852 u (780 u with the connection band).

### 6.2 Map layer
Sources: Killer-bound `S2C_WorldState.Players` (Killer + Signal Survivors including Lagging, forced, afterimage, and decoys), `Terminals`, `ExitOpen`, `ExitLocked`, FirewallBlock effect; static map.

| Element | Trigger | Rendering |
|---|---|---|
| Floor and walls | static | floor `panel`; walls fill `line`, 1 u outline `muted`; unlit flat materials for gameplay geometry |
| Killer (self) | own entry (`Role = Killer`) | 20 u filled disc `danger` + a 10 u facing tick `text` from the Killer's own input direction |
| Blip | anonymous blip (§6.3) with `Status = Normal` | 16 u filled disc `signal`. No name, number, facing or trail. |
| Lagging blip | anonymous blip with `Status = Lagging` | 16 u disc `signal` + 1 u ring `text` at 24 u + label `Lagging` (S `text` on a `panel` tag). Decoys are never Lagging, so this is decoy-safe. |
| (no capture marker) | — | `CaptureCount` is **not rendered**: a WhiteNoise decoy can re-borrow a different Survivor's Id mid-life, so the value is not stable on a decoy, and the marker would not change the Killer's action (a touch is the action either way). |
| (no last-seen mark) | — | A vanished blip leaves nothing behind. Entries kept alive by the server's PacketAfterimage are ordinary blips (§6.3), so the skill's value comes from the server, and non-owners get no free afterimage. |
| Terminal | `Terminals[]` | 14 u square outline `text`, label `40%` (S `muted`) under it; done: filled `muted`, no label. The square vs the blip disc fixes U4 follow-up 7. |
| Exit | static position + `ExitOpen`, `ExitLocked` | 2 m ring. Label to the left of the ring, inside the map (fixes the U4 overlap): `Exit Locked` (`muted`, dashed ring) / `Exit Open` (`text`, solid `text` ring) / `Exit Locked · 4 s` (`text`; solid ring with a bar across, `text`; seconds from the Killer's own FirewallBlock `Duration`) |

### 6.3 Anonymous blips (normative; decoy safety)
Decoys are `PlayerState` entries shaped like a Normal Signal Survivor that borrow the Id of an absent Survivor (U2b rule 9). The server sets the `Afterimage` flag only on real Survivors. Therefore:
1. **Read only `Role`, `Status`, `Flags.Signal`, `X`, `Z`** of Survivor entries. Never `Id`, `Yaw`, `StatusTimer`, `CaptureCount` or the `Afterimage` flag. Entries with `Afterimage` set are drawn exactly like every other blip. (They are amber although no longer transmitting: to the Killer an amber blip means "reported by the server", and making it truthful would expose decoys — §11 Q8.)
2. **Match by position, not by Id.** In each snapshot, the Survivor entries form an unordered set.
   - The server sends them sorted by Id, so the client shuffles the received list before use. Distance ties are broken randomly. Nothing (spawn order, sound, z-order) follows the wire order.
   - Match greedily by nearest distance to the previous snapshot's blips, accepting only pairs within 0.4 m (2 × 4.0 m/s × 0.05 s).
   - Matched pairs tween over one tick.
   - Unmatched new entries appear in place.
   - Unmatched old blips disappear with no trace.

   A borrowed Id that jumps across the map therefore looks exactly like one blip vanishing and another appearing.
3. Status-change cues use matched pairs or the Killer's own position only (§6.4).
4. No per-blip identity anywhere: no names, colors, numbers or sounds per entry.

### 6.4 Killer sounds
| Cue | Trigger (all observable in the Killer view) |
|---|---|
| `blip_first` (soft ping, −12 dB) | the blip count goes from 0 to ≥ 1 after ≥ 2 s with none |
| `capture_confirm` (hit) | a matched pair changes `Status` Normal → Lagging, **or** a new unmatched blip with `Status = Lagging` appears within 1.2 m of the Killer (the usual ambush: the Survivor was Silent until captured). Decoys are never Lagging. At most once per 0.5 s. |
| `execute_confirm` (low hit) | a Lagging blip within 1.2 m of the Killer has no match in the next snapshot. Lagging Survivors are always visible, so this means Eliminated; decoys are never Lagging. |
| `exit_open` | `ExitOpen` 0 → 1 |
| `skill_use` / `skill_ready` | own effect / own `SkillCooldown` reaching 0 |
| `time_60` | `TimeLeft` crosses 60 |

There is no cue for capturing a Normal Survivor whose `CaptureCount` is already ≥ 1. That Survivor is eliminated and leaves the view in the same tick, which looks the same as going Silent (§11 Q7).

### 6.5 Killer play menu
Esc / Start opens a 300×208 block in the **right gutter**, at x = gutter start + 24, y 24 (x 1274 + 300 = 1574 ≤ 1600 at 720p). Padding 12, inner width 276 u (≈ 33 S characters). It holds:
- The skill name (M 600, one line).
- The effect (§8; S, max 3 lines — the longest Killer effect is 65 characters, so 2 lines).
- `Resume` (primary, focused) and `Leave match`, stacked as 276×40 u buttons with an 8 u gap. Two 160 u buttons do not fit side by side.

Height: 12 + 23 + 4 + 3 × 18 + 12 + 40 + 8 + 40 + 12 = 205 ≤ 208.
- Behaviour is the same as §5: one zero MoveInput, and the cursor is released.
- Focus: Resume → Leave match. Esc / B = Resume.
- The HUD itself has no focusable elements.

---

## 7. Result screen and return to lobby

Sources: `S2C_GameResult{WinnerRole, Reason, EscapedIds[]}`; own role (`MatchStart`); names from the stored LobbyState; the last `S2C_WorldState` (`Terminals`, `TerminalsRequired`, `TimeLeft`). The server returns to the lobby right after GameResult and broadcasts `S2C_LobbyState`, which the client stores.

Layout: menu style, left column, margin 48 u, full `bg` behind (the 3D view is not shown).

| # | Element | Content | Style |
|---|---|---|---|
| 7.1 | Headline | `Survivors win` / `Killer wins` | L 600 `text` [8] |
| 7.2 | Your outcome | Survivor, in `EscapedIds`: `You reached the Exit.` · Survivor, not escaped, Survivors win: `Your team escaped.` · Survivor, Killer wins: `You did not escape.` · Killer, `AllSurvivorsDown`: `You stopped every Survivor.` · Killer, `TimeUp`: `Time ran out before the Survivors escaped.` · Killer, `SurvivorEscaped`: `A Survivor reached the Exit.` | M `text` [8] |
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
| Killer | 2 | Packet afterimage | `Passive: Survivors stay on your map 1 s after their Signal ends.` | a solid dot with two hollow dots trailing behind |
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
- On the Killer screen the left column shifts down 72 u while the band shows. On the Survivor screen nothing else is at the top.
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
| Killer blips | Killer-bound `PlayerState.{Role, Status, Flags.Signal, X, Z}` as a shuffled anonymous set |
| Killer effect line | own Overclock / Killer-bound TrafficOverload `S2C_SkillEffect.Duration` |
| Spectate | Survivors' `PlayerState.{X, Z, Yaw, Status}` |
| Result | `S2C_GameResult.{WinnerRole, Reason, EscapedIds}` + the last WorldState |
| Connection band | socket, `S2C_Pong`, `S2C_JoinResult.Result` |

Never used:
- The Killer's position or direction, on a Survivor client.
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
| Q7 | Result stats and a Killer capture event. | **No, not now.** | The result shows terminals only (§7.4). There is no cue for eliminating a previously captured Normal Survivor (§6.4). |
| Q8 | Decoy detectability outside the UI. | **Under design, settled in U2c.** The user chose to evaluate shuffled per-snapshot indices instead of Ids in the Killer view, plus afterimage applied to decoys. | §6.3 keeps position-only matching. The Killer view **may stop carrying stable Ids**, so no Killer-side code may depend on `PlayerState.Id` of Survivor entries. §6.3 already forbids it. |
| Q9 | Host role choice online. | **Final.** Shown online as `Host plays as` (host only). | §2.8 |
| Q10 | Localization. | **Final.** English first with a string table; a Korean layout pass at 720p before Store screenshots. | All copy. |
| Q11 | Exit-open color. | **Final.** Survivors see an `ally` bar and ring; the Killer sees a `text` ring. | §4.7, §6.2 |
| Q12 | Lagging stutter and photosensitivity. | **Final.** 8 Hz hold + 30 % blend by default. The reduced alternative is blend only, with no border. It is a launch option until a settings screen exists, and a settings toggle is required before Store submission. | §4.5 |
| Q13 | S text at 720p. | **Final.** S renders at ≥ 12 px physical; the panel-scale minimum stays 0.8. | §0.3 |
| Q14 | Hosting from the game. | **Yes.** In-process `GameServer` + `TcpTransport` from the client. Expect a Windows Firewall prompt on the first host, and the Store package must declare the network-server capability (`privateNetworkClientServer`). | §1.7 |
| Q15 | Panel scale above 1080p. | **Final.** The scale stays clamped at 1.0 for now. | §0.2 |
