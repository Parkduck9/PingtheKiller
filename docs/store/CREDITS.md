# Credits (attribution text for the game and the store page)

Status 2026-11-19. Audit: `docs/qa/L1-license-audit.md`. Legal basis is as far as it could be verified from public terms; the owner must confirm against the purchase records.

## Required by a licence

**Inter font (SIL Open Font License 1.1).** The OFL requires the copyright notice and licence text to travel with the font whenever it is redistributed. The game ships the font in the build, so ship the text too. Put this in an in-game Licences screen or a `Licenses/` file installed next to the game, with the full text of `Client/Assets/Resources/Fonts/OFL.txt`:

> Inter. Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter). Licensed under the SIL Open Font License, Version 1.1.

**DOTween (free).** The licence says copies of the plugin must keep the original copyright notice and readme. The plugin DLL is compiled into the build and no game script uses it. Safest: remove DOTween (no code needs it), or add the line below to the Licences screen:

> DOTween. Copyright (c) 2014-2026 Daniele Giardini, Demigiant. http://dotween.demigiant.com

## Not required by a licence found, recommended as courtesy

The Unity Asset Store EULA text read for this audit contains no attribution requirement. Mixamo says credit is optional. Kevin Iglesias, Hovl, QAtmo, Fries and Seagull, the SFX pack publisher and Toon Shaders Pro: terms are not in the repository and could not be read, so a credit is harmless and may be needed. Confirm on each Asset Store listing.

## Recommended in-game credits list (Settings or Main menu, Credits)

```
Ping the Killer
Design and programming: <developer name>

Characters: Robot Kyle and Rio (Unity Technologies)
Animations: Human Animations by Kevin Iglesias; Mixamo (Adobe)
City environment: City 03 by Fries and Seagull
Props: Explosive Device C4 and Defuse Kit by QAtmo
Visual effects: Hovl Studio
Shaders: Toon Shaders Pro
Sound effects: Sci-Fi Weapons - Bullet Hell Sound Effects Pack
Font: Inter by The Inter Project Authors (SIL Open Font License 1.1)
Tween library: DOTween by Demigiant
```

Remove any line whose package is replaced or not shipped. Cyberpunk RPG GUI Pack and Layer Lab CartoonBoxMap are not in the build and need no credit.
