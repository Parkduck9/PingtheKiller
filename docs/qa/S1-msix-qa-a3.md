# S1-msix QA report (attempt 3) — Fable

Reviewed: commit `74ede35` on `claude/s1-msix` (implementer: Claude Opus 5.5). Reviewer: Claude Fable 5.2.
Tools: Windows PowerShell 5.1, `makeappx.exe` / `makepri.exe` from Windows SDK 10.0.26100.0 (x64). No install, no certificate, no system setting touched. Unity was not started.

**Verdict: PASS** — F1 and F2 from a2 are fixed; no regression found.

Not tested (forbidden by rule): install and tile rendering at 200 %, signing, WACK, Store certification.

## a2 findings re-checked (package in `Packaging/out`, SHA-256 `BB041D26...13079`, 68,241,394 bytes)

| a2 | Status | Evidence |
|---|---|---|
| F1 scale-200 unreachable (major) | Fixed | `makeappx unpack`: exactly one PRI, `resources.pri` (3,352 bytes); no `resources.scale-200.pri`. `makepri dump`: `Square150x150Logo.png`, `Square44x44Logo.png`, `StoreLogo.png`, `Wide310x150Logo.png` each have a `Scale-200` candidate plus the neutral one; `Square44x44Logo.png` also has all 10 `TargetSize-{16,24,32,48,256}` candidates (plated and `AlternateForm-UNPLATED`). 18 candidates for the 18 files in `Assets`. |
| F2 `priconfig.xml` indexed (minor) | Fixed | Dump has no `priconfig` entry; no `priconfig*` file in the package. |

## Script change
- Read: config goes to `<stage>-cfg` (outside the `/pr` root); `<packaging>` removed through `XmlDocument`, with a throw if it survives; throw if `resources.pri` is missing or any other `*.pri` is in the stage; `finally` removes all three temp folders. PowerShell 5.1 compatible.
- Default pack run (`-OutDir` scratch): exit 0, zero error/warning lines, 68,241,394 bytes (same size as the committed run's output), layout has one `resources.pri` and the placeholder identity, no `%TEMP%\ptk-msix-*` left.
- Earlier guards still work: `-Version 1.0.0.1` exit 1; `-IdentityName "Bad Name!"` exit 1, layout identity unchanged, no temp folder left.
- Unpacked package: 194 files, 18 Assets PNG, no DoNotShip / `.pdb` / logs.
- Doc §0 PRI sentence now matches the script.

## Notes (no action required)
- The scale-100 base files are unqualified (neutral) rather than named `.scale-100`; the index is valid as it is.
- Open owner items are unchanged from a1/a2: logo approval, real identity values, `companyName`, dev flags shipping, install test.

## State left behind
- `Packaging/out/PingTheKiller_1.0.0.0_x64.msix` untouched (hash unchanged). `Packaging\layout` was rewritten by my default run with the same placeholder content. QA packages, unpack folders and temp folders deleted.
