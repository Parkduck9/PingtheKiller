# L1-license audit (attempt 1, 2026-11-19)

Implementer: Claude Sonnet 5.5. Read-only audit of `main` f92e5aa plus public licence pages. No downloads, no Unity run. Not legal advice.

## Method
- Shipping set: a script followed GUID references from `Assets/Scenes/Client.unity` and every file under any `Resources/` folder through `.unity/.prefab/.mat/.asset/.controller/.anim/.shader` files and importer `.meta` files. `Resources.Load` paths in `Assets/Scripts` were checked. `ProjectSettings` and URP settings hold no `ThirdParty` GUIDs. Result: 492 shipped GUIDs.
- Editor-time copies: `CharacterArtSetup` copies Kevin Iglesias clips into `Assets/Art/Characters/*.anim`, and `WorldArtSetup` copies City 03 FBX into `Assets/Art/World/Models`. These copies ship even though the vendor folders are not referenced.
- Terms read: Unity Asset Store EULA (https://unity.com/legal/as-terms), DOTween (http://dotween.demigiant.com/license.php), Mixamo (the Adobe help page returned 403; used search summaries of the Adobe FAQ and community FAQ), Robot Kyle listing 4696 (deprecated; publisher and licence fields not readable).
- No package folder contains a licence file or readme. Only DOTween `readme.txt` and Inter `OFL.txt` exist.

## Table
Allowed = allowed for paid Microsoft Store distribution of the compiled game.

| Item | Shipped? | Licence | Allowed | Attribution needed | Evidence |
|---|---|---|---|---|---|
| Cyberpunk RPG GUI Pack | Not shipped (0 of 504 files) | Asset Store EULA presumed | n/a, not in build | no | GUID trace; no references |
| Fries and Seagull City 03 | Shipped (13 of the 19 copied FBX) | Asset Store EULA presumed | Yes if bought under the standard EULA; unverified | none in EULA | `Art/World/Models/*.fbx` byte-identical to `ThirdParty/Fries and Seagull/City 03/Models/*.fbx`; `WorldArtSetup.cs:28` |
| Hovl Studio (HSFiles, Map track markers VFX) | Shipped (11 files) | Asset Store EULA presumed | Unclear: listing not verified | none found | GUID trace; `SkillFxArt.asset`, `Exit marker MarkersAtlas1bcg.mat` |
| Kevin Iglesias Human Animations | Shipped as copies of 3 clips (Idle, Walk, Run) | Asset Store EULA presumed | Unclear: creator terms not found; EULA allows embedded use in a compiled game | unknown | `CharacterArtSetup.cs:23,100-104`; `Art/Characters/Idle.anim` |
| Layer Lab CartoonBoxMap | Not shipped (0 of 246) | Asset Store EULA presumed | n/a | no | GUID trace |
| QAtmo C4 and Defuse Kit | Shipped (3 FBX, material, 8 textures) | Asset Store EULA presumed | Yes if standard EULA; unverified | none found | GUID trace |
| Sci-Fi Weapons-Bullet Hell SFX | Shipped: 12 of 94 WAV | Asset Store EULA presumed | Unclear: audio packs can forbid raw-file redistribution; compiled embedding is covered by EULA 2.2.1; listing not verified | none found | `Resources/SkillFxArt.asset` Clips list |
| Toon Shaders Pro | Shipped as derived shader (`PtkToon.shader` forks the vendor shader and includes vendor HLSL) plus 4 outline files in a Resources folder | Asset Store EULA presumed (EULA allows modification) | Likely yes; unverified | none found | `PtkToon.shader:1-5,77,161`; `WorldMaterials.cs:71` |
| Unity Rio (LowpolyCharacterRio) | Shipped (survivor model, albedo) | Unity-published item; EULA applies equally to Unity-made assets (EULA 1.1). Listing not found | Likely yes; unverified | none found | `CharacterArtSetup.cs:21,39` |
| Robot Kyle (SpaceRobotKyle) | Shipped (killer model, 3 textures, material). Its SFX and animation FBX are not referenced | Listing 4696, free, deprecated; publisher and licence field not readable | Likely yes; unverified | none found | https://assetstore.unity.com/packages/3d/characters/robots/space-robot-kyle-4696 |
| Mixamo animations (9 FBX) | Shipped | Adobe Mixamo terms: royalty free for commercial games, credit optional, no redistribution of raw files | Yes (secondary sources; Adobe page 403) | no | `Art/Characters/Mixamo`; Adobe community FAQ via search |
| DOTween free | Shipped (`DOTween.dll`, `DOTweenSettings.asset`); no script uses it | DOTween default licence | Yes | Keep copyright notice and readme with copies of the plugin | http://dotween.demigiant.com/license.php |
| Inter font | Shipped | SIL OFL 1.1 | Yes | Yes: copyright notice and licence text must accompany the font | `Resources/Fonts/OFL.txt` |
| World FBX "unknown" | Shipped (13 of 19) | Same as City 03 | see City 03 | see City 03 | Source proven, see below |
| `com.coplaydev.unity-mcp` v10.3.0 | Editor tooling | Not read | Unclear for build: confirm it is excluded from the player | unknown | `Client/Packages/manifest.json` |
| Unity engine, URP, Input System, uGUI | Shipped | Unity Editor terms, Unity Companion Licence | Yes within the Unity plan terms; plan versus revenue not checked | none | `Client/Packages/manifest.json` |

Own content: `V2Cues` and `SurvivorSoundSynth` audio is synthesised in code. Only the Inter font ships (no TextMesh Pro, no Cyberpunk fonts).

## Unknown world models: source proven
All 19 files in `Client/Assets/Art/World/Models` are byte-identical to the same-named files in `Client/Assets/ThirdParty/Fries and Seagull/City 03/Models`. They were added in commit `bcbb4bb` (2026-11-03, "U4h: world art for map v2"), and `WorldArtSetup.cs:13-14` says they are readable copies of the City 03 models. Shipped users: House 1-8 and 10 (Building prefabs, Backdrop), Roadside Billboard 2 (Building 6, 16), Dumpster 2 (Obstacle 0, 2, 10, 12), Box 1 and Box 2 (Obstacle 8, 11, 14 and others). Not referenced by any prefab, so not shipped: Gray Car, Red Car, Barricade 3, Manhole Covers 1, Road Fence 1, Roadside Billboard 1. City 03 itself comes from the Asset Store purchase (commit `2030b21`, no licence file); the publisher name comes from the folder name only.

## Key licence reading
- Asset Store EULA 2.2.1: a standard asset may be embedded in a larger product with substantial original content; reproduce, distribute, monetise and modify it as embedded. Standalone distribution is banned. A compiled game fits. No attribution clause. "Restricted Assets" have separate terms needing Unity's written approval; the files cannot show whether any asset is restricted.
- The licence is non-transferable and belongs to the purchaser. The seller on the Store must be the buying account or entity.

## Blockers for release
1. **major**: No licence file or proof of purchase for the 8 shipped Asset Store packs (City 03, Hovl, Kevin Iglesias, QAtmo, SFX pack, Toon Shaders Pro, Rio, Kyle). "Allowed" assumes Standard EULA. Needs order IDs and a listing check.
2. **major**: Inter OFL text and copyright are not shown to players; the OFL requires them with redistribution. Add a Licences screen or installed text (see CREDITS.md).
3. **major**: 12 raw WAV files of the Sci-Fi SFX pack ship in the build. Check the listing for an audio clause.
4. **minor**: Kevin Iglesias clips are copied into `Art/Characters`. Confirm his terms allow shipping animation data in a game.
5. **minor**: `PtkToon.shader` is a modified vendor shader fork. Confirm on the listing.
6. **minor**: DOTween DLL ships unused. Remove it or add its credit line.
7. **minor**: `unity-mcp` must stay editor-only. Unity plan versus revenue not verified.
8. **minor**: Robot Kyle listing is deprecated and the publisher unreadable; keep the My Assets record.

## Owner actions
- Fill Order IDs in `docs/store/THIRD_PARTY.md` from My Assets; the buying account must match the Store seller.
- Read each shipped package's listing licence field (Standard EULA vs Restricted or custom). Priority: Sci-Fi SFX pack, Hovl, Kevin Iglesias.
- Keep the Mixamo account and download date; re-read Adobe's current terms.
- Decide on the Credits/Licences screen and give the developer name.
- Optional cleanup: remove Cyberpunk and Layer Lab from the repo, remove DOTween, move the Toon Shaders Pro outline shaders out of Resources.
- Confirm the Unity plan fits the sales revenue.
