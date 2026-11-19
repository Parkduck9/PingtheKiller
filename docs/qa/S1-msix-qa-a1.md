# S1-msix QA report (attempt 1) — Fable

Reviewed: commit `d4dd980` on `claude/s1-msix` (implementer: Claude Sonnet 5.5). Reviewer: Claude Fable 5.2.
Tools: Windows PowerShell 5.1.26100, `makeappx.exe` from Windows SDK 10.0.26100.0 (x64). No install, no certificate, no system setting touched. Unity was not started.

**Verdict: PASS** (no blocker, no major; 7 minors, none blocks the commit).

Not tested (out of QA scope by rule): `Add-AppxPackage` install, signing, WACK, Partner Center upload, firewall prompt, update 1.0 -> 1.1. Manifest correctness is judged against the documented schema and by `makeappx pack` validation only; Store certification itself is unverified.

## Checks

| # | Check | Result |
|---|---|---|
| 1 | Manifest | PASS. `Identity` (Name, Publisher, Version, `ProcessorArchitecture="x64"`), `EntryPoint="Windows.FullTrustApplication"`, `Executable="PingTheKiller.exe"` (file exists at package root), `rescap` namespace URI correct and listed in `IgnorableNamespaces="uap rescap"`, capabilities in schema order (`Capability` x3, then `rescap:Capability`), `TargetDeviceFamily Windows.Desktop` Min 10.0.17763.0 / MaxTested 10.0.26100.0, `Resource en-us`, all four logo paths resolve to files in the package. `makeappx pack` validates the manifest with zero errors. |
| 2 | Script | PASS. Run with `-IdentityName QaTest.PingTheKiller -Publisher "CN=QA Test" -PublisherDisplayName "QA & <Sons> x" -Version 1.0.1.0`: exit 0, "Package creation succeeded", output `PingTheKiller_1.0.1.0_x64.msix` (68,271,868 bytes). Manifest line 8 `Name="QaTest.PingTheKiller" Publisher="CN=QA Test" Version="1.0.1.0"`, line 12 `QA &amp; &lt;Sons&gt; x`; file parses as XML; UTF-8 without BOM (first bytes `3C 3F 78`). Stale file planted in `layout\` was gone after the run. Failures are loud: missing `-BuildDir` -> Resolve-Path error, exit 1; empty build dir -> "PingTheKiller.exe not found ... Run BuildScript.BuildWindowsRelease first.", exit 1; `-Version 1.0.1` -> "Version must have four parts", non-zero exit; `-IdentityName "Bad Name!" -Publisher NotADN` -> "makeappx pack failed with exit code 1", exit 1, no package written. |
| 3 | Package contents | PASS. `makeappx unpack`: `PingTheKiller.exe`, `UnityPlayer.dll`, `UnityCrashHandler64.exe`, `PingTheKiller_Data\`, `MonoBleedingEdge\`, `D3D12\`, `Assets\` (4 PNG), `AppxManifest.xml`, `AppxBlockMap.xml`; 179 files. No `*_DoNotShip`, no `BackUpThisFolder`, no `.pdb`/`.mdb`, no logs, no screenshots, no `.md`/`.txt`. The only "Burst" hits are runtime files (`Unity.Burst.dll`, `Unity.Burst.Unsafe.dll`, `lib_burst_generated.dll`), which must ship. `boot.config` has no `player-connection-debug` line (non-development player); `app.info` = `PingTheKillerDev` / `Ping the Killer`. |
| 4 | Read-only folder | PASS. Unpacked exe run with `-logFile <scratch>\player.log -ptk-offline -ptk-bots 7 -ptk-start -ptk-role survivor -ptk-quit-after 60`: exit 0, 256 log lines, no exception. Recursive listing (path, size, mtime ticks) before/after: 197 / 197 entries, identical. Code grep (`Client/Assets` without Editor/Tests, `Shared`, `Server.Core`): the only file writes are `DevCapture.cs:94` (`Directory.CreateDirectory(args.Shots)`) and `DevCapture.cs:954` (`File.WriteAllBytes`), both behind `-ptk-shots`; `DevCapture` is added only when `devArgs.Active` (`ClientApp.cs:160-162`). `GameSettings.cs:23-24` uses PlayerPrefs (registry). No `Application.dataPath`/`persistentDataPath` use. Claim confirmed. |
| 5 | Logos | PASS on size: StoreLogo 50x50, Square44x44 44x44, Square150x150 150x150, Wide310x150 310x150. Flat amber dot with two rings on #101113, Inter text on the wide tile; no gradient, glow or shadow (no SLOP V-gate tell). See M3, M4. |
| 6 | Doc | PASS with minors. §0 commands and parameter names match the script; the description of what the script does is accurate. Korean steps tell the owner to do Developer Mode / certificate work personally and never mention Trusted Root. See M1, M2, M6. |
| 7 | ProjectSettings | PASS. Diff is exactly `companyName`, `productName`, `bundleVersion` (1.0 -> 1.0.0). |

## Findings

**M1 (minor) — doc says the unsigned package "can be test-installed locally".**
Repro: read `docs/store/MSIX_PACKAGING.md` §0, sentence after the commands. Expected: states that the unsigned `.msix` cannot be installed as-is; local test is layout registration (Method A) or a signed copy (Method B). Actual: "that package packs and can be test-installed locally". `Add-AppxPackage` rejects an unsigned `.msix`. The Korean steps below are correct, so the risk is only confusion.

**M2 (minor) — Method B does not name the certificate store.**
Repro: Korean owner steps §2 방법 B step 2: "신뢰하도록 설정한 뒤". Expected: names `Cert:\LocalMachine\TrustedPeople` (신뢰할 수 있는 사용자) and says not to use Trusted Root. Actual: store not named; an owner following a web search may put a self-signed cert into Trusted Root. Not unsafe as written, but one clause would close it.

**M3 (minor) — exe has no icon.**
Evidence: `ProjectSettings.asset:300-301` `m_BuildTargetIcons: []`, `m_BuildTargetPlatformIcons: []`. Expected: game icon in the exe. Actual: default Unity icon, which shows in the window title bar and Alt+Tab even when the Start tile uses the manifest logo. Owner art decision; not in the unit's stated scope.

**M4 (minor) — only scale-100 logos, no `targetsize` variants.**
Evidence: `Packaging/Assets` has four files. Expected for a polished listing: `Square44x44Logo.targetsize-*_altform-unplated.png` and scale-200/400. Actual: taskbar and Start list icons are upscaled and plated on #101113. The package is valid without them. The ring mark is also a generic target/radio shape; the owner approves the art.

**M5 (minor) — a failed or parameterised run leaves `Packaging\layout` with that run's identity.**
Repro: run with `-IdentityName "Bad Name!"`; makeappx fails; `layout\AppxManifest.xml` keeps the bad values. Method A (`Add-AppxPackage -Register layout\AppxManifest.xml`) then registers whatever was packed last. Expected: doc step A.2 says to check the Identity line, or the script removes the layout manifest on failure. Also the script does not enforce the Store rule that the fourth version part is 0 (doc marks it "(verify)").

**M6 (minor) — older doc sections contradict §0.**
§1-§8 still say IL2CPP and `winapp`; §8 checklist item 1 reads "Build is IL2CPP". §0 says it wins, so this is only a reading hazard.

**M7 (minor, beyond the brief) — dev flags and `[DEV]` logging ship in the Store build.**
Evidence: `player.log` has `[DEV] ...` lines; `-ptk-shots <dir>` creates a folder and writes PNGs anywhere the user can write. Harmless inside a package (a write into the install folder fails), and the implementer disclosed it. Decide before release whether the Store build should compile them out.

## Notes
- The implementer's package `Packaging/out/PingTheKiller_1.0.0.0_x64.msix` is untouched (SHA-256 `6D7E7F8B...99A0DB`, 68,271,849 bytes). A default-parameter rerun gives the same size.
- QA runs rewrote `Packaging\layout`; it was restored by a final default-parameter run (Identity `PingTheKillerDev.PingtheKiller`, 1.0.0.0). The QA package `PingTheKiller_1.0.1.0_x64.msix` was deleted from `Packaging\out` because it carried a fake identity; unpack folders were deleted.
- PowerShell 5.1 stripped the embedded double quotes from my `-PublisherDisplayName` test value before the script saw it, so `"` escaping was not exercised; `&`, `<`, `>` were.
