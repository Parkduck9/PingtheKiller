# Brief Q5b-client: shutters and vault rails in the Unity client (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Time box:** about 100 minutes.
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/q5-client`, branch `claude/q5-client`, from main 3675877.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/q3-skill-reject/Client/Library` into `Client/Library`. Skip any `ArtifactDB-lock`.
- **Hard rules:**
  - Never commit, push or download.
  - The only merge you may do is the one in step 0.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
- **Skills:** read the project skills `unity-animation`, `unity-lighting-vfx`, `game-feel` and `unity-graphics` under `.claude/skills/`.
- Read `AGENTS.md`, `docs/ui/SLOP_DEFINITION.md` and `docs/ui/HUD_SPEC.md`.

## Dependency: the Shared protocol comes from Q5a

The server unit Q5a-rules runs in parallel in `C:/Unity/MultiPlayGame/PtK-work/q5-rules` (branch `claude/q5-rules`). Its brief is at `C:/Unity/MultiPlayGame/PtK-work/q5-rules/docs/briefs/Q5a-rules-a1.md`; read it for the rules and numbers.

**Step 0.**
1. When `C:/Unity/MultiPlayGame/PtK-work/q5-rules/SHARED_READY` exists, read it.
2. Run `git merge --no-edit <hash from that file>` in your worktree. That brings in the S0 commit: the Shared types, the packet and the flags.
3. Until then, do the parts that do not need Shared: art choice, prefab building, animation clip choice, the copy, the HUD_SPEC section and a test skeleton.
4. If you run out of such work, wait with a background shell loop that checks for the file every 30 s. Do not loop through tool calls.
5. If `SHARED_CHANGED` appears later, merge again from `claude/q5-rules`.

Run `git log claude/q5-rules` to see the commits.

## Spec

### S1 World props

1. **Shutter:** a roll-down metal shutter in a frame spanning the alley gap, with three looks:
   - `Up`: rolled up in its housing, thin, not in the way;
   - `Down`: a closed panel about 1.2 m high, readable from both sides;
   - `Broken`: a bent panel lying flat, or torn halves.
2. **Rail:** a low safety rail about 3 m long and 1.0 m high, with a small marker at the vault point.
3. **Where the art comes from:**
   - Build from existing project art only: the City asset props already used by `Editor/Setup/WorldArtSetup.cs`, `Assets/ThirdParty/*`, or simple generated meshes in the WorldArt style.
   - Download nothing.
   - Positions come from the Shared static data. Nothing is hard-coded on the client.
   - Generate the props through the WorldArt setup path, or a sibling setup, so that `WorldArt.Matches` and the existing WorldArt tests still hold.
4. **Readability:**
   - an interactable `Up` shutter or rail within range shows the existing prompt row: `E  Pull shutter`, `E  Vault`, `Hold E  Force shutter` (Killer);
   - follow the SLOP copy rules;
   - the Killer sees shutters and rails as world geometry like any prop.
5. **Visibility:** the props are low and do not block sight. They must not hide the Survivor world tags or the Killer markers more than existing low obstacles do.

### S2 Animation and feedback

1. **Survivor pull:** a short pull-down arm motion. Survivor and Killer vault: a hop or vault clip, from Kevin Iglesias Human Animations (`Movement/Jump`) or the existing Mixamo set. Time it to the server durations (0.3 s pull, 0.8 s and 1.6 s vaults, 1.0 s shutter vault). Use the WorldState action flags; never predict.
2. **Killer stunned (2 s):**
   - a stagger pose;
   - a small star/glitch burst over the head, using a Hovl effect tinted to the Killer colour;
   - for the Killer player, a short screen impulse plus the line `Stunned` on the Killer result line, kept brief.
   Respect Reduced motion.
3. **Killer break:** a hold progress on the existing prompt/progress UI, and a hitting motion.
4. **Sounds:** procedural cues, following `Audio/V2Cues.cs` (download nothing):
   - a metallic slam on the shutter going down, positional, heard by everyone in range;
   - a crunch on break;
   - a soft step-thud on vault.
5. **Late join or reconnect:** shutter states render from the state packet.

### S3 HUD_SPEC

Add a section "Shutters and rails (11/17, Q5)" covering:
- the visuals;
- the prompts;
- the stun feedback;
- the sounds;
- the safety notes. Nothing reveals a Silent Survivor: an acting Survivor is Signal anyway.

## Tests

EditMode tests for:
- state → visual mapping;
- prompt selection by range and role;
- animation trigger mapping from the flags;
- the stun feedback timing;
- the WorldArt catalog still matching the map;
- the props placed exactly at the Shared positions.

## Gates

```
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -quit -projectPath Client -executeMethod <WorldArt setup method, if you regenerate art> -logFile <log>
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <ABSOLUTE xml> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
Client/Builds/Win64/PingTheKiller.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -ptk-offline -ptk-bots 7 -ptk-start -ptk-role <killer|survivor> -ptk-shots <abs dir> -ptk-quit-after 240 -logFile <abs log>
```

- **Live runs:**
  - The offline exe embeds `Server.Core`. After merging S0 it only has Q5a's protocol, not Q5a's rules.
  - Before the final live run, merge the then-current `claude/q5-rules` working state if Q5a has committed more. If it has not, run with what exists and say so.
  - Optionally, build the offline server from the q5-rules worktree's uncommitted `Server.Core` by copying files. Only do this if `Server.Core` is referenced as source; check how the offline server is built.
- **Pass criteria:**
  - All EditMode tests pass. Main had 1486.
  - BuildWindows succeeds.
  - `dotnet build -warnaserror` stays green.
- **Screenshots** at 1280x720: a shutter Up, a shutter Down and a rail, plus a vault or stun if live rules exist.
- **Paths:** shots go in `docs/qa/Q5b-screens/`, logs in `docs/qa/Q5b-runs/`.
- **After each Unity run:** revert files that changed only in line endings.

## Owned files

- New client files under `Client/Assets/Scripts/World/` (for example `ChaseProps*.cs`), `Audio/`, `UI/` (prompt copy) and `Editor/Setup/`.
- Minimal hooks in `ClientApp.cs` and `Net/NetClient.cs`.
- Generated art under `Client/Assets/Art/World/**` and `Resources/WorldArt.asset`.
- Client tests.
- `docs/ui/HUD_SPEC.md` (new section).
- `docs/qa/Q5b-*`.

Units running in parallel:
- U8-hitfeel owns `LaggingStutter`, `CharacterMotion`, `KillerAttackKick`, `SkillFx` and `CharacterViews`. If you need an animation hook there, add a small new file instead and note it.
- IP-audit owns the `UI/**` copy files. Put your new copy in a new file, e.g. `UI/ChasePropCopy.cs`.

## Report

Write `docs/qa/Q5b-client-impl-a1.md` in English, ≤ 300 words. Cover:
- the merge hash used;
- the art built;
- the animations;
- the sounds;
- the prompts;
- the tests;
- the gates with counts;
- the shots;
- what is unverified (for example, no live server rules);
- the risks.

Also write `docs/qa/Q5b-client-commit-message.txt`.
