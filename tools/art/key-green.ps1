# Key green: cuts a figure off a flat chroma green background (generator sheets, novel pictures) into a PNG with
# transparency, without a fringe.
#
# Method, deterministic, per pixel: green dominance d = G - max(R, B).
#   - d >= Cut (default 40): background, fully transparent;
#   - below it the pixel belongs to the figure and is opaque: alpha is binary, so no half-green edge pixels stay;
#   - d > 0 (a figure pixel tinted by the green around it): its green is brought down to max(R, B) (despill), and if it
#     lies on the edge (a transparent 4-neighbour) its colour is replaced by the average of its opaque inner neighbours,
#     so the outline does not get an olive or green rim.
# -X -Y -W -H crop a region first (e.g. one figure of a sheet); the result is trimmed to the figure.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file):
#   powershell -ExecutionPolicy Bypass -File tools/art/key-green.ps1 -Source sheet.png -Out figure.png [-X 300 -Y 60 -W 200 -H 870] [-Cut 40]
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0,
    [int]$Cut = 40
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class KeyGreen
{
    public static string Run(string source, string outPath, int x0, int y0, int cw, int ch, int cut)
    {
        int w, h; int[] px;
        using (var loaded = new Bitmap(source))
        using (var argb = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(argb)) g.DrawImage(loaded, new Rectangle(0, 0, loaded.Width, loaded.Height));
            w = argb.Width; h = argb.Height; px = new int[w * h];
            var data = argb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w, w);
            argb.UnlockBits(data);
        }

        if (cw <= 0) { x0 = 0; y0 = 0; cw = w; ch = h; }
        var c = new int[cw * ch];
        var dom = new int[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int p = px[(y0 + y) * w + x0 + x], r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255, m = Math.Max(r, b);
                int d = g - m; dom[y * cw + x] = d;
                if (d >= cut || ((p >> 24) & 255) < 128) { c[y * cw + x] = 0; continue; }
                if (d > 0) g = m;
                c[y * cw + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
            }

        // Edge pixels that carried green: take the colour of the opaque inner neighbours (those without green).
        var res = (int[])c.Clone();
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int i = y * cw + x;
                if (c[i] == 0 || dom[i] <= 0) continue;
                bool edge = x == 0 || y == 0 || x == cw - 1 || y == ch - 1 || c[i - 1] == 0 || c[i + 1] == 0 || c[i - cw] == 0 || c[i + cw] == 0;
                if (!edge) continue;
                long r = 0, g = 0, b = 0; int n = 0;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= cw || yy >= ch) continue;
                        int j = yy * cw + xx;
                        if (c[j] == 0 || dom[j] > 0) continue;
                        r += (c[j] >> 16) & 255; g += (c[j] >> 8) & 255; b += c[j] & 255; n++;
                    }
                if (n > 0) res[i] = unchecked((int)0xFF000000) | (int)(r / n) << 16 | (int)(g / n) << 8 | (int)(b / n);
            }

        int top = ch, bottom = -1, left = cw, right = -1;
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                if (res[y * cw + x] != 0) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, x); right = Math.Max(right, x); }
        if (bottom < 0) throw new InvalidOperationException("Nothing is left after keying: the region is all green.");
        int ow = right - left + 1, oh = bottom - top + 1;
        using (var bmp = new Bitmap(ow, oh, PixelFormat.Format32bppArgb))
        {
            var data = bmp.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < oh; y++) Marshal.Copy(res, (top + y) * cw + left, data.Scan0 + y * data.Stride, ow);
            bmp.UnlockBits(data);
            bmp.Save(outPath, ImageFormat.Png);
        }

        return string.Format("figure {0}x{1} -> {2}", ow, oh, outPath);
    }
}
'@

$full = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($full)) | Out-Null
[KeyGreen]::Run((Resolve-Path -LiteralPath $Source).Path, $full, $X, $Y, $W, $H, $Cut)
