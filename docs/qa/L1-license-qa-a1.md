# L1-license QA, attempt 1 (2026-11-19)

Reviewer: Claude Fable 5.2 (implementer: Claude Sonnet 5.5). Commit reviewed: `e2f30f9` on `claude/l1-license`.
Text-only review: no Unity run, no downloads. Not legal advice.

**Verdict: FAIL** (2 major, 5 minor). The shipped/not-shipped trace and the City 03 proof are correct; the audit misses two third-party code components that are in the player, and CREDITS.md states one publisher as fact that nothing supports.

## Method

- Own GUID trace (Python, scratch script, not committed): roots = `Assets/Scenes/Client.unity`, every file under a non-Editor `Resources/` folder, and assets referenced from `ProjectSettings`. Followed `guid:` references through text assets and `.meta` files, plus `#include` paths in shaders. Result: 499 files (audit: 492 GUIDs; the difference is the included HLSL files and settings assets).
- Cross-check against a real player build: the "Used Assets" list and assembly list in `docs/qa/integ-1118-fixes/build-windows.log` (same content as `main` f92e5aa) and the `Managed/` folder of the existing build in the main checkout (`Client/Builds/Win64/PingTheKiller_Data/Managed`, built 2026-11-03/05).
- SHA-256 of all 19 world FBX against the City 03 originals. The worktree holds real LFS content, not pointers; the git blob ids of the pointer files also match.
- Pages read: Unity Asset Store EULA (`unity.com/legal/as-terms`), DOTween licence page, unity-mcp `LICENSE` at tag v10.3.0, two Asset Store listings for "Rio".

## What was confirmed (no finding)

| Claim | Result | Evidence |
|---|---|---|
| Cyberpunk RPG GUI Pack not shipped | Confirmed | 0 files in trace, 0 in build log. Pack holds only png/prefab/mat/psd/FBX, no runtime scripts, so nothing is compiled in either |
| Layer Lab not shipped | Confirmed | same |
| Kevin Iglesias: only Idle/Walk/Run copied | Confirmed | `CharacterArtSetup.cs:102-104`; `Locomotion.controller` and `Resources/Motion/Motion.controller` reference only the three `Art/Characters/*.anim` GUIDs; no file under `ThirdParty/Kevin Iglesias` in trace or build log |
| 12 of 94 SFX WAV shipped | Confirmed | 94 WAV in the folder; 12 in trace; 12 in build log |
| DOTween unused by scripts but shipped | Confirmed | no `DG.Tweening`/`DOTween` in `Assets/Scripts`; `DOTween.dll` is in `Managed/` and in the build log; the 9 DOTween module scripts also compile into `Assembly-CSharp-firstpass.dll` |
| Toon Shaders Pro via `PtkToon.shader` fork | Confirmed | header lines 1-5; `#include "Assets/ThirdParty/Toon Shaders Pro/Shaders/Toon/ToonFunctions.hlsl"` at line 161; 4 files in `Shaders/Outline/Resources` |
| World FBX are City 03 copies | Confirmed, all 19 | every file SHA-256-identical to the same-named file in `ThirdParty/Fries and Seagull/City 03/Models` (for example `House 1.fbx` 42d94ace67ac449c..., 1 975 072 bytes); 13 of them are in the build log |
| Hovl 11, QAtmo 12, Rio 2, Kyle 5 files | Confirmed | trace |
| Inter and DOTween notices in CREDITS.md | Accurate | match `Resources/Fonts/OFL.txt` line 1 and `DOTween/readme.txt` line 1 |
| No StreamingAssets, no other fonts or plugin DLLs under `Assets` | Confirmed | file search |

## Findings

### F1 (major) unity-mcp runtime and Newtonsoft.Json ship in the player; audit calls unity-mcp "Editor tooling" and never lists Newtonsoft.Json
- Reproduce: `grep -n "MCPForUnity.Runtime\|Newtonsoft" docs/qa/integ-1118-fixes/build-windows.log`; list `Client/Builds/Win64/PingTheKiller_Data/Managed` in the main checkout.
- Expected: every third-party component in the build is listed with licence and attribution.
- Actual: the audit row says "Editor tooling ... confirm it is excluded from the player", severity minor, licence "Not read". THIRD_PARTY.md says the same. Newtonsoft.Json appears nowhere.
- Evidence: build log line 696 `CopyFiles Library/Bee/PlayerScriptAssemblies/MCPForUnity.Runtime.dll`, line 889 `Dependency assembly - MCPForUnity.Runtime.dll`, line 1099 `Dependency assembly - Newtonsoft.Json.dll`; seven `Packages/com.coplaydev.unity-mcp/Runtime/...cs` entries in the used-assets list; `Managed/MCPForUnity.Runtime.dll` (30 208 bytes) and `Managed/Newtonsoft.Json.dll` (691 712 bytes) exist. The package's `Runtime` asmdef has no platform restriction and `autoReferenced: true`. Its `package.json` depends on `com.unity.nuget.newtonsoft-json`.
- Licence: unity-mcp is MIT ("Copyright (c) 2025 CoplayDev", `LICENSE` at tag v10.3.0). Newtonsoft.Json is MIT (the Unity wrapper package carries `Third Party Notices.md`). MIT requires the copyright and permission notice with every copy, so both need a notice in the game unless they are removed from the build.
- Fix direction: either strip the package from release builds (preferred: AI-bridge code has no place in a Store binary) or add both MIT notices to CREDITS.md; update the audit row and severity either way.

### F2 (major) CREDITS.md states "Rio (Unity Technologies)" as fact; no evidence supports it
- Reproduce: read `docs/store/CREDITS.md` line 25 against the audit row "Unity Rio ... Listing not found" and THIRD_PARTY.md "per earlier audit; unverified".
- Expected: credits contain only verified names; unverified ones are marked for the owner.
- Actual: the credit line asserts the publisher. The only basis is the folder name `ThirdParty/Unity Technologies/LowpolyCharacterRio`. Asset Store listings that match the name exist ("Lowpoly Survival Character Rio" 273074 and "Lowpoly Magician RIO" 288942, both deprecated; publisher not readable from the page), which the audit did not find. Whether Unity published this asset is unknown, and a wrong credit on a Store product is worse than none.
- Also the audit sentence "Unity-published item; EULA applies equally to Unity-made assets" builds a licence conclusion ("Likely yes") on the same unverified premise.
- Fix direction: mark the Rio publisher "unknown, owner to confirm from My Assets" in all three files; take it out of the Unity Technologies credit line until confirmed. (Robot Kyle as Unity Technologies is plausible: the folder contains `StarterAssetsThirdPerson.controller`. Still owner-confirmable only.)

### F3 (minor) Blocker 3 is over-rated and misdescribed
- Audit: "major: 12 raw WAV files of the Sci-Fi SFX pack ship in the build."
- Actual: a Unity player does not contain raw WAV files; the clips are re-encoded into the player data files, which is exactly the "embedded component" case of EULA 2.2.1. The EULA text has no audio-specific clause. The remaining risk (a non-standard listing licence) is already covered by blocker 1. Should be minor, or folded into blocker 1.

### F4 (minor) OFL wording is stronger than the licence
- Audit and CREDITS: the OFL text and copyright must be "shown to players".
- Actual: OFL 1.1 condition 2 requires that each copy contains the notice and licence, as a text file, header or metadata the user can view. `OFL.txt` is already in the build (4.4 kb in the used-assets list) but inside a data file, not viewable. The recommendation (Licences screen or installed text file) is right and "major" is acceptable; the wording should say "must accompany the font in a form the user can view", not "must be shown".

### F5 (minor) "Only the Inter font ships" is not accurate
- Evidence: build log lists `Packages/com.unity.render-pipelines.core/Runtime/Debugging/Prefabs/Fonts/PerfectDOSVGA437.ttf` (79.7 kb). It is Unity package content (covered by the package's own notices), so no action beyond listing it under the Unity packages row. Transitive Unity packages in the player (Burst, Collections, Mathematics, Shader Graph library, Multiplayer Center common, SRP Core) are also not named in that row.

### F6 (minor) Toon Shaders Pro C# is compiled into the game and not mentioned
- Evidence: `ThirdParty/Toon Shaders Pro/Shaders/Outline/Scripts/{OutlineFeature,OutlineSettings,ToonShaderUtility}.cs` are in the build log and compile into `Assembly-CSharp.dll` (no asmdef). Same licence row, so no change to the conclusion; the "shipped" description is incomplete. The "optional cleanup: move the outline shaders out of Resources" advice would not remove these.

### F7 (minor) Small inconsistencies
- THIRD_PARTY.md City 03 row: "13 of 19 copies referenced, 11 distinct models". The audit and the build log both give 13 distinct FBX (House 1-8, House 10, Roadside Billboard 2, Dumpster 2, Box 1, Box 2).
- CREDITS.md puts DOTween under "Required by a licence". The DOTween licence page ties the notice and readme duty to verbatim source-form copies and is unclear for a compiled game. Keeping the line is the safe choice; the heading overstates certainty.
- Derived assets not named: `Art/Characters/KyleBody.asset` and `KyleHead.asset` (meshes cut from the Kyle FBX) and the 131 `Art/World/Meshes/*.asset` ship. Same licences as their sources.

## Severity review of the audit's own list

| Audit item | Audit | QA view |
|---|---|---|
| 1 no proof of purchase for 8 packs | major | agree; this is the real release gate, and the non-transferable point (buyer account = Store seller) is correctly raised |
| 2 Inter OFL notice | major | agree (wording, F4) |
| 3 SFX "raw WAV" | major | minor (F3) |
| 4 Kevin Iglesias, 5 PtkToon fork, 6 DOTween, 8 Kyle | minor | agree |
| 7 unity-mcp "must stay editor-only" | minor | under-rated: it is in the player now (F1) |
| Rio publisher | not listed | add (F2) |

EULA summary (2.2.1 embed/distribute/monetise/modify, non-transferable, no attribution clause, Restricted Assets separate) matches the page as read today. Mixamo conclusion is correctly hedged as secondary-source.

## Required for PASS

1. Fix the unity-mcp row and add Newtonsoft.Json; decide strip-from-build or MIT notices (F1).
2. Remove the unverified Rio publisher claim from CREDITS.md and align the three files (F2).
3. Correct F3-F7 wording and counts.
