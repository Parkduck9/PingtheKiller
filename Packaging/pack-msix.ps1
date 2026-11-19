<#
.SYNOPSIS
  Builds an unsigned MSIX from the Unity release build (Client/Builds/StoreWin64, made by build-release.ps1). Windows PowerShell 5.1 compatible.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Packaging\pack-msix.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File Packaging\pack-msix.ps1 -IdentityName "12345Studio.PingtheKiller" `
    -Publisher "CN=00000000-0000-0000-0000-000000000000" -PublisherDisplayName "Studio" -Version 1.0.1.0
.NOTES
  Uses only the Windows SDK makeappx.exe. Nothing is signed or installed: the Store signs the upload, and a local test
  install is an owner step (docs/store/MSIX_PACKAGING.md). Identity defaults are placeholders, not valid for the Store.
#>
param(
    [string]$IdentityName = 'PingTheKillerDev.PingtheKiller',
    [string]$Publisher = 'CN=PingTheKillerDev',
    [string]$PublisherDisplayName = 'PingTheKillerDev',
    [string]$Version = '1.0.0.0',
    [string]$BuildDir = '',
    [string]$OutDir = '',
    [string]$MakeAppx = '',
    [string]$MakePri = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $BuildDir) { $BuildDir = Join-Path $here '..\Client\Builds\StoreWin64' }
if (-not $OutDir) { $OutDir = Join-Path $here 'out' }
$BuildDir = (Resolve-Path $BuildDir).Path
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "Version must have four parts (e.g. 1.0.0.0), got '$Version'." }
if ($Version -notmatch '\.0$') { throw "The 4th version part must be 0 (Microsoft Store reserves the revision), e.g. 1.0.1.0. Got '$Version'." }
if (-not (Test-Path (Join-Path $BuildDir 'PingTheKiller.exe'))) { throw "PingTheKiller.exe not found in $BuildDir. Run Packaging\build-release.ps1 first." }
# Never pack editor tooling (unity-mcp, Newtonsoft.Json): a dev build (BuildScript.BuildWindows) contains them.
. (Join-Path $here 'release-guard.ps1')
Assert-NoEditorTooling $BuildDir

if (-not $MakeAppx) {
    $candidates = @(Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } | Where-Object { Test-Path $_ })
    if ($candidates.Count -eq 0) { throw 'makeappx.exe not found. Pass -MakeAppx <path> (Windows SDK, x64).' }
    $MakeAppx = $candidates[0]
}

function Escape-Xml([string]$s) { return [System.Security.SecurityElement]::Escape($s) }

$layout = Join-Path $here 'layout'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

# Stage in a fresh temp dir. Packaging\layout is replaced only after a successful pack, so a failed run never leaves a
# half-built or wrong-identity layout behind.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ('ptk-msix-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
try {
    # 1. Copy the build; leave out Unity debug-only folders and symbols.
    & robocopy $BuildDir $stage /E /XD '*_DoNotShip' '*_BackUpThisFolder_ButDontShipItWithYourGame' /XF '*.pdb' /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

    # 2. Logos (base scale-100 files plus scale-200 and targetsize variants).
    $assets = Join-Path $here 'Assets'
    foreach ($n in 'StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png', 'Wide310x150Logo.png') {
        if (-not (Test-Path (Join-Path $assets $n))) { throw "Missing logo $n. Run Packaging\make-logos.ps1." }
    }
    New-Item -ItemType Directory -Force (Join-Path $stage 'Assets') | Out-Null
    Copy-Item (Join-Path $assets '*.png') (Join-Path $stage 'Assets')

    # 3. Manifest from the template (UTF-8 without BOM).
    $manifest = Get-Content (Join-Path $here 'AppxManifest.template.xml') -Raw
    $manifest = $manifest.Replace('{{IdentityName}}', (Escape-Xml $IdentityName)).Replace('{{Publisher}}', (Escape-Xml $Publisher)).
        Replace('{{PublisherDisplayName}}', (Escape-Xml $PublisherDisplayName)).Replace('{{Version}}', $Version)
    $manifestPath = Join-Path $stage 'AppxManifest.xml'
    [System.IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

    # 4. resources.pri: Windows resolves the qualified logo names (scale-200, targetsize-*) through a PRI index.
    #    Index only the Assets folder (a temp copy), not the whole Unity build. The config lives in a separate temp
    #    folder so it is not indexed as a resource itself.
    if (-not $MakePri) { $MakePri = Join-Path (Split-Path -Parent $MakeAppx) 'makepri.exe' }
    if (-not (Test-Path $MakePri)) { throw "makepri.exe not found next to makeappx ($MakePri). Pass -MakePri <path>." }
    $priRoot = $stage + '-pri'
    $priCfgDir = $stage + '-cfg'
    New-Item -ItemType Directory -Force (Join-Path $priRoot 'Assets') | Out-Null
    New-Item -ItemType Directory -Force $priCfgDir | Out-Null
    Copy-Item (Join-Path $assets '*.png') (Join-Path $priRoot 'Assets')
    $priConfig = Join-Path $priCfgDir 'priconfig.xml'
    & $MakePri createconfig /cf $priConfig /dq en-US /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makepri createconfig failed with exit code $LASTEXITCODE" }

    #    createconfig always adds <packaging><autoResourcePackage .../></packaging>, which makes `makepri new` split the
    #    Scale/Language candidates into resources.<qualifier>.pri files. Those are resource-pack PRIs that only a bundle
    #    loads; in this single msix they would be dead. Remove <packaging> so one resources.pri holds every candidate.
    $cfgXml = New-Object System.Xml.XmlDocument
    $cfgXml.PreserveWhitespace = $true
    $cfgXml.Load($priConfig)
    $packagingNodes = @($cfgXml.DocumentElement.SelectNodes('packaging'))
    foreach ($node in $packagingNodes) { [void]$node.ParentNode.RemoveChild($node) }
    if ($cfgXml.DocumentElement.SelectNodes('packaging').Count -ne 0) { throw 'Could not remove <packaging> from the PRI config.' }
    $cfgSettings = New-Object System.Xml.XmlWriterSettings
    $cfgSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $cfgWriter = [System.Xml.XmlWriter]::Create($priConfig, $cfgSettings)
    try { $cfgXml.Save($cfgWriter) } finally { $cfgWriter.Close() }

    & $MakePri new /pr $priRoot /cf $priConfig /mn $manifestPath /of (Join-Path $stage 'resources.pri') /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makepri new failed with exit code $LASTEXITCODE" }
    if (-not (Test-Path (Join-Path $stage 'resources.pri'))) { throw 'makepri new did not write resources.pri.' }
    $splitPri = @(Get-ChildItem $stage -Filter '*.pri' -File | Where-Object { $_.Name -ne 'resources.pri' })
    if ($splitPri.Count -ne 0) {
        throw ('makepri split the index into resource-pack PRIs (' + (($splitPri | ForEach-Object { $_.Name }) -join ', ') +
            '); a single msix would not load them. Check the <packaging> removal.')
    }

    # 5. Pack.
    $msix = Join-Path $OutDir ("PingTheKiller_{0}_x64.msix" -f $Version)
    & $MakeAppx pack /v /o /d $stage /p $msix
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed with exit code $LASTEXITCODE" }

    # 6. Success: replace Packaging\layout (used by Add-AppxPackage -Register) with exactly what was packed.
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    New-Item -ItemType Directory -Force $layout | Out-Null
    & robocopy $stage $layout /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy to layout failed with exit code $LASTEXITCODE" }
}
finally {
    foreach ($d in $stage, ($stage + '-pri'), ($stage + '-cfg')) { if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue } }
}
$item = Get-Item $msix
Write-Host ("OK {0} ({1:N1} MB)" -f $item.FullName, ($item.Length / 1MB))