# Split row (Visual Production Standard 0.3, E-9): cuts a hand-made row of animation frames that stand at uneven
# distances into separate frames on one common canvas. A frame is a run of columns with opaque pixels; frames are
# separated by fully transparent columns. Frames keep their rows (the sheet's bottom line is the floor for all of
# them, so the head bob stays as drawn); horizontally each frame is placed so that its head centre lands on the same
# canvas column - in a side-view walk the head moves least, so the figure does not jump between frames.
# Head centre = mean of (leftmost + rightmost) / 2 of the rows top+8, top+12, top+16 of the frame.
# Pixels are copied as they are: no scaling, no colour changes.
#
# Fails if the row has a semi-transparent pixel, if the frame count differs from -Count, or if a head row is empty.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file):
#   powershell -ExecutionPolicy Bypass -File tools/art/split-row.ps1 -Sheet walk.png -Count 7 -Out Game/.../Hero_Walk
#   -> Hero_Walk_0.png ... Hero_Walk_<Count-1>.png, left to right
param(
    [Parameter(Mandatory = $true)][string]$Sheet,
    [Parameter(Mandatory = $true)][int]$Count,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap ((Resolve-Path $Sheet).Path)
try {
    $w = $bitmap.Width; $h = $bitmap.Height
    $alpha = New-Object 'int[,]' $w, $h
    $used = New-Object 'bool[]' $w
    for ($x = 0; $x -lt $w; $x++) {
        for ($y = 0; $y -lt $h; $y++) {
            $a = $bitmap.GetPixel($x, $y).A; $alpha[$x, $y] = $a
            if ($a -gt 0 -and $a -lt 255) { throw "Semi-transparent pixel at ($x, $y): pixel art must have hard edges." }
            if ($a) { $used[$x] = $true }
        }
    }
    $frames = @(); $start = -1
    for ($x = 0; $x -le $w; $x++) {
        $on = $x -lt $w -and $used[$x]
        if ($on -and $start -lt 0) { $start = $x }
        if (-not $on -and $start -ge 0) { $frames += , @($start, ($x - 1)); $start = -1 }
    }
    if ($frames.Count -ne $Count) { throw "Found $($frames.Count) frames (runs of opaque columns), expected $Count." }

    $info = @()
    foreach ($f in $frames) {
        $top = -1
        for ($y = 0; $y -lt $h -and $top -lt 0; $y++) { for ($x = $f[0]; $x -le $f[1]; $x++) { if ($alpha[$x, $y]) { $top = $y; break } } }
        $sum = 0
        foreach ($dy in 8, 12, 16) {
            $yy = $top + $dy; $l = -1; $r = -1
            for ($x = $f[0]; $x -le $f[1]; $x++) { if ($alpha[$x, $yy]) { if ($l -lt 0) { $l = $x }; $r = $x } }
            if ($l -lt 0) { throw "Frame at columns $($f[0])..$($f[1]): head row $yy is empty." }
            $sum += ($l + $r) / 2
        }
        $info += , @($f[0], $f[1], [int][Math]::Round($sum / 3))
    }
    # Canvas: every frame shifted so that its head centre sits at column $cx.
    $cx = ($info | ForEach-Object { $_[2] - $_[0] } | Measure-Object -Maximum).Maximum
    $cw = ($info | ForEach-Object { $cx + $_[1] - $_[2] + 1 } | Measure-Object -Maximum).Maximum
    $dir = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Out)); $name = [IO.Path]::GetFileName($Out)
    for ($i = 0; $i -lt $Count; $i++) {
        $f = $info[$i]; $left = $cx - ($f[2] - $f[0])
        $frame = New-Object System.Drawing.Bitmap $cw, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for ($x = $f[0]; $x -le $f[1]; $x++) { for ($y = 0; $y -lt $h; $y++) { if ($alpha[$x, $y]) { $frame.SetPixel($left + $x - $f[0], $y, $bitmap.GetPixel($x, $y)) } } }
            $frame.Save((Join-Path $dir "$($name)_$i.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $frame.Dispose() }
        Write-Output ("frame {0}: sheet columns {1}..{2}, head centre {3} -> canvas column {4}" -f $i, $f[0], $f[1], $f[2], $cx)
    }
    Write-Output "$Count frames on a common canvas ${cw}x$h"
}
finally { $bitmap.Dispose() }
