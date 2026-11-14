# Reduce stepwise: shrinks a clean cut-out figure (transparent background) to a target height in many gentle steps,
# each with its own method, as found by hand in paint.net: large steps first, then smaller and smaller ones; the large
# steps use the methods that lose least, small middle steps of nearest pull pixels together into groups, and the
# last step is a sharp smooth one.
#
# Step sizes, deterministic: in log scale the steps shrink proportionally - step i of N takes a share (N - i) / sum of
# the whole log reduction (the first step the largest, the last the smallest), so there is no abrupt step. -Shape 2
# makes the shrinking steeper (shares (N - i)^2 / sum), -Shape 0 makes all steps equal.
#
# -Space srgb (default) mixes colours as stored: a thin dark line keeps its darkness; -Space linear mixes light
# physically (as paint.net does with its default "Use gamma correction"), which makes thin dark lines (eyes, brows) fade
# when they blend with skin. Chosen for the characters (variant D): -MaxStep 0.05 -Pattern lanczos -Last sharp -Space srgb.
# Methods (on premultiplied alpha):
#   area      box average (supersampling): exact for large reductions, no ringing;
#   bilinear  tent filter as wide as the step;
#   lanczos   Lanczos-3: sharp, little loss;
#   sharp     Catmull-Rom bicubic: a sharp cubic, close to paint.net "Adaptive (sharp)" (whose exact filter is not public);
#   nearest   the source pixel at each output pixel centre: no new colours, pixels snap into groups.
# Alpha stays smooth between steps and is made binary (>= 0.5) at the very end.
#
# -MaxStep 0.05 (instead of -Methods' count and -Shape): no step reduces by more than that share. The number of steps is
# computed - the fewest steps that keep every step within it, N = ceil(ln(target/source) / ln(1 - MaxStep)) - and the
# steps are equal in log scale; every step's size is a whole pixel size (width and height rounded from the same scale,
# the last step exactly the target), checked again after rounding. -Pattern gives the methods of all steps but the last,
# repeated in order (e.g. "lanczos,lanczos,nearest"); -Last gives the last step's method (default sharp). -Before also
# writes the picture before the last step (for doing the last step by hand, e.g. paint.net Adaptive (Sharp)).
# Note on paint.net: its "Use gamma correction" is on by default, i.e. it mixes in linear light; -Space linear matches it.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file):
#   powershell -ExecutionPolicy Bypass -File tools/art/reduce-stepwise.ps1 -Source figure.png -Height 196 -Out sprite.png `
#       -MaxStep 0.05 -Pattern "lanczos,lanczos,nearest" -Last sharp [-Space linear] [-Before before.png]
#   powershell -ExecutionPolicy Bypass -File tools/art/reduce-stepwise.ps1 -Source figure.png -Height 196 -Out sprite.png `
#       -Methods "area,area,area,lanczos,lanczos,nearest,lanczos,nearest,sharp" [-Shape 1]
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][int]$Height,
    [Parameter(Mandatory = $true)][string]$Out,
    [string[]]$Methods,
    [double]$MaxStep = 0,
    [string[]]$Pattern = @('sharp'),
    [string]$Last = 'sharp',
    [string]$Before,
    [double]$Shape = 1,
    [ValidateSet('srgb', 'linear')][string]$Space = 'srgb'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Stepwise
{
    private static bool Linear;

    private static double ToLinear(int c) { double v = c / 255.0; return !Linear ? v : v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }

    private static int ToSrgb(double v)
    {
        v = Math.Max(0, Math.Min(1, v));
        if (!Linear) return (int)Math.Round(v * 255);
        v = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        return (int)Math.Round(v * 255);
    }

    private static double Kernel(string method, double x)
    {
        x = Math.Abs(x);
        switch (method)
        {
            case "bilinear": return Math.Max(0, 1 - x);
            case "lanczos":
                if (x < 1e-9) return 1;
                if (x >= 3) return 0;
                double p = Math.PI * x;
                return 3 * Math.Sin(p) * Math.Sin(p / 3) / (p * p);
            case "sharp": // Catmull-Rom (B = 0, C = 0.5)
                if (x < 1) return 1.5 * x * x * x - 2.5 * x * x + 1;
                if (x < 2) return -0.5 * x * x * x + 2.5 * x * x - 4 * x + 2;
                return 0;
            default: throw new ArgumentException("Unknown kernel " + method);
        }
    }

    private static double Support(string method) { return method == "bilinear" ? 1 : method == "sharp" ? 2 : 3; }

    /// <summary>One axis: n samples to m, 'count' lines across; planes [c][...].</summary>
    private static double[][] Axis(double[][] p, int n, int m, int count, bool horizontal, string method)
    {
        double scale = n / (double)m, filter = Math.Max(1.0, scale);
        var res = new double[4][];
        for (int c = 0; c < 4; c++) res[c] = new double[m * count];
        for (int o = 0; o < m; o++)
        {
            double a0 = o * scale, a1 = a0 + scale, centre = a0 + scale / 2;
            int from, to; double[] wts;
            if (method == "nearest")
            {
                from = to = Math.Min(n - 1, (int)Math.Floor(centre)); wts = new double[] { 1 };
            }
            else if (method == "area")
            {
                from = (int)Math.Floor(a0); to = Math.Min(n - 1, (int)Math.Ceiling(a1) - 1);
                wts = new double[to - from + 1]; double sum = 0;
                for (int i = from; i <= to; i++) { wts[i - from] = Math.Max(0, Math.Min(i + 1, a1) - Math.Max(i, a0)); sum += wts[i - from]; }
                for (int i = 0; i < wts.Length; i++) wts[i] /= sum;
            }
            else
            {
                double support = Support(method) * filter;
                from = Math.Max(0, (int)Math.Floor(centre - support)); to = Math.Min(n - 1, (int)Math.Ceiling(centre + support));
                wts = new double[to - from + 1]; double sum = 0;
                for (int i = from; i <= to; i++) { wts[i - from] = Kernel(method, (i + 0.5 - centre) / filter); sum += wts[i - from]; }
                for (int i = 0; i < wts.Length; i++) wts[i] /= sum;
            }

            for (int line = 0; line < count; line++)
                for (int c = 0; c < 4; c++)
                {
                    double v = 0;
                    for (int i = from; i <= to; i++) v += wts[i - from] * (horizontal ? p[c][line * n + i] : p[c][i * count + line]);
                    if (c == 3) v = Math.Max(0, Math.Min(1, v));
                    if (horizontal) res[c][line * m + o] = v; else res[c][o * count + line] = v;
                }
        }
        return res;
    }

    public static string Run(string source, int target, string outPath, string methodList, double shape, bool linear,
        double maxStep, string pattern, string last, string beforePath)
    {
        Linear = linear;
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

        int top = h, bottom = -1, left = w, right = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (((px[y * w + x] >> 24) & 255) >= 128) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, x); right = Math.Max(right, x); }
        if (bottom < 0) throw new InvalidOperationException("The source has no opaque pixels.");
        int sw = right - left + 1, sh = bottom - top + 1;
        if (target >= sh) throw new ArgumentException("The target height " + target + " is not smaller than the figure (" + sh + ").");

        var p0 = new double[4][];
        for (int c = 0; c < 4; c++) p0[c] = new double[sw * sh];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int c = px[(top + y) * w + left + x], i = y * sw + x;
                double a = ((c >> 24) & 255) / 255.0;
                p0[0][i] = ToLinear((c >> 16) & 255) * a; p0[1][i] = ToLinear((c >> 8) & 255) * a; p0[2][i] = ToLinear(c & 255) * a; p0[3][i] = a;
            }

        string[] methods;
        if (maxStep > 0)
        {
            // The fewest steps that keep each within maxStep; every step equal in log scale.
            int count = (int)Math.Ceiling(Math.Log(target / (double)sh) / Math.Log(1 - maxStep) - 1e-9);
            var cycle = pattern.Split(',');
            methods = new string[count];
            for (int i = 0; i < count - 1; i++) methods[i] = cycle[i % cycle.Length];
            methods[count - 1] = last;
        }
        else
        {
            if (string.IsNullOrEmpty(methodList)) throw new ArgumentException("Give -Methods, or -MaxStep with -Pattern and -Last.");
            methods = methodList.Split(',');
        }
        int n = methods.Length;
        if (maxStep > 0) shape = 0; // equal steps
        foreach (var m in methods)
            if (m != "area" && m != "bilinear" && m != "lanczos" && m != "sharp" && m != "nearest")
                throw new ArgumentException("Unknown method '" + m + "': use area, bilinear, lanczos, sharp or nearest.");

        int[] heights;
        if (maxStep > 0)
        {
            // Whole pixel sizes, none more than maxStep below the one before: each height is the geometric one rounded,
            // but never below ceil(previous * (1 - maxStep)); if the target is not reached in n steps, one more step.
            while (true)
            {
                heights = new int[n]; int prev = sh; bool ok = true;
                for (int i = 0; i < n; i++)
                {
                    int floor = (int)Math.Ceiling(prev * (1 - maxStep) - 1e-9);
                    int want = i == n - 1 ? target : (int)Math.Round(sh * Math.Pow(target / (double)sh, (i + 1) / (double)n));
                    heights[i] = Math.Max(want, floor);
                    if (i == n - 1 && heights[i] != target) { ok = false; break; }
                    if (heights[i] >= prev) throw new InvalidOperationException("Step " + (i + 1) + " would not reduce (" + prev + ").");
                    prev = heights[i];
                }
                if (ok) break;
                n++;
                var grown = new string[n];
                var cyc = pattern.Split(',');
                for (int i = 0; i < n - 1; i++) grown[i] = cyc[i % cyc.Length];
                grown[n - 1] = last;
                methods = grown;
            }
        }
        else
        {
            // Heights: log shares (n - i)^shape, the first step largest.
            var share = new double[n]; double total = 0;
            for (int i = 0; i < n; i++) { share[i] = Math.Pow(n - i, shape); total += share[i]; }
            heights = new int[n]; double logR = Math.Log(target / (double)sh), acc = 0; int prev = sh;
            for (int i = 0; i < n; i++)
            {
                acc += share[i] / total;
                heights[i] = i == n - 1 ? target : (int)Math.Round(sh * Math.Exp(logR * acc));
                if (heights[i] >= prev) throw new ArgumentException("Too many steps: step " + (i + 1) + " would not reduce (" + prev + " -> " + heights[i] + "). Use fewer steps.");
                prev = heights[i];
            }
        }

        var p = p0; int cw = sw, ch = sh; var plan = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (i == n - 1 && !string.IsNullOrEmpty(beforePath)) Save(p, cw, ch, beforePath);
            int nh = heights[i], nw = Math.Max(1, (int)Math.Round(sw * nh / (double)sh));
            var rows = Axis(p, cw, nw, ch, true, methods[i]);
            p = Axis(rows, ch, nh, nw, false, methods[i]);
            plan.Add(methods[i] + ":" + nh);
            cw = nw; ch = nh;
        }

        Save(p, cw, ch, outPath, true);
        return string.Format("source {0}x{1} -> {2}x{3}; {4} steps: {5}", sw, sh, cw, ch, n, string.Join(" ", plan.ToArray()));
    }

    /// <summary>Planes to PNG; final: binary alpha (>= 0.5), else the soft alpha kept (a picture to finish by hand).</summary>
    private static void Save(double[][] p, int cw, int ch, string path, bool final = false)
    {
        var outPx = new int[cw * ch];
        for (int i = 0; i < outPx.Length; i++)
        {
            double a = p[3][i];
            if (final ? a < 0.5 : a <= 0) continue;
            int ia = final ? 255 : (int)Math.Round(Math.Min(1, a) * 255);
            outPx[i] = (ia << 24) | (ToSrgb(p[0][i] / a) << 16) | (ToSrgb(p[1][i] / a) << 8) | ToSrgb(p[2][i] / a);
        }

        using (var result = new Bitmap(cw, ch, PixelFormat.Format32bppArgb))
        {
            var data = result.LockBits(new Rectangle(0, 0, cw, ch), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < ch; y++) Marshal.Copy(outPx, y * cw, data.Scan0 + y * data.Stride, cw);
            result.UnlockBits(data);
            result.Save(path, ImageFormat.Png);
        }
    }
}
'@

$full = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($full)) | Out-Null
# powershell -File passes "a,b,c" as an array: join it back into one comma list.
$methodList = if ($Methods) { ($Methods | ForEach-Object { $_ -split ',' }) -join ',' } else { '' }
$patternList = ($Pattern | ForEach-Object { $_ -split ',' }) -join ','
$beforeFull = if ($Before) { [IO.Path]::GetFullPath($Before) } else { '' }
[Stepwise]::Run((Resolve-Path -LiteralPath $Source).Path, $Height, $full, $methodList, $Shape, $Space -eq 'linear', $MaxStep, $patternList, $Last, $beforeFull)
