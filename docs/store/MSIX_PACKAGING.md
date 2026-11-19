# Packaging Ping the Killer for Microsoft Store

Target: Unity 6000.3 LTS, Windows Standalone, x64. First written 2026-10-27 against Microsoft Learn pages listed in §9; §0 was added 2026-11-19 (unit S1-msix) and records the path that was actually built and tested. Where §0 and the older sections disagree, **§0 wins**. Statements marked **(verify)** are not confirmed by a page I read; test them before relying on them. `docs/PLAN.md` §7 sets the plan: MSIX, or the Store's EXE/MSI path.

---

## 0. Tested build and pack path (2026-11-19)

**Scripting backend is Mono, not IL2CPP.** IL2CPP Windows support is not installed on the build machine (only the Mono variations of the Windows module), so the Store build uses Mono. Everything below about "IL2CPP" in §1-§8 is superseded: the output has `Mono\` / `MonoBleedingEdge\` and `Assembly-CSharp.dll`, no `GameAssembly.dll`. Switching to IL2CPP needs the module installed first; it is not required for certification.

**Packaging tool is `makeappx.exe` from the Windows SDK 10.0.26100.0, not `winapp`.** No download or install was needed. `winapp` (preview) is not used, so ignore §4 Steps 2, 4, 6, 7 except as background. Nothing is signed here: the Store signs the upload.

### Commands

```powershell
# 1. Release build (Mono, non-development) -> Client\Builds\StoreWin64\PingTheKiller.exe
& "C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe" -batchmode -quit `
  -projectPath "<repo>\Client" -executeMethod BuildScript.BuildWindowsRelease -logFile "<abs path>\build.log"

# 2. (only when the artwork changes) regenerate Packaging\Assets\*.png from the game's own palette and font
powershell -NoProfile -ExecutionPolicy Bypass -File Packaging\make-logos.ps1

# 3. Stage, write the manifest, pack -> Packaging\out\PingTheKiller_1.0.0.0_x64.msix
powershell -NoProfile -ExecutionPolicy Bypass -File Packaging\pack-msix.ps1 `
  -IdentityName "<Identity Name>" -Publisher "<Identity Publisher>" -PublisherDisplayName "<Publisher display name>" -Version 1.0.0.0
```

Without parameters the script uses placeholders (`PingTheKillerDev.PingtheKiller`, `CN=PingTheKillerDev`); that package cannot be uploaded to the Store, and the unsigned `.msix` **cannot be installed as-is** (`Add-AppxPackage` rejects an unsigned package). To try it locally the owner uses one of the two methods in the Korean owner steps §2: A (developer mode + `Add-AppxPackage -Register` on `Packaging\layout`) or B (a signed copy with the owner's own self-signed certificate).

What `pack-msix.ps1` does: stages in a fresh temp folder (so `Packaging\layout` is replaced only after a successful pack; a failed run never leaves a stale layout) with `Client\Builds\StoreWin64` (robocopy; excludes `*_DoNotShip`, `*_BackUpThisFolder_ButDontShipItWithYourGame` and `*.pdb`), copies the logos into `Assets` (scale-100 base files, scale-200 and `Square44x44Logo.targetsize-*` plated/unplated variants), fills `Packaging\AppxManifest.template.xml` (`runFullTrust`, `internetClient`, `internetClientServer`, `privateNetworkClientServer`, `Windows.Desktop` 10.0.17763.0, x64), builds a single `resources.pri` with `makepri.exe` that indexes every logo candidate (neutral, scale-200 and all targetsize variants; without it Windows cannot pick the qualified files). The script strips the `<packaging>` block that `makepri createconfig` adds, because that block splits the scale-200 candidates into a `resources.scale-200.pri` that only a bundle loads, and it stops with an error if any split `resources.*.pri` appears. It then runs `makeappx pack /v /o`. The version must have four parts and the 4th must be `0` (the Store's rule; the script stops with an error otherwise). `Packaging\layout\` and `Packaging\out\` are git-ignored. The exe icon is `Client\Assets\Art\Icon\AppIcon.png`, set in `BuildScript.Build()`; both it and the logos come from `make-logos.ps1`.

### What the shipped exe writes (audit, 2026-11-19)

With no command-line flags the game writes nothing inside its install folder. PlayerPrefs (HKCU registry) and Unity's `Player.log` (`%USERPROFILE%\AppData\LocalLow\<company>\<product>\`) are the only writes. The only code that writes files next to a path the caller chooses is the dev screenshot feature: `DevCapture` creates the `-ptk-shots <folder>` directory and writes PNGs there, and only when that flag is given (`DevCapture` exists only if a `-ptk-*` flag is present). The other `-ptk-*` flags (`-ptk-offline`, `-ptk-bots`, `-ptk-quit-after`, ...) do not write files. The dev flags (and `[DEV]` log lines) ship in the Store build on purpose, because the offline smoke test needs them; they are inert unless the game is started with `-ptk-*` arguments, and players never pass them. Changing `productName` or `companyName` moves the PlayerPrefs registry key, so existing settings are reset (fine before release).

### Verified results

See `docs/qa/S1-msix-impl-a1.md` for the run: pack succeeded with zero errors, unpack check, and an offline run from the staging folder with a before/after file listing.

---

## Owner steps (한국어)

아래는 소유자가 직접 해야 하는 일입니다. 에이전트는 인증서 생성, 개발자 모드 변경, 패키지 설치를 하지 않았습니다.

### 1. Partner Center에서 이름 예약하고 Identity 값 복사하기
1. Partner Center에서 새 제품(MSIX 또는 PWA 앱, 게임이면 Game)을 만들고 `Ping the Killer` 이름을 예약합니다.
2. 제품의 **Product identity** 페이지(메뉴 이름은 현재 UI에서 확인)에서 세 값을 복사합니다: `Package/Identity/Name`, `Package/Identity/Publisher` (`CN=...` 형태), `Package/Properties/PublisherDisplayName`.
3. 이 값으로 패키지를 다시 만듭니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Packaging\pack-msix.ps1 `
  -IdentityName "<Identity Name>" -Publisher "<Identity Publisher>" -PublisherDisplayName "<PublisherDisplayName>" -Version 1.0.0.0
```
4. `Client\ProjectSettings\ProjectSettings.asset`의 `companyName`(지금은 자리표시자 `PingTheKillerDev`)도 실제 회사/개발자 이름으로 바꿉니다. 바뀌면 PlayerPrefs 위치가 달라집니다.
5. Partner Center에는 `Packaging\out\PingTheKiller_1.0.0.0_x64.msix` 파일을 올립니다. 서명은 스토어가 합니다. 업로드 전에 로고 4종(`Packaging\Assets`)이 마음에 드는지 확인하세요. 로고는 게임 팔레트와 Inter 글꼴로 만든 임시안이며 소유자 승인이 필요합니다.

### 2. 내 PC에서 테스트 설치하기 (소유자가 직접)
방법 A: 개발자 모드 + 레이아웃 등록 (서명 불필요, 가장 간단)
1. Windows 설정 → 개인 정보 및 보안 → 개발자용 → **개발자 모드** 켜기.
2. 먼저 `Packaging\pack-msix.ps1`을 한 번 실행해 `Packaging\layout`을 만듭니다.
3. PowerShell에서: `Add-AppxPackage -Register "<repo>\Packaging\layout\AppxManifest.xml"`
4. 시작 메뉴에서 "Ping the Killer"를 실행합니다. 제거: `Get-AppxPackage *PingtheKiller* | Remove-AppxPackage`
5. 이 방식은 레이아웃 폴더를 그대로 쓰므로 폴더를 지우거나 옮기지 마세요. 끝나면 개발자 모드를 다시 끄세요.

방법 B: 직접 만든 자체 서명 인증서 (서명된 .msix를 설치해 보고 싶을 때)
1. 인증서 `Subject`가 매니페스트 `Publisher`와 정확히 같아야 합니다(기본값은 `CN=PingTheKillerDev`). 인증서와 .pfx는 저장소 밖에 두고 커밋하지 마세요.
2. 소유자가 직접 `New-SelfSignedCertificate`로 만들고, 인증서(.cer)를 **신뢰할 수 있는 사용자(Trusted People)** 저장소(`Cert:\LocalMachine\TrustedPeople`)에만 넣습니다. **신뢰할 수 있는 루트(Trusted Root)에는 절대 넣지 마세요.** 그런 다음 SDK의 `signtool.exe sign /fd SHA256 /f <pfx> /p <암호> <msix>`로 서명하고 `Add-AppxPackage <msix>`로 설치합니다.
3. 테스트가 끝나면 인증서를 신뢰 저장소에서 제거하세요.

테스트할 것: 시작 메뉴 실행, Offline Bots 한 판, Host(방화벽 허용 창이 뜨는지), 다른 PC에서 Join, 제거 후 잔여물.

### 3. runFullTrust 사유 (Partner Center → Submission options → Restricted capabilities에 붙여넣기)
```
Ping the Killer is a Unity Windows Standalone (x64) desktop game. It is a Win32 desktop application and must run as a full-trust packaged desktop app, which is why the runFullTrust capability is declared. It does not run other programs, install services, drivers or startup entries, or change system settings. It uses the network only for multiplayer matches: TCP port 7777 when hosting, outbound TCP when joining. Offline Bots mode needs no network and is the recommended way to test.
```

### 4. 소유자 확인 항목
- `companyName` 자리표시자, Identity 값 3개, 로고 승인.
- 설치 테스트, Store 인증(WACK 포함)은 아직 하지 않았습니다.

---


> **§1-§8 below are the original 2026-10-27 research. Superseded by §0 where they differ:** the build is Mono (not IL2CPP), packing uses `makeappx`/`makepri` via `Packaging\pack-msix.ps1` (not `winapp`), and the logo and manifest files are the ones in `Packaging\`. The Store and Partner Center content stays valid.

## 1. Recommendation

**Use MSIX, built from the Unity output folder with the `winapp` CLI (`winapp pack`).**

Reasons:
- The Store signs an MSIX for you. Microsoft's `winapp` packaging guide says: "The Microsoft Store will sign the MSIX for you, no need to sign before submission." The EXE/MSI route "highly recommends" a code-signing certificate that chains to the Microsoft Trusted Root Program, which costs money and time before 2026-10-30.
- You upload the file in Partner Center. The EXE/MSI route needs an HTTPS URL that hosts the installer, and the file at that URL must never change after submission.
- No installer to write, no silent-install switches to get right, and Windows handles uninstall.
- The timeline is the same: both routes go through certification, which takes up to three business days per Microsoft's MSI/EXE page. Plan for the submission on 10-30 to publish after that.

Costs of this choice:
- `runFullTrust` is a **restricted capability** that you must justify in Partner Center, which can add time to certification (§4).
- `winapp` CLI is in **public preview**: "Features and commands may change before the final release." Fallback: `makeappx.exe` from the Windows SDK, which the capability page mentions for manifest validation, or the MSIX Packaging Tool.
- Packaged files live in a read-only install folder. The game must not write next to the `.exe` (§5, step 1).

### Comparison

| | (a) MSIX | (b) EXE/MSI as a Win32 app |
|---|---|---|
| How the file is built | `winapp pack` on the Unity build folder. The MSIX Packaging Tool also works but converts an **installer** (MSI, EXE, App-V or scripts), so a loose Unity folder would first need an installer | Write an installer (Inno Setup, WiX, NSIS) around the Unity folder |
| Signing | Store signs the package. Local test needs a dev certificate | Certificate chaining to the Microsoft Trusted Root Program is "highly recommended" for the installer and every PE file inside |
| Where the file lives | Uploaded to Partner Center | Your own HTTPS URL. The binary there "must not change" after submission; use versioned URLs |
| Installer requirements | None beyond a valid package | Silent install (you give the silent switches), works from a standard user account, Start menu and Programs list entry, correct product info, clean uninstall, standalone/offline installer, no bundleware |
| Restricted capability | `runFullTrust` needs justification | Not applicable |
| Firewall prompt | Same Windows prompt as any desktop program; an optional manifest rule is possible (§3) | Same Windows prompt; the installer can add a firewall rule if it runs elevated |
| Failure modes | Manifest or identity mismatch, restricted-capability rejection | Silent-install failure, URL or signature problems, changed binary at the URL |
| Tooling maturity | `winapp` is preview; `makeappx` is stable | Mature tooling, more parts to manage |

Choose (b) only if Microsoft rejects `runFullTrust` for this game, or if `winapp pack` fails on the Unity output and you cannot fix it by the deadline.

## 2. Required manifest capabilities

Declare these in `Package.appxmanifest`:

| Capability | Namespace | Why |
|---|---|---|
| `internetClient` | default | Outbound connections to a host reached over the internet |
| `internetClientServer` | default | Inbound connections from the internet, needed when hosting on TCP 7777 |
| `privateNetworkClientServer` | default | Connections on home or work networks (LAN play) |
| `runFullTrust` | `rescap` | Required for a Win32 desktop game inside an MSIX |

Notes:
- Microsoft's schema page says `internetClientServer` makes `internetClient` unnecessary. Declaring all three is harmless and matches the plan; the Store listing shows the network capabilities to customers, which matches the privacy policy.
- Microsoft's capability page says a full-trust app runs at medium integrity as the user and already can do what `internetClient` grants. The three network capabilities describe the intent and are listed to customers; `runFullTrust` is the one the package needs. Packaging the game as an AppContainer app (`uap10:TrustLevel="appContainer"`) is not an option for a Unity standalone build.
- Declare nothing else (no camera, microphone, location, user account).

## 3. Windows Firewall behavior when hosting

The game opens a TCP listener on port 7777 only in Online → Host.

What happens (general Windows behavior from Microsoft support and administrator guides; no Microsoft page I read covers MSIX full-trust apps specifically, so **test it on a clean PC**):
- On the first listen on a non-loopback address, Windows shows a **Windows Security Alert** with **Private networks** and **Public networks** checkboxes and **Allow access** / **Cancel**.
- If the player cancels or closes the dialog, Windows does not ask again and the app stays blocked. Other players on the LAN or internet cannot reach the host.
- Fix later: Windows Security → Firewall & network protection → Allow an app through firewall → Change settings → select the game. Or an administrator runs `netsh advfirewall firewall add rule name="Ping the Killer" dir=in action=allow program="<path to exe>" enable=yes`.
- **Joining** a match is an outbound connection and shows no prompt.
- Port forwarding on the host's router is separate. The firewall prompt does not configure the router. The in-game Host screen should say so in plain words.
- If the listener binds `127.0.0.1` only (loopback), there is no prompt; remote players cannot connect.
- Offline Bots mode should open **no socket at all**. Confirm this, because a prompt during certification looks like a failure.

Options for the manifest:
1. **Do nothing (recommended for first release).** Show the prompt, and add an in-game message before the first Host: "Windows will ask whether to allow this game on private and public networks. Allow it to let other players connect." That text is copy for the Host screen; check it against `docs/ui/SLOP_DEFINITION.md` §4.
2. **Add an inbound rule in the package** with the `desktop2:FirewallRules` extension (schema: `desktop2:Rule`, Windows 10 version 1703 or later). Example:

```xml
<Extensions>
  <desktop2:Extension Category="windows.firewallRules">
    <desktop2:FirewallRules Executable="PingTheKiller.exe">
      <desktop2:Rule Direction="in" IPProtocol="TCP" LocalPortMin="7777" LocalPortMax="7777" Profile="private"/>
    </desktop2:FirewallRules>
  </desktop2:Extension>
</Extensions>
```

   Add `xmlns:desktop2="http://schemas.microsoft.com/appx/manifest/desktop/windows10/2"` to `<Package>` and to `IgnorableNamespaces`. I found Microsoft's schema page for `desktop2:Rule` but not a Microsoft page that documents the extension as a whole, and I did not confirm that the Store accepts it for a game or that it removes the prompt. **(verify)** Use `Profile="private"` rather than `all` so the game does not open itself to public networks by default.

Also **(verify)**: a packaged app's install path contains the version, so an update may trigger a new prompt. Test an update from version 1.0.0.0 to 1.1.0.0 before relying on it.

## 4. Steps — MSIX with `winapp` (superseded by §0: use `Packaging\pack-msix.ps1`; kept as background)

Unity build specifics below come from general Unity practice and were not re-checked against the Unity 6000.3 manual. **(verify)** against the Unity manual.

### Step 1. Build in Unity

1. Install the Visual Studio workloads IL2CPP needs on Windows (Desktop development with C++ and a Windows SDK). **(verify)**
2. Player Settings (Windows): Company Name, Product Name `Ping the Killer`, Version `1.0.0`, icon set, Scripting Backend **IL2CPP**, Architecture **x64**.
3. Build to `Builds\Win64\`. Expect `PingTheKiller.exe`, `UnityPlayer.dll`, `GameAssembly.dll`, `UnityCrashHandler64.exe`, `PingTheKiller_Data\` and `MonoBleedingEdge` or equivalent.
4. Make a clean copy for packaging and **leave out** the folders Unity names `*_BurstDebugInformation_DoNotShip` and `*_BackUpThisFolder_ButDontShipItWithYourGame`.
5. Confirm the game writes only to `Application.persistentDataPath` or similar user folders, never beside the executable. The installed folder is read-only. This also applies to the server logic: no log files in the install folder.
6. Run the packaged copy once as a plain folder before packaging: Offline Bots mode, then Host.

### Step 2. Install `winapp`

```powershell
winget install Microsoft.winappcli --source winget
winapp --help
```

### Step 3. Get the Store identity first

Do **Partner Center §5, steps 1–3** (account, name reservation) before you write the manifest, then copy the identity values (package name and publisher) from the product's identity page in Partner Center. **(verify the page name in the current Partner Center UI.)** The Store signs the package, but the manifest `Identity` must match the values Partner Center assigned.

### Step 4. Generate the manifest

```powershell
winapp manifest generate .\Package --package-name "<Identity/Name from Partner Center>" `
  --publisher-name "<Identity/Publisher from Partner Center>" --version 1.0.0.0 `
  --description "Ping the Killer" --entrypoint .\Package\PingTheKiller.exe `
  --logo-path .\store-assets\logo.png
```

`Package\` is the clean copy from Step 1. The Store requires the last number of an MSIX version to be `0` **(verify)**, so use `1.0.0.0`, `1.1.0.0` and so on.

### Step 5. Edit `Package.appxmanifest`

Check these parts of the generated file (Microsoft's CLI guide edits the same elements; it is written for a command-line tool, so **keep the Start menu entry and do not add an execution alias** for a game):

```xml
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">

  <Identity Name="<from Partner Center>" Publisher="<from Partner Center>" Version="1.0.0.0" ProcessorArchitecture="x64"/>

  <Properties>
    <DisplayName>Ping the Killer</DisplayName>
    <PublisherDisplayName><name shown in Partner Center></PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0"/>
  </Dependencies>

  <Applications>
    <Application Id="App" Executable="PingTheKiller.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Ping the Killer" Description="1-vs-7 chase game"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png"/>
    </Application>
  </Applications>

  <Capabilities>
    <Capability Name="internetClient"/>
    <Capability Name="internetClientServer"/>
    <Capability Name="privateNetworkClientServer"/>
    <rescap:Capability Name="runFullTrust"/>
  </Capabilities>
</Package>
```

- `MinVersion` 10.0.17763.0 is Windows 10 version 1809, the minimum Microsoft lists for `winapp` packaging. Raise it if the game needs a newer Windows.
- Replace the generated placeholder images in `Assets\` with the real logo set. The Store will not accept placeholder art.
- Optional: add the firewall extension from §3, option 2.

### Step 6. Package and test locally

Keep the certificate outside the package folder.

```powershell
winapp cert generate --manifest .\Package\Package.appxmanifest --output ..\devcert.pfx
winapp cert install ..\devcert.pfx            # run PowerShell as Administrator
winapp pack .\Package --manifest .\Package\Package.appxmanifest --executable PingTheKiller.exe `
  --cert ..\devcert.pfx --output PingTheKiller_1.0.0.0_x64_test.msix
Add-AppxPackage .\PingTheKiller_1.0.0.0_x64_test.msix
```

- `--executable` is required here: `winapp pack` uses the single `.exe` at the input root and errors when there are several, and Unity's output also has `UnityCrashHandler64.exe`.
- The dev certificate uses a public default password. It is for local testing only; do not upload it and do not commit it.
- Start the game from the Start menu. Test Offline Bots end to end, then Host, then Join from a second PC. Remove with `Get-AppxPackage *PingTheKiller* | Remove-AppxPackage`.

### Step 7. Make the package to upload

```powershell
winapp pack .\Package --manifest .\Package\Package.appxmanifest --executable PingTheKiller.exe `
  --no-sign --output PingTheKiller_1.0.0.0_x64.msix
```

Per Microsoft's guide the Store signs it, so the upload is unsigned. Microsoft recommends running the Windows App Certification Kit (WACK, part of the Windows SDK) on the package before submitting. **(verify the exact WACK steps for MSIX in the Windows SDK documentation.)**

## 5. Partner Center steps (MSIX)

1. **Account.** Open https://storedeveloper.microsoft.com → Individual developer → sign in with a Microsoft account → government ID and selfie verification → profile. Microsoft's page says the new flow has no registration fee. Use **Company** only if you publish for a business.
2. **Name.** https://aka.ms/submitwindowsapp → **New product** → **MSIX or PWA app**. Microsoft's reserve-name page says to click **Game** instead if you submit an MSIX game. Enter `Ping the Killer` → **Check availability** → **Reserve product name**. A reservation lasts three months.
3. **Identity.** Copy the package identity values into the manifest (Step 4 above).
4. **Create the submission** and fill in each section:
   - Pricing and availability: Free, markets, release date.
   - Properties: category, privacy policy URL (publish `PRIVACY_POLICY.md` first), system requirements.
   - Age ratings: complete the IARC questionnaire (`STORE_LISTING.md` §7).
   - Packages: upload `PingTheKiller_1.0.0.0_x64.msix`.
   - Store listings: `STORE_LISTING.md` §1–§8 for `en-us` and `ko-kr`.
   - Submission options: restricted-capability details and Notes for certification (below and `STORE_LISTING.md` §9).
5. **Submit.** Certification takes up to three business days on the MSI/EXE path; the MSIX path should be similar but I did not find a figure for it. Customers usually see the listing within about 15 minutes after approval (MSI/EXE page).

### `runFullTrust` justification (paste into Submission options)

```
Ping the Killer is a Unity Windows Standalone (x64) desktop game. It is a Win32 desktop application and must run as a full-trust packaged desktop app, which is why the runFullTrust capability is declared. It does not run other programs, install services, drivers or startup entries, or change system settings. It uses the network only for multiplayer matches: TCP port 7777 when hosting, outbound TCP when joining. Offline Bots mode needs no network and is the recommended way to test.
```

Microsoft's capability page says Partner Center detects restricted capabilities when you upload and asks for details on the Submission options page, and that your use may be approved or fail certification with feedback.

## 6. Steps — EXE/MSI route (only if you pick (b))

1. Build an installer around the clean Unity folder (Inno Setup, WiX or NSIS) that installs silently from a standard user account, adds a Start menu entry and a Programs list entry with correct name, publisher and version, uninstalls cleanly, contains everything (no download at install time) and adds nothing unrelated.
2. Sign the installer and every PE file in it with a certificate that chains to a CA in the Microsoft Trusted Root Program. `winapp az-sign` can sign `.exe` files with Azure Trusted Signing; it needs an Azure account and a signing profile.
3. Host the installer at a versioned **HTTPS** URL, for example `https://example.com/downloads/1.0.0/PingTheKiller-Setup.exe`. The Store downloads it from there; the file must not change after you submit.
4. Partner Center: **New product → EXE or MSI app** → reserve the name → submission → enter the package URL and the silent-install parameters → listing, age rating, privacy policy URL, notes for certification.
5. Run WACK and test with a standard user account before submitting.

## 7. What the developer does by hand

| # | Item | Done by |
|---|---|---|
| 1 | Create the Partner Center developer account (ID verification with a government ID and selfie) | Developer only. I cannot do identity checks |
| 2 | Reserve the name `Ping the Killer` | Developer, in Partner Center |
| 3 | Copy the package identity (name, publisher) into the manifest | Developer |
| 4 | Real logo and tile images, 6 screenshots | Developer / design |
| 5 | Publish the privacy policy page and paste the URL | Developer |
| 6 | Run the IARC questionnaire | Developer (accuracy is theirs) |
| 7 | Signing: nothing to buy for MSIX (the Store signs). For the EXE/MSI route, obtain a code-signing certificate or Azure Trusted Signing profile | Developer |
| 8 | Upload the package, fill the listing, add the certification notes, press Submit | Developer |
| 9 | Respond to the certification report if it fails | Developer |

## 8. Pre-submission checklist

- [ ] Build is x64, Release, Mono (see §0; IL2CPP is not installed), and the folder excludes the two Unity debug folders
- [ ] Game writes nothing beside the executable
- [ ] No analytics, ads or crash-upload service is enabled in the Unity project (the privacy policy says none)
- [ ] Offline Bots mode works with no network and no firewall prompt
- [ ] Online Host shows the firewall prompt once and works after Allow; Join works from a second PC
- [ ] Manifest: correct Identity, `Windows.FullTrustApplication`, four capabilities, real assets, version ends in `.0`
- [ ] Signed test package installs with `Add-AppxPackage`, starts from Start menu, uninstalls cleanly
- [ ] WACK run on the package
- [ ] Privacy policy URL is public and opens without login
- [ ] Notes for certification and the `runFullTrust` justification pasted
- [ ] Listing text matches the build (numbers, mode names)

## 9. Sources (Microsoft Learn unless noted; read 2026-10-27)

- Publish hub and submit paths: https://learn.microsoft.com/windows/apps/publish/
- Open a developer account: https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account
- Reserve an MSIX app name: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name
- Reserve an MSI/EXE app name: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/reserve-your-apps-name
- MSI/EXE certification process: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-certification-process
- Age ratings for MSIX apps: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/age-ratings
- Store Policies (10.5 Personal Information): https://learn.microsoft.com/en-us/windows/apps/publish/store-policies
- App capability declarations: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations
- Capability element schema: https://learn.microsoft.com/en-ca/uwp/schemas/appxpackage/uapmanifestschema/element-capability
- `desktop2:Rule` firewall schema: https://learn.microsoft.com/en-au/uwp/schemas/appxpackage/uapmanifestschema/element-desktop2-rule
- winapp CLI overview: https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli
- winapp CLI usage reference: https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/usage
- winapp guide, packaging an EXE: https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/packaging-cli
- MSIX Packaging Tool overview: https://learn.microsoft.com/en-us/windows/msix/packaging-tool/tool-overview
- Firewall prompt behavior (secondary, not Microsoft Learn): https://support.microsoft.com/help/4012988 and the administrator guides returned by search; nothing found for MSIX specifically

---

## Open items for the developer

1. Create the Partner Center account and reserve the name today. Individual accounts need ID verification and the name reservation is a prerequisite for the manifest identity.
2. After reserving, send the package identity name and publisher so the manifest values can be filled in. I could not read the Partner Center UI.
3. Decide MSIX (recommended) versus EXE/MSI. If MSIX, accept the `runFullTrust` approval step and the preview status of `winapp`.
4. Test on a clean Windows PC: install the signed test package, the Host firewall prompt, Join from a second PC, and an update from 1.0.0.0 to 1.1.0.0.
5. Confirm Offline Bots mode opens no socket (it would trigger the firewall prompt for the certification tester).
6. Confirm there is no analytics or crash-reporting upload in the Unity project, to match the privacy policy.
7. Verify the items marked **(verify)** in the live Partner Center, the Unity 6000.3 manual and the Windows SDK documentation before 2026-10-30. In particular: the version-revision rule, the identity page location, WACK for MSIX, and whether the `desktop2:FirewallRules` extension is accepted.
8. Provide the real logo set and the 6 screenshots; none are in the repository.
9. Keep the submission date realistic. Certification can take up to three business days, so a 2026-10-30 submission does not publish on 10-30.
