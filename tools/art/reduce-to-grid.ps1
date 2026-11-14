# Reduce to grid (Visual Production Standard 0.3, experiment E-7 attempt 4): shrinks a clean cut-out figure to a target
# height in grid pixels. NOTE: the standard does not accept plain reduction as a replacement for drawing pixel art
# (section 6.4); this is the experiment that tests how far it gets at the 960x540 grid density.
#
# Method, every step deterministic:
#   1. one scale factor for both axes: target height / opaque height of the source (width follows, never stretched);
#   2. area averaging: each output pixel is the coverage-weighted mean of the source pixels under it
#      (colour premultiplied by alpha);
#   3. alpha is binary: opaque when at least half of the output pixel is covered by the figure;
#   4. colour is snapped to the nearest colour already used in the source, so no new blended shades appear.
# -Method nearest instead of steps 2-4: each output pixel takes the source pixel at the centre of its block (alpha by
# that pixel). Thin lines of a face stay lines instead of blending into a smudge; best at a whole factor (e.g. 1/4).
# -Steps "bilinear:600,lanczos:392,nearest:196": the reduction in steps, each with its method and the figure height it
# reduces to; the last height is -Height. Methods: bilinear (tent filter as wide as the step), lanczos (Lanczos-3),
# area (box average) - all in linear light on premultiplied alpha - and nearest (the source pixel at each block centre).
# The last step must be nearest and should divide by a whole number (2, 3, 4): the smooth steps bring the figure to
# exactly that many times the target, then every pixel row and column keeps the same weight. Alpha is binary at the end.
# Clean-up for small faces (any method):
#   -Sharpen 0.8   unsharp mask on the source before reducing (radius 2 px): thin lines of eyes and brows survive;
#   -Colors 32     after reducing, the palette is cut to that many colours (median cut, deterministic): skin, hair and
#                  clothes become whole areas instead of a speckle of near shades;
#   -Despeckle     last, a pixel whose four neighbours all share one other colour takes that colour.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file):
#   powershell -ExecutionPolicy Bypass -File tools/art/reduce-to-grid.ps1 -Source figure.png -Height 144 -Out master.png `
#       [-Preview preview.png -PreviewScale 2] [-Method nearest | -Steps "lanczos:392,nearest:196"]
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][int]$Height,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Preview,
    [int]$PreviewScale = 2,
    [ValidateSet('area', 'nearest')][string]$Method = 'area',
    [string]$Steps,
    [double]$Sharpen = 0,
    [switch]$Despeckle,
    [int]$Colors = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ReduceToGrid
{
    // ---------- steps: smooth reductions (linear light, premultiplied) and a final nearest ----------

    private static double ToLinear(int c) { double v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }

    private static int ToSrgb(double v)
    {
        v = Math.Max(0, Math.Min(1, v));
        v = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        return (int)Math.Round(v * 255);
    }

    private static double Lanczos(double x)
    {
        x = Math.Abs(x);
        if (x < 1e-9) return 1;
        if (x >= 3) return 0;
        double p = Math.PI * x;
        return 3 * Math.Sin(p) * Math.Sin(p / 3) / (p * p);
    }

    /// <summary>Resamples planes [c][y*w+x] of w x h to ow x oh along both axes with a 1D kernel per axis.</summary>
    private static double[][] Resample(double[][] src, int w, int h, int ow, int oh, string kernel)
    {
        var rows = Axis(src, w, ow, h, true, kernel);      // h lines of ow
        return Axis(rows, h, oh, ow, false, kernel);       // oh lines of ow
    }

    private static double[][] Axis(double[][] p, int n, int m, int count, bool horizontal, string kernel)
    {
        {
            // n source samples along the axis, m outputs, 'count' lines across it.
            double scale = n / (double)m, filter = Math.Max(1.0, scale);
            var res = new double[4][];
            for (int c = 0; c < 4; c++) res[c] = new double[m * count];
            for (int o = 0; o < m; o++)
            {
                double a0 = o * scale, a1 = a0 + scale, centre = a0 + scale / 2;
                int from, to;
                double[] wts;
                if (kernel == "lanczos" || kernel == "bilinear")
                {
                    bool tent = kernel == "bilinear";
                    double support = (tent ? 1 : 3) * filter;
                    from = Math.Max(0, (int)Math.Floor(centre - support)); to = Math.Min(n - 1, (int)Math.Ceiling(centre + support));
                    wts = new double[to - from + 1]; double sum = 0;
                    for (int i = from; i <= to; i++)
                    {
                        double d = (i + 0.5 - centre) / filter;
                        wts[i - from] = tent ? Math.Max(0, 1 - Math.Abs(d)) : Lanczos(d);
                        sum += wts[i - from];
                    }
                    for (int i = 0; i < wts.Length; i++) wts[i] /= sum;
                }
                else
                {
                    from = (int)Math.Floor(a0); to = Math.Min(n - 1, (int)Math.Ceiling(a1) - 1);
                    wts = new double[to - from + 1]; double sum = 0;
                    for (int i = from; i <= to; i++) { wts[i - from] = Math.Max(0, Math.Min(i + 1, a1) - Math.Max(i, a0)); sum += wts[i - from]; }
                    for (int i = 0; i < wts.Length; i++) wts[i] /= sum;
                }

                for (int line = 0; line < count; line++)
                    for (int c = 0; c < 4; c++)
                    {
                        double v = 0;
                        for (int i = from; i <= to; i++)
                            v += wts[i - from] * (horizontal ? p[c][line * n + i] : p[c][i * count + line]);
                        if (horizontal) res[c][line * m + o] = v; else res[c][o * count + line] = v;
                    }
            }
            return res;
        }
    }

    private static int[] Chain(int[] px, int w, int h, int left, int top, int sw, int sh, int ow, int oh, string[] methods, int[] heights)
    {
        var p = new double[4][];
        for (int c = 0; c < 4; c++) p[c] = new double[sw * sh];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int c = px[(top + y) * w + left + x], i = y * sw + x;
                double a = ((c >> 24) & 255) / 255.0;
                p[0][i] = ToLinear((c >> 16) & 255) * a; p[1][i] = ToLinear((c >> 8) & 255) * a; p[2][i] = ToLinear(c & 255) * a; p[3][i] = a;
            }

        // Smooth steps down to the size the final nearest step divides.
        int cw = sw, ch = sh;
        for (int i = 0; i < methods.Length - 1; i++)
        {
            int nh = heights[i], nw = (int)Math.Round(sw * nh / (double)sh);
            if (nh >= ch) throw new ArgumentException(string.Format("Step {0} ({1}:{2}) does not reduce: the figure is already {3} high.", i + 1, methods[i], nh, ch));
            p = Resample(p, cw, ch, nw, nh, methods[i]);
            cw = nw; ch = nh;
        }

        var s2 = p; int w2 = cw, h2 = ch;
        var outPx = new int[ow * oh];
        double kx = w2 / (double)ow, ky = h2 / (double)oh;
        for (int j = 0; j < oh; j++)
            for (int i = 0; i < ow; i++)
            {
                int x = Math.Min(w2 - 1, (int)Math.Floor((i + 0.5) * kx)), y = Math.Min(h2 - 1, (int)Math.Floor((j + 0.5) * ky)), k = y * w2 + x;
                double a = s2[3][k];
                if (a < 0.5) { outPx[j * ow + i] = 0; continue; }
                outPx[j * ow + i] = unchecked((int)0xFF000000) | (ToSrgb(s2[0][k] / a) << 16) | (ToSrgb(s2[1][k] / a) << 8) | ToSrgb(s2[2][k] / a);
            }
        return outPx;
    }

    // ---------- clean-up for small faces ----------

    /// <summary>Unsharp mask on opaque pixels: c + amount * (c - blur), blur = three box passes of radius 2 over opaque pixels.</summary>
    private static void SharpenSource(int[] px, int w, int h, double amount)
    {
        var plane = new double[3][];
        for (int c = 0; c < 3; c++) plane[c] = new double[w * h];
        var weight = new double[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            if (((px[i] >> 24) & 255) < 128) continue;
            weight[i] = 1; plane[0][i] = (px[i] >> 16) & 255; plane[1][i] = (px[i] >> 8) & 255; plane[2][i] = px[i] & 255;
        }
        var blur = new double[4][] { (double[])plane[0].Clone(), (double[])plane[1].Clone(), (double[])plane[2].Clone(), (double[])weight.Clone() };
        for (int pass = 0; pass < 3; pass++)
            for (int c = 0; c < 4; c++) blur[c] = Box(blur[c], w, h, 2);
        for (int i = 0; i < px.Length; i++)
        {
            if (weight[i] == 0 || blur[3][i] <= 0) continue;
            int a = (px[i] >> 24) & 255, res = a << 24;
            for (int c = 0; c < 3; c++)
            {
                double v = plane[c][i], b = blur[c][i] / blur[3][i];
                int o = (int)Math.Round(Math.Max(0, Math.Min(255, v + amount * (v - b))));
                res |= o << (16 - 8 * c);
            }
            px[i] = res;
        }
    }

    private static double[] Box(double[] v, int w, int h, int radius)
    {
        var tmp = new double[v.Length]; var res = new double[v.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sum = 0; for (int k = -radius; k <= radius; k++) sum += v[y * w + Math.Max(0, Math.Min(w - 1, x + k))];
                tmp[y * w + x] = sum / (2 * radius + 1);
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sum = 0; for (int k = -radius; k <= radius; k++) sum += tmp[Math.Max(0, Math.Min(h - 1, y + k)) * w + x];
                res[y * w + x] = sum / (2 * radius + 1);
            }
        return res;
    }

    /// <summary>A pixel whose four neighbours are opaque and all one other colour takes that colour (one pass).</summary>
    private static void DespeckleOut(int[] o, int w, int h)
    {
        var src = (int[])o.Clone();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int c = src[y * w + x]; if (c == 0) continue;
                int n = src[(y - 1) * w + x];
                if (n == 0 || n == c) continue;
                if (src[(y + 1) * w + x] == n && src[y * w + x - 1] == n && src[y * w + x + 1] == n) o[y * w + x] = n;
            }
    }

    /// <summary>Median cut to at most 'count' colours over the opaque pixels, then each pixel to the nearest of them.</summary>
    private static void Quantize(int[] o, int count)
    {
        var all = new List<int>();
        foreach (var c in o) if (c != 0) all.Add(c);
        if (all.Count == 0) return;
        all.Sort();
        var boxes = new List<List<int>> { all };
        while (boxes.Count < count)
        {
            // Split the box with the widest channel range (ties: the first such box).
            int best = -1, bestRange = 0, bestShift = 0;
            for (int b = 0; b < boxes.Count; b++)
            {
                if (boxes[b].Count < 2) continue;
                for (int shift = 16; shift >= 0; shift -= 8)
                {
                    int lo = 255, hi = 0;
                    foreach (var c in boxes[b]) { int v = (c >> shift) & 255; if (v < lo) lo = v; if (v > hi) hi = v; }
                    if (hi - lo > bestRange) { bestRange = hi - lo; best = b; bestShift = shift; }
                }
            }
            if (best < 0) break;
            var box = boxes[best]; int sh = bestShift;
            box.Sort((a, b) => { int d = ((a >> sh) & 255) - ((b >> sh) & 255); return d != 0 ? d : a.CompareTo(b); });
            int mid = box.Count / 2;
            boxes[best] = box.GetRange(0, mid);
            boxes.Insert(best + 1, box.GetRange(mid, box.Count - mid));
        }
        var palette = new List<int>();
        foreach (var box in boxes)
        {
            long r = 0, g = 0, bl = 0;
            foreach (var c in box) { r += (c >> 16) & 255; g += (c >> 8) & 255; bl += c & 255; }
            palette.Add(unchecked((int)0xFF000000) | (int)(r / box.Count) << 16 | (int)(g / box.Count) << 8 | (int)(bl / box.Count));
        }
        for (int i = 0; i < o.Length; i++)
        {
            if (o[i] == 0) continue;
            int c = o[i], bestC = palette[0]; double bestD = double.MaxValue;
            foreach (var p in palette)
            {
                double dr = ((c >> 16) & 255) - ((p >> 16) & 255), dg = ((c >> 8) & 255) - ((p >> 8) & 255), db = (c & 255) - (p & 255);
                double d = 0.3 * dr * dr + 0.59 * dg * dg + 0.11 * db * db;
                if (d < bestD) { bestD = d; bestC = p; }
            }
            o[i] = bestC;
        }
    }

    public static string Run(string source, int targetHeight, string outPath, string preview, int scale, bool nearest, string steps,
        double sharpen, bool despeckle, int colorCount)
    {
        string[] methods = null; int[] heights = null;
        bool chain = !string.IsNullOrEmpty(steps);
        if (chain)
        {
            var parts = steps.Split(',');
            methods = new string[parts.Length]; heights = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var kv = parts[i].Trim().Split(':');
                methods[i] = kv[0]; heights[i] = int.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                if (methods[i] != "bilinear" && methods[i] != "lanczos" && methods[i] != "area" && methods[i] != "nearest")
                    throw new ArgumentException("Unknown step method '" + methods[i] + "': use bilinear, lanczos, area or nearest.");
                if (i > 0 && heights[i] >= heights[i - 1]) throw new ArgumentException("Steps must get smaller: " + steps);
            }
            if (methods[parts.Length - 1] != "nearest") throw new ArgumentException("The last step must be nearest: " + steps);
            if (heights[parts.Length - 1] != targetHeight) throw new ArgumentException("The last step's height must be the target height " + targetHeight + ": " + steps);
        }

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

        if (sharpen > 0) SharpenSource(px, w, h, sharpen);

        // Opaque bounds and the palette of the source.
        int top = h, bottom = -1, left = w, right = -1;
        var palette = new HashSet<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = px[y * w + x];
                if (((c >> 24) & 255) < 128) continue;
                palette.Add(c | unchecked((int)0xFF000000));
                top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, x); right = Math.Max(right, x);
            }
        if (bottom < 0) throw new InvalidOperationException("The source has no opaque pixels.");
        var colours = new List<int>(palette);
        colours.Sort();

        int sh = bottom - top + 1, sw = right - left + 1;
        double k = (double)sh / targetHeight;                 // source pixels per output pixel, both axes
        int ow = (int)Math.Ceiling(sw / k), oh = targetHeight;
        double offsetX = (ow * k - sw) / 2;                    // centre the figure horizontally
        var outPx = chain ? Chain(px, w, h, left, top, sw, sh, ow, oh, methods, heights) : new int[ow * oh];

        for (int j = 0; j < oh && !chain; j++)
            for (int i = 0; i < ow; i++)
            {
                double x0 = left - offsetX + i * k, x1 = x0 + k, y0 = top + j * k, y1 = y0 + k;
                if (nearest)
                {
                    int cx = (int)Math.Floor(x0 + k / 2), cy = (int)Math.Floor(y0 + k / 2);
                    var c = cx < 0 || cy < 0 || cx >= w || cy >= h ? 0 : px[cy * w + cx];
                    outPx[j * ow + i] = ((c >> 24) & 255) < 128 ? 0 : c | unchecked((int)0xFF000000);
                    continue;
                }

                double a = 0, r = 0, gg = 0, b = 0, area = 0;
                for (int y = (int)Math.Floor(y0); y < Math.Ceiling(y1); y++)
                    for (int x = (int)Math.Floor(x0); x < Math.Ceiling(x1); x++)
                    {
                        double cover = (Math.Min(x + 1, x1) - Math.Max(x, x0)) * (Math.Min(y + 1, y1) - Math.Max(y, y0));
                        if (cover <= 0) continue;
                        area += cover;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        var c = px[y * w + x];
                        double al = ((c >> 24) & 255) / 255.0 * cover;
                        a += al; r += al * ((c >> 16) & 255); gg += al * ((c >> 8) & 255); b += al * (c & 255);
                    }
                if (a < area * 0.5) { outPx[j * ow + i] = 0; continue; }
                r /= a; gg /= a; b /= a;
                int best = 0; double bestD = double.MaxValue;
                foreach (var c in colours)
                {
                    double dr = ((c >> 16) & 255) - r, dg = ((c >> 8) & 255) - gg, db = (c & 255) - b;
                    double d = dr * dr + dg * dg + db * db;
                    if (d < bestD) { bestD = d; best = c; }
                }
                outPx[j * ow + i] = best;
            }

        if (colorCount > 0) Quantize(outPx, colorCount);
        if (despeckle) DespeckleOut(outPx, ow, oh); // after the palette: only then do neighbours share exact colours

        var used = new HashSet<int>();
        foreach (var c in outPx) if (c != 0) used.Add(c);

        using (var result = new Bitmap(ow, oh, PixelFormat.Format32bppArgb))
        {
            var data = result.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < oh; y++) Marshal.Copy(outPx, y * ow, data.Scan0 + y * data.Stride, ow);
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
            "source figure {0}x{1}, factor 1/{2:F3}{7}; result {3}x{4}; source colours {5}, used {6}",
            sw, sh, k, ow, oh, colours.Count, used.Count, chain ? ", steps " + steps : nearest ? ", nearest" : "");
    }
}
'@

$report = [ReduceToGrid]::Run((Resolve-Path $Source).Path, $Height, [IO.Path]::GetFullPath($Out),
    $(if ($Preview) { [IO.Path]::GetFullPath($Preview) } else { '' }), $PreviewScale, $Method -eq 'nearest', $Steps, $Sharpen, $Despeckle.IsPresent, $Colors)
Write-Output $report
