<#
.SYNOPSIS
  Builds an unsigned MSIX from the Unity release build (Client/Builds/StoreWin64). Windows PowerShell 5.1 compatible.
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
    [string]$MakeAppx = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $BuildDir) { $BuildDir = Join-Path $here '..\Client\Builds\StoreWin64' }
if (-not $OutDir) { $OutDir = Join-Path $here 'out' }
$BuildDir = (Resolve-Path $BuildDir).Path
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "Version must have four parts (e.g. 1.0.0.0), got '$Version'." }
if (-not (Test-Path (Join-Path $BuildDir 'PingTheKiller.exe'))) { throw "PingTheKiller.exe not found in $BuildDir. Run BuildScript.BuildWindowsRelease first." }

if (-not $MakeAppx) {
    $candidates = @(Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } | Where-Object { Test-Path $_ })
    if ($candidates.Count -eq 0) { throw 'makeappx.exe not found. Pass -MakeAppx <path> (Windows SDK, x64).' }
    $MakeAppx = $candidates[0]
}

function Escape-Xml([string]$s) { return [System.Security.SecurityElement]::Escape($s) }

$layout = Join-Path $here 'layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout, $OutDir | Out-Null
$layout = (Resolve-Path $layout).Path
$OutDir = (Resolve-Path $OutDir).Path

# 1. Copy the build; leave out Unity debug-only folders and symbols.
& robocopy $BuildDir $layout /E /XD '*_DoNotShip' '*_BackUpThisFolder_ButDontShipItWithYourGame' /XF '*.pdb' /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

# 2. Logos.
$assets = Join-Path $here 'Assets'
foreach ($n in 'StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png', 'Wide310x150Logo.png') {
    if (-not (Test-Path (Join-Path $assets $n))) { throw "Missing logo $n. Run Packaging\make-logos.ps1." }
}
New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null
Copy-Item (Join-Path $assets '*.png') (Join-Path $layout 'Assets')

# 3. Manifest from the template (UTF-8 without BOM).
$manifest = Get-Content (Join-Path $here 'AppxManifest.template.xml') -Raw
$manifest = $manifest.Replace('{{IdentityName}}', (Escape-Xml $IdentityName)).Replace('{{Publisher}}', (Escape-Xml $Publisher)).
    Replace('{{PublisherDisplayName}}', (Escape-Xml $PublisherDisplayName)).Replace('{{Version}}', $Version)
[System.IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

# 4. Pack.
$msix = Join-Path $OutDir ("PingTheKiller_{0}_x64.msix" -f $Version)
& $MakeAppx pack /v /o /d $layout /p $msix
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed with exit code $LASTEXITCODE" }
$item = Get-Item $msix
Write-Host ("OK {0} ({1:N1} MB)" -f $item.FullName, ($item.Length / 1MB))
