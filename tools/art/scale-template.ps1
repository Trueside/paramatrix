# Scale template (scene kit, section 1): a picture for the image generator that shows the scale of a scene in the
# generator's own 16:9 size (1672x941 = 960x540 grid pixels x 1.7417). Plain colours, no text (text leaks into
# generated images):
#   - wall light grey, floor darker, the floor line (where feet stand) red;
#   - characters as dark silhouettes at their real heights, cut from the game sprites;
#   - door, window, desk as outlined boxes at their sizes from the scene kit tables.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file), from the repository root:
#   powershell -ExecutionPolicy Bypass -File tools/art/scale-template.ps1 -Layout office -Out _img/templates/office_scale.png
param(
    [Parameter(Mandatory = $true)][ValidateSet('office', 'corridor')][string]$Layout,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$art = 'Game/Assets/Paramatrix/Art'
$W = 1672; $H = 941; $k = $W / 960.0          # source pixels per grid pixel
$floorDepth = 100; $wallBase = 120              # grid pixels above the bottom of the frame

function Px([double]$grid) { [int][Math]::Round($grid * $k) }
function Y([double]$aboveBottom) { $H - (Px $aboveBottom) }   # grid height above the frame bottom -> source y

$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(170, 170, 170))
$floorBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 110, 110))
$g.FillRectangle($floorBrush, 0, (Y $wallBase), $W, $H - (Y $wallBase))
# ceiling: a 3.3 m room (314 grid pixels of wall above its base), the rest of the frame is the ceiling
$ceilingBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(140, 140, 140))
$g.FillRectangle($ceilingBrush, 0, 0, $W, (Y ($wallBase + 314)))
$red = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(220, 30, 30)), 3
$g.DrawLine($red, 0, (Y $floorDepth), $W, (Y $floorDepth))

$box = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 40, 40)), 4
function Box([double]$centerX, [double]$base, [double]$width, [double]$height) {
    $g.DrawRectangle($box, (Px ($centerX - $width / 2)), (Y ($base + $height)), (Px $width), (Px $height))
}

function Figure([string]$file, [double]$centerX, [bool]$flip) {
    $s = New-Object System.Drawing.Bitmap (Join-Path $art $file)
    if ($flip) { $s.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX) }
    $left = [Math]::Floor($s.Width / 2)
    for ($y = 0; $y -lt $s.Height; $y++) {
        for ($x = 0; $x -lt $s.Width; $x++) {
            if ($s.GetPixel($x, $y).A -eq 0) { continue }
            $gx = $centerX - $left + $x; $gy = $floorDepth + ($s.Height - 1 - $y)
            $g.FillRectangle([System.Drawing.Brushes]::Black, (Px $gx), (Y ($gy + 1)), [Math]::Ceiling($k), [Math]::Ceiling($k))
        }
    }
    $s.Dispose()
}

if ($Layout -eq 'office') {
    Box 888 $wallBase 104 208          # door with frame, in the back wall, right edge
    Box 480 ($wallBase + 86) 171 152   # office window
    Box 300 $wallBase 133 71           # desk
    Box 108 $wallBase 86 190           # bookcase
    Figure 'Paranoik_Stand.png' 200 $false
    Figure 'Trinity_Stand.png' 640 $true
}
else {
    Box 72 $wallBase 104 208           # door back to the office, left edge
    Box 888 $wallBase 104 208          # door to the street, right edge
    Box 480 $wallBase 81 90            # armchair
    Figure 'Paranoik_Stand.png' 220 $false
    Figure 'Morpheus_Stand.png' 600 $true
}

$g.Dispose()
$dir = Split-Path $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory $dir | Out-Null }
$bmp.Save((Join-Path (Get-Location) $Out), [System.Drawing.Imaging.ImageFormat]::Png)
"$Layout -> $Out ($W x $H)"
