# Swap legs (Visual Production Standard 0.3, E-9): makes the missing walk pose of one half of a side-view cycle from
# the matching pose of the other half. In profile the two legs differ only in shading: the near leg is lighter
# (pants R, shade K; shoe N, highlight S, shade H), the far leg darker (pants K, shade G; shoe H, highlight N, shade F).
# The script finds the four parts (near/far pants leg, near/far shoe) by flood fill from a seed pixel each and gives
# the near parts the far colours and the far parts the near colours, so a "far leg forward" pose becomes the
# "near leg forward" pose. Geometry, outlines, body and arms stay as they are; only colours of the sheet are used.
# Once the near leg is in front it covers the whole hip: in the hip rows (HipTop..Top-1, from column SplitX) the far
# thigh takes near pants colours, its last pixel before the outline stays a shade.
#
# Fails if a seed is not on a colour of its part, if two parts meet, or if a part holds a colour the maps do not know.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file); points are "x,y" in the frame:
#   powershell -ExecutionPolicy Bypass -File tools/art/swap-legs.ps1 -Frame Walk_3.png -NearLeg "28,110" -FarLeg "48,110" `
#     -NearShoe "25,141" -FarShoe "42,132" -Top 81 -HipTop 74 -SplitX 41 -Out Walk_new.png
param(
    [Parameter(Mandatory = $true)][string]$Frame,
    [Parameter(Mandatory = $true)][string]$NearLeg,
    [Parameter(Mandatory = $true)][string]$FarLeg,
    [Parameter(Mandatory = $true)][string]$NearShoe,
    [Parameter(Mandatory = $true)][string]$FarShoe,
    # First row where the two pants legs are separated by an outline; the fills stay below it.
    [Parameter(Mandatory = $true)][int]$Top,
    [Parameter(Mandatory = $true)][int]$HipTop,
    [Parameter(Mandatory = $true)][int]$SplitX,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Hex([string]$s) { [System.Drawing.Color]::FromArgb(255, [Convert]::ToInt32($s.Substring(0, 2), 16), [Convert]::ToInt32($s.Substring(2, 2), 16), [Convert]::ToInt32($s.Substring(4, 2), 16)).ToArgb() }
function Point([string]$s) { $p = $s.Split(','); if ($p.Count -ne 2) { throw "'$s' is not an x,y point." }; return , @([int]$p[0], [int]$p[1]) }

# Palette of the Paranoik walk sheet (SpriteCook, E-9 attempt 1).
$R = Hex '4F3B65'; $K = Hex '322447'; $G = Hex '201629'
$S = Hex '613C5A'; $N = Hex '422740'; $H = Hex '2E1A2F'; $F = Hex '25131D'
$pantsColours = @($R, $K, $G)
# G (deepest shade) is shared by pants and shoes and maps to itself in shoes: shoe fills skip it.
$shoeColours = @($S, $N, $H, $F)
$parts = @(
    @{ name = 'near leg'; seed = (Point $NearLeg); colours = $pantsColours; map = @{ $R = $K; $K = $G; $G = $G } },
    @{ name = 'far leg'; seed = (Point $FarLeg); colours = $pantsColours; map = @{ $K = $R; $G = $K } },
    @{ name = 'near shoe'; seed = (Point $NearShoe); colours = $shoeColours; map = @{ $S = $N; $N = $H; $H = $F; $F = $F; $G = $G } },
    @{ name = 'far shoe'; seed = (Point $FarShoe); colours = $shoeColours; map = @{ $N = $S; $H = $N; $F = $H; $G = $G } }
)

$bitmap = New-Object System.Drawing.Bitmap ((Resolve-Path $Frame).Path)
try {
    $w = $bitmap.Width; $h = $bitmap.Height
    $px = New-Object 'int[,]' $w, $h
    for ($y = 0; $y -lt $h; $y++) { for ($x = 0; $x -lt $w; $x++) { $px[$x, $y] = $bitmap.GetPixel($x, $y).ToArgb() } }

    $owner = @{}
    $changes = New-Object System.Collections.Generic.List[int[]]
    foreach ($part in $parts) {
        $sx = $part.seed[0]; $sy = $part.seed[1]
        if ($sy -lt $Top -or $part.colours -notcontains $px[$sx, $sy]) { throw "The $($part.name) seed ($sx,$sy) is not on its colours below row $Top." }
        $seen = New-Object 'bool[,]' $w, $h; $count = 0
        $stack = New-Object System.Collections.Generic.Stack[int[]]; $stack.Push(@($sx, $sy))
        while ($stack.Count) {
            $p = $stack.Pop(); $x = $p[0]; $y = $p[1]
            if ($x -lt 0 -or $y -lt $Top -or $x -ge $w -or $y -ge $h -or $seen[$x, $y]) { continue }
            $c = $px[$x, $y]
            if ($part.colours -notcontains $c) { continue }
            $seen[$x, $y] = $true; $count++
            $key = "$x,$y"
            if ($owner.ContainsKey($key)) { throw "The $($part.name) meets the $($owner[$key]) at ($key): move the seeds or raise -Top." }
            $owner[$key] = $part.name
            if (-not $part.map.ContainsKey($c)) { throw "The $($part.name) has an unexpected colour $('{0:X8}' -f $c) at ($key)." }
            $changes.Add(@($x, $y, $part.map[$c]))
            $stack.Push(@(($x + 1), $y)); $stack.Push(@(($x - 1), $y)); $stack.Push(@($x, ($y + 1))); $stack.Push(@($x, ($y - 1)))
        }
        Write-Output "$($part.name): $count pixels"
    }
    $hipMap = @{ $K = $R; $G = $K; $H = $K }
    for ($y = $HipTop; $y -lt $Top; $y++) {
        $last = -1
        for ($x = $SplitX; $x -lt $w -and ($pantsColours + $shoeColours) -contains $px[$x, $y]; $x++) {
            $c = $px[$x, $y]
            if (-not $hipMap.ContainsKey($c)) { continue }
            $changes.Add(@($x, $y, $hipMap[$c])); $last = $x
        }
        if ($last -ge 0) { $changes.Add(@($last, $y, $K)) }
    }
    foreach ($c in $changes) { $bitmap.SetPixel($c[0], $c[1], [System.Drawing.Color]::FromArgb($c[2])) }
    $bitmap.Save([IO.Path]::GetFullPath($Out), [System.Drawing.Imaging.ImageFormat]::Png)
}
finally { $bitmap.Dispose() }
