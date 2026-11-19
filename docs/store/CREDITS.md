# Credits (attribution text for the game and the store page)

Status 2026-11-19. The in-game screen exists (S3-credits): main menu, Credits, `CreditsScreen` with all copy in `CreditsCopy.cs`. It shows the credits list below, the Inter copyright line with the full text of `Resources/Fonts/OFL.txt` loaded at runtime, and the DOTween notice. It does not show unity-mcp or Newtonsoft. The developer name is `CreditsCopy.Developer` (NIPAD, set by the owner); the Robot Kyle and Rio publishers are still not named. Audit: `docs/qa/L1-license-audit.md`. Legal basis is as far as it could be verified from public terms; the owner must confirm against the purchase records.

## Required by a licence (or safest to include)

**Inter font (SIL Open Font License 1.1).** The OFL requires the copyright notice and licence text to accompany every copy of the font in a form the user can view (a licences file or screen). Inside a data file does not count. Put this in an in-game Licences screen or a `Licenses/` text file installed with the game, with the full text of `Client/Assets/Resources/Fonts/OFL.txt`:

> Inter. Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter). Licensed under the SIL Open Font License, Version 1.1.

**DOTween (free).** The licence ties the notice and readme duty to verbatim copies of the plugin; for a compiled game it is unclear. The DLL ships and no game script uses it. Safest: remove DOTween, or add the line below to the Licences screen:

> DOTween. Copyright (c) 2014-2026 Daniele Giardini, Demigiant. http://dotween.demigiant.com

**unity-mcp (MIT)** and **Newtonsoft.Json (MIT)** (stripped from the Store build by S2, so the in-game screen does not show them). Kept here for reference only. If either is ever left in a release build, add these notices, each with the full MIT text, to the Licences screen:

> MCP for Unity. MIT License. Copyright (c) 2025 CoplayDev.

> Json.NET (Newtonsoft.Json). The MIT License (MIT). Copyright (c) 2007 James Newton-King.

MIT text to include with each (from the package files): "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE." The unity-mcp copyright line is from its GitHub `LICENSE` at v10.3.0 (the package cache has no licence file); the Newtonsoft line is from the package's `Third Party Notices.md`.

## Not required by a licence found, recommended as courtesy

The Unity Asset Store EULA text read for this audit contains no attribution requirement. Mixamo says credit is optional. Kevin Iglesias, Hovl, QAtmo, Fries and Seagull, the SFX pack publisher and Toon Shaders Pro: terms are not in the repository and could not be read, so a credit is harmless and may be needed. Confirm on each Asset Store listing.

## Recommended in-game credits list (Main menu, Credits)

```
Ping the Killer
Design and programming: NIPAD

Characters: Robot Kyle and Rio (publishers to be confirmed by the owner before release; do not name Unity Technologies until confirmed)
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
