# IP audit findings (unit IP-audit, attempt 1, 11/17)

Scope: player-facing and store-facing expression only. Mechanics are not protected, so the borrowed round structure (generators/terminals, exit gates, chase, second chance state) is not listed as a finding. Names, distinctive terms, iconic presentation, characters, sounds and text are.

Checked against: Dead by Daylight (DbD), Identity V, Friday the 13th, Evil Dead, Texas Chain Saw Massacre, Among Us. No character, logo, map name, sound, quote or art from any of them is used anywhere in the client, server or store text. The 12 hits below are the whole list.

Counts: **high 1, medium 4, low 7 (kept), listed-only 3 groups.**

## A. Changed (high and medium)

| # | Where | Resembles | Risk | Why | Change |
|---|---|---|---|---|---|
| 1 | `UI/RulesV2/SyncCheckRing.cs` (ring, sweeping needle, highlighted zone, Great/Good) + `RulesV2.uss` `.v2-sync` | DbD "skill check" (circular dial, rotating needle, white success zone with a wider "great" part, Space to press) | **high** | Iconic, recognisable UI presentation; the combination of shape, motion, zone and press key is the signature of that game | Replaced with a horizontal **sync lock** bar: waveform track, travelling pulse, lock window. File renamed `SyncLockBar.cs`. Same timing function, same packets and judge |
| 2 | `RulesV2Copy.cs` `CollapseCopy.Main`, `ResultV2Copy.Reason`, `RulesV2Wiring.HatchCloseHint`, `SurvivorFeedback` (elimination reason) | DbD "Endgame Collapse" (a clock that eliminates survivors once the exits are powered or the hatch is closed) | medium | Distinctive named phase of one game, used with the same trigger and the same consequence | "Collapse" → **"Shutdown"** (`Shutdown 1:52`, `The shutdown eliminated the Survivors still inside.`, `Disconnected: the shutdown reached you.`, `Stand still · starts the shutdown`) |
| 3 | `RulesV2Copy.TerminalFlagCopy.Regressing` | DbD generator "regression" | medium | Term of art for exactly this state (progress draining when nobody works on it) | "Regressing" → **"Rolling back"** |
| 4 | `HoldCopy.Label/Hint`, `RulesV2Wiring` prompt (`Hold [E] to kick Terminal 3`) | DbD "kick a generator" | medium (low as a word, medium as the exact action name on the same object type) | Word is generic; the verb on this object is the game's named Killer action | "kick" → **"jam"** (`Jamming the Terminal`, `moving cancels the jam`, `Hold [E] to jam Terminal 3`). Matches the GDD's own word for it (단말 교란) |
| 5 | `RulesV2Copy.HoldCopy.Label`, `RulesV2Wiring` (`Press [E] to escape through the Hatch`, `Hold [E] to close the Hatch`) | DbD "the Hatch" (a late-game extra exit the Killer can close) | medium | "Hatch" is an English word, but the escape hatch that the Killer can close is DbD's named object. The game's own name for it (GDD v2, store notes) is "Emergency port" | Player text → **"Emergency port"** (`Press [E] to escape through the Emergency port`, `Hold [E] to close the Emergency port`, `Closing the Emergency port`) |

## B. Reviewed and kept (low)

| # | Where | Resembles | Why it stays |
|---|---|---|---|
| 6 | `Killer`, `Survivor` role words (HUD, lobby, results, store) | DbD roles | Generic English role words used by many games (Friday the 13th, Evil Dead, Identity V too). Presentation and the rest of the vocabulary differ |
| 7 | `Great` / `Good` / `Fail` result words (`SyncCheckMath.ResultWord`) | DbD "Great" skill check | Ordinary one-word grades. Kept short as the brief allows; the presentation that made them distinctive is gone |
| 8 | Skill names: Ping scanner, Overclock, Packet afterimage, Firewall block, Dummy packet, Stealth relay, Traffic overload, Emergency restore, White noise | Perk/ability names in several games | All original, network-themed; no match to another game's named ability that I know of |
| 9 | `Exit`, `Locked`, `Powered`, `Terminal`, `Rescue`, `Eliminated`, `Escaped`, `Lagging`, `Signal`/`Silent` | DbD "Exit gates", "Generator", "Rescue" | Generic English words or this game's own terms (Signal/Silent, Lagging are original) |
| 10 | Result screen: `Killer wins` / `Survivors win`, `N of M escaped`, row states | DbD "Sacrificed / Escaped" | Plain factual lines; no "sacrifice", no banner. Matches SLOP §6 |
| 11 | Game title `Ping the Killer` / `핑 더 킬러` | none found | Not a name of any known game. A trademark search before submission is still the developer's job (store listing open item 1) |
| 12 | Sounds: `Audio/V2Cues.cs` synthesized tones (Great: B5 then E6, Fail), `SurvivorSoundSynth` footsteps and breathing, Sci-Fi Weapons SFX pack | DbD skill-check tones, heartbeat/terror radius | Cues are generated sine/noise tones, not recordings. The Killer-hears-survivors idea is a mechanic (SLOP/GDD). The SFX pack is a third-party Asset Store pack. Not changed |

## C. Store listing and public docs

- `docs/store/STORE_LISTING.md`: **no other game is named or compared anywhere** (the file already says "Do not add other games' names"). Text describes the game on its own terms. It uses none of the renamed words (no Hatch, Collapse, Regressing, kick). No edit needed.
- `docs/store/PRIVACY_POLICY.md`, `MSIX_PACKAGING.md`: no game names. The word "identity" in MSIX is the package identity. No edit.
- Root `README`: none exists.

## D. Third-party assets (`Client/Assets/ThirdParty`) — not deleted

None of the 10 folders contains a license, readme, PDF or text file (searched all depth). Origin is only recorded in the commit message `2030b21` ("curated Asset Store content") and in the folder names. Needs the developer's purchase or download proof:

| Folder | Files | Likely source |
|---|---|---|
| Cyberpunk RPG GUI Pack | 504 | Asset Store GUI pack |
| Fries and Seagull / City 03 | 108 | Asset Store city kit |
| Hovl Studio (HSFiles, Map track markers VFX) | 23 | Asset Store VFX |
| Kevin Iglesias / Human Animations | 71 | Asset Store animations |
| Layer Lab / 3D Props-CartoonBoxMap | 246 | Asset Store props |
| QAtmo / Explosive Device C4 & Defuse Kit | 24 | Asset Store props |
| Sci-Fi Weapons-Bullet Hell Sound Effects Pack | 94 | Asset Store audio |
| Toon Shaders Pro | 32 | Asset Store shaders |
| Unity Technologies / LowpolyCharacterRio | 6 | Unity-provided sample character |
| UnityTechnologies / SpaceRobotKyle | 25 | Unity-provided sample character |

**Missed in the first pass (outside `ThirdParty`), added in attempt 2:**

| Location | What | Note |
|---|---|---|
| `Client/Assets/Art/Characters/Mixamo/` | 9 Adobe Mixamo animation FBX files | No licence note; covered by Adobe's Mixamo terms for use in a game, keep proof of the download account |
| `Client/Assets/Plugins/Demigiant/DOTween` | DOTween (free edition) | `readme.txt` present; Demigiant licence permits shipping in a game |
| `Client/Assets/Resources/Fonts/Inter-Regular.ttf` | Inter font | SIL OFL 1.1; `OFL.txt` is present, which is the requirement |
| `Client/Assets/Art/World/Models/*.fbx` | House, Gray Car, Dumpster and others | Look like they come from a pack; the source pack is not recorded. Owner must identify it |

**Keep the Asset Store order history** (invoices and order IDs, plus the Mixamo account and download date). It is the only proof of origin if a store review or a claim asks.

Recommendation (not done, needs the developer): keep the Asset Store invoice or order numbers for the paid ones, add a `docs/store/THIRD_PARTY.md` listing publisher, order ID and license, and confirm Asset Store EULA terms allow shipping on the Microsoft Store (they do for standard packs, but check each one for "no redistribution of source/raw audio" clauses). Third-party names in the UI are not shown to players.

## E. Internal design references (listed, not changed)

Not player-facing; an influence in design notes is not infringement.

- `docs/GDD-v2.md` lines 1, 7, 13, 37, 42, 57, 58, 63, 107 ("데바데식", 스킬 체크, 발차기, 해치, 갈고리)
- `docs/GDD.md` lines 18, 19 ("데바데식")
- `docs/briefs/U2e-a1.md` (DbD-style visibility), `U6a-a1.md` (DbD-style rules, "skill check"), `U6e-a1.md` (DbD-style loops, junk piles)
- `docs/ui/HUD_SPEC.md` §6 (positional sound "in the style of Dead by Daylight", decision of 2026-10-27) and the N3 table row. These two are in sound-design sections outside this unit's owned sections, so only listed.
- `docs/qa/*` reports that repeat the above.
- Code comments: **no** comment in `Client/`, `Shared/`, `Server*` or `Tools/` names another game (grep for the game names, "DbD", "skill check").
- Server log lines `hatch open/closed`, `collapse start`, `kick terminal` in `Server.Core/Runtime/GameServer.Exits.cs` and `GameServer.Terminals.cs`: server console logs only, not sent to clients. Not changed (`Server.Core` is out of scope); rename if the Server console is ever shown to players.
- Dev flag names `-ptk-preview-v2-hud kick-terminal|collapse-urgent|sync-check` and enum names `HoldKind.KickTerminal/CloseHatch`, `EliminationReason.Collapse`, `TerminalFlags.Regressing` are internal identifiers (also `Shared/` protocol), kept.

## F. Q5 names "Shutter" and "Rail"

Not in this worktree yet (grep: no "Shutter"/"Rail" string in `Client/`). Check on merge. "Shutter" and "Rail" are generic words (Q5 is the pallet/window-vault unit). The risk is in the mechanic's presentation (pallet drop that stuns the Killer, window vault) and its exact names "Pallet" and "Vault"; the brief's names avoid both, which is the right call. Re-check the final copy and visuals at merge.
