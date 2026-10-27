# Attempt 1

# UI spec review — `docs/ui/HUD_SPEC.md`

- Reviewer: Claude Sonnet 5.5 (author was Claude Opus)
- Spec attempt: 1
- Date: 2026-10-27
- Inputs read: SLOP_DEFINITION (§2–9), GDD v0.2, `docs/briefs/U2b-a1.md`, `Shared/Runtime/{Packets,Game}/*`, `Server.Core/Runtime/GameServer.{Skills,Visibility,Lobby}.cs`, `FixedMap.cs`, U4 screens and judgement.
- Contrast numbers below are WCAG 2.x, computed by script, not copied from the spec.

## 1. Score

| Row | Max | Score | Reasons |
|---|---:|---:|---|
| Visual tells (V1–V12) | 20 | 16 | No gradient, glass, glow, emoji, hacker costume or default Unity look. Deductions: 1 u borders and 1 u outlines render at 0.8 px at 720p (scale 0.8); `line` borders are 1.27:1 on `panel` and 1.40:1 on `bg`, so the text fields and the empty Silent bar are almost invisible; the level-3 band uses a 35 % alpha blend that is not a palette color (§4.2). |
| Layout (L1–L8, one spacing scale) | 15 | 10 | Left-aligned menus, role-specific layouts, no corner scatter. Arithmetic and fit faults: lobby rows end at y 468, not 472 (§2.3: 120 + 8×40 + 7×4); the Survivor intro band (104 u tall) cannot hold its copy (§3); the capture-marker line overflows the 320 u meter (§4.1); the connection band covers the Killer column top (§9 vs §6); the Killer play menu collides with the effect line (§6.1c vs §6.5); many y offsets are off the declared scale (212, 296, 484, 516). |
| Copy / vocabulary | 15 | 11 | Good: no hype, no `!`, sentence case, specific errors. Faults: host info tells players to run `dotnet run` (§1.2); `Result = 1` is shown as "The lobby is full (8 of 8)" although the server also returns 1 when a match is running and no bot can be replaced (§1.6); Killer win by `TimeUp` says "You stopped every Survivor." (§7.2); "Catch them before the Exit opens" is false as a rule (§3); `Skill not used.` gives no reason (§4.8); terminal labels use `Id` (0-based on the wire) as "Terminal 2" with no +1 rule (§4.3); new unfixed term "ally". |
| Every element answers "what state, what action" | 15 | 12 | Reason lines, disabled reasons and prompts are strong. Gaps: the "near a Lagging ally" prompt exists for about one tick because rescue is automatic (§4.4); `Exit Locked` gives no action; the Survivor skill block has no name (§4.8); the Lagging bar and the rescue bar claim the same slot (§4.1/§4.4). |
| Role-specific HUDs, no information leak | 20 | 13 | Survivor side is clean: no Killer position, direction, pan or per-ally threat; spectate hides the cue. The Killer map has decoy tells the spec does not see (F1, F2, F3). The spec also argues that skills "differ by icon and name only" is acceptable. SLOP §6 says the opposite (F6). |
| Interactive states, keyboard and gamepad | 10 | 6 | §0.5 is a good button table. Missing: states for text fields (1.3, 1.8); a gamepad path to type an address or name; spatial focus rules for the two-column lobby; releasing hold-E when the play menu opens. |
| Contrast and 720p readability | 5 | 2 | Claimed ratios are wrong and `danger` text on `panel` fails (F4). S text is 11.2 px at 720p and carries load-bearing information. Sub-pixel strokes (F5). |
| **Total** | **100** | **70** | Pass threshold is 85 with no gate failure. |

## 2. Gate results

| Gate | Result | Evidence |
|---|---|---|
| V1 gradients | PASS | §0.2 "no gradients"; palette §0.4. |
| V2 glass | PASS | §0.2 "no blur". |
| V4 hacker cliché | PASS | No monospace or matrix motifs. The static noise mask is a GDD §3.2 requirement and is not animated. |
| V8 emoji | PASS | Icons are 1 u line drawings (§8). |
| V10 defaults | PASS | Pretendard, custom states (§0.3, §0.5). |
| Every element answers state and action | PASS | Weak spots are minor (rows above). |
| Role-specific HUDs and no forbidden leak | **FAIL** | §6.2 and §6.3 claim decoys are identical to real blips. They are not (F1, F2, F3). §8 and §4.8 make skills differ by icon only, which SLOP §6 lists as slop (F6). Survivor-side leak: none found. |
| Interactive states and keyboard/gamepad | **FAIL** | §0.5 covers only buttons and options. §1.3 and §1.8 are interactive, have no states and no gamepad entry method (F7). |
| **Buildable from protocol (extra gate)** | **FAIL** | §6.4 `capture_confirm` for Eliminated needs data the Killer never receives (F2). §6.2/§6.3 depend on Id continuity the spec forbids elsewhere (F3). `PlayerState` cannot carry the proposed Q5/Q6 fields without leaking (see §4 below). Everything else is wire, static or clearly marked derived. |

### Buildable-gate checks that passed (adversarial)

- **Survivor inferring Killer position or direction.** No element carries it. `ThreatLevel` is distance only. The cue is centered, not panned. Edge markers never point at the Killer. FirewallBlock `X/Z` is the exit position. The Survivor-bound PingScanner effect is `0,0` (`GameServer.Skills.cs`). Spectating hides the threat cue, correctly, because the server computes `ThreatFor` from the spectator's own frozen position.
- **SkillEffect recipients match U2b rule 10.**
  - Survivor skills go to all Survivors (self included), as the spec assumes for the cooldown timer.
  - FirewallBlock goes to everyone. PingScanner goes to the Killer and the target. TrafficOverload goes to the Killer and Survivors.
  - Dummy and WhiteNoise never reach the Killer.
- **Decoy fields.** `Id`, `Yaw`, `StatusTimer` are correctly banned from rendering (§6.3.1). `CaptureCount` is copied from the borrowed Survivor, so the notch is safe. Real Lagging entries have `StatusTimer > 0` and decoys never are Lagging, so the Lagging label is safe.
- **Derived items are marked.** Rescue progress and skill cooldown are not on the wire. The spec marks both as derived and lists them in Q5/Q6.
- **Threat distances, cooldowns, durations, radii, TerminalsFor and the 60 s timeout** match the GDD and `GameConstants`/`SkillIds`.

### Verified numbers

- **Panel scale.** `Clamp(min(w/1920, h/1080), 0.8, 1)` matches `ClientScreens.cs:38`. 1280×720 gives 0.8 and a logical canvas of 1600×900.
- **Containment.** Menu column to y 512, lobby to 1040 u wide, Survivor cluster 560 u, and Killer left column (24 + 300 ≤ 350 gutter) all fit 1600×900.
- **Cluster width.** 8+72+8+320+8+136+8 = 560.
- **Snap threshold.** 4.0 m/s × 0.05 s × 2 = 0.4 m.
- **Contrast.** Computed against `panel #1A1C1F`, not the spec's figures:

| Token | Claimed | Actual on `panel` | On `bg` |
|---|---:|---:|---:|
| `text` | 14.6 | 13.69 | 15.15 |
| `muted` | 6.0 | 5.91 | 6.54 |
| `signal` | 10.0 | 9.34 | 10.33 |
| `ally` | 7.6 | 7.06 | 7.81 |
| `danger` | 4.6 | **4.36 (fails 4.5)** | 4.83 |

  - `muted` on `line` (disabled primary) is 4.65. `bg` on `muted` (pressed primary) is 6.54. `text` on `line` is 10.78.
  - Non-text: `line` vs `panel` 1.27, `line` vs `bg` 1.40.

## 3. Findings

[blocker] F1 §6.2 Afterimage blip, §6.3 — Afterimage ring is a decoy detector — The server sets `Afterimage` only on real Survivors. Decoys never get it and simply vanish when they expire (`GameServer.Visibility.cs`: `AddDecoy` sets `Flags = Signal`). A Killer with PacketAfterimage sees real Survivors turn into a ring for 1 s and decoys disappear with no ring. That reveals every expired decoy and contradicts "decoys identical". Q8 does not mention it. — Make the ring client-synthesized: ignore the `Afterimage` flag, draw a `muted` ring for 1 s at the last position of any blip that vanished (real or decoy), and drop flagged server entries from the blip list. Also raise it with the server owner: add an afterimage tail to decoys or remove the flag from the Killer view.

[blocker] F2 §6.4 `capture_confirm`, §6.2 — Eliminated capture is not observable — Rule 11 omits Escaped and Eliminated Survivors from the Killer view, so "any → Eliminated while visible" never occurs; the blip just vanishes (indistinguishable from going Silent, or from Lagging timeout). Buildable gate fail. — Keep the cue only for Normal → Lagging (observable). For second capture or execution use a Killer-local trigger: the Killer's own capture radius contact with a blip that then vanishes within 0.5 s, or drop the cue.

[major] F3 §6.3.3, §6.2 Last-seen mark — Id-keyed tracking leaks borrowed Ids — The spec forbids Id for rendering, but "interpolate if displacement ≤ 0.4 m" and "present last snapshot, absent now" are keyed by Id. When a real Survivor becomes visible while its Id was borrowed by a decoy, the Id stays present: the blip teleports from A to B and no `×` is drawn at A. Two real Survivors never produce a teleport without `×`. The spec's claim that this "looks like one vanishing and another appearing" is false. — Treat every snapshot's blips as anonymous: match blips to the previous snapshot by nearest neighbour within 0.4 m (greedy, no Id), tween only matched pairs, spawn unmatched new ones in place, draw `×` (and the ring from F1) for every unmatched old one.

[major] F4 §0.4, §4.1, §4.7, §4.9 — `danger` text fails 4.5:1 and the spec's own rule — Actual 4.36:1 on `panel`, not 4.6. Used for text on `panel` at M or S: Lagging word and countdown (§4.1), `Exit Locked 4 s` at S (§4.7, contradicts "M or larger"), time last 60 s (§4.9). M 18 u is 14.4 px at 720p, not WCAG large text. SLOP §9 requires ≥ 4.5. Claimed numbers for every token are also overstated (table above). — Replace `danger` with a lighter red of ≥ 4.5:1 on `panel` for text (for example `#F0555A`; check it), or put danger text only on `bg` (4.83) or at L SemiBold. State true ratios.

[major] F5 §0.2, §0.3, §0.5 — 720p readability: sub-pixel strokes and 11.2 px text — At scale 0.8, 1 u = 0.8 px (borders, outlines, empty bar), 2 u = 1.6 px, 4 u = 3.2 px, S = 11.2 px (Hangul worse). S carries Terminals, Exit and every reason line. — Snap all strokes to `max(1, round(u × scale))` physical px. Raise S to 16 u (12.8 px at 720p) or raise `MinScale` to 0.9. Move Terminals and Exit to M.

[major] §4.2 — Level 3 shows two screen effects — The row says "Visual (exactly one)" but level 3 draws three segments and the edge band. GDD §3.2 is sound plus one screen effect. Levels 1–2 also replace the GDD's "meter-edge noise" with segments without recording the deviation. — Level 3: band replaces the segments (the band is the one effect). Levels 1–2: the segment count is the one effect. Record the GDD deviation in §11.

[major] F6 §4.8, §8 — Skills differ by icon only — SLOP §6 forbids "skills differ by icon only". The spec cites SLOP §6 as allowing it and the Survivor block has no name. — Give each skill one structural difference: the Survivor block shows the name in S under the icon (or a one-word label), and the ring shows the skill's active duration in a second arc (`Active` shape differs from cooldown). At minimum add the name label to the block.

[major] F7 §0.5, §1.3, §1.8, §0.6 — Text fields have no states and no gamepad entry — No normal/hover/focused-editing/invalid/disabled table for the Address and Name fields; no way to type on a gamepad; Enter-to-edit and Esc-to-leave behaviour is undefined. SLOP §9 gate and Store gamepad navigation. — Add a field row to §0.5 (focused = 2 u outline plus caret, invalid = reason line), specify Enter/A opens the on-screen keyboard (Windows touch keyboard or a built-in D-pad character picker) and Esc/B exits editing; fields also remember the last value so a gamepad user rarely types.

[major] §1.2, §1.5 — No Host action; host info is a developer command — GDD §7 and SLOP §8 name Host / Join. The spec has only Join; the "host info" tells the player to run `dotnet run --project Server`, which a Store player does not have. — Either add a **Host** button (in-process TCP listener using the same `GameServer`) or say plainly in the spec that hosting is a separate server executable, and word it as: `To host, start PingTheKillerServer.exe on this PC.` State the decision.

[major] §3 — Intro layouts overflow — Survivor band 560×104 u must hold L (about 34 u), the M skill line (about 60 chars at 18 u wraps to 2 lines in 560 u) and the S rule line (100 chars, 2 lines), about 120 u plus padding. The Killer intro "replaces the time block" (72 u) with about 160 u of wrapped text in a 300 u column. It also sits where the prompt row lives. — Recompute heights; allow 144 u for the Survivor band and move the prompt row above it while the intro shows; give the Killer intro its own block above the skill block and keep time visible. Shorten the effect lines in §8 for the intro.

[major] §9 vs §6.1 — The connection band covers the Killer column — The band is full-width, 48 u tall at the top; the Killer time block starts at y 24. The spec claims it does not cover the Killer column. — Offset the Killer column top to 72 u while the band shows, or confine the band to the right gutter on the Killer screen.

[major] §4.1 — State table is ambiguous and can lie — Row "Silent — relay: `Stealthed` set" has no "and `Signal` clear". Stealth does not override forced exposure (U2b rule 8), so the own entry can have `Stealthed` and `Signal`; the table would print "Silent" while visible. Table has no precedence. The capture-marker line (about 45 chars at S) exceeds the 320 u meter; the Lagging ≤ 10 s L countdown and the Lagging rescue bar have no slot in the 48+24 u split. — Add precedence (Lagging, forced exposure, then Stealthed/Silent) and "Signal clear" to the relay row; move the capture line to the info block or shorten it (`Captured once.`); define where the countdown and rescue bar draw.

[minor] §4.4 — "Stay next to Mina to rescue" prompt is a one-tick state — Rescue is automatic within 1.5 m, so `Rescuing` is set on the next tick. The prompt is unreachable as written. — Trigger it from 6 m (derived) to guide the approach: `Mina is Lagging · 5 m — stay within 1.5 m to rescue`.

[minor] §6.2, Q8 — Stationary and simultaneous decoys are honest-player tells — The spec says §6.3 hides Q8 "from honest players only". A Dummy blip stands still 3 s away from any terminal. White Noise puts five static blips on the map in the same snapshot. Both are visible to any player. — Reword Q8: this is a skill-design trait plus UI-visible; suggest jittered decoy motion for Dummy (server) and staggered White Noise spawn. Not a UI fix.

[minor] §7.2, §7.3 — Result copy errors — Killer + `TimeUp` shows "You stopped every Survivor." (false). `Terminals hacked 3 of 3` can exceed required (more terminals are placed than required and hacking continues). Rescue counts are observable to Survivors (Lagging to Normal transitions) but dropped. — Per-reason Killer line: `Time ran out before the Survivors escaped.`; print `Terminals hacked 4 (3 needed)`; show `Rescues` to Survivors, or fix via Q7.

[minor] §1.6 — `Result = 1` copy — Also returned when a match is running and no bot slot can be replaced. — `No free slot. The lobby or match is full.` Also specify behaviour when `Join` succeeds into a running match and `S2C_MatchStart` arrives from the menu (the spec covers only the result screen).

[minor] §4.3, §6.2 — Terminal numbering — Wire `Id` is 0-based (`FixedMap.CreateTerminals`). Specify the label as `Id + 1`.

[minor] §2.3, §2.4 — Lobby geometry — Rows end at y 468; Bots rows read `Bot 6` plus a `Bot` tag. — Fix to 468 and drop the tag for bots, or show nicknames without the number.

[minor] §6.5 — Overlap — Play menu "above the skill block" collides with the effect line at bottom − 152 u. — Put the play menu at the right gutter or push the effect line up.

[minor] §0.4, §4.9, §6.1b — Palette drift — `danger` is defined as "Killer-caused threat" but is used for time pressure and for the Killer's terminal count; `ally` is used for the Exit-open, skill-active and result bars shown to the Killer. — Use `text` for time pressure; keep `ally` Survivor-only.

[minor] §4.7 — Reconnect case — After a reconnect the client has no FirewallBlock `Duration`, so `Exit Locked 4 s` cannot count. — Show `Exit Locked` without seconds until a new effect arrives.

[minor] §5, §4.3 — Play menu releases — Opening the menu must send `C2S_Interact{Active=0}` and a zero `MoveInput`. The text says only "on focus loss". The sentence about "leaving 1.5 m is not required" is garbled.

[minor] §4.5 — Stutter accessibility — 8 Hz hold plus 30 % blend for up to 60 s has no off switch. — Add a setting before Store, even if the setting UI is later.

## 4. Opinions on §11 open questions

- **Q1 vocabulary:** Agree with the spec. `Lagging` is a protocol and GDD term; SLOP §8 `Frozen` is stale. The orchestrator should amend SLOP §8: add Lagging, Eliminated, Escaped, Terminal, hack, spectate, "ally" (or replace "ally" with "Survivor").
- **Q2 level-3 noise:** Keep it static (photosensitivity, SLOP §8). Fix the two-effect problem (finding above) rather than allow flicker.
- **Q3 no sound on Signal/Silent:** Agree. It fires several times a second and would mask the beep. The visual meter plus words keeps it accessible.
- **Q4 pre-match freeze:** Do it. A 3 s server-side input and timer freeze is the only way to make a 3 s intro readable. It must freeze `TimeLeft` and cooldowns too and must not change the masking. Keep the non-blocking band as fallback.
- **Q5 cooldown on the wire:** Add it, but not to `PlayerState`. `PlayerState` is also sent in the Killer view, so any new field must be zero for decoys and real Survivors alike. Use a small own-only packet.
- **Q6 rescue progress:** Same answer as Q5. Fold both into one own-only `S2C_SelfState {SkillCooldown, RescueProgress, InvulnerableLeft}`; it also fixes the reconnect case.
- **Q7 result stats:** Add captures and rescues to `S2C_GameResult`; both are server-known and the GDD §7 lists rescues.
- **Q8 decoy detectability:** Wider than the spec says (see F1 and the stationary-blip finding). The Killer view needs no Survivor Ids at all, so the server should send per-snapshot shuffled indices for Survivor entries. Then decoys need no borrowed Id and Id-continuity tells disappear.
- **Q9 online role choice:** Keep it host-only online; it is harmless and useful for friends. Update GDD §7 to say so.
- **Q10 localization:** English first is fine, but build a string table now with no hard-coded strings; Korean is wider and 11.2 px Hangul is too small, so test layouts in Korean before Store screenshots.
- **Q11 Exit open color:** Agree: `text` for the Killer. Also stop using `ally` for non-team things (skill-active, results shown to the Killer).
- **Q12 panel scale clamp:** Raise the upper clamp (for example `[0.8, 2.0]`) now; it is a one-line change in `ClientScreens.cs` and needs no settings screen. At 4K the 560 px cluster is 15 % of the width with 14 px text.

## 5. What to fix first (attempt 2)

1. Decoy and Id handling (F1–F3, §6.2–§6.4) and the Buildable gate.
2. Text fields and gamepad entry (§0.5, §1).
3. Contrast and sizes (`danger`, S size, stroke snapping).
4. Level-3 threat cue, skill block distinctness, intro geometry, Host action and copy.

SPEC-FAIL (score 70)


# Attempt 2

- Reviewer: Claude Sonnet 5.5 (author: Claude Opus)
- Spec attempt: 2 (full rewrite of `docs/ui/HUD_SPEC.md`)
- Date: 2026-10-27
- I re-read the whole spec. Arithmetic below was recomputed by hand; contrast ratios were recomputed by script (the spec's §0.4 table now matches: text 13.69, muted 5.91, signal 9.34, ally 7.06, danger 4.36 on `panel`; danger 4.83 on `bg`).

## A2.1 Score

| Row | Max | Score | Reasons |
|---|---:|---:|---|
| Visual tells | 20 | 18 | Clean. Strokes are snapped (§0.2), `danger` is non-text, outlines that matter are `muted` (5.91:1). Remaining: secondary-button borders are `line` (1.27:1, declared decorative, so a button is only recognisable by its label); the level-3 noise mask is heavy but GDD-mandated and static. |
| Layout | 15 | 11 | Spacing is now a stack-with-gaps scale; lobby (532 u) and menu (about 615 u) heights and the cluster width (640 u) are correct. Several stated fits are false (G1). |
| Copy / vocabulary | 15 | 13 | Join error, result lines, terminal numbering and "ally" removal are fixed. Remaining: the menu rule line is false as written (N5); the Killer intro drops the effect line that §8 says is shown there (N10); no truncation rule for 48-byte nicknames (N7). |
| Every element answers state and action | 15 | 14 | Meter has a precedence table; the rescue prompt is reachable; skill reasons exist. Minor: `Exit Locked` still names no action. |
| Role-specific HUDs, no leak | 20 | 16 | Survivor side clean. Killer side is now anonymous and mostly sound, but `CaptureCount` is not decoy-safe (N2) and array order is not neutralised (N2b). |
| Interactive states, keyboard and gamepad | 10 | 8 | Field states and a gamepad character pad exist (§0.5, §0.6). Missing: character-pad key states, the address-history popup overlapping Join, Name placed after the buttons that use it (N6). |
| Contrast and 720p readability | 5 | 4 | True ratios, no danger text, S floor 12 px. The floor breaks the u-based wrap maths and Hangul at 12 px is still small (Q10/Q13). |
| **Total** | **100** | **84** | Below the 85 threshold. |

## A2.2 Gates

| Gate | Result | Evidence |
|---|---|---|
| V1 / V2 / V4 / V8 / V10 | PASS | §0.2, §0.3, §0.4, §8. |
| Every element answers state and action | PASS | §4.1 precedence, §4.4, §4.8. |
| Role-specific HUDs and no forbidden leak | **FAIL** | §6.2 "Captured-once tick" is declared decoy-safe, but WhiteNoise decoys re-borrow an Id when the borrowed owner turns visible (`GameServer.Visibility.cs`, `AddDecoys`), so the `CaptureCount` of a stationary blip can change (N2). Survivor side: no leak found. |
| Interactive states and keyboard/gamepad | PASS | Field row in §0.5; character pad and spatial focus in §0.6. |
| **Buildable from protocol** | **FAIL** | Same finding (N2): the notch is rendered from a field whose value is not stable across a decoy's lifetime. Everything else is wire, static or marked derived. |

Buildable-gate checks that now pass:
- `capture_confirm` and `execute_confirm` (§6.4) use only Lagging status and the Killer's own position.
- SkillEffect recipients still match U2b rule 10 (Survivor skills to Survivors, FirewallBlock to everyone, PingScanner to Killer and target, TrafficOverload to Killer and Survivors).
- No Survivor element carries Killer position or direction. The threat cue is centered and hidden when Eliminated or Escaped.
- The anonymous matching of §6.3 holds for the cases I tried: a borrowed-Id jump (ring plus new disc), an Afterimage-flagged entry (drawn as an ordinary disc, then vanishes like a decoy), a Dummy caster turning visible (the decoy hands over to the real blip within 0.4 m).

## A2.3 Status of attempt-1 findings

| Finding | Status | Notes |
|---|---|---|
| F1 Afterimage ring tells decoys | **Fixed** | §6.3.1 ignores the flag; §6.2 draws a client-side ring for every vanished blip. Side effect in N4. |
| F2 `capture_confirm` for Eliminated | **Fixed** | §6.4 adds `execute_confirm` from a vanishing Lagging blip and states there is no cue for the second capture of a Normal Survivor. Incomplete trigger in N3. |
| F3 Id-keyed tracking | **Fixed, with N2b** | §6.3.2 position matching within 0.4 m. The wire array is still Id-sorted. |
| F4 `danger` text | **Fixed** | True ratios in §0.4; `danger` carries no text (checked §4.1, §4.7, §4.9, §6.1). |
| F5 sub-pixel strokes, 11.2 px | **Fixed** | Stroke snapping (§0.2), S floor 12 px (§0.3). See G1 and N8. |
| Level 3 two effects | **Fixed** | §4.2: strip at levels 1–2, edge noise replaces it at 3. |
| F6 skills differ by icon only | **Fixed (adequate)** | Name in the block and a distinct active arc (§4.8). Stealth relay and Traffic overload share a 2 s arc; the names carry the difference. |
| F7 text fields, gamepad entry | **Fixed** | §0.5, §0.6 (pad is 12 × 32 u keys = 428 u wide; the Name pad is 6 rows, 212 u, and fits under the field). |
| No Host action, `dotnet run` copy | **Fixed** | §1.7, Q14. |
| §3 intro overflow | **Partly fixed** | The arithmetic is right (123 u and 140 u) but only for one-line skill text (G1). |
| §9 band covers Killer column | **Fixed** | 72 u shift (§9, §6). |
| §4.1 ambiguous table | **Fixed** | Precedence rows 1–11, relay row has "Signal clear", capture line moved to a tag. |
| Rescue prompt one tick | **Fixed** | 6 m approach prompt (§4.4). |
| Decoy honest-player tells | **Fixed (as text)** | Reframed in Q8(b). |
| Result copy | **Fixed** | Per-reason Killer lines, `4 (3 needed)`. Rescue counts still dropped although Survivors could derive them from Lagging to Normal transitions (minor, not fixed). |
| `Result = 1` copy, join into a running match | **Fixed** | §1.6 and the §1 note. |
| Terminal numbering | **Fixed** | `Id + 1`. |
| Lobby geometry | **Fixed** | 348 u rows, 532 u column. |
| Killer play menu overlap | **Fixed** | Right gutter (its height is in G1). |
| Palette drift | **Partly fixed** | `ally` is Survivor-only. `danger` bars still mark time pressure (§4.9, §6.1a), which is not Killer-caused. |
| Exit lock after reconnect | **Fixed** | §4.7. |
| Play menu releases | **Fixed** | §4.3, §5. |
| Stutter accessibility | **Partly fixed** | Launch flag only (Q12, N11). |

## A2.4 New findings

[major] N2 §6.2 "Captured-once tick", §6.3 — `CaptureCount` is not decoy-safe — The spec says decoys copy `CaptureCount`, "so this is decoy-safe". That is true at creation only. A WhiteNoise point keeps one borrowed Id "when possible"; when that Survivor becomes visible the server picks another absent Survivor, whose `CaptureCount` can differ. The tick on a stationary, non-Lagging blip can then appear or vanish in place. Real blips change `CaptureCount` only by becoming Lagging. — Latch the tick per blip: set it when the blip is created, update it only on a Normal to Lagging transition, ignore later `CaptureCount` changes on matched Normal pairs.

[minor] N2b §6.3.2 — Array order is Id order — The server sorts Killer-view entries by Id (`SortById`). "Unordered set" must also mean: iterate in random order, break distance ties randomly, never spawn or play sounds in array order. — Add one sentence.

[major] G1 §4.0, §4.1, §4.7, §4.8, §3, §5, §6.5 — Stated geometry does not fit — Checks:
- Meter 304×72 with padding 8: rows 24 + 20 + 16 = 60 > 72 − 16 = 56.
- Info block 136×72: no padding, three M lines = 69 of 70 inner u. `Exit Locked · 4 s` is 17 characters at M (about 135–150 u) plus its 4×18 bar, in 136 u.
- Skill Active arc at radius + 6 u on the 64 u ring is 76 u > the 72 u block.
- Survivor intro: 123 u holds only for a one-line skill line. Stealth relay (85 characters with its name) and Traffic overload wrap in 616 u, giving 146 > 144.
- Survivor play menu with line 3: 12 + 23 + 4 + 17 + 4 + 17 + 8 + 40 + 12 = 137 > 136.
- Killer play menu in 300×160: the effect line wraps to 2 lines and two 160 u buttons cannot sit in a row in 276 u; stacked, the total is about 181 > 160.
- The 12 px S floor makes S text about 7 % larger than the u maths assumes at 720p.

Fix: meter vertical padding 4 u (inner 64) or rows 20 + 20 + 16; info block 168 u wide (cluster 672) or shorten to `Locked · 4 s`; arc radius + 4 with the ring at 56 u; intro 160 u; play menus 144 u and 184 u; recompute with S at 15 u effective.

Confirmed correct: cluster 8 + 168 + 8 + 304 + 8 + 136 + 8 = 640 u (512 px at 720p, 40 % and 33 % of width); Killer left column 119 + 271 = 390 u of 852 u; gutter 350 u ≥ 24 + 300; Killer intro 140 u ≤ 160 u; lobby left column 532 u; Killer play menu x 1274 + 300 = 1574 ≤ 1600.

[minor] N3 §6.4 `capture_confirm` — Misses the usual capture — An ambushed Survivor is usually Silent, so the first Lagging entry has no previous Normal blip, and a matched Normal to Lagging pair never occurs. — Also fire when a new blip appears with `Status = Lagging` within 1.2 m of the Killer.

[minor] N4 §6.2 last-seen ring, §6.3.1 — Afterimage semantics — Afterimage-flagged entries are drawn as full `signal` discs although they are not transmitting (palette meaning, §0.4), and every Killer now gets a 1 s ring, so PacketAfterimage is partly given away for free. — Draw the ring only when the Killer owns PacketAfterimage. Decoy parity does not need the ring, because flagged entries already persist as ordinary discs.

[minor] N5 §1.2 — Menu rule line is false — `Survivors are visible to the Killer only while they move.` Hacking, rescuing, scans and Lagging also expose (§3 itself says `Moving or hacking`). It is also a tagline on the menu (SLOP L8, though small). — `Survivors are visible to the Killer only while they transmit.` or remove it.

[minor] N6 §0.6, §1.3, §1.9 — Focus and popup — Name sits after Host and Offline, which already use the nickname. The 3 last-address options under the focused Address field overlap the reason line and Join with no layering or dismissal rule. The character-pad keys have no state table. — Put Name before Join; state that the options popup overlays Join and closes on B; give pad keys the §0.5 option states.

[minor] N7 all name-bearing copy — No truncation rule — Nicknames can be 48 bytes; prompts, tags, the spectate line and lobby rows have fixed widths. — Ellipsize at the container width.

[minor] N8 §0.3, §0.4, §4.9, §6.1a — `danger` meaning, Hangul size — `danger` bars on time left are not "threat the Killer poses". Hangul at 12 px remains marginal. — Use a `text` bar for time pressure; test Hangul at 720p before locking S.

[minor] N9 §1.9 — Empty name sent as `Player` — The server default is `Player {id}` (`SanitizeNickname`); sending `Player` makes duplicate names. — Send empty and let the server name it.

[minor] N10 §3 Killer intro vs §8 — §8 heads its effect column "intro, play menu" but the Killer intro shows only the skill name. — Show the effect (needs about 3 more lines, see G1) or edit §8.

[minor] N11 §4.5, Q12 — `-ptk-reduce-stutter` is a launch flag — Not discoverable by a player who needs it, and it swaps the stutter for another static noise border that competes with the level-3 edge noise. — Ship a settings toggle before Store; reduce to a blend-only presentation with no border.

## A2.5 Opinions on new open questions

- **Q12 stutter:** Keep 8 Hz as the default, but a settings toggle is mandatory before a Store build; a launch flag does not count. The alternative should be "no hold, 30 % blend only", with no extra border.
- **Q13 S floor:** Take the 12 px floor and also raise `MinScale` to 0.9. It costs 1280×720 about 6 % of canvas width and removes most overflow risk in G1. Verify Hangul at that size.
- **Q14 in-process Host:** Accept. Record three consequences: the Windows Firewall prompt on first host (the spec says so), the Store capability `privateNetworkClientServer`, and that the host's own 127.0.0.1 path never shows the connection band. A separate server executable is wrong for a Store title.
- **Q15 panel scale above 1080p:** It can wait, but a `[0.8, 2.0]` clamp is a one-line change; ship it with the next U4 pass so 1440p and 4K Store screenshots are not tiny.

## A2.6 What to fix for attempt 3

1. N2 (latch the notch) and N2b; this clears both failing gates.
2. G1: recompute the geometry (meter, info block, arc, intros, play menus).
3. N3 (new Lagging blip cue) and N4 (ring only with the skill).
4. Minor copy and focus items (N5, N6, N7, N9, N10).

SPEC-FAIL (score 84)


# Attempt 3

- Reviewer: Claude Sonnet 5.5 (author: Claude Opus)
- Spec attempt: 3 (last allowed)
- Date: 2026-10-27
- I re-read the whole spec and recomputed every geometry sum by hand (section A3.3). Contrast ratios are unchanged from attempt 2 and still match my script (danger 4.36 on `panel`, never used for text).

## A3.1 Score

| Row | Max | Score | Reasons |
|---|---:|---:|---|
| Visual tells | 20 | 18 | Clean. Same remaining nit as before: secondary-button borders are `line` (1.27:1, declared decorative). |
| Layout | 15 | 12 | Nearly every sum now checks out (A3.3). Remaining: the meter's 64 u inner height does not fit once the cluster border is counted (M1); the skill ring area is 64 u inside a block described as padding 8 (M7). |
| Copy / vocabulary | 15 | 14 | Tagline gone, name ellipsis rule, `Result = 1`, result lines, terminal numbers all fixed. Remaining: `Locked 4 s` is ambiguous next to `Exit Locked` (M3). |
| Every element answers state and action | 15 | 14 | Meter precedence, rescue approach prompt, disabled reasons all present. Countdown digits sit in S lines against the spec's own rule (M2). |
| Role-specific HUDs, no leak | 20 | 18 | Survivor side clean. Killer map now reads only `Role, Status, Flags.Signal, X, Z`, shuffles the wire order and leaves no traces; I could not find a decoy tell inside the UI (A3.4). Remaining tells are server-side and listed in Q8. Deduction: amber used for non-transmitting Afterimage entries (M4). |
| Interactive states, keyboard and gamepad | 10 | 9 | Field states, character pad with key states, popup layering and dismissal, Name before Join. Remaining: the popup opens on mere focus and hides the validation line (M8). |
| Contrast and 720p readability | 5 | 4 | True ratios, no danger text, 12 px S floor, wrap maths budgeted at 15 u. Hangul at 12 px is still marginal (Q10, Q13). |
| **Total** | **100** | **89** | Pass threshold 85, no gate failure. |

## A3.2 Gates

| Gate | Result | Evidence |
|---|---|---|
| V1 / V2 / V4 / V8 / V10 | PASS | §0.2–§0.5, §8. |
| Every element answers state and action | PASS | §4.1, §4.4, §4.8. |
| Role-specific HUDs and no forbidden leak | PASS | §6.3 (Id, Yaw, StatusTimer, CaptureCount, Afterimage never read; shuffled order; no trace on vanish). Survivor side: no Killer position or direction anywhere, level-3 cue hidden when Eliminated or Escaped, FirewallBlock X/Z is the exit, Survivor-bound PingScanner is 0,0. |
| Interactive states and keyboard/gamepad | PASS | §0.5 field row, §0.6 pad and popup, spatial focus. |
| **Buildable from protocol** | PASS | Every element is wire, static or marked derived. SkillEffect recipients match U2b rule 10. `capture_confirm` and `execute_confirm` use only Lagging status and the Killer's own position. No field outside the viewing role's data is used. |

## A3.3 Geometry recomputed

| Item | Spec | My result |
|---|---|---|
| Cluster §4.0 | 8+168+8+304+8+168+8 = 672 | 672 (42 % of 1600, 35 % of 1920; 538 × 58 px at 720p) |
| Info block §4.0 | inner 152; widest `Terminals 1/3` 129 u, `Exit Locked` 109, bar+`Locked 4 s` 107; 3 × 23 = 69 ≤ 70 | Correct (13 × 9.9 = 128.7) |
| Meter §4.1 | 24+20+4+16 = 64 inner with 4 u top and bottom padding | 64 sums correctly, but the cluster has a 1 u border inside its 72 u (the info block counts 70 u inner), so the meter has 62 u available (M1) |
| Meter reason width | 34 chars ≈ 282 ≤ 288 | Correct; all reasons are ≤ 31 characters |
| Skill block §4.8 | name column 168−8−64−8−8 = 80 u, `Emergency` 9 chars ≈ 75 u; 2×18+18 = 54 ≤ 56 | Correct. Ring 56 + arc 64 inside the 64 u area is correct; the area is 64 u in a "padding 8" block with 56 u inner, which the text resolves as 4 u top/bottom (M7) |
| Survivor intro §3 | inner 648; worst case 12+34+4+46+4+36+12 = 148 ≤ 160 | Correct (Stealth relay 86 chars ≈ 851 u, 2 lines) |
| Killer intro §3 | 12+34+4+23+4+54+4+54+12 = 201 ≤ 208 | Correct (91-character muted line wraps to exactly 3 lines of ≤ 33) |
| Survivor play menu §5 | 12+23+4+18+4+18+8+40+12 = 139 ≤ 152 | Correct; longest effect 68 chars ≈ 564 u ≤ 648 |
| Killer play menu §6.5 | 12+23+4+54+12+40+8+40+12 = 205 ≤ 208; x 1274 + 300 = 1574 ≤ 1600 | Correct |
| Killer column §6.1 | top 18+4+34+24+23+18 = 121; bottom 72+8+18+8+208 = 314; total 435 ≤ 852 (780 with band) | Correct; 24 + 300 ≤ 350 gutter; effect line 282 + 8 = 290 ≤ 300 |
| Killer skill block | 300−8−64−8−8 = 212; name ≈ 168, state line ≈ 158; 23+18 = 41 ≤ 56 | Correct |
| Main menu §1 | sum of the stack = 606 u ≤ 804 | Correct (66 + 86 + 66 + 34 + 56 + 68 + 96 + 94 + 40); gaps are all on the 4/8/16/24/32/48 scale |
| Lobby §2 | 34+8+18+24+348+12+18+24+48 = 534; right column x 640, width 400, role row 3×120+16 = 376 | Correct |
| Prompt row §4.3 | 672×40; `Mina is Lagging…` prompt with a 12-char name ≈ 624 u ≤ 648 | Correct |

## A3.4 Attempts to break §6.3 and §6.4

| Attack | Result |
|---|---|
| Use array order to pair entries with Ids | Blocked: the list is shuffled each snapshot; ties are random; nothing follows wire order (§6.3.2). |
| `CaptureCount` change on a stationary blip (White noise re-borrow) | Blocked: the field is never read, and the marker is removed (§6.2). |
| Afterimage ring tells real from decoy | Blocked: no ring at all; Afterimage-flagged entries are plain blips and expire like decoys. |
| Borrowed-Id jump | Looks like one blip vanishing (no trace) and another appearing, the same as two real Survivors. |
| Dummy decoy hands over to the real caster when the caster turns visible | The decoy blip and the real blip are within 0.4 m, so they match: a stationary blip starts to move. Same as a real Survivor. |
| Cues as a tell | `capture_confirm` and `execute_confirm` depend on Lagging status, which decoys never have. `blip_first` fires for decoys too. |
| Stationary Dummy blip or five static White noise blips | Not hideable in the UI; the spec says so in Q8(b) and proposes server changes. |
| Honest-player cue edge case | `execute_confirm` can misfire if an EmergencyRestore turns a Lagging blip Silent while the Killer is within 1.2 m (the server arms execution only after the Killer leaves contact). Harmless, no leak (M5). |

## A3.5 Status of attempt-2 findings

| Finding | Status | Notes |
|---|---|---|
| N2 `CaptureCount` not decoy-safe | **Fixed** | Marker removed, field never read (§0.1, §6.2, §6.3.1, §10). |
| N2b array order is Id order | **Fixed** | Shuffle plus random ties (§6.3.2). |
| G1 geometry | **Mostly fixed** | All the sums listed in G1 now hold, with 15 u / 18 u S budget and a 672 u cluster. Residual M1 and M7. |
| N3 `capture_confirm` misses ambush | **Fixed** | New unmatched Lagging blip within 1.2 m (§6.4), rate-limited. |
| N4 Afterimage ring / semantics | **Fixed** | Ring removed. Residual amber stretch (M4), now documented in §6.3.1 and Q8. |
| N5 menu rule line | **Fixed** | Removed. |
| N6 focus and popup | **Fixed** | Name before Join (§1.2), popup layering and dismissal (§0.6), pad key states. Residual M8. |
| N7 truncation | **Fixed** | §0.3 ellipsis rule, 12-character name budget in prompts, 400 u in lobby rows. |
| N8 `danger` / Hangul | **Partly fixed** | Time bars are `text` (§4.9, §6.1a). The Killer effect line still uses a `danger` bar for being slowed (§6.1c). Hangul at 12 px unchanged. |
| N9 empty name | **Fixed** | Sent empty (§1.2). |
| N10 Killer intro effect | **Fixed** | Effect line shown (§3). |
| N11 stutter flag | **Partly fixed** | Blend-only alternative, no border; still a launch flag until settings exist, with a Store-blocking requirement written in. Acceptable for a spec. |

## A3.6 Remaining findings

All minor; none blocks the pass.

[minor] M1 §4.1 vs §4.0 — Meter inner height — The meter's 64 u inner height needs 72 u of block, but the cluster border takes 1 u each side (the info block correctly uses 70 u). Two u over. — Row B 18 u (S line is 18 u): 24 + 18 + 4 + 16 = 62.

[minor] M2 §0.3 vs §4.1, §4.8, §6.1 — Rule broken by its author — §0.3 says countdowns and cooldown seconds are M or L, never S. The Lagging countdown, scan and overload seconds (§4.1 reasons), `Cooldown 7 s` and the Killer effect line seconds are S. — Move each number to an M word or amend §0.3 to "the primary number of a block".

[minor] M3 §4.7 — `Locked 4 s` — Alone it does not say what is locked, and it sits beside `Exit Locked` (not open yet), which means something else. The bar is the only cue. — `Exit Locked 4 s` without the bar is 149 u ≤ 152 u; the number already distinguishes it.

[minor] M4 §6.3.1 — Amber for non-transmitting entries — The palette says `signal` means "transmitting now". Afterimage-flagged blips are amber although they are not. The spec explains it as "reported by the server" and ties it to Q8, which is the right trade-off. — Add a one-line change to §0.4: for the Killer, amber means "the server is showing this".

[minor] M5 §6.4 — `execute_confirm` false positive — See A3.4. — Add: only when the Killer is also within 1.2 m and no ally Survivor blip is within 1.5 m of the vanished blip.

[minor] M6 Implementation note, not a spec fault — The current U4 client keys blips by Id (`SnapshotBuffer.Sample(int id, …)`, `ClientMath.Fade`). §6.3 requires replacing it with an anonymous matcher. The U5 brief must say so and the QA brief should test shuffled order and borrowed-Id jumps.

[minor] M7 §4.8 — "padding 8" vs a 64 u area — Inner height is 56 u; the ring area is 64 u (4 u top and bottom, per the text). — Say "padding 4 top and bottom, 8 left and right".

[minor] M8 §0.6 — History popup opens on focus — It covers the validation line and Join as soon as the Address field is focused, before the user chooses to edit. — Open it on Down or A only, or put the list beside the field.

[minor] M9 §6.1c, §0.4 — `danger` bar on being slowed — The Killer is not threatened by the Killer; palette meaning stretched. — Use `text`.

## A3.7 Opinions on the open questions that changed

- **Q8 (updated):** The new text is right. The best server fix is still per-snapshot shuffled indices in the Killer view, which makes decoy Id borrowing unnecessary. Until then the §6.3 client is as safe as a client can be. Add a QA test: replay a recorded snapshot stream in shuffled order and assert identical rendering.
- **Q12 stutter:** Acceptable now. Keep "settings toggle before Store" as a release gate.
- **Q13 S floor:** Keep 12 px. Raising `MinScale` to 0.9 remains an option; it is no longer needed for fit.
- **Q14 in-process Host:** Accept with the Firewall prompt and Store capability noted.
- **Q15 panel scale above 1080p:** Ship the `[0.8, 2.0]` clamp with the next U4 pass.

SPEC-PASS (score 89)


# Attempt 4 (camera revision)

- Reviewer: Claude Sonnet 5.5 (author: Claude Opus)
- Spec: `docs/ui/HUD_SPEC.md` with the camera revision (`git diff HEAD` read in full, then the whole file)
- Date: 2026-10-27
- Files read: `docs/ui/HUD_SPEC.md`, `docs/ui/SLOP_DEFINITION.md`, `docs/GDD.md`, `docs/briefs/U2b-a1.md`, `docs/briefs/U2c-a1.md`, `Shared/Runtime/Packets/{Client,Server}Packets.cs`, `Shared/Runtime/Game/{PlayerTypes,GameConstants,SkillIds}.cs`, `Server.Core/Runtime/Map/FixedMap.cs`, `Server.Core/Runtime/GameServer.{Visibility,Rules,Simulation}.cs` (to check Signal, Afterimage, decoy and execution-grace behaviour), and the earlier sections of this file.
- Method: I recomputed every new number by hand and with a small script (projection maths, word wrap at 35 S characters, block sums). The camera is judged against GDD §2, everything else against SLOP, as instructed.

## A4.1 Score

| Row | Max | Score | Reasons |
|---|---:|---:|---|
| Visual tells | 20 | 18 | Clean: flat fills, 1 u strokes, no glow (§6.2 says so), no emoji, the dither is functional. Deduction: through-wall markers and the 1 u amber outline sit on an arbitrary 3D background with no contrast backing, and the old "unlit flat materials" rule was dropped with the map (m5). |
| Layout | 15 | 11 | Camera framing and most block sums are right. Deductions: edge-indicator placement is not defined and on-screen markers can sit behind the cluster (M2); blocks 6.1a and 6.1c ignore their 1 u border, 6.1p needs off-scale padding, the normal-play cluster height is mis-summed (m1–m3). |
| Copy / vocabulary | 15 | 14 | New copy (`Wait 1.6 s, step back, touch again.`, `Lagging Survivor`) follows SLOP §4 and the fixed vocabulary; no "map" wording is left in player-facing text. Carried: countdown digits in S against §0.3, `Locked 4 s` on the Survivor side (m8). |
| Every element answers state and action | 15 | 14 | Each new element names its state and the action it prompts. The execution guidance can show the wrong action after a touch during grace (M3). |
| Role-specific HUDs, no leak | 20 | 16 | The Killer view reads only `Role/Status/X/Z`, matches by position, shares one model, and adds no sound or animation that separates decoys, except the terminal percent tag (M1). The Survivor view cannot show the Killer (A4.4). Deduction for M1 and the carried Afterimage amber stretch. |
| Interactive states, keyboard and gamepad | 10 | 9 | Unchanged and still complete; look/orbit rows added to the input map. No sensitivity setting yet (N6 covers it). |
| Contrast and 720p readability | 5 | 4 | Ratios unchanged; S 12 px floor held. World markers have no backing (m5). |
| **Total** | **100** | **86** | Threshold 85, no gate failure, no blocker. |

## A4.2 Gates

| Gate | Result | Evidence |
|---|---|---|
| V1 / V2 / V4 / V8 / V10 | PASS | §0.2–0.5; §6.2 "no glow"; no gradients, blur, emoji or default Unity look. |
| Every element answers state and action | PASS | §4.10 readability list, §6.1 table, §6.4 table. |
| Role-specific HUDs, no forbidden leak | PASS | Killer: Silent Survivors are omitted by masking rule 11 and nothing in §6 reads a field that would reveal one; M1 is a residual hint (terminal progress), not a leak of a Silent Survivor's position. Survivor: no Killer model, shadow, sound or marker (§4.10). |
| Interactive states, keyboard and gamepad | PASS | §0.5, §0.6 unchanged; the HUD has no focusable elements; menus are gamepad-reachable. |
| Buildable from protocol | PASS with M2 | All sources are Wire / Static / Derived and available to the viewing role (own `X/Z/Yaw`, `FixedMap.Walls`, Killer-bound blips, `Terminals`, own `SelfState`). Missing formula: edge-indicator placement (M2). |

## A4.3 Geometry recomputed

| Item | Spec | My result |
|---|---|---|
| §4.10 camera height / distance | 0.7 + 4.0 sin 20° = 2.07 m; 3.76 m | Correct (2.068 m, 3.759 m) |
| Feet and head on screen | feet ≈ 67 % (600 u), head ≈ 19 % | Correct. Perspective projection, vertical FOV 50°, 900 u canvas: feet 600 u (66.7 %), head 175 u (19.4 %) |
| Intro / prompt / cluster tops | 636 / 756 / 804 u | Correct: 900−24−72 = 804; 804−8−40 = 756; 804−8−160 = 636. Feet clear the intro by 36 u at the default pitch |
| Other pitches | "the cluster may cover the feet" | Not true inside the stated pitch range: feet fall between 523 u (+60°) and 622 u (−10°), always above 636 u. Only a shortened boom moves them out of frame, and that already triggers the dither. Harmless, but the sentence is wrong (m11) |
| Killer FOV | 90° horizontal at 16:9 = 59° vertical | Correct (58.7°) |
| §6.1 worst case | 208+8+48+8+64+8+26+8+72 = 450, top 426 u | Correct |
| §6.1 normal play | 186 u | Wrong: 64+8+26+8+72 = 178 (144 without the effect line). Conservative, but stated as a sum (m1) |
| 6.1a block | 23+23+18 = 64 in a 64 u block with a 1 u border | Inner height is 62 u: 2 u over (the Survivor cluster counts its border: 70 u inner). Same for 6.1c: 18+8 = 26 in a bordered 26 u block (m2) |
| 6.1p block | 2 S lines (36 u) in 48 u | Needs 6 u top/bottom padding, which is not on the spacing scale; the width check assumes padding 8 (m3) |
| 6.1p line 2 | ≤ 35 characters ≈ 290 u in 304 u | Longest is `Wait 1.6 s, step back, touch again.` = 35 characters = 289 u. Fits |
| 6.1d text area | 232 u | Correct; `Packet afterimage` 168 u, `Passive — always on` 158 u |
| 6.1c width | 282+4+4 = 290 ≤ 312 | Correct (34 characters) |
| §3 Killer intro | inner 296, 35 chars/line, 201 ≤ 208 | Wrap simulated: the muted line is 97 characters (spec says 96) and wraps to exactly 3 lines at 35; the longest effect line (`Passive: Survivors stay visible 1 s after their Signal ends.`) is 60 characters, not 65, and wraps to 2 lines. Sum 12+34+4+23+4+54+4+54+12 = 201. No overflow; the two counts are stale (m4) |
| §6.6 menu | 12+23+4+54+12+40+8+40+12 = 205 ≤ 208 | Correct |
| Overlap with the §9 band | top 48 u vs cluster top 426 u | No overlap with the cluster. Edge indicators inset 24 u at the top edge sit under the band while it shows (m7) |

## A4.4 Attacks on decoy safety and leaks

| Attack | Result |
|---|---|
| Tell decoys by facing | Blocked: facing comes only from matched position deltas; a never-moved instance faces the nearest terminal for real and fake alike (§6.3.5). Real stationary Signal Survivors away from terminals (rescuing, scanned, afterimage) still exist and decoys do not stand there; that is N7, a server residual |
| Tell decoys by animation or outline | Blocked: walk/idle from speed only, Lagging pose only for Lagging (decoys are never Lagging), identical outline, no hack or rescue animation (N2) |
| Track identity across snapshots | Blocked: greedy 0.4 m matching on positions only, shuffled input, no spawn or despawn animation, one shared model and material (N1) |
| Edge indicators "6 nearest" | Safe: ranking uses distance only, so decoys and real blips rank alike. The cap only hides some blips |
| Sounds | Safe: `blip_first`, `capture_confirm`, `execute_confirm` use count, Lagging status and the Killer's own position; no positional audio (N3) |
| Terminal `40%` tag (§6.2) | **Tell** (M1): live progress cross-checks a stationary blip at a terminal (decoys do not raise it) and shows a Stealth-relay hacker, who is Silent, as a progress jump with no blip |
| Execution guidance | Not a tell, but wrong in one case (M3) |
| Silent Survivor revealed in front of the camera | Not possible: no entry, no shadow, no sound, no marker. An ambush capture spawns a Lagging character in place (§6.5), as before |
| Survivor peeks around a corner and sees the Killer | Not possible: no Killer position is received, and §4.10 forbids a model, shadow, footstep or marker. `C2S_MoveInput.Yaw` = facing, and orbiting sends no direction, so it does not trigger Signal (checked in `GameServer.Simulation.cs`: Signal needs a non-zero direction) |
| Survivor-bound effects | Unchanged from U2b rule 10; PingScanner X/Z = 0,0 and FirewallBlock = exit |

## A4.5 Findings

No blocker.

[major] M1 Terminal percent tag is a decoy and Stealth tell — §6.2 Terminals (and §10) — The Killer sees `40%` on every incomplete terminal through walls. U2c decoys stand still "hacking" at terminals (U2c brief §3) but never raise the percentage; a real hacker does. Conversely, a Stealth-relay Survivor hacks while Silent (2 s = a 20 % jump) and the Killer can read it. This is information beyond positions that the UI makes trivial to use. — Show only `Terminal` (and a done state) on the Killer's terminal markers; keep the completed count in 6.1a. Record the wire-level residual (`Terminals[].Progress` is sent to the Killer) as a new open question N9.

[major] M2 Edge-indicator and marker placement are not defined — §6.2 Off-screen, §4.6 — "pointing along the direction to the blip" gives no rule for blips behind the camera, above or below the view, or for how the direction maps to a point on the screen edge. Behind the Killer is the case that matters most. Also, an on-screen marker or character can sit behind the bottom-left cluster (the draw order puts the cluster on top), where it has neither a marker nor an edge indicator. — Specify: angle = signed yaw difference between view yaw and the direction to the blip; place the chevron where a ray from the screen center at that angle meets the screen rectangle inset by 24 u (behind = bottom edge); treat anything whose projected point falls inside the cluster rectangle as off-screen. Apply the same rule to §4.6.

[major] M3 `away` latch ignores the server's re-arm rule — §6.4 — In `GameServer.Rules.cs` a touch during the grace sets `ExecutionArmed = false`, and leaving contact sets it true. If the Killer steps back, then touches again inside the 2 s, the spec's `away` stays true and shows `Ready` / `Touch again to eliminate.` while the server will not execute until the Killer leaves and re-enters. Also, during grace a Killer who already stepped back is told to `step back`. — Define `away` as: reset to false on any tick the Killer is within 1.2 m of the instance while `now − t0 < 2 s`, set to true on any tick it is outside 1.2 m. Grace line 2 when `away` is true: `Wait 1.6 s, then touch again.`

[minor] m1 §6.1 — Normal-play height — "186 u" is 64+8+26+8+72 = 178 u (144 u without the effect line). Also "every block except the last two is shown only when it applies" is wrong: 6.1a and 6.1d are always shown, and 6.1c hides when no effect applies. — Fix the sum and the sentence.

[minor] m2 §6.1 — Border and block heights — 6.1a (64 u) and 6.1c (26 u) hold content of exactly their height but carry a 1 u border; inner heights are 62 u and 24 u. The Survivor cluster counts its border. — Make them 66 and 28 u (worst case 454 u, top 422 u, still below the band) or state that borders are drawn outside the block.

[minor] m3 §6.1 — 6.1p padding — 48 u for two 18 u lines needs 6 u vertical padding; the scale is 4/8/12. — Use 52 u (padding 8/8) and update the sums.

[minor] m4 §3, §6.1 — Stale counts — The muted Killer line is 97 characters, not 96; the longest Killer effect line is 60 characters, not 65. Wrapping still gives 3 and 2 lines. — Correct the numbers.

[minor] m5 §6.2, §0.4 — World markers have no contrast backing — The 16 u `signal` diamond, the 1 u amber outline and the `text` rings sit on whatever the 3D scene shows, and the earlier "unlit flat materials" rule was dropped with the map. — Add a 1 u `bg` stroke around diamonds and chevrons, state that gameplay geometry uses flat unlit materials, and require a 3:1 check of marker colors against floor and wall colors.

[minor] m6 §6.2 — Model visibility per class — The spec classifies by one ray at 1.2 m but does not say whether the character model is hidden in the `occluded` and `off-screen` classes. At a wall corner the model can show without an outline while the 100 ms hold keeps the class, or show beside its diamond. — State that models are always depth-tested, the outline is drawn only for the `in view` class, and the diamond is hidden while the character's screen bounds are more than 50 % visible.

[minor] m7 §9, §6.2 — Band and top-edge indicators — The 48 u connection band covers indicators at the top edge (inset 24 u). — Inset top-edge indicators below the band while it shows.

[minor] m8 §0.3, §4.7, §6.5, §6.1c — Carried findings — `Wait 1.6 s` (S) is another countdown in S against §0.3's own rule; the Survivor exit line `Locked 4 s` is still ambiguous next to `Exit Locked`, while the Killer marker uses `Exit Locked 4 s`; `execute_confirm` can misfire if an Emergency restore turns a Lagging instance Silent while the Killer is within 1.2 m; the Killer's slow bar still uses `danger`. — Same fixes as attempt 3 (M2, M3, M5, M9).

[minor] m9 §6.2 — Killer-side reconnect fallback — The Survivor "locked by the Killer" row falls back to `Exit Locked` when no FirewallBlock effect was received; the Killer marker's `Exit Locked 4 s` has no such fallback after a reconnect. — Add the same fallback.

[minor] m10 §6.3.5 — Facing threshold — Facing updates on a matched displacement ≥ 0.05 m, but a Lagging Survivor moves exactly 1.0 m/s × 0.05 s = 0.05 m per tick, so float noise can freeze its facing. — Use ≥ 0.03 m.

[minor] m11 §4.10 — Wrong sentence — "At other pitches or with a shortened boom, the cluster may cover the feet" is untrue for pitch alone (A4.3). — Say "with a shortened boom the feet leave the frame; the dither keeps the view clear".

[minor] m12 §11 Q7, §10, §6.2 — Cross-references and index — Q7 points to §6.4 for "no cue for eliminating"; that text is now §6.5. §10 has no row for the Survivor camera (own `X/Z/Yaw`, `FixedMap.Walls`). N8 is not referenced from §6.2. — Fix the three references.

[minor] m13 §6.2 — Far characters get less help than hidden ones — An in-view Survivor at 50 m is a figure about 40 u tall with a 1 px outline and no distance tag, while an occluded one gets a 16 u diamond and a tag. The map has only four interior walls, so this is the common case. — Add the distance tag (and optionally the diamond) to in-view Survivors beyond 25 m.

## A4.6 What is good

- The camera framing is correct and checked: the character stays above every bottom-center block at the default pitch.
- Movement is relative to camera yaw while `Yaw` is the character's facing, so orbiting never makes a Survivor Signal. The server code confirms it.
- The Killer view is built on anonymous blips with one shared model, position-only matching and no per-instance sound, animation or facing that could separate decoys. Hack and rescue animations are rejected (N2).
- The new open questions (N1–N8) are honest and point at the right owners, especially N4 (SLOP lines), N7 (stationary decoys) and N8 (occlusion source).
- Grace guidance is derived from observable events only, with a safe fallback when `t0` is unknown (N5).
- The cluster arithmetic is mostly right and conservative; the worst case of 450 u leaves the top edge below the screen center.

## A4.7 Fix order

1. M1 (hide the percent), M2 (edge-indicator rule), M3 (`away` latch).
2. m1–m4 (sums and counts), m12 (cross-references).
3. m5–m7 and m9–m13 as time allows; they do not block the pass.

SPEC-PASS (score 86)


# Attempt 5 (N1–N9 decisions, positional sound)

- Reviewer: Claude Sonnet 5.5 (author: Claude Opus)
- Date: 2026-10-27
- Scope: quick re-review of `git diff a4b8f56 -- docs/ui/HUD_SPEC.md docs/ui/SLOP_DEFINITION.md`: §0.7, new §6.5.1, §6.4, §6.2 terminals, §10, §11.1, header changelog, and the SLOP §6 / §8 role lines. Cross-checked against `docs/GDD.md` §2, `docs/briefs/U2c-a1.md`, `Shared/Runtime/Game/GameConstants.cs`, `Server.Core/Runtime/GameServer.{Rules,Simulation,Visibility}.cs`. Rows with nothing changed are carried from attempt 4; I also spot-checked that the attempt-4 fixes (M1 percent removed, M2 placement rule, M3 re-arm, m1–m3 heights, m10 0.03 m, keylines, band offset) are present in the spec.

## A5.1 Score

| Row | Max | Score | Reasons |
|---|---:|---:|---|
| Visual tells | 20 | 19 | Carried 18, +1: the marker keyline fix from A4 m5 is in. No new visual element; the sound section adds none. |
| Layout | 15 | 12 | Carried 11, +1: edge-indicator rule and cluster sums fixed. Typo left in 6.1a (m5 below). |
| Copy / vocabulary | 15 | 14 | Carried. `Wait 1.6 s` is still a countdown in S (A4 m8), now a live server value. |
| Every element answers state and action | 15 | 15 | Re-arm rule (A4 M3) is fixed and now server-sourced; each §6.5.1 sound maps to a visible state. |
| Role-specific HUDs, no leak | 20 | 17 | The positional sound is decoy-safe by construction (A5.2). Deductions: the normative §6.3 rule 4 still forbids per-entry sound (M1); voice cap and edge-indicator cap can disagree (m2). |
| Interactive states, keyboard and gamepad | 10 | 9 | Carried. |
| Contrast and 720p readability | 5 | 4 | Carried. |
| **Total** | **100** | **90** | Threshold 85, no gate failure, no blocker. |

## A5.2 Attacks on decoy safety and leaks (§6.5.1, §6.4)

| Attack | Result |
|---|---|
| Speed estimate `v` (mean of last 4 matched displacements ÷ 0.05 s) | Safe. Computed from on-wire X/Z only. U2c phantoms move through `Map.Move` at `SurvivorSpeed`, so v matches real Survivors (4.0 m/s, or lower when wall-sliding). Residual: a decoy that only ever walks at exactly 4.0 m/s differs from a gamepad Survivor at partial stick. That is a speed feature already covered by the U2c indistinguishability report and is visible in the animation too. |
| Cadence, phase, pitch, sample variant | Safe. RNG is client-side and not seeded from wire data; first step is delayed by `[0, interval)`; cadence is a function of v and Status only. |
| Silent Survivor audible | Not possible. No entry on the wire, so no instance, no voice. Despawn cancels pending steps. A Lagging Survivor is always visible server-side (`IsVisibleToKiller`), so Ragged breathing cannot reveal a Silent one. |
| Hackers and rescuers (stationary Signal) | Safe. `IsVisibleToKiller` returns true while `Hacking` or `Rescuing`, so real stationary Signals exist and Calm breathing is the same for them and for a phantom at a terminal. |
| Afterimage entries | Safe. The entry stays at the last seen position (`ShowsAfterimage`), so v decays to 0 over 4 ticks and the instance breathes Calm, like any stationary blip. Nothing about the Silent Survivor's real movement is audible. Same residual as the visual afterimage tail (U2c report). |
| Voice cap (4 nearest) and hysteresis | Safe for identity: ranking uses distance only; hysteresis state is per instance and depends only on distance. It does let a voiced instance fall outside the 6-nearest edge-indicator set (m2). |
| Occlusion low-pass, despawn fade | Safe. Same ray and same 50 ms fade for every instance. |
| Breathing variants | Safe. Variant = f(Status, v). Decoys are never Lagging. |
| `ExecutionGraceLeft` (§6.4) | Safe. It describes the nearest Lagging Survivor within 3 m, who is always visible and whose capture time the Killer already saw. It is 0 in a Survivor's own copy of `S2C_SelfState`. No Silent state is involved. |
| `Progress` as 0 or 100 | Safe. The Killer learns only "done", which both roles already see in the done count. |

## A5.3 Checks

- Cadence: Normal 1.2 m ÷ 4.0 m/s = 0.30 s, inside the 0.25–0.9 clamp. Lagging speed = 4.0 × 0.25 = 1.0 m/s, so 0.8 m ÷ 1.0 = 0.8 s, inside 0.5–1.5. Both match the table. Walk threshold 0.5 m/s matches §6.3 rule 5.
- Grace text: `Wait 1.6 s, step back, touch again.` is 35 characters, so it still fits S (≈ 290 u in 302 u).
- Re-arm logic matches `GameServer.Rules.cs` Capture(): not in contact sets armed; in contact during grace clears it; in contact with armed set and grace over eliminates.
- SLOP edits: three lines only (§6 camera line, two §8 role lines), consistent with GDD §2 (Survivor third-person orbit with the Killer never drawn; Killer first person, Signal as character or through-wall marker, Silent shows nothing). The audio rules in SLOP are untouched.
- §0.7 exemption: sound in principle (scene audio from a character, bounded by 4 voices and by the "shows nothing the visuals do not" guard; SLOP §5 only restricts hover sound and §8 only asks for a steady indicator for continuous states). It is a spec-local carve-out with no SLOP counterpart, and its wording is wider than its one use (m3).

## A5.4 Findings

No blocker.

[major] M1 Rule 4 contradicts §6.5.1 — §6.3 rule 4, §6.3 rule 1 — Rule 4 (normative decoy-safety text) says "no per-instance identity anywhere: no names, colors, numbers, cosmetics or sounds per entry", yet §6.5.1 gives each instance its own footstep and breathing voices. An implementer following §6.3 could drop or randomise the sound, or a reviewer could fail it. — Reword rule 4 to "no per-instance identity: …; the only per-instance sound is §6.5.1, a function of position, matched speed and `Status`", and add §6.5.1 to rule 1's list of readers of `X/Z/Status`.

[minor] m1 Audio formulas incomplete — §6.5.1 — "log rolloff" has no min/max distance or curve; "silent beyond 20 m / 6 m / 8 m / 10 m" is a hard cutoff that will click, and at inverse-distance rolloff the breathing is already inaudible well before 6 m (−30 dB at 2 m is about −39 dB at 6 m, so the quoted ranges mean little); dB values have no stated reference; the step timer is not defined (recompute the interval from current v each step, or accumulate dt ÷ interval(v)?); source height is not given; the occlusion filter has no 100 ms hold or crossfade although the visual class has one; the 0.5 m/s Calm/Heavy and walk threshold has no hysteresis. — State the rolloff (inverse distance, min 2 m, linear fade over the last 2 m before max), the dB reference (master bus), source height 1.0 m, an accumulating step timer, a 100 ms occlusion crossfade, and enter 0.6 / exit 0.4 m/s hysteresis for the speed threshold (v-only, so decoy-safe). Set the breathing ranges where the rolloff reaches an audible floor.

[minor] m2 Voice cap versus edge-indicator cap — §6.5.1, §6.2 Off-screen — Voices go to the 4 nearest with a 1 m hysteresis, edge indicators go to the 6 nearest. In a White noise burst (5 points close together) a voiced instance can rank 7th and have a sound but no marker, which breaks §0.7's "never encodes anything the visuals do not show". — Rank both caps with one function, or drop the hysteresis when the instance would fall outside the marker set.

[minor] m3 Diegetic exemption is wider than its use, and SLOP does not mention it — §0.7, SLOP §8 Feedback — "Positional sound emitted by a character in the 3D scene" admits any future world sound, and "only while its source is on the wire and visible" conflicts with off-screen and occluded instances that sound. SLOP's feedback line still says continuous states have a steady indicator only. — Say "the only diegetic world sound is §6.5.1" and "shown in any class of §6.2"; add one line to SLOP §8 Feedback (needs user approval, as with N4) so the two documents agree. Also note that the 50 ms fade is the only tail ("exactly as its model disappears" is not literally true).

[minor] m4 Mix versus priority — §6.5.1, §0.7 — World sound is "lowest priority" but footsteps peak at −14 dB per voice (4 voices at about 0.3 s cadence), louder than `ui_join` at −18 dB and close to `blip_first` at −12 dB, and the duck applies only under `capture_confirm` and `execute_confirm`. `exit_open`, `skill_ready` and `time_60` are not ducked. — Duck under every §6.5 cue, or lower the footstep peak to about −20 dB.

[minor] m5 Arithmetic and stale references — §6.1a, header, §6.2, §6.3, §6.5 — 6.1a says `1 + 23 + 23 + 23 + 1 = 70`; it is 71 (still ≤ 72). The changelog still says "New open questions N1–N8 in §11.1" and the Korean summary says the new questions are in §11.1, but §11.1 now records decisions N1–N9. `§11 N1`, `§11 N2`, `§11 N6` (lines 353, 387, 427, 475) point to the Q table; they should be `§11.1 N#`. — Fix the sum, reword both statements, and update the references.

[minor] m6 U2c brief lacks the new wire changes — §6.4, §6.2, §11.1 N5/N9 — `docs/briefs/U2c-a1.md` lists `S2C_SelfState { SkillCooldown, RescueProgress, InvulnerableLeft }` and says it is "never about another player"; it has neither `ExecutionGraceLeft` (which concerns another player, the Lagging target) nor the Killer `Progress` quantisation to 0 or 100. The spec correctly says it cannot be built until U2c. — Add both to the next U2c brief and harness (mask check: Killer `Progress` in {0, 100}); reword "never about another player" to "never about a hidden player".

[minor] m7 Carried — §6.4 — `Wait 1.6 s` is a live countdown in S, against §0.3's rule that countdowns use M or L (A4 m8). — Put the seconds in an M run or record it as an accepted exception.

[minor] m8 GDD not updated for N3 — docs/GDD.md §2 — The positional footsteps and breathing exist only in HUD_SPEC §6.5.1 and §11.1; the GDD "보이는 것" row for the Killer does not mention sound. — Add one line to GDD §2 so design and UI agree.

## A5.5 What is good

- The positional sound uses exactly the inputs the Killer view already treats as safe (position, matched speed, `Status`) and nothing else, so a phantom walking at `SurvivorSpeed` sounds like a real Survivor by construction, and Silent Survivors cannot sound.
- The cadence and Lagging numbers check out against `GameConstants` (0.30 s and 0.8 s).
- `ExecutionGraceLeft` and the derived `armed` flag mirror the server's re-arm rule, with a safe default.
- The SLOP edits touch only the three role/camera lines and match GDD §2.

## A5.6 Fix order

1. M1 (rule 4 wording).
2. m1, m2, m3, m5.
3. m4, m6–m8 as time allows; none block the pass.

SPEC-PASS (score 90)
