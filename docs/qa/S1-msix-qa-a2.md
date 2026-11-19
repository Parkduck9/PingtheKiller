# S1-msix QA report (attempt 2) — Fable

Reviewed: commit `83712d9` on `claude/s1-msix` (implementer: Claude Sonnet 5.5). Reviewer: Claude Fable 5.2.
Tools: Windows PowerShell 5.1, `makeappx.exe` / `makepri.exe` from Windows SDK 10.0.26100.0 (x64). No install, no certificate, no system setting touched. Unity was not started.

**Verdict: FAIL** — one major (F1). Everything else passes. The fix is small and was tried locally.

Not tested (forbidden by rule): install, signing, WACK, Store certification. That Windows ignores a split PRI inside a single msix is from documentation knowledge (resource-pack PRIs are loaded from resource packages in a bundle); what was verified locally is that the main `resources.pri` does not index the scale-200 files.

## a1 minors re-reviewed

| a1 | Status | Evidence |
|---|---|---|
| M1 unsigned "can be installed" | Fixed | §0 now says the unsigned `.msix` cannot be installed as-is and points to Methods A/B. |
| M2 cert store not named | Fixed | Korean Method B step 2 names `Cert:\LocalMachine\TrustedPeople` and forbids Trusted Root. |
| M3 exe icon | Fixed | Icon extracted from the rebuilt `PingTheKiller.exe` is the amber ping mark on #101113. `ProjectSettings.asset` adds only `m_BuildTargetIcons` for Standalone: 8 slots (1024..16), all the same GUID (`AppIcon.png`, 256x256, LFS-tracked). `BuildScript.Build()` throws if the asset is missing. Reasonable; the 1024/512 slots are upscaled from 256 but a Windows `.ico` tops out at 256. |
| M4 logo variants | **Half fixed — see F1.** targetsize variants work; scale-200 does not. |
| M5 layout after a failed run, revision 0 | Fixed | `-Version 1.0.1.1` -> "The 4th version part must be 0 ...", exit 1. `-IdentityName "Bad Name!"` -> makeappx schema error, exit 1, `layout\AppxManifest.xml` still the previous identity, no temp folder left. Good run: layout replaced, planted stale file gone, temp folders removed. |
| M6 old doc sections | Fixed | Superseded banner before §1, §4 heading, §8 checklist item now says Mono. |
| M7 dev flags ship | Accepted | Documented as deliberate and inert without `-ptk-*`. Owner decision. |

## Findings

**F1 (major) — scale-200 logos are packed but not reachable.**
Repro: `pack-msix.ps1` (any parameters) -> `makeappx unpack` -> `makepri dump /if resources.pri` and `/if resources.scale-200.pri`.
Expected: one `resources.pri` whose `Square150x150Logo.png`, `Square44x44Logo.png`, `StoreLogo.png`, `Wide310x150Logo.png` resources each carry a `Scale-200` candidate.
Actual: main `resources.pri` (3,032 bytes) has the neutral candidates and the 10 `TargetSize-*` candidates only. All four `Scale-200` candidates are in `resources.scale-200.pri` (1,272 bytes). That file is a resource-pack PRI; in a single non-bundle msix nothing loads it, so the four `*.scale-200.png` files are dead weight and tiles are upscaled from scale-100 on 150 %+ displays.
Cause: `makepri createconfig` writes `<packaging><autoResourcePackage qualifier="Language"/><autoResourcePackage qualifier="Scale"/><autoResourcePackage qualifier="DXFeatureLevel"/></packaging>` by default. `createconfig /?` in this SDK offers no switch to turn that off (`/cf /dq /pv /o /ex` only).
Fix (verified locally): after `createconfig`, delete the `<packaging>...</packaging>` element from the config, then run the same `makepri new`. Result: a single `resources.pri` (3,312 bytes), no `resources.scale-200.pri`, each of the four resources has `Scale-200` plus neutral, and `Square44x44Logo.png` keeps all 10 targetsize candidates. Add a check after `makepri new` that no `resources.*.pri` other than `resources.pri` exists in the stage.
Also in the doc: §0 says the PRI is "needed so the qualified logo names resolve"; true only after this fix. The impl report says the split file was "kept" without stating the consequence.

**F2 (minor) — `priconfig.xml` is indexed as a package resource.**
Evidence: main PRI dump has `<NamedResource name="priconfig.xml" ...><Value>priconfig.xml</Value>`; the file is not in the package. Cause: the config sits in the `/pr` root. Fix: write the config outside `$priRoot` (verified: the entry disappears).

**F3 (minor, note) — an interrupted run leaves `%TEMP%\ptk-msix-*`.**
Seen once when my own pipeline cut the child process off mid-run; a normal failing run cleans up (verified). No action needed beyond knowing the folder name.

## Other checks
- QA-parameter pack (`QaTest.PingTheKiller`, `CN=QA Test`, `QA & Sons`, 1.0.1.0): exit 0, 68,242,097 bytes, manifest values injected and escaped.
- Unpack: 18 Assets PNG, exe, `UnityPlayer.dll`, `UnityCrashHandler64.exe`, `PingTheKiller_Data`, `MonoBleedingEdge`, `D3D12`; no DoNotShip, `.pdb`, logs, or `priconfig.xml` file.
- Logo sizes all correct (scale-200 = 2x; targetsize 16/24/32/48/256 plated and transparent unplated). Flat, no SLOP gate tell.
- Rebuilt exe, offline survivor run 30 s from the unpacked folder: exit 0, no exception, listing identical before/after (213 entries).
- Doc §0 commands match the script parameters; Korean steps are accurate and safe.

## State left behind
- Implementer's `Packaging/out/PingTheKiller_1.0.0.0_x64.msix` untouched (SHA-256 `49C2023B...5474E9`).
- `Packaging\layout` restored by a final default-parameter run (placeholder identity, 1.0.0.0). All QA packages, unpack folders and temp folders deleted.
