# Prepare characters and props: makes every sprite from its clean source by a list (characters.json by default,
# props.json for scene pieces). Height rule: an entry with its own 'height' keeps it; any other gets 'default_height'
# (a list without default_height requires a height on every entry).
# Each figure is reduced with reduce-to-grid.ps1 (one factor, no stretching, source colours only). With 'canvas', the
# figure is placed on a transparent canvas of that size at x, y (top-left of the figure), e.g. to match animation frames.
# Fails on the first problem: a missing source, a figure that does not fit its canvas.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file), from the repository root:
#   powershell -ExecutionPolicy Bypass -File tools/art/prepare-characters.ps1 [-List props.json] [-Only character.trinity]
param(
    [string]$List = 'characters.json',
    [string]$Only
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$data = Get-Content (Join-Path $PSScriptRoot $List) -Raw -Encoding UTF8 | ConvertFrom-Json
$names = $data.PSObject.Properties.Name
if ($names -contains 'characters') { $entries = @($data.characters) }
elseif ($names -contains 'props') { $entries = @($data.props) }
else { throw "$List has neither 'characters' nor 'props'." }
$reduce = Join-Path $PSScriptRoot 'reduce-to-grid.ps1'
$stepwise = Join-Path $PSScriptRoot 'reduce-stepwise.ps1'
$done = 0

foreach ($c in $entries) {
    if ($Only -and $c.id -ne $Only) { continue }
    if (-not (Test-Path $c.source)) { throw "$($c.id): source $($c.source) is missing." }
    if ($null -eq $c.height -and $null -eq $data.default_height) { throw "$($c.id): no height, and $List has no default_height." }
    $height = if ($null -ne $c.height) { [int]$c.height } else { [int]$data.default_height }
    $rule = if ($null -ne $c.height) { 'own' } else { 'default' }

    # "final": a sprite finished by hand (e.g. its face touched up) is used as it is: it is copied, never regenerated.
    # It must have the entry's height; tools/art reduce settings above stay as the record of how its base was made.
    if ($c.final) {
        if (-not (Test-Path $c.final)) { throw "$($c.id): final sprite $($c.final) is missing." }
        $img = New-Object System.Drawing.Bitmap ((Resolve-Path $c.final).Path)
        try {
            $rows = 0
            for ($y = 0; $y -lt $img.Height; $y++) { for ($x = 0; $x -lt $img.Width; $x++) { if ($img.GetPixel($x, $y).A -ge 128) { $rows++; break } } }
        }
        finally { $img.Dispose() }
        if ($rows -ne $height) { throw "$($c.id): final sprite $($c.final) is $rows high, the entry says $height." }
    }

    $figure = if ($c.canvas) { [System.IO.Path]::GetTempFileName() + '.png' } else { $c.out }
    if ($c.final) {
        # With a canvas the finished figure is placed on it like a generated one (e.g. to match walk frames).
        Copy-Item -LiteralPath $c.final -Destination $figure -Force
        $line = "final (hand-finished) $($c.final)"
    }
    else {
    $method = if ($c.method) { $c.method } else { 'area' }   # 'nearest': keeps a detailed face sharp (see reduce-to-grid.ps1)
    # Clean-up for small faces (see reduce-to-grid.ps1): "sharpen": 0.8, "despeckle": true, "colors": 32
    $extra = @()
    if ($c.sharpen) { $extra += @('-Sharpen', [string]$c.sharpen) }
    if ($c.despeckle) { $extra += '-Despeckle' }
    if ($c.colors) { $extra += @('-Colors', [string]$c.colors) }
    if ($c.stepwise) {
        # "stepwise": many gentle steps (reduce-stepwise.ps1), e.g.
        # { "max_step": 0.05, "pattern": "lanczos", "last": "sharp", "space": "srgb" }: no step shrinks by more than
        # max_step, the step count is computed, every step lands on whole pixels, the last step uses 'last'.
        $s = $c.stepwise
        foreach ($k in 'max_step', 'pattern', 'last', 'space') { if ($null -eq $s.$k) { throw "$($c.id): stepwise has no '$k'." } }
        $line = & powershell -ExecutionPolicy Bypass -File $stepwise -Source $c.source -Height $height -Out $figure -MaxStep ([string]$s.max_step) -Pattern $s.pattern -Last $s.last -Space $s.space
    }
    elseif ($c.steps) {
        # "steps": reduction in steps, e.g. "lanczos:392,nearest:196" (the last height is the figure's height)
        $line = & powershell -ExecutionPolicy Bypass -File $reduce -Source $c.source -Height $height -Out $figure -Steps $c.steps @extra
    }
    else {
        $line = & powershell -ExecutionPolicy Bypass -File $reduce -Source $c.source -Height $height -Out $figure -Method $method @extra
    }
    }
    if (-not $c.final -and $LASTEXITCODE -ne 0) { throw "$($c.id): reduction failed." }

    if ($c.canvas) {
        $src = New-Object System.Drawing.Bitmap $figure
        try {
            if ($c.canvas.x + $src.Width -gt $c.canvas.width -or $c.canvas.y + $src.Height -gt $c.canvas.height) {
                throw "$($c.id): figure $($src.Width)x$($src.Height) at $($c.canvas.x),$($c.canvas.y) does not fit the canvas $($c.canvas.width)x$($c.canvas.height). Its height is tied to the canvas: $($c.canvas.why). Nothing after it was made."
            }
            $canvas = New-Object System.Drawing.Bitmap ([int]$c.canvas.width), ([int]$c.canvas.height), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            for ($y = 0; $y -lt $src.Height; $y++) {
                for ($x = 0; $x -lt $src.Width; $x++) {
                    $canvas.SetPixel($c.canvas.x + $x, $c.canvas.y + $y, $src.GetPixel($x, $y))
                }
            }
            $canvas.Save((Join-Path (Get-Location) $c.out), [System.Drawing.Imaging.ImageFormat]::Png)
            $canvas.Dispose()
        }
        finally {
            $src.Dispose()
            Remove-Item $figure
        }
    }

    "$($c.id): height $height ($rule) -> $($c.out); $line"
    $done++
}

if ($Only -and $done -eq 0) { throw "No entry '$Only' in $List." }
