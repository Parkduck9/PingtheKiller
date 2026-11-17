# M1-minors QA a1 (Fable)

Reviewed: commit 2341859 on `claude/m1-minors` (base main 7b23663). Implementer: Claude Sonnet. Reviewer: Fable. No product code edited.

**Verdict: PASS**, with 4 minor findings and 2 optional improvements. No blocker, no major.

## Spec items

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | R1 M2: unseen / eliminating hit shows as a hit; never a hit the server did not make | PASS | `LateHit` now plays the kick and the `Hit` line; the eliminating hit shows `Hit` too. No path found that shows `Hit` on a real miss (section a). Not seen live: no unseen hit occurred in 240 s. See F1, F2. |
| 2 | R1 M3: `Missed` timing after a Silent vanish | PASS | Keeping the 1.0 s wait is correct. A no-vanish miss still reads at 0.55 s. Safe sooner rules do exist (O1); the doc sentence saying none exists is too strong (F4). |
| 3 | Gamepad Start, A, B dismiss the intro card; it never returns | PASS | By code read and EditMode tests; no gamepad hardware used. |
| 4 | Indexed `for` in `NearestSwingableBlip` / `NearestNormalBlip` | PASS | Both loops indexed; source test passes. |
| 5 | HUD_SPEC 6.1f and section 3 marked `(11/18, M1)` | PASS | Both present. See F4. |

Diff scope: client scripts, one EditMode test file, docs. No `Shared/`, `Server.Core/` or protocol change.

## Gates (re-run by QA)

| Gate | Result |
|------|--------|
| `dotnet build PingTheKiller.sln -warnaserror` | 0 warnings, 0 errors |
| `dotnet test Server.Tests --filter "Category!=BalanceSweep"` | 1147 passed, 0 failed, 0 skipped |
| Harness, fresh servers on 7795 (sandbox) / 7796 (H6) | 100/100, gatesPassed True (`M1-minors-qa-a1/harness.json`) |
| Unity EditMode (absolute results path) | 1615 total: 1613 passed, 0 failed, 2 skipped; all 7 `M1MinorsTests` passed (`M1-minors-qa-a1/unity-editmode.xml`) |
| Live Killer run, existing exe, 1280x720, 7 bots, `-ptk-chase 230`, 240 s | 16 swings. Server: 4 hit, 12 miss, 0 interrupted. Client: 16 wind-ups, 4 hit, 12 Missed, 0 late. Exact match, 0 exceptions (`M1-minors-qa-a1/live-killer.log`) |

The exe is fresh by content: sources last written 01:01, player DLLs built 01:14, tree clean at 2341859, `ConsumeBack` present in `PingTheKiller.Client.dll`. After the Unity run `DOTweenSettings.asset` differed in line endings only and was reverted. The servers and the player I started have exited.

## (a) Can `Hit` show on a real miss?

`Hit` needs a flag fall with a length over 4 and at most 17 ticks. Checked against `Server.Core/Runtime/GameServer.Combat.cs`:

- Server time advances by a fixed dt per tick, so a hit is exactly 16 ticks and a miss exactly 20 (pinned by `TheOwnSwingingFlagLastsSixteenTicksAfterAHitAndTwentyAfterAMiss`).
- Stun in the wind-up: the interrupt check runs before the hit check, so the flag is at most 4 ticks. Not a hit.
- Stun in the recovery: the swing is not cut; the length stays 16 or 20.
- Vault, pull, break: cannot start while swinging, and a swing cannot start during them.
- Match end: `EndMatch` sets `playing = false` and later WorldStates are ignored; `MatchStart` resets the watch.
- Dropped or reordered ticks: TCP, and `link.Pump()` handles every WorldState in order; the tick is read from the packet. A tick regression gives a negative length, which is not a hit. A false hit would need the rise seen 3 or more ticks late, which this client cannot produce.
- Chained swings (F2): the merged flag is 32 ticks or more, so it never reads as a hit.

`Missed` then `Hit` after about 0.25 s: acceptable. It only happens on a hit with no visible blip, and holding every verdict to the flag instead would delay every `Missed` to 0.9 to 1.0 s. Not observed live. O2 would remove most of these flashes.

Copy and slot: `Hit` is one plain word, no exclamation mark, pairs with `Missed`, `text` tone, in the 6.1c 320x32 line. No score-popup styling. SLOP Section 9 for this change: no gate failure.

## (b) Intro card input

- Esc or Start on the card: consumed, card dismissed, menu stays closed. Once the card is gone `ConsumeBack` returns false, so Start and Esc open the menu as before. Nothing is swallowed.
- B is Back only while the menu or Settings is open (`cancel` needs one of them); the card never shows under the menu, so the two cannot collide.
- A in menus goes through UI Toolkit navigation, untouched. In play nothing else is bound to A or B.

## (c) Masking

No new data reaches the Killer: no server or `Shared/` change. `Hit` uses the own flag's length, already on the wire. A hit Survivor is Lagging (always visible) or Eliminated, so the line reveals nothing the rules hide. Harness H3 and H6 leak scans pass.

## Shots looked at

- `M1-minors-qa-a1/shots/r1-killer-miss.png`: `Missed` in the 6.1c slot, plain text, no impact. Correct.
- `M1-minors-qa-a1/shots/r1-killer-hit.png`: first hit, impact and `Lagging` tag, no line. Correct.
- `M1-minors-qa-a1/shots/u8-killer-capture-2-strike.png`: hit 0.85 s after a miss, `Lagging` tag, line already gone.
- `M1-minors-qa-a1/shots/play-1s.png`: Killer intro card, bottom-centre, four lines, clear of the cluster.
- `M1-minors/shots/r1-killer-miss.png` (implementer): `Missed`, correct.
- `M1-minors/shots/r1-killer-hit.png` (implementer): shows F1.

## Findings

### F1 (minor, pre-existing, visible): a stale `Missed` stays up over the next swing's confirmed hit
- Repro: miss, then swing again at once and hit a visible Survivor. Implementer run: `Missed` at t=36.92, server hit at t=37.77.
- Expected: no `Missed` once the next swing has hit.
- Actual: `Missed` stays for its full 1 s, next to the impact and the `Lagging` tag.
- Evidence: `docs/qa/M1-minors/shots/r1-killer-hit.png`; `docs/qa/M1-minors/live-killer.log` lines 209-223.
- Cause: only the new `KillerFxState.OnHit` clears `missedEnd`. The capture-confirm hit path (`ClientApp`, `swingWatch.OnCapture`) does not, and neither does a new swing's start.
- Fix: clear the `Missed` line when a new swing starts (`SwingEvent.Started`) or on the capture-confirm hit. Add a test.

### F2 (minor, pre-existing, server-side cause): chained swings merge into one flag, so later swings get no verdict
- Repro: press Attack about 5 times a second. A press processed in the tick a recovery ends starts the next swing in that same tick.
- Expected: one rise and one verdict per swing.
- Actual: the own `Swinging` flag never falls between swings (80 ticks continuous, 5 swings). The client sees one swing. Later misses show no `Missed`; an unseen hit in a chained swing shows nothing or stays `Missed`. It never shows a false `Hit`.
- Evidence: `M1-minors-qa-a1/chain-swing-scratch-output.txt` and `chain-swing-scratch-test.cs.txt` (scratch xUnit run outside the repo); `GameServer.Combat.cs` `Swing()` ends a recovery and starts a new swing in the same pass.
- Not fixable client-only. Suggested for a server unit: start a chained swing one tick after the recovery ends, so the flag falls for one snapshot. Add a server test.

### F3 (minor, pre-existing, not reproduced): impact at a stale point
- `ObserveOwnSwing` plays `PlayImpact(killerLayer.VanishedPoint)` on every `SwingEvent.Hit` from `Observe`. If the flag falls while the swing is still pending and not held (a frame stall over 0.25 s, so `Tick` did not run first), `VanishedPoint` is the origin or an older vanish.
- Expected: no impact when no blip vanished in this swing, as on the `LateHit` path.
- Evidence: code read only.
- Fix: play the impact only when the verdict was held for a vanish.

### F4 (minor, docs)
- HUD_SPEC 6.1f a2 still says "≤ 18 ticks (and over 4) is the hit"; the code and the M4 note say 17.
- HUD_SPEC 6.1f a2, M1 sentence "That cannot be made sooner without risking a hit read as a miss" is too strong (O1).
- `docs/briefs/M1-minors-a1.md` says "16 ticks or fewer = hit"; the spec and the code say 17.

## Optional improvements (not required for this unit)

- **O1, sooner `Missed` after a Silent vanish.** Two safe rules:
  - The flag still up in a snapshot 18 or more ticks after the rise is a miss: 0.9 s instead of 1.0 s.
  - The own Killer entry moving after the wind-up is a miss, because a hit's recovery is 0 % speed: about 0.3 s while a move key is held, no gain when standing still.
- **O2, sooner unseen hit with no `Missed` flash.** A non-eliminating hit on a Silent Survivor makes it Lagging, which the Killer always receives. A Lagging blip that spawns within the swing radius during a pending swing, on continuous snapshots, is a hit at about 0.2 s, with a position for the impact. `LateHit` then only covers the unseen eliminating hit.

## Unverified

- `LateHit` and the eliminating `Hit` line live: unit tests and code read only (the dev driver only swings at visible Normal blips).
- Gamepad Start, A and B on hardware.
