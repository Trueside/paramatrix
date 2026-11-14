# Cut emotion cards: a generated card is a grid of framed cells (dark rounded frames on a light background, a title
# bar on top). Every cell is cut out without its frame, all cells of all given cards at one common size, nothing
# scaled: the pixels are copied as they are.
#
# Method, deterministic:
#   1. frame lines: columns where more than 45% of the pixels are frame-coloured (blue, darker than the light background), then rows where more
#      than 45% of the pixels next to the cells' sides are (a head drawn over a cell's top line does not hide it);
#      neighbouring lines merge, the title bar (top 60 rows) is skipped; pairs of lines give the cells' outer edges;
#   2. the frame is measured: its thickness (the median frame run at the middle of the sides) and the radius of its
#      rounded corners (where the straight top line begins, the largest over all cells);
#   3. a cut is clean when it keeps one pixel off the frame on every side and none of its corners lies outside the
#      rounded corner of the frame; the common size is the largest area clean in every cell of every card (or
#      -Width / -Height, checked the same way);
#   4. each cell is cut centred at that size and saved as <prefix><row>-<col><suffix>.png (rows and columns from 1).
# Geometry, not colour, decides cleanliness: dark outlines of the drawing near the frame do not matter.
# Fails when the cards do not have the same grid.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file):
#   powershell -ExecutionPolicy Bypass -File tools/art/cut-card.ps1 -Cards "a.png,b.png" -Suffixes "_01,_02" -Out dir [-Prefix em_] [-Width 230 -Height 280]
param(
    [Parameter(Mandatory = $true)][string[]]$Cards,
    [Parameter(Mandatory = $true)][string[]]$Suffixes,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Prefix = 'em_',
    [int]$Width = 0,
    [int]$Height = 0
)

$ErrorActionPreference = 'Stop'
# powershell -File passes "a.png,b.png" as one string.
$Cards = @($Cards | ForEach-Object { $_ -split ',' })
$Suffixes = @($Suffixes | ForEach-Object { $_ -split ',' })
if ($Cards.Count -ne $Suffixes.Count) { throw 'Give one suffix per card.' }
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public sealed class Card
{
    public string Path; public int W, H; public int[] Px;
    public List<int[]> Cols = new List<int[]>(); // [left, right] outer edges, inclusive
    public List<int[]> Rows = new List<int[]>(); // [top, bottom]
    public int Thickness, Radius;

    public static bool Frame(int c) { int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255; return r < 130 && g < 130 && b >= r + 40; }

    private bool FrameAt(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H && Frame(Px[y * W + x]); }

    public static Card Load(string path)
    {
        var card = new Card { Path = path };
        using (var bmp = new Bitmap(path))
        using (var argb = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(argb)) g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            card.W = argb.Width; card.H = argb.Height; card.Px = new int[card.W * card.H];
            var d = argb.LockBits(new Rectangle(0, 0, card.W, card.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < card.H; y++) Marshal.Copy(d.Scan0 + y * d.Stride, card.Px, y * card.W, card.W);
            argb.UnlockBits(d);
        }

        const int Title = 60;
        var cols = new List<int>();
        for (int x = 0; x < card.W; x++)
        {
            int n = 0; for (int y = Title; y < card.H; y++) if (Frame(card.Px[y * card.W + x])) n++;
            if (n > 0.45 * (card.H - Title)) cols.Add(x);
        }
        Pair(Merge(cols), card.Cols, path, "columns");
        // Rows are found only next to the cells' sides (past the rounded corner): a head drawn over the middle of a
        // cell's top line must not hide that line.
        var rows = new List<int>();
        for (int y = Title; y < card.H; y++)
        {
            int n = 0, all = 0;
            foreach (var col in card.Cols)
                for (int x = col[0] + 20; x < col[0] + 60; x++)
                {
                    all += 2;
                    if (Frame(card.Px[y * card.W + x])) n++;
                    if (Frame(card.Px[y * card.W + col[0] + col[1] - x])) n++;
                }
            if (n > 0.45 * all) rows.Add(y);
        }
        Pair(Merge(rows), card.Rows, path, "rows");
        card.Measure();
        return card;
    }

    private static List<int[]> Merge(List<int> lines)
    {
        var groups = new List<int[]>();
        foreach (var v in lines)
            if (groups.Count > 0 && v - groups[groups.Count - 1][1] <= 2) groups[groups.Count - 1][1] = v;
            else groups.Add(new[] { v, v });
        return groups;
    }

    private static void Pair(List<int[]> groups, List<int[]> into, string path, string what)
    {
        if (groups.Count % 2 != 0) throw new InvalidOperationException(path + ": odd number of frame lines in " + what + " (" + groups.Count + ").");
        for (int i = 0; i < groups.Count; i += 2) into.Add(new[] { groups[i][0], groups[i + 1][1] });
    }

    /// <summary>Thickness: the median frame run inward from each side at its middle (a dark part of the drawing that
    /// touches the frame in one cell must not count as frame). Radius: the largest distance from a cell's corner to where
    /// the top and bottom lines are straight.</summary>
    private void Measure()
    {
        var runs = new List<int>();
        foreach (var col in Cols)
            foreach (var row in Rows)
            {
                int midY = (row[0] + row[1]) / 2, midX = (col[0] + col[1]) / 2;
                runs.Add(Run(col[0], midY, 1, 0));
                runs.Add(Run(col[1], midY, -1, 0));
                runs.Add(Run(midX, row[0], 0, 1));
                runs.Add(Run(midX, row[1], 0, -1));
                Radius = Math.Max(Radius, Straight(col[0], row[0], 1, 1));
                Radius = Math.Max(Radius, Straight(col[1], row[0], -1, 1));
                Radius = Math.Max(Radius, Straight(col[0], row[1], 1, -1));
                Radius = Math.Max(Radius, Straight(col[1], row[1], -1, -1));
            }

        runs.Sort();
        Thickness = runs[runs.Count / 2];
    }

    private int Run(int x, int y, int dx, int dy)
    {
        int start = 0;
        while (start < 4 && !FrameAt(x + dx * start, y + dy * start)) start++; // anti-aliased first pixels
        int n = start;
        while (n < 20 && FrameAt(x + dx * n, y + dy * n)) n++;
        return n;
    }

    /// <summary>Distance from the corner along the horizontal line to where it is frame on its outermost three rows.</summary>
    private int Straight(int x, int y, int dx, int dy)
    {
        for (int i = 0; i < 60; i++)
            for (int k = 0; k < 3; k++)
                if (FrameAt(x + dx * i, y + dy * k)) return i;
        return 60;
    }

    public Rectangle Centred(int c, int r, int w, int h)
    {
        var col = Cols[c]; var row = Rows[r];
        int x = (col[0] + col[1] + 1 - w) / 2, y = (row[0] + row[1] + 1 - h) / 2;
        return new Rectangle(x, y, w, h);
    }

    /// <summary>Clean: off the frame on every side, and every corner of the cut inside the frame's rounded corner.</summary>
    public string Problem(int c, int r, int w, int h, int thickness, int radius)
    {
        var col = Cols[c]; var row = Rows[r]; var cut = Centred(c, r, w, h);
        int left = cut.Left - col[0], right = col[1] - (cut.Right - 1), top = cut.Top - row[0], bottom = row[1] - (cut.Bottom - 1);
        int need = thickness + 1;
        if (left < need || right < need || top < need || bottom < need) return "touches the frame line";
        double inner = radius - need;
        foreach (var m in new[] { new[] { left, top }, new[] { right, top }, new[] { left, bottom }, new[] { right, bottom } })
            if (m[0] < radius && m[1] < radius)
            {
                double ax = radius - m[0], ay = radius - m[1];
                if (ax * ax + ay * ay > inner * inner) return "a corner reaches the rounded frame corner";
            }
        return null;
    }

    public void Save(Rectangle r, string path)
    {
        using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
        {
            var d = bmp.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < r.Height; y++) Marshal.Copy(Px, (r.Top + y) * W + r.Left, d.Scan0 + y * d.Stride, r.Width);
            bmp.UnlockBits(d);
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
'@

$loaded = @($Cards | ForEach-Object { [Card]::Load((Resolve-Path -LiteralPath $_).Path) })
$cols = $loaded[0].Cols.Count; $rows = $loaded[0].Rows.Count
$thickness = 0; $radius = 0
foreach ($c in $loaded) {
    if ($c.Cols.Count -ne $cols -or $c.Rows.Count -ne $rows) { throw "$($c.Path): $($c.Rows.Count)x$($c.Cols.Count) cells, the first card has ${rows}x$cols." }
    $thickness = [Math]::Max($thickness, $c.Thickness); $radius = [Math]::Max($radius, $c.Radius)
    $sizes = for ($i = 0; $i -lt $cols; $i++) { $c.Cols[$i][1] - $c.Cols[$i][0] + 1 }
    $heights = for ($j = 0; $j -lt $rows; $j++) { $c.Rows[$j][1] - $c.Rows[$j][0] + 1 }
    Write-Output ("{0}: {1}x{2} cells, outer widths {3}, heights {4}; frame {5} px, corner radius {6} px" -f `
        [IO.Path]::GetFileName($c.Path), $rows, $cols, ($sizes -join ' '), ($heights -join ' '), $c.Thickness, $c.Radius)
}

function Problems([int]$w, [int]$h) {
    foreach ($c in $loaded) { for ($j = 0; $j -lt $rows; $j++) { for ($i = 0; $i -lt $cols; $i++) {
        $p = $c.Problem($i, $j, $w, $h, $thickness, $radius)
        if ($p) { "{0} cell {1}-{2}: {3}" -f [IO.Path]::GetFileName($c.Path), ($j + 1), ($i + 1), $p } } } }
}

if ($Width -le 0 -or $Height -le 0) {
    # Largest clean area: for every width, the tallest clean height.
    $maxW = (@($loaded | ForEach-Object { $c = $_; 0..($cols - 1) | ForEach-Object { $c.Cols[$_][1] - $c.Cols[$_][0] + 1 } }) | Measure-Object -Minimum).Minimum
    $maxH = (@($loaded | ForEach-Object { $c = $_; 0..($rows - 1) | ForEach-Object { $c.Rows[$_][1] - $c.Rows[$_][0] + 1 } }) | Measure-Object -Minimum).Minimum
    $best = 0
    for ($w = $maxW; $w -ge 16; $w--) {
        if ($w * $maxH -le $best) { break }
        for ($h = $maxH; $h -ge 16 -and $w * $h -gt $best; $h--) {
            if (-not (Problems $w $h)) { $best = $w * $h; $Width = $w; $Height = $h; break }
        }
    }
    if ($best -eq 0) { throw 'No frame-free size found.' }
}
else {
    $bad = @(Problems $Width $Height)
    if ($bad.Count -gt 0) { throw "${Width}x$Height is not clean: $($bad -join '; ')" }
}

New-Item -ItemType Directory -Force $Out | Out-Null
for ($k = 0; $k -lt $loaded.Count; $k++) {
    for ($j = 0; $j -lt $rows; $j++) { for ($i = 0; $i -lt $cols; $i++) {
        $name = "{0}{1}-{2}{3}.png" -f $Prefix, ($j + 1), ($i + 1), $Suffixes[$k]
        $loaded[$k].Save($loaded[$k].Centred($i, $j, $Width, $Height), [IO.Path]::GetFullPath((Join-Path $Out $name)))
    } }
}
Write-Output ("frame {0} px, corner radius {1} px; cut {2} cells per card at {3}x{4} into {5}" -f $thickness, $radius, ($rows * $cols), $Width, $Height, $Out)
