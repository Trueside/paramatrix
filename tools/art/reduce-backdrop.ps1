# Reduce backdrop (Visual Production Standard 0.3, section 6.5.1): shrinks an opaque background picture to a target
# size in grid pixels (e.g. 960x540 for a fixed screen, 1536x540 for a scrolling one).
#
# Method, deterministic:
#   1. one scale factor for both axes, the smallest that covers the target (never stretched); what is left over beyond
#      the target (a fraction of a pixel, or more if the aspect differs) is cut evenly from both sides;
#   2. each grid pixel covers a window of the source; its colour is the source colour in that window closest to the
#      window's coverage-weighted mean. So no blended shades appear (edges stay crisp), and the choice is local:
#      no whole-palette search, fast on big pictures. With a whole factor (6144x2160 -> 1536x540) the windows are
#      exact N x N blocks.
# Refuses: any transparency (backdrops are opaque; layers with transparency go through reduce-to-grid.ps1), and a source
# smaller than the target.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file):
#   powershell -ExecutionPolicy Bypass -File tools/art/reduce-backdrop.ps1 -Source office.png -Width 960 -Height 540 -Out backdrop.png
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][int]$Width,
    [Parameter(Mandatory = $true)][int]$Height,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ReduceBackdrop
{
    public static string Run(string source, int ow, int oh, string outPath)
    {
        int w, h; int[] px;
        using (var loaded = new Bitmap(source))
        using (var argb = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(argb)) g.DrawImage(loaded, new Rectangle(0, 0, loaded.Width, loaded.Height));
            w = argb.Width; h = argb.Height;
            var data = argb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            px = new int[w * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            argb.UnlockBits(data);
        }

        if (ow < 1 || oh < 1) throw new Exception("Target size must be positive.");
        if (w < ow || h < oh) throw new Exception(string.Format("Source {0}x{1} is smaller than the target {2}x{3}.", w, h, ow, oh));
        for (int i = 0; i < px.Length; i++)
            if (((px[i] >> 24) & 255) != 255)
                throw new Exception(string.Format("Pixel {0},{1} is not opaque: a backdrop must be fully opaque.", i % w, i / w));

        // One factor (source pixels per grid pixel) that covers the target; the rest is cut evenly.
        double k = Math.Min((double)w / ow, (double)h / oh);
        double ox = (w - ow * k) / 2.0, oy = (h - oh * k) / 2.0;

        var result = new int[ow * oh];
        for (int j = 0; j < oh; j++)
        for (int i = 0; i < ow; i++)
        {
            double x0 = ox + i * k, x1 = x0 + k, y0 = oy + j * k, y1 = y0 + k;
            int sx0 = (int)Math.Floor(x0), sx1 = Math.Min(w, (int)Math.Ceiling(x1));
            int sy0 = (int)Math.Floor(y0), sy1 = Math.Min(h, (int)Math.Ceiling(y1));
            double r = 0, gg = 0, b = 0, area = 0;
            for (int y = sy0; y < sy1; y++)
            {
                double cy = Math.Min(y + 1, y1) - Math.Max(y, y0);
                if (cy <= 0) continue;
                for (int x = sx0; x < sx1; x++)
                {
                    double cx = Math.Min(x + 1, x1) - Math.Max(x, x0);
                    if (cx <= 0) continue;
                    double a = cx * cy; int c = px[y * w + x];
                    r += a * ((c >> 16) & 255); gg += a * ((c >> 8) & 255); b += a * (c & 255); area += a;
                }
            }
            double mr = r / area, mg = gg / area, mb = b / area;
            int best = 0; double bestD = double.MaxValue;
            for (int y = sy0; y < sy1; y++)
            for (int x = sx0; x < sx1; x++)
            {
                if (Math.Min(y + 1, y1) - Math.Max(y, y0) <= 0 || Math.Min(x + 1, x1) - Math.Max(x, x0) <= 0) continue;
                int c = px[y * w + x];
                double dr = ((c >> 16) & 255) - mr, dg = ((c >> 8) & 255) - mg, db = (c & 255) - mb;
                double d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = c; }
            }
            result[j * ow + i] = best;
        }

        using (var bmp = new Bitmap(ow, oh, PixelFormat.Format32bppArgb))
        {
            var data = bmp.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(result, 0, data.Scan0, result.Length);
            bmp.UnlockBits(data);
            bmp.Save(outPath, ImageFormat.Png);
        }

        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "source {0}x{1}, factor 1/{2:0.###}, cut {3:0.#} x {4:0.#} source px per side; result {5}x{6}", w, h, k, ox, oy, ow, oh);
    }
}
'@

$src = (Resolve-Path $Source).Path
$out = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Out)) }
[ReduceBackdrop]::Run($src, $Width, $Height, $out)
