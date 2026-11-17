# R3-help implementation report, attempt 1 (Sonnet)

**Built.** S1 explicit rescue: hold E within 1.5 m for 3 s; release or leaving range resets; rescuer reports `ActionKind.Rescue (7)` plus progress. E priority is rescue, chase prop, exit switch, terminal (documented in code and HUD_SPEC 4.4). Bots hold E next to a Lagging ally; Emergency restore kept. S2: helper prompt, ally tag and edge chevron (amber Signal chevron, reuses MarkerLayer/EdgeSpread), downed line. S3: 7 s intro card (any key), How to play (two pages), once-per-match hints; `PlayMenuCopy.KillerEffect` added. S5: a restored Survivor gets up 1.5 s (speed 0, no E, no skill, yaw read, inside the 2 s protection); `PlayerFlags.GettingUp = 32` to Survivor recipients; Mixamo GettingUp clip from the flag, sped to 1.5 s; line `Getting up…`.

**Copy.** `Hold [E] to restore Mina — you will be Signal` / `Restoring Mina — you are Signal` / `Lagging · an ally can restore you with [E] · nearest ally 18 m · 54 s`.

**Balance (1v7 Survivor win, sweep / BalanceSweep).** Before 53 % / 63 %; after rescue change 60 % / 60 %; after get-up 48 % / 50 %. 1v3 after get-up 58 % / 75 % (limit 75 %). 1v1 40 %. BalanceSweep 18/18.

**Gates.** Build `-warnaserror` 0/0. Server.Tests 1131 pass (non-sweep); Harness.Tests 641 pass; Harness 3x 100/100 on 7785/7786 (fresh servers); Unity EditMode 1580 pass, 2 skipped, 0 fail; BuildWindows OK.

**Shots** (`docs/qa/r3-help-runs/shots/`): intro-survivor, intro-killer, howto-survivor, howto-killer, r3-ally-tag, r3-ally-edge, r3-rescue-prompt, r3-restoring, r3-downed-line, r3-getting-up (1280x720).

**Unverified.** Getting-up pose looks only checked in one shot; the speed-up is runtime (`Animate` rate), the MotionSetup controller is unchanged. The new How to play Restore line was edited after its shot.

**Risks / deviations.** Prompt wording differs from the brief (`Hold [E] to restore ...`). Marker is amber, not `ally` teal; the Lagging viewer's nearest-2 Normal markers are not built (downed line replaces them). `GettingUp` is not sent in the Killer view (a flag decoys lack would reveal real Survivors), so the Killer client shows no get-up. Skill press while getting up reuses `NotNormal`. Merge overlap with R1/R2 in `ClientApp.cs`, `PlayMenuCopy`, `GameServer.Rules.cs`. 1v3 rate sits at the limit.
