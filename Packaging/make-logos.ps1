# Renders the MSIX tile/logo PNGs from the game's own look (docs/ui/SLOP_DEFINITION.md section 8):
# near-black #101113, one Signal amber #FFB020, text #E8E6E1, Inter (Client/Assets/Resources/Fonts). Flat fills, no gradients,
# no glow, no shadows. The mark is a transmitting dot with two rings: "you are visible only while you transmit".
# Windows PowerShell 5.1 (System.Drawing). Output: Packaging/Assets/*.png. Run only when the artwork changes.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'Assets'
New-Item -ItemType Directory -Force $out | Out-Null
$ink = [System.Drawing.ColorTranslator]::FromHtml('#101113')
$amber = [System.Drawing.ColorTranslator]::FromHtml('#FFB020')
$text = [System.Drawing.ColorTranslator]::FromHtml('#E8E6E1')
$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$fonts.AddFontFile((Join-Path $root '..\Client\Assets\Resources\Fonts\Inter-Regular.ttf'))
$family = $fonts.Families[0]

# Draws the mark centred at (cx, cy) with outer radius r.
function Draw-Mark($g, [single]$cx, [single]$cy, [single]$r) {
    $brush = New-Object System.Drawing.SolidBrush $amber
    $dot = $r * 0.26
    $g.FillEllipse($brush, $cx - $dot, $cy - $dot, 2 * $dot, 2 * $dot)
    $pen = New-Object System.Drawing.Pen $amber, ([single]($r * 0.13))
    foreach ($k in 0.62, 1.0) {
        $rr = $r * $k * 0.93
        $g.DrawEllipse($pen, $cx - $rr, $cy - $rr, 2 * $rr, 2 * $rr)
    }
    $pen.Dispose(); $brush.Dispose()
}

function New-Logo([string]$name, [int]$w, [int]$h, [bool]$wide, [bool]$transparent = $false, [string]$dir = $out) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    if ($transparent) { $g.Clear([System.Drawing.Color]::Transparent) } else { $g.Clear($ink) }
    if ($wide) {
        $r = $h * 0.30
        Draw-Mark $g ([single]($h * 0.5)) ([single]($h * 0.5)) ([single]$r)
        $size = [single]($h * 0.15)
        $font = New-Object System.Drawing.Font $family, $size, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
        $brush = New-Object System.Drawing.SolidBrush $text
        $fmt = New-Object System.Drawing.StringFormat; $fmt.LineAlignment = 'Center'
        $rect = New-Object System.Drawing.RectangleF ([single]($h * 0.95)), 0, ([single]($w - $h * 0.95 - 8)), ([single]$h)
        $g.DrawString('Ping the Killer', $font, $brush, $rect, $fmt)
        $font.Dispose(); $brush.Dispose()
    } else {
        $r = [Math]::Min($w, $h) * 0.40
        Draw-Mark $g ([single]($w / 2)) ([single]($h / 2)) ([single]$r)
    }
    $g.Dispose()
    $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Scale-100 base files (referenced by the manifest) and scale-200 versions.
New-Logo 'StoreLogo.png' 50 50 $false
New-Logo 'Square44x44Logo.png' 44 44 $false
New-Logo 'Square150x150Logo.png' 150 150 $false
New-Logo 'Wide310x150Logo.png' 310 150 $true
New-Logo 'StoreLogo.scale-200.png' 100 100 $false
New-Logo 'Square44x44Logo.scale-200.png' 88 88 $false
New-Logo 'Square150x150Logo.scale-200.png' 300 300 $false
New-Logo 'Wide310x150Logo.scale-200.png' 620 300 $true
# Taskbar / list icon sizes: plated (ink background) and unplated (transparent, mark only).
foreach ($s in 16, 24, 32, 48, 256) {
    New-Logo ("Square44x44Logo.targetsize-{0}.png" -f $s) $s $s $false
    New-Logo ("Square44x44Logo.targetsize-{0}_altform-unplated.png" -f $s) $s $s $false $true
}
# Source for the Windows exe icon (BuildScript.Build sets it via PlayerSettings.SetIcons).
$iconDir = Join-Path $root '..\Client\Assets\Art\Icon'
New-Item -ItemType Directory -Force $iconDir | Out-Null
New-Logo 'AppIcon.png' 256 256 $false $false (Resolve-Path $iconDir).Path
Get-ChildItem $out -Filter *.png | Select-Object Name, Length