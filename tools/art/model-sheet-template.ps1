# Model sheet template: a fixed layout the image generator fills in, so every character sheet has the same format and
# the same scale, and its parts can be cut out at fixed places (visual standard, model sheet format).
#
# Layout (1536x1024, the generator's landscape size):
#   - left panel, four full-height slots (front, three-quarter right, profile right, back); tick marks on the panel
#     edges show the head-top line and the feet line: every figure stands between them, so the figure height in
#     pixels is fixed (FigureTop..FigureFeet) and known before cutting;
#   - right panel, six emotion cells of the avatar cut (229x270): head and shoulders, three-quarter right;
#   - flat bright green inside the panels (a colour no character wears): figures and heads come off it cleanly.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file), from the repository root:
#   powershell -ExecutionPolicy Bypass -File tools/art/model-sheet-template.ps1 -Out _img/templates/model_sheet_template.png
param([Parameter(Mandatory = $true)][string]$Out)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$W = 1536; $H = 1024
$FigureTop = 150; $FigureFeet = 900          # every full-height figure: head top to soles, 750 px
$CellW = 229; $CellH = 270                    # the avatar cut (DialogueView: half of it in the game)

$navy = [System.Drawing.Color]::FromArgb(14, 16, 40)
$frame = [System.Drawing.Color]::FromArgb(150, 160, 210)
$green = [System.Drawing.Color]::FromArgb(0, 255, 0)
$tick = [System.Drawing.Color]::FromArgb(255, 60, 200)

$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear($navy)
$pen = New-Object System.Drawing.Pen $frame, 3
$tickPen = New-Object System.Drawing.Pen $tick, 4
$fill = New-Object System.Drawing.SolidBrush $green
$text = New-Object System.Drawing.SolidBrush $frame
$title = New-Object System.Drawing.Font 'Segoe UI', 22, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$label = New-Object System.Drawing.Font 'Segoe UI', 17, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$center = New-Object System.Drawing.StringFormat
$center.Alignment = [System.Drawing.StringAlignment]::Center

function Panel([int]$x, [int]$y, [int]$w, [int]$h, [string]$name) {
    $g.FillRectangle($fill, $x, $y + 40, $w, $FigureFeet + 24 - ($y + 40)) # green down to just below the feet; labels on navy
    $g.DrawRectangle($pen, $x, $y, $w, $h)
    $g.DrawString($name, $title, $text, (New-Object System.Drawing.RectangleF $x, ($y + 6), $w, 32), $center)
}

# Full height: x 16..996, y 16..1008; four slots of 245.
$ax = 16; $ay = 16; $aw = 980; $ah = 992
Panel $ax $ay $aw $ah 'FULL HEIGHT - ONE SCALE'
$slot = [int]($aw / 4)
$names = @('FRONT', '3/4', 'PROFILE', 'BACK')
for ($i = 0; $i -lt 4; $i++) {
    $sx = $ax + $i * $slot
    $g.DrawString($names[$i], $label, $text, (New-Object System.Drawing.RectangleF $sx, ($FigureFeet + 40), $slot, 30), $center)
}
foreach ($y in @($FigureTop, $FigureFeet)) {
    $g.DrawLine($tickPen, $ax, $y, $ax + 22, $y)
    $g.DrawLine($tickPen, $ax + $aw - 22, $y, $ax + $aw, $y)
}

# Emotions: x 1012..1520, six cells 2 x 3.
$bx = 1012; $by = 16; $bw = 508; $bh = 992
$g.FillRectangle((New-Object System.Drawing.SolidBrush $navy), $bx, $by, $bw, $bh)
$g.DrawRectangle($pen, $bx, $by, $bw, $bh)
$g.DrawString('EMOTIONS', $title, $text, (New-Object System.Drawing.RectangleF $bx, ($by + 6), $bw, 32), $center)
$gap = 18
$cx0 = $bx + [int](($bw - 2 * $CellW - $gap) / 2)
$cy0 = $by + 52
$emotions = @('CALM', 'SMILE', 'LAUGH', 'SURPRISE', 'SHY', 'PLAYFUL')
for ($r = 0; $r -lt 3; $r++) {
    for ($c = 0; $c -lt 2; $c++) {
        $x = $cx0 + $c * ($CellW + $gap); $y = $cy0 + $r * ($CellH + 40)
        $g.FillRectangle($fill, $x, $y, $CellW, $CellH)
        $g.DrawRectangle($pen, $x - 2, $y - 2, $CellW + 3, $CellH + 3)
        $g.DrawString($emotions[$r * 2 + $c], $label, $text, (New-Object System.Drawing.RectangleF $x, ($y + $CellH + 6), $CellW, 26), $center)
    }
}

$full = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($full)) | Out-Null
$bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "template: $full (figures $FigureTop..$FigureFeet px, cells ${CellW}x$CellH at x $cx0, y $cy0, step $($CellW + $gap) x $($CellH + 40))"
