# Brief U9-skillfx: make every skill instantly recognisable when it is used (attempt 1)

- **Implementer:** Claude Opus. **QA:** Fable.
- **Time box:** about 90 minutes. Other units are running in parallel.
- **Where:** worktree `C:/Unity/MultiPlayGame/PtK-work/u9-skillfx`, branch `claude/u9-skillfx`, from main 3675877.
  - LFS is checked out.
  - Before the first Unity run, copy `C:/Unity/MultiPlayGame/PtK-work/q3-skill-reject/Client/Library` into `Client/Library`. Skip any `ArtifactDB-lock`.
- **Hard rules:**
  - Never commit, push, merge or download.
  - Never send keyboard or mouse input to the desktop.
  - Never kill a process you did not start.
  - Leave every Unity editor you did not launch alone.
  - Do not spawn subagents.
- **Skills:** first read the project skills under `.claude/skills/`: `unity-lighting-vfx`, `game-feel` and `unity-graphics`. Say which advice you applied.
- Also read `AGENTS.md`, `docs/ui/SLOP_DEFINITION.md`, and `docs/ui/HUD_SPEC.md` §14 (U7b skill effects: viewers, lines, sounds, safety). The safety rules are absolute.

## User report (Korean, translated)

"Even when a skill is used, it is very hard to tell which skill it was. Make effects so it can be understood intuitively."

## Context

- U7b already plays one world effect per skill, from Hovl markers, plus a result line and a sound.
- `Client/Assets/Scripts/World/SkillFx.cs` and `Editor/Setup/SkillFxSetup.cs` hold it. You own both files now.
- Many of the 9 effects reuse similar Hovl "Marker" rings with similar colours, so they do not read differently.
- The skills (Shared `SkillIds`) are:
  - **Killer:** Ping scanner, Overclock, Firewall block, Packet afterimage (passive).
  - **Survivor:** Dummy packet, Stealth relay, Traffic overload, Emergency restore, White noise.

## Spec

1. **One visual signature per skill.** Each skill must differ from all the others in at least **two** of: **shape**, **motion** and **colour**, so it can be told apart at a glance at 1280x720 from 10 m. Suggested direction; refine it with a reason:
   - **Ping scanner:** an expanding radar sweep ring with a ping blip, Killer red.
   - **Overclock:** red speed streaks and a heat shimmer trailing the Killer.
   - **Firewall block:** a hex-grid barrier wall that flashes up at the locked exit switch, orange.
   - **Dummy packet:** a cyan wireframe double of the caster that pops in.
   - **Stealth relay:** a soft blue dome that fades allies into a shimmer.
   - **Traffic overload:** a burst of jagged yellow data spikes from the caster.
   - **Emergency restore:** a green upward beam plus a plus-sign glyph on the restored ally.
   - **White noise:** scattered static/noise puffs, grey-white.
   - **Packet afterimage (passive):** the Killer's existing afterimage, unchanged unless you find it unreadable.
2. **A skill glyph.** A small icon-like glyph floats above the effect for about 1 s. Make it simple and generated: a mesh, a sprite drawn in code, or a TextMeshPro/UITK label.
   - Combined with the existing result line, the viewer knows *which* skill it was.
   - Use the same glyph on the skill HUD block, so the HUD and the world match.
   - SLOP: one meaning per colour. Check it against the HUD_SPEC colour tokens, and record new colour uses in HUD_SPEC.
3. **Own cast feedback.** The caster gets a short HUD flash of the glyph and the skill name for about 0.6 s, near the skill block. It must not cover the character's feet (HUD_SPEC §4.10 budget). Respect Reduced motion.
4. **Safety.**
   - Every viewer rule in HUD_SPEC §14.2 stays.
   - The Killer never sees Survivor-skill effects or glyphs that §14.2 hides, for example Dummy packet and White noise.
   - No effect is drawn at a position the viewer would not otherwise receive.
5. **Performance.** Prewarm or pool every effect at match start (`SkillFx.Warm`), so the first cast does not allocate or compile. Report the first-cast frame time from a dev log line.
6. **Assets.** Use only `Client/Assets/ThirdParty/Hovl Studio` and the other existing project art, plus generated meshes, materials and particles. Download nothing.

## Tests

EditMode tests for:
- the skill → signature table: all 9 distinct, each pair differing in at least 2 attributes;
- glyph mapping;
- viewer and safety rules: what the Killer sees per skill;
- the pool or prewarm, with no instantiate on first play;
- the own-cast HUD flash timing.

Update the U7b tests only where they pinned the old effect choice, and list each change.

## Gates

```
U="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe"
"$U" -batchmode -quit -projectPath Client -executeMethod <SkillFxSetup method, check the name> -logFile <log>     (if you regenerate)
"$U" -batchmode -projectPath Client -runTests -testPlatform EditMode -testResults <ABSOLUTE xml> -logFile <log>
"$U" -batchmode -quit -projectPath Client -executeMethod BuildScript.BuildWindows -logFile <log>
Client/Builds/Win64/PingTheKiller.exe ... -ptk-offline -ptk-bots 7 -ptk-start -ptk-role <killer|survivor> -ptk-want-skill <n> -ptk-skill-at <s> -ptk-shots <abs dir> -ptk-quit-after 200 -logFile <abs log>
```

- **Dev flags:** read `Client/Assets/Scripts/Dev` for how `-ptk-want-skill` and `-ptk-skill-at` work.
- **Contact sheet:** produce one screenshot per skill that can be cast. Run several short runs, one per skill id per role. Then make a contact sheet `docs/qa/U9-screens/contact-sheet.png` showing all of them side by side, labelled. A small editor or `System.Drawing` script is fine.
- **Pass criteria:**
  - All EditMode tests pass. Main had 1486.
  - BuildWindows succeeds.
  - `dotnet build -warnaserror` stays green.
- **After each Unity run:** revert files that changed only in line endings.

## Owned files

- `World/SkillFx.cs` and `Editor/Setup/SkillFxSetup.cs`.
- New FX files.
- The skill HUD glyph, in a new file. Hook it into the HUD with minimal edits to `UI/SkillRing.cs` or `KillerHud`/`SurvivorHud`.
- Generated FX assets under their existing folder.
- Client tests.
- `docs/ui/HUD_SPEC.md` §14.2 and the colour tokens.
- `docs/qa/U9-*`.

Units running in parallel:
- **U8:** capture FX and LaggingStutter, in new files.
- **IP-audit:** `UI/**` copy and the sync bar.
- **Q5b:** shutters and rails.
- **U4l:** building art.

Keep your edits to shared UI files tiny and localised.

## Report

Write `docs/qa/U9-skillfx-impl-a1.md` in English, ≤ 300 words. Cover:
- the signature table;
- the glyphs;
- the safety check;
- the prewarm numbers;
- the tests;
- the gates with counts;
- the contact sheet path;
- what is unverified;
- the risks.

Also write `docs/qa/U9-skillfx-commit-message.txt`.
