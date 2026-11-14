# Split frames (Visual Production Standard 0.3, E-9): cuts a horizontal sprite sheet of equal frames into separate
# master assets. Every frame gets the SAME crop - the union of the opaque bounds of all frames - so the frames keep
# their authored placement relative to each other (no per-frame trimming, which would make the figure jump).
# Pixels are copied as they are: no scaling, no colour changes.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file):
#   powershell -ExecutionPolicy Bypass -File tools/art/split-frames.ps1 -Sheet walk.png -FrameWidth 156 -Out Game/.../Hero_Walk
#   -> Hero_Walk_0.png ... Hero_Walk_<n-1>.png
param(
    [Parameter(Mandatory = $true)][string]$Sheet,
    [Parameter(Mandatory = $true)][int]$FrameWidth,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap ((Resolve-Path $Sheet).Path)
try {
    if ($bitmap.Width % $FrameWidth -ne 0) { throw "Sheet width $($bitmap.Width) is not a multiple of the frame width $FrameWidth." }
    $count = $bitmap.Width / $FrameWidth
    $top = [int]::MaxValue; $bottom = -1; $left = [int]::MaxValue; $right = -1
    for ($f = 0; $f -lt $count; $f++) {
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $FrameWidth; $x++) {
                $a = $bitmap.GetPixel($f * $FrameWidth + $x, $y).A
                if ($a -eq 0) { continue }
                if ($a -lt 255) { throw "Frame $f has a semi-transparent pixel at ($x, $y): pixel art must have hard edges." }
                if ($y -lt $top) { $top = $y }; if ($y -gt $bottom) { $bottom = $y }
                if ($x -lt $left) { $left = $x }; if ($x -gt $right) { $right = $x }
            }
        }
    }
    if ($bottom -lt 0) { throw 'The sheet has no opaque pixels.' }

    $w = $right - $left + 1; $h = $bottom - $top + 1
    $dir = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Out)); $name = [IO.Path]::GetFileName($Out)
    for ($f = 0; $f -lt $count; $f++) {
        $frame = $bitmap.Clone((New-Object System.Drawing.Rectangle ($f * $FrameWidth + $left), $top, $w, $h), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $frame.Save((Join-Path $dir "$($name)_$f.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $frame.Dispose()
    }
    Write-Output "$count frames, common crop x $left..$right y $top..$bottom of each $FrameWidth x $($bitmap.Height) frame -> $w x $h"
}
finally { $bitmap.Dispose() }
