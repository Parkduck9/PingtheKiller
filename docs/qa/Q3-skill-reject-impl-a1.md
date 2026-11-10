# Q3-skill-reject — implementation report, attempt 1 (Claude Opus)

**Packet.** `S2C_SkillRejected` 0x0016 `[byte SkillId][byte Reason]`. `SkillRejectReason` values: 1 NotInMatch, 2 NotYourSkill, 3 NotNormal, 4 Cooldown, 5 NoTarget, 6 NoSwitchInProgress, 7 Passive, 8 Sandbox. A reason of 0, above 8, or the wrong length is a `PacketFormatException`. No uncovered path found, so no reasons added.

**Paths.**
- Lobby `UseSkill` (used to be dropped silently): NotInMatch.
- `RequestSkill`: sandbox gives Sandbox; an out-of-range id gives NotYourSkill.
- `TryUseSkill`: NotInMatch, another id gives NotYourSkill, not Normal gives NotNormal, the epsilon cooldown gives Cooldown.
- Per skill: Ping scanner and Restore give NoTarget, Firewall gives NoSwitchInProgress, Afterimage (`default`) gives Passive.

**Privacy.** Every Killer `WorldState` carries each exit's `SwitchProgress`, and a Firewall use is a visible effect, so no new information leaks. Ping scanner gets NoTarget only when no Survivor is active. Tested: Silent positions never change what the Killer receives.

**Flood.** At most one answer per player per tick; a use counts as the answer. The request list is unchanged. Logging is the first line per player per second, with `unlogged=N`. Bots get nothing.

**Client.** Only the press that armed the watch shows a line, at once, and disarms the watch. Late rejections, or rejections for presses the client already explained, are ignored. The 0.5 s watch stays as the fallback.

**Tests.** New: `SkillRejectTests.cs` (15) and `SkillRejectFeedbackTests.cs` (11). Changed: ProtocolV02 count 21→22 and Reverify S2C count 11→12. Added literal and byte-field rows, plus H3, H6 and WireClient harness tests.

**Gates.** Build clean · Server.Tests 1087/1087 · BalanceSweep 18/18 · Harness.Tests 641/641 · harness 100/100 ×3 · EditMode 1486/1486 · BuildWindows OK.

**Live.** No exceptions. Player 4 only got 7 NoSwitchInProgress (Killer) and 6 NoTarget (Survivor) rejections. The screen shows the same pre-check text (`play-4s.png`).

**Unverified / risks.** No dev flag forces an armed rejection, so the server-reason line was not seen live. `--bots auto` does not exist; I used the documented 7777 sandbox and 7778 H6 servers. I updated `docs/PROTOCOL.md` to v0.6.
