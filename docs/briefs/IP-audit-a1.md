# Brief IP-audit: remove anything that copies another game too closely (attempt 1)

- **Implementer:** Claude Sonnet. **QA:** Fable.
- **Time box:** about 75 minutes.
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/ip-audit`, branch `claude/ip-audit`, from main 3675877.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/q3-skill-reject/Client/Library` into `Client/Library`. Skip any `ArtifactDB-lock`.
- **Hard rules:**
  - Never commit, push, merge or download.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
- Read `AGENTS.md` and `docs/ui/SLOP_DEFINITION.md`. SLOP is mandatory for copy and UI.

## Goal (user request)

"Change everything that is so similar to other games that it could cause a copyright dispute."

- The game's design notes say it borrows its structure from Dead by Daylight ("데바데식"). Game mechanics themselves are not protected. **Names, distinctive terms, iconic UI presentation, characters, logos, sounds and text are.**
- Remove every player-facing or store-facing element that copies a specific existing game's expression:
  - Dead by Daylight;
  - Identity V;
  - Friday the 13th;
  - Evil Dead;
  - Texas Chain Saw Massacre;
  - Among Us;
  - any other game you identify.

## Part 1 — Audit (first ~25 minutes) → `docs/qa/IP-audit-findings.md`

Scan:
1. **In-game copy:** every string in `Client/Assets/Scripts/**`, including `HudCopy`, `LobbyCopy`, `ResultCopy`, `KillerHudCopy`, `UI/RulesV2/RulesV2Copy.cs`, `SkillFeedback`, `PlayMenu`, `SurvivorReadout`, and any UXML/USS or asset text.
2. **Store and public docs:** `docs/store/*`, the root `README` if one exists, the game title, and the store listing.
3. **UI presentation:** look at screenshots in `docs/qa/*screens*` and at the code.
   - **Known high-risk item:** the **sync check** (`UI/RulesV2/SyncCheckRing.cs`). It is a circle with a sweeping needle and a highlighted success zone, judged Great/Good. That is close to Dead by Daylight's signature "skill check" presentation.
4. **Names:**
   - character, skill and status names: `Killer`, `Survivor`, `Lagging`, the 9 skill names, `Emergency port`/hatch, terminals, exits;
   - result-screen phrasing;
   - sound cues.
5. **Third-party assets in `Client/Assets/ThirdParty`:** check that each has a license or readme indicating Asset Store or free use. List any asset without one. **Do not delete assets.**
6. **Internal docs and code comments** that only mention other games as design references (GDD, PLAN, comments like "DbD kick"). They are not player-facing. **List them but do not change them.** Mentioning an influence in internal design notes is not infringement.

For each finding give:
- where it is;
- what it resembles;
- the risk (high/medium/low) and why;
- the proposed change.

Generic words are low risk: Killer, Survivor, exit, hatch as an English word, generator as an English word. Change them only if the combination or presentation is distinctive.

## Part 2 — Apply (the rest of the time)

1. **Change every high and medium item.**
   - Use player-facing text that fits this game's cyber/network theme. Examples of the existing voice: Signal/Silent, Lagging, Ping scanner, Firewall block.
   - Keep every term consistent across HUD, lobby, results and tutorial copy, and update the copy tests that pin strings.
2. **Redesign the sync check presentation so it no longer reads as a ring with a needle:**
   - **Proposed:** a horizontal "signal lock" bar. A bright pulse travels left to right along a waveform track. A lock window is highlighted. The player presses Space when the pulse is inside the window. Great = the inner narrow band, Good = the outer band.
   - **Change only the client presentation:** the shape, motion and labels. The server judge, the timing windows, the packets and the `-ptk-auto-sync` dev behaviour must be **unchanged**. The pulse position must map exactly to the same time-to-window function the ring used. Reuse that math and test it.
   - **Keep:**
     - the HUD_SPEC placement budget (it must not cover the character's feet);
     - the 720p floor sizes;
     - Reduced motion behaviour;
     - the result words if they are fine.
     "Great"/"Good" are generic. If you rename them, keep them short.
   - Update HUD_SPEC to the new design, marked `(11/17, IP)`.
3. **Store listing:** remove any comparison or reference to another game's name in `docs/store/STORE_LISTING.md` (for example "like Dead by Daylight"). Describe the game on its own terms.
4. **Not in scope:**
   - Do not touch `ClientApp.cs` except a one-line rename if unavoidable.
   - Do not touch `World/LaggingStutter.cs`, `CharacterMotion.cs`, `KillerAttackKick.cs` or `SkillFx.cs`; unit U8 owns them.
   - Do not touch `Shared/` or `Server.Core`. If a server-side string or log is player-visible, list it instead.
   - The parallel Q5 unit adds the names "Shutter" and "Rail". Check them in your audit too.

## Tests and gates

Copy tests and the sync-bar math test, for example: the pulse at window centre ⇔ the same time the ring needle reached centre.

```
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <ABSOLUTE xml> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
Client/Builds/Win64/PingTheKiller.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role survivor -ptk-auto-sync <check the flag syntax in the client> -ptk-shots <abs dir> -ptk-quit-after 200 -logFile <abs log>
```

- **Pass criteria:**
  - All EditMode tests pass. Main had 1486.
  - BuildWindows succeeds.
  - `dotnet build -warnaserror` stays green.
- **Screenshot:** the new sync bar in a live run, at 1280x720. If the human never hacks, find the dev flags that make the human hack, such as an auto-hack or want flag; read `Client/Assets/Scripts/Dev`.
- **Paths:** shots go in `docs/qa/IP-screens/`, logs in `docs/qa/IP-runs/`.
- **After each Unity run:** revert files that changed only in line endings.

## Owned files

- `Client/Assets/Scripts/UI/**`
- `Client/Assets/Scripts/Killer/KillerHudCopy.cs`
- other copy-only files you find, listed in the report
- client copy tests
- `docs/ui/HUD_SPEC.md`: the sync check and copy sections
- `docs/store/*`
- `docs/qa/IP-*`

## Report

Write `docs/qa/IP-audit-impl-a1.md` in English, ≤ 300 words. Cover:
- the findings count by risk;
- what changed, with an old → new table;
- the sync-bar design;
- the items left unchanged and why;
- the tests;
- the gates;
- the shots;
- the risks.

Also write `docs/qa/IP-audit-commit-message.txt`.
