# L1-license QA, attempt 2 (2026-11-19)

Reviewer: Claude Fable 5.2 (implementer: Claude Sonnet 5.5). Commit reviewed: `7f4ae3d` on `claude/l1-license`, diffed against `e2f30f9`.
Text-only review: no Unity run, no downloads. Not legal advice.

**Verdict: PASS** (0 blocker, 0 major, 2 minor notes that do not block the merge).

## a1 findings

| a1 finding | Status | Check |
|---|---|---|
| F1 unity-mcp and Newtonsoft.Json ship | Fixed | Both rows added to the audit and THIRD_PARTY.md as shipped, MIT; blocker 2 raised to major with the strip-or-notice choice; notices in CREDITS.md |
| F2 Rio credited to Unity Technologies | Fixed | Publisher "unconfirmed" in all three files; credit line no longer names Unity; blocker 3 (major) and an owner action added. Kyle also marked unconfirmed |
| F3 SFX "raw WAV" major | Fixed | Now minor (blocker 5), wording says re-encoded and embedded |
| F4 OFL wording | Fixed | "accompany ... in a form the user can view" in audit and CREDITS.md |
| F5 second font | Fixed | `PerfectDOSVGA437.ttf` listed; "only Inter" sentence corrected |
| F6 Toon Shaders Pro C# | Fixed | Three scripts named in both tables and in the cleanup note |
| F7 counts, DOTween heading, derived assets | Fixed | "13 distinct models"; heading "Required by a licence (or safest to include)"; Kyle and world mesh assets named |

## Checks requested for attempt 2

- **unity-mcp version.** `Client/Packages/packages-lock.json` pins `...unity-mcp.git?path=/MCPForUnity#v10.3.0` with `hash aa5fc638d623f56178d50329d9ad8c541a57fe66`. `git ls-remote` on the public repository gives `refs/tags/v10.3.0^{}` = `aa5fc638d623f56178d50329d9ad8c541a57fe66`. The licence read at tag v10.3.0 is therefore the licence of the exact commit that is locked. The package cache folder has no `LICENSE` (the git path is the `MCPForUnity` subfolder), as the audit says.
- **unity-mcp copyright line.** "MIT License. Copyright (c) 2025 CoplayDev" matches the `LICENSE` at that tag.
- **Newtonsoft 3.2.2.** The lock file has `com.unity.nuget.newtonsoft-json` 3.2.2 at depth 1; the cached `package.json` says 3.2.2. "The MIT License (MIT) / Copyright (c) 2007 James Newton-King" matches `Third Party Notices.md` lines 8-10. The wrapper line "copyright 2022 Unity Technologies ApS", Unity Companion Licence, matches `LICENSE.md`.
- **MIT text in CREDITS.md.** Matches the standard text in the package notices.
- **PerfectDOSVGA437.** In the build log at line 5132 (79.7 kb). SRP Core `THIRD PARTY NOTICES.md` lists only RadeonRays 4.1, Bullet Physics SDK and Sobol sampler; no font entry. The claim "the package notices name no licence for it" is correct, and "unclear, low risk, minor" is a fair rating for content Unity distributes inside its own package.
- **Inter and DOTween lines.** Unchanged from attempt 1 and still match `OFL.txt` line 1 and `readme.txt` line 1.
- **Build log line numbers** 696, 889, 1099 are correct.
- **Severities.** Major: purchase proof, shipped MIT components, Rio publisher, OFL notice. Minor: SFX listing, Kevin Iglesias terms, shader fork, DOTween, SRP font, Kyle and Unity plan. All justified; nothing under-rated.

## Notes (minor, not blocking)

### N1 (minor) Newtonsoft notice reproduces one of four components
- Evidence: the package's `Third Party Notices.md` lists four MIT components: Newtonsoft.Json (2007 James Newton-King), Json.Net.Unity3D (2016 SaladLab), Newtonsoft.Json-for-Unity (2019 Kalle Jillheden), com.newtonsoft.json (2019 Mike Wuetherick). CREDITS.md carries only the first.
- Expected vs actual: the first is the library itself and is the essential one; whether code from the other three is inside the shipped DLL cannot be told from text files. If Newtonsoft stays in the build, the safe choice is to ship the whole `Third Party Notices.md`. Moot if unity-mcp is stripped.

### N2 (minor) Leftover wording
- Audit SFX row still says "audio packs can forbid raw-file redistribution" and the THIRD_PARTY.md SFX row still says "check the listing for audio terms", while blocker 5 now says the EULA has no audio clause. Not wrong (it refers to a possible custom listing licence), only uneven.
- Audit method still reports "492 shipped GUIDs", which predates the added package assemblies; the table is what counts.

## Not verifiable from here (unchanged, owner actions)
Order IDs and listing licence fields for the Asset Store packs, the Rio and Kyle publishers, Adobe's current Mixamo terms (primary page), Unity plan versus revenue.
