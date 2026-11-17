# INTEG-R QA report, attempt 2: the minors fix (Fable; implementer Claude Sonnet)

Commit 1b4df5e on `claude/integ-1117b`, diff `12b31d9..1b4df5e`. Review only: no product code edited, nothing committed.
Evidence: `docs/qa/INTEG-R-qa2/` (harness log and json, EditMode xml and log, live logs and shots).

**Verdict: PASS.** All seven a1 minors are fixed. Three small notes remain, none blocking.

## The seven minors

| # | Minor | Result |
|---|---|---|
| 1 | `Esc Menu` orphan | FIXED. Both legends break after the Hold E item. Seen live: `live/killer/play-1s.png` (line 2 `Q Skill · Esc Menu`), `live/survivor-howto/play-1s.png` (two even lines). |
| 2 | Esc on the intro card | FIXED. `IntroCardState.ConsumeEscape` runs before the menu toggle in `ClientApp.cs`; the press is consumed, so the menu stays closed and the card cannot return. Unit test only; not exercised live (no input allowed). |
| 3 | Attack prompt behind a shutter or wall | FIXED. `KillerWorldLayer.NearestSwingableBlip` feeds the prompt through `KillerHudCopy.SwingClear`. Not seen live in my 14 s Killer run. |
| 4 | `Restored` wording | FIXED. `Rescued N`, `N rescued you`, `N rescued N`; tests and HUD_SPEC 14.2.8 follow. `ally` is left on purpose (reason given in the impl report; accepted). |
| 5 | PROTOCOL.md reasons 6 and 7 | FIXED, and the `SkillRejectReason.cs` comments match. |
| 6 | RTT loopback bound | FIXED: 0.05 s to 0.25 s with a comment. Still far below any stale or client-supplied timestamp. |
| 7 | Clamp test comment | FIXED: the comment now says what is asserted and where the rest is. |

## (a) Attack prompt

- **Same test as the server.** Walls go through the same `FixedMap.SegmentBlocked` code the server calls (the client compiles `Server.Core`). The shutter slab test is the server's `Wall.IntersectsSegment` rewritten in floats: a closed box, a degenerate axis handled by an inside check, enter/exit clamped to 0..1. It is correct. The only differences are float against double and a 1e-6 threshold against an exact zero, which cannot matter at these sizes.
- **Can it hide the prompt for a swing that would hit?** Not systematically. The inputs differ in time only: the client uses the rendered Killer position and blip positions (about 100 ms old), and the server judges 0.2 s after the press. Around a wall corner or a shutter edge the prompt can be wrong for a few frames in either direction. The prompt is advisory: `AttackFrame` sends the press whatever the prompt shows, so no hit is ever lost.
- **Allocation.** `SwingClear` and the slab test allocate nothing. `NearestSwingableBlip` iterates `scene.Views` (an `IReadOnlyList`) with `foreach`, which boxes one enumerator per call. `NearestNormalBlip`, which it replaced in the per-frame path, did exactly the same, so there is no new allocation. See note 2.
- Test `ADownShutterOrAWallBetweenTheKillerAndABlipMakesTheSwingUnclear` covers Up, Down, Broken, a line beside the shutter, a wall, and null inputs.

## (b) Esc flow

- Esc with the card visible: card dismissed, menu not opened. Esc with the menu open: `Visible` is false, so Esc closes the menu as before. Esc in Settings: skipped by the `!SettingsOpen` guard. Esc after the card ended or expired: the menu opens as before.
- Gamepad Start is not consumed and still opens the menu. A gamepad-only player has no dismiss button, so for them the card can still return when the menu closes inside the 7 s. See note 1.
- No regression found in the play-menu path. The existing `IntroDismissPressed` branch still runs after it and is a no-op once the card is dismissed.

## (c) The `\n` in the legends

- Consumers of the legend strings: `IntroCard` (the controls label) and `HowToPlayScreen` (the Controls section). Nothing else reads them; `PlayMenu` does not.
- How to play, both pages, captured with `-ptk-howto` and looked at: `live/survivor-howto/howto-survivor.png` and `howto-killer.png`. The Controls section is two clean lines on each page and the sections below move down by one line with nothing clipped.
- Intro cards for both roles looked at (shots in the table above). The card height is unchanged: both legends were already two lines.

## Gates (on 1b4df5e)

| Gate | Result |
|---|---|
| `dotnet build PingTheKiller.sln -warnaserror` | 0 warnings, 0 errors |
| Server.Tests `Category!=BalanceSweep` | 1147/1147 |
| Server.Tests `Category=BalanceSweep` | 18/18 |
| Unity EditMode, absolute `-testResults` | 1606 passed, 0 failed, 2 skipped (the same two `[Explicit]` tests) |
| Harness once, fresh servers, 7795 / 7796, `--threshold 100 --expect-los` | 100/100 |

- Exe: used. Every `.cs` file is older than the built `PingTheKiller.Client.dll` (23:22:48). Three asset and settings files have later timestamps from the post-build line-ending revert and the commit; the tree was clean, so their content equals HEAD.
- Live runs, 1280x720, offline, no input sent: Survivor 30 s with `-ptk-howto`, Killer 14 s. Both exited by themselves.
- After the Unity run, files changed only in line endings were reverted. Every server and exe I started has exited.

## Notes (not blocking)

1. Gamepad: Start during the intro card still opens the menu and the card returns afterwards; there is no gamepad dismiss button. Keyboard and mouse are fixed.
2. `NearestSwingableBlip` and `NearestNormalBlip` both box a list enumerator every frame. Older than this fix; a `for` loop over the list would remove it.
3. HUD_SPEC was not updated for two of the changes: row 6.1p still says the prompt shows for "the nearest Normal tracked blip within 3 m" with no mention of walls or lowered shutters, and section 3 does not say that Esc on the card does not open the menu.
