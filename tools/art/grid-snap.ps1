# Grid snap (Visual Production Standard 0.3, section 6.4, step 1): turns generator "pseudo-pixel" art into a master
# asset with one file pixel per grid pixel. It measures the block pitch of the source, then takes ONE colour per block
# (the most common colour in the block's inner area). It never resizes the picture as a whole: if the source has no
# regular blocks, the result is wrong and must be rejected, not tuned.
#
# Background: flood fill from the crop border over pixels close to the background colour; everything reached
# becomes transparent. Blocks that are mostly transparent become transparent.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file):
#   powershell -ExecutionPolicy Bypass -File tools/art/grid-snap.ps1 -Source sheet.png -Crop 210,60,140,560 `
#       -Out master.png [-Preview preview.png -PreviewScale 2] [-BackgroundTolerance 60]
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][int[]]$Crop,          # x, y, width, height in source pixels
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Preview,
    [int]$PreviewScale = 2,
    [int]$BackgroundTolerance = 60
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class GridSnap
{
    const double MinFit = 0.5;

    static int[] Read(Bitmap b)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[b.Width * b.Height];
        for (int y = 0; y < b.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * b.Width, b.Width);
        b.UnlockBits(data);
        return px;
    }

    static int Dist(int a, int c)
    {
        return Math.Abs(((a >> 16) & 255) - ((c >> 16) & 255)) + Math.Abs(((a >> 8) & 255) - ((c >> 8) & 255)) + Math.Abs((a & 255) - (c & 255));
    }

    // Pitch and phase along one axis from positions of colour edges: the pitch whose grid the edges sit on best.
    // Among near-best pitches the largest wins (a grid of p also fits p/2, p/3 equally well).
    static double[] Pitch(List<int> edges, double min, double max)
    {
        double best = 0; var scores = new List<double[]>();
        for (double p = min; p <= max; p += 0.01)
        {
            double c = 0, s = 0;
            foreach (var e in edges) { var a = 2 * Math.PI * e / p; c += Math.Cos(a); s += Math.Sin(a); }
            var r = Math.Sqrt(c * c + s * s) / edges.Count;
            scores.Add(new[] { p, r, Math.Atan2(s, c) });
            if (r > best) best = r;
        }
        double[] pick = null;
        foreach (var sc in scores) if (sc[1] >= 0.9 * best) pick = sc;   // ascending p: keeps the largest
        var phase = pick[2] / (2 * Math.PI) * pick[0];
        if (phase < 0) phase += pick[0];
        return new[] { pick[0], phase, pick[1] };
    }

    public static string Run(string source, int cx, int cy, int cw, int ch, string outPath, string preview, int scale, int tol)
    {
        using (var src = new Bitmap(source))
        using (var crop = src.Clone(new Rectangle(cx, cy, cw, ch), PixelFormat.Format32bppArgb))
        {
            var px = Read(crop);
            int w = cw, h = ch;
            int bg = px[0];

            // Background: flood fill from the border.
            var clear = new bool[w * h];
            var queue = new Queue<int>();
            for (int x = 0; x < w; x++) { queue.Enqueue(x); queue.Enqueue((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { queue.Enqueue(y * w); queue.Enqueue(y * w + w - 1); }
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                if (clear[i] || Dist(px[i], bg) > tol) continue;
                clear[i] = true;
                int x = i % w, y = i / w;
                if (x > 0) queue.Enqueue(i - 1);
                if (x < w - 1) queue.Enqueue(i + 1);
                if (y > 0) queue.Enqueue(i - w);
                if (y < h - 1) queue.Enqueue(i + w);
            }

            // Colour edges inside the figure.
            var ex = new List<int>(); var ey = new List<int>();
            for (int y = 0; y < h; y++)
                for (int x = 1; x < w; x++)
                {
                    int i = y * w + x;
                    if (!clear[i] && !clear[i - 1] && Dist(px[i], px[i - 1]) > 40) ex.Add(x);
                }
            for (int y = 1; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!clear[i] && !clear[i - w] && Dist(px[i], px[i - w]) > 40) ey.Add(y);
                }
            var gx = Pitch(ex, 2.5, 10); var gy = Pitch(ey, 2.5, 10);
            // A real block grid puts most edges on block borders (fit near 1) with the same pitch on both axes.
            if (gx[2] < MinFit || gy[2] < MinFit || Math.Abs(gx[0] - gy[0]) > 0.15 * Math.Max(gx[0], gy[0]))
                throw new InvalidOperationException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The source has no regular pixel blocks (pitch x {0:F2} fit {1:F2}, pitch y {2:F2} fit {3:F2}; need fit >= {4} and equal pitches). It cannot be snapped to the grid: it has to be drawn as pixel art.",
                    gx[0], gx[2], gy[0], gy[2], MinFit));

            // One colour per block: the most common colour of the inner half of the block.
            int bw = (int)Math.Floor((w - gx[1]) / gx[0]), bh = (int)Math.Floor((h - gy[1]) / gy[0]);
            var outPx = new int[bw * bh];
            int top = bh, bottom = -1, left = bw, right = -1;
            for (int j = 0; j < bh; j++)
                for (int i = 0; i < bw; i++)
                {
                    double x0 = gx[1] + i * gx[0], y0 = gy[1] + j * gy[0];
                    var counts = new Dictionary<int, int>(); int transparent = 0, total = 0;
                    for (int y = (int)Math.Ceiling(y0 + gy[0] * 0.25); y < y0 + gy[0] * 0.75; y++)
                        for (int x = (int)Math.Ceiling(x0 + gx[0] * 0.25); x < x0 + gx[0] * 0.75; x++)
                        {
                            if (x < 0 || y < 0 || x >= w || y >= h) continue;
                            total++;
                            var k = y * w + x;
                            if (clear[k]) { transparent++; continue; }
                            int c; counts.TryGetValue(px[k], out c); counts[px[k]] = c + 1;
                        }
                    if (total == 0 || transparent * 2 >= total) { outPx[j * bw + i] = 0; continue; }
                    int bestColour = 0, bestCount = -1;
                    foreach (var kv in counts) if (kv.Value > bestCount || (kv.Value == bestCount && kv.Key < bestColour)) { bestColour = kv.Key; bestCount = kv.Value; }
                    outPx[j * bw + i] = bestColour | unchecked((int)0xFF000000);
                    top = Math.Min(top, j); bottom = Math.Max(bottom, j); left = Math.Min(left, i); right = Math.Max(right, i);
                }

            // Trim to the figure, feet on the last row.
            int ow = right - left + 1, oh = bottom - top + 1;
            using (var result = new Bitmap(ow, oh, PixelFormat.Format32bppArgb))
            {
                var data = result.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < oh; y++) Marshal.Copy(outPx, (y + top) * bw + left, data.Scan0 + y * data.Stride, ow);
                result.UnlockBits(data);
                result.Save(outPath, ImageFormat.Png);
                if (!string.IsNullOrEmpty(preview))
                    using (var big = new Bitmap(ow * scale, oh * scale, PixelFormat.Format32bppArgb))
                    using (var g = Graphics.FromImage(big))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        g.DrawImage(result, new Rectangle(0, 0, ow * scale, oh * scale));
                        big.Save(preview, ImageFormat.Png);
                    }
            }

            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "pitch x {0:F2} (phase {1:F2}, fit {2:F2}), pitch y {3:F2} (phase {4:F2}, fit {5:F2}); grid {6}x{7}; figure {8}x{9} grid px",
                gx[0], gx[1], gx[2], gy[0], gy[1], gy[2], bw, bh, ow, oh);
        }
    }
}
'@

$report = [GridSnap]::Run((Resolve-Path $Source).Path, $Crop[0], $Crop[1], $Crop[2], $Crop[3],
    [IO.Path]::GetFullPath($Out), $(if ($Preview) { [IO.Path]::GetFullPath($Preview) } else { '' }), $PreviewScale, $BackgroundTolerance)
Write-Output $report
