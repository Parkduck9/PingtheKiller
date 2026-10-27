# UI/UX "Slop" Definition — Ping the Killer

> **한국어 요약:** "슬롭"은 *아무 게임에나 붙여도 되는* UI다. 이 게임의 규칙(패킷을 보내야만 보인다)에서 나온 결정이 하나도 없고, AI·템플릿·엔진 기본값이 "그럴듯해 보이게" 채운 흔적이다. 아래 목록은 **금지 목록(체크리스트)**이며, UI 작업물은 리뷰 때 이 항목 기준으로 감점된다. 맨 아래 "이 게임의 대안 방향"이 실제로 따라야 할 기준이다.

This document is context for every agent that touches UI, HUD, menus, or copy.
Step 1 of the UI process is this definition. Step 2 is: **build the UI while rigorously excluding everything listed here.** A UI review scores against Section 9.

---

## 1. Core definition

**Slop = UI that could be pasted into any other game without anyone noticing.**

It is produced when the author optimizes for *"looks like a finished product at a glance"* instead of *"communicates this game's state to this player at this moment."* Slop has three roots:

1. **Default-filled**: engine/template/AI defaults left in place because nothing forced a decision (Unity default sliders, Arial/LiberationSans, centered-everything layouts, 16 px rounded cards).
2. **Decoration without information**: elements that exist to look "premium" (gradients, glows, glass blur, particles, drop shadows) but encode no game state.
3. **Generic voice**: copy and iconography that describe a mood ("Epic!", "Unleash your power") instead of a fact the player can act on.

The test for any element: **"What game state does this encode, and what does the player do differently because they saw it?"** If the answer is "nothing / it looks nice", it is slop.

---

## 2. Visual tells (forbidden)

| # | Tell | Why it is slop |
|---|------|----------------|
| V1 | Purple→blue / pink→orange gradients on panels, buttons, or titles | The single most recognizable AI-generated palette; carries no meaning |
| V2 | Glassmorphism (frosted blur panels) over gameplay | Decorative, hurts readability over moving 3D, costs GPU |
| V3 | Neon glow / outer-glow on every text and icon | When everything glows, nothing is highlighted |
| V4 | "Hacker terminal" cliché: green monospace on black, Matrix rain, fake hex dumps, glitch-shader on everything | The obvious reading of "network game"; zero thought. Network theme must come from *mechanics*, not costume |
| V5 | Uniform 12–16 px rounded corners on every container | Template look; no hierarchy |
| V6 | Soft drop shadows under every panel/card | Fake depth; muddies edges at 1080p |
| V7 | More than one accent color competing (cyan + magenta + gold + red) | No color means anything anymore |
| V8 | Emoji used as icons (🔪 🏃 ⚡ 🎯) | Platform-dependent rendering, unserious, unscalable |
| V9 | Generic icon-pack glyphs with no relation to the mechanic (lightning bolt = "skill", star = "score") | Icon must depict *this* skill's effect |
| V10 | Default Unity look: grey sliders, white `Button` sprite, LiberationSans/Arial, default TMP font | Signals "unfinished" more than plain boxes do |
| V11 | Decorative particles/animated backgrounds in menus | Motion with no state change trains players to ignore motion |
| V12 | Low-contrast grey-on-grey text "for elegance" (< 4.5:1) | Unreadable over a 3D scene and in Store screenshots |

## 3. Layout tells (forbidden)

| # | Tell | Why it is slop |
|---|------|----------------|
| L1 | Everything centered, stacked vertically, same width (the "landing page" column) | Web template, not a game screen; no reading order |
| L2 | Card grid for things that are not comparable items (e.g. settings as cards) | Structure chosen for looks, not data |
| L3 | Equal visual weight for primary and secondary actions ("Play" and "Credits" same size) | Player has to read everything to find the one action |
| L4 | HUD elements in all four corners plus the center | Eye has to scan 5 zones during a chase |
| L5 | Modal popups in the center of the screen during play | Blocks the exact area the player is watching |
| L6 | Arbitrary spacing (8, 13, 20, 27 px) | No grid = visual noise; use one spacing scale |
| L7 | Scrollable "wall of text" tutorial screen before first match | Nobody reads it; teach through play |
| L8 | Huge hero title + tagline on the main menu taking 50% of the screen | Marketing-page reflex; the menu is a tool |

## 4. Copy / voice tells (forbidden)

- Hype verbs and adjectives: *Unleash, Epic, Ultimate, Seamless, Immersive, Dive into, Embark, Get ready to…*
- "Welcome to Ping the Killer!" / "Are you ready?" / "Let's go!" filler.
- Exclamation marks on system messages (`Connected!`).
- Vague errors: *"Something went wrong."* → must say what and what to do (`Server not reachable at 192.168.0.10:7777 — check that the host is running.`).
- Title Case Everywhere On Every Label.
- Inconsistent terms for the same thing (exit / escape / goal / portal). One term per concept, defined in Section 8.
- Lorem-ipsum-shaped placeholder that "sounds right" but is not true to the rules.

## 5. Interaction / motion tells (forbidden)

- Every button scales up 1.1× + plays a whoosh on hover. Hover feedback must be uniform and subtle; sound is reserved for state changes.
- Easing everything with long (> 250 ms) bouncy tweens; menus must respond in ≤ 1 frame and finish transitions in ≤ 150 ms.
- Screen shake / flash for non-critical events.
- Loading spinners with no text when the wait has a known cause (connecting, waiting for players).
- Toasts that stack and cover the HUD.
- Buttons with no disabled / pressed / focused states (also required for gamepad & keyboard navigation for Store).

## 6. Game-specific tells (forbidden)

- **Information the rules say the player cannot have.** The killer's HUD must never hint at hidden survivors (no "3 survivors nearby" counter unless a skill grants it). Leaking state = slop *and* a design bug.
- Skill buttons that look identical for every skill; skills differ by icon only.
- Cooldown shown only as a number with no radial/bar.
- Showing the same HUD to killer (top-down) and survivors (first-person). The two roles have different cameras and different questions; they need different HUDs.
- Kill feed / score popups borrowed from shooters ("+100 FREEZE!").
- Minimap for survivors "because games have minimaps" — it would undermine the hide-by-not-moving tension unless designed deliberately.
- Victory/defeat screen as a generic "VICTORY" banner with confetti.

## 7. Process tells (how slop gets made — forbidden practices)

- Generating the whole UI in one pass from "make a modern game UI".
- Styling before the state list exists (what the player must know, per role, per phase).
- Adding an element because "it feels empty".
- Leaving a placeholder in a screenshot that goes to the Store listing.

---

## 8. Alternative direction for THIS game (what to do instead)

The theme comes from the **one rule**: *you are visible only while you transmit.* UI language should be **signal vs. silence**, expressed through mechanics, not costume.

**Vocabulary (fixed terms):** Killer, Survivor, Signal (= being visible), Silent (= hidden), Lagging (= captured once, slowed, always visible), Eliminated, Escaped, Rescue, Terminal, hack (verb), Exit, Locked (exit), Skill, Cooldown, spectate (verb).

**Palette (max 4 + neutrals):**
- Neutrals: near-black `#101113`, panel `#1A1C1F`, line `#2C2F33`, text `#E8E6E1`, muted text `#9A9890` (≥ 4.5:1 on panel).
- `Signal` amber `#FFB020` — used *only* for things that are currently transmitting/visible.
- `Danger` red `#E5484D` — killer-related threat only.
- `Ally` teal `#3FB8AF` — survivor team info only.
- No gradients. Flat fills, 1 px lines.

**Type:** one family with tabular numerals (e.g. Inter / Pretendard for Korean coverage), 3 sizes only (14 / 18 / 28 at 1080p), weight for hierarchy, not color.

**Shape & spacing:** 4 px spacing scale (4/8/12/16/24/32). Corner radius 2 px or 0. Hierarchy from size, position, and the single accent — not shadows.

**Role-specific HUD:**
- *Survivor (first-person):* bottom-center only. One **transmit meter** showing whether you are currently Signal or Silent (the core tension), skill + cooldown ring, frozen-ally direction markers at screen edge. Nothing else.
- *Killer (top-down):* the map *is* the HUD. Survivor blips appear only as Signal, then fade per server timer (afterimage skill lengthens the fade). Skill + cooldown at bottom-left; exit lock state on the exit itself, not in a panel.

**Feedback:** state changes get sound + one visual change; continuous states get a steady indicator, never a looping animation.

**Menus:** left-aligned list, one primary action visually dominant (Host / Join), IP field with validation message in plain language, lobby shows 8 fixed slots (player / bot) — the slot grid *is* the data.

---

## 9. Review checklist (used by QA / Opus review)

Score each UI deliverable. Any **gate** failure = reject regardless of score.

| Check | Points | Gate |
|-------|-------:|:----:|
| No V1–V12 visual tells present | 20 | V1, V2, V4, V8, V10 are gates |
| No L1–L8 layout tells; one spacing scale used | 15 | |
| Copy follows Section 4 and fixed vocabulary | 15 | |
| Every element answers "what state, what action" | 15 | ✔ |
| Role-specific HUDs; no forbidden information leak (Section 6) | 20 | ✔ |
| All interactive elements have normal/hover/pressed/disabled/focused states; keyboard & gamepad navigable | 10 | ✔ |
| Contrast ≥ 4.5:1 for text; readable at 1280×720 | 5 | |
| **Pass threshold** | **≥ 85 / 100 and no gate failures** | |
