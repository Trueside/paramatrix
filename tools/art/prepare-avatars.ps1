# Prepare avatars (decision R10) and novel pictures (R12). Avatars: makes every emotion picture of a character exactly
# the avatar size (115x135 Full HD screen pixels, DialogueView.AvatarWidth x AvatarHeight: all avatars of all characters
# are the same size) and puts it where the game takes it:
# Game/Assets/Paramatrix/Art/Characters/<name>/emotions/<emotion>.png for character.<name>.
# Novel pictures (-Kind novel, or 'novel' in characters.json): fitted into 630x880 (DialogueView.NovelPicture*), keeping
# proportions, bottom centred, the rest transparent, into .../<name>/novel/<emotion>.png.
# A source on a flat chroma green background (-KeyGreen, or "key": "green") has the green taken away first: green-dominant
# pixels become transparent, the soft edge keeps part of its alpha and loses its green cast.
# Then run Paramatrix > Collect Content (or Build Scenes) and Validate Project.
#
# Avatars are pictures in screen pixels, not pixel art on the world grid: the game shows one texel per Full HD pixel and
# scales them only with the screen resolution (smoothly, with mipmaps).
#
# Source layout: <folder>/<emotion>.png, one picture per emotion, only the files directly in the folder (subfolders are
# the artist's archive and are not read). default.png is required by content.
# Method, deterministic, per picture:
#   1. what is drawn: the opaque bounds when the picture has transparency, the whole picture otherwise;
#   2. the drawn area must have the standard avatar framing: the proportions of the avatar size (the 229x270 card cut,
#      head and shoulders as on the hero cards) within 1%, and not smaller than the avatar; it is resized to exactly
#      the avatar size (229x270 -> 115x135, half). Anything else is an error, so no avatar comes out a different size
#      or scale. -Pad (placeholders only): fit any picture inside, keeping proportions, enlarging if needed, and fill the
#      rest with transparency, bottom centred;
#   3. Lanczos-3 resampling in linear light (sRGB decoded, premultiplied alpha), so dark lines keep their weight;
#   4. a source drawn looking left is mirrored: every avatar in the game looks right, the game mirrors it by facing.
# Fails on the first problem: no source folder, no picture, a picture with nothing drawn.
#
# Usage (Windows PowerShell 5.1, no installs; ASCII only in this file), from the repository root:
#   all characters with 'avatars' in characters.json:
#     powershell -ExecutionPolicy Bypass -File tools/art/prepare-avatars.ps1 [-Only character.trinity]
#   one folder directly:
#     powershell -ExecutionPolicy Bypass -File tools/art/prepare-avatars.ps1 -Id character.trinity -Source D:/art/Trinity [-Facing left] [-Pad]
#   novel pictures: add -Kind novel (from 'novel' entries of characters.json, or with -Id/-Source) [-KeyGreen]
param(
    [string]$Only,
    [string]$Id,
    [string]$Source,
    [ValidateSet('right', 'left')][string]$Facing = 'right',
    [ValidateSet('avatar', 'novel')][string]$Kind = 'avatar',
    [int]$Width = 0,
    [int]$Height = 0,
    [switch]$Pad,
    [switch]$KeyGreen
)

if ($Kind -eq 'novel') {
    if ($Width -eq 0) { $Width = 630 }; if ($Height -eq 0) { $Height = 880 }
    $Pad = [switch]::Present  # a novel picture is fitted, whatever its framing
}
else {
    if ($Width -eq 0) { $Width = 115 }; if ($Height -eq 0) { $Height = 135 }
}

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class FitAvatar
{
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
        double px = Math.PI * x;
        return 3 * Math.Sin(px) * Math.Sin(px / 3) / (px * px);
    }

    /// <summary>Weights of the source samples [first, first + n) for each of 'size' outputs covering [start, start + length).</summary>
    private static void Weights(int start, int length, int size, int limit, out int[] first, out double[][] weights)
    {
        double scale = length / (double)size, filter = Math.Max(1.0, scale), support = 3 * filter; // enlarging: the plain kernel
        first = new int[size]; weights = new double[size][];
        for (int o = 0; o < size; o++)
        {
            double centre = start + (o + 0.5) * scale;
            int a = (int)Math.Floor(centre - support), b = (int)Math.Ceiling(centre + support);
            a = Math.Max(a, start); b = Math.Min(b, Math.Min(start + length, limit) - 1);
            var w = new double[b - a + 1]; double sum = 0;
            for (int s = a; s <= b; s++) { w[s - a] = Lanczos((s + 0.5 - centre) / filter); sum += w[s - a]; }
            for (int i = 0; i < w.Length; i++) w[i] /= sum;
            first[o] = a; weights[o] = w;
        }
    }

    public static string Run(string source, int width, int height, bool pad, bool mirror, bool keyGreen, string outPath)
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

        if (keyGreen)
        {
            // Green dominance (green over the larger of red and blue): above 90 background, below 30 the figure, between a
            // soft edge; the edge's green is brought down to the other channels so no green fringe stays.
            for (int i = 0; i < px.Length; i++)
            {
                int c = px[i], r = (c >> 16) & 255, gr = (c >> 8) & 255, b = c & 255, m = Math.Max(r, b), dom = gr - m;
                double a = dom <= 30 ? 1 : dom >= 90 ? 0 : 1 - (dom - 30) / 60.0;
                if (a <= 0) { px[i] = 0; continue; }
                if (dom > 0) gr = m;
                px[i] = ((int)Math.Round(a * ((c >> 24) & 255)) << 24) | (r << 16) | (gr << 8) | b;
            }
        }

        int top = h, bottom = -1, left = w, right = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (((px[y * w + x] >> 24) & 255) == 0) continue;
                top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, x); right = Math.Max(right, x);
            }
        if (bottom < 0) throw new InvalidOperationException(source + ": nothing is drawn.");

        int sw = right - left + 1, sh = bottom - top + 1, ow, oh;
        if (pad)
        {
            double k = Math.Max(sw / (double)width, sh / (double)height); // source pixels per output pixel
            ow = Math.Max(1, Math.Min(width, (int)Math.Round(sw / k))); oh = Math.Max(1, Math.Min(height, (int)Math.Round(sh / k)));
        }
        else
        {
            double aspect = (sw / (double)sh) / (width / (double)height);
            if (Math.Abs(aspect - 1) > 0.01)
                throw new InvalidOperationException(string.Format("{0}: drawn {1}x{2}, its proportions are not the avatar size {3}x{4} (the standard card cut, e.g. 229x270).", source, sw, sh, width, height));
            if (sw < width || sh < height)
                throw new InvalidOperationException(string.Format("{0}: drawn {1}x{2} is smaller than the avatar {3}x{4}: it would be enlarged.", source, sw, sh, width, height));
            ow = width; oh = height;
        }

        // Linear, premultiplied: r, g, b, a planes of the drawn area.
        var src = new double[4][];
        for (int c = 0; c < 4; c++) src[c] = new double[sw * sh];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int p = px[(top + y) * w + left + x], i = y * sw + x;
                double a = ((p >> 24) & 255) / 255.0;
                src[0][i] = ToLinear((p >> 16) & 255) * a; src[1][i] = ToLinear((p >> 8) & 255) * a; src[2][i] = ToLinear(p & 255) * a; src[3][i] = a;
            }

        int[] fx, fy; double[][] wx, wy;
        Weights(0, sw, ow, sw, out fx, out wx);
        Weights(0, sh, oh, sh, out fy, out wy);

        var rows = new double[4][];                                   // horizontal pass: sh x ow
        for (int c = 0; c < 4; c++) rows[c] = new double[sh * ow];
        for (int y = 0; y < sh; y++)
            for (int o = 0; o < ow; o++)
                for (int c = 0; c < 4; c++)
                {
                    double v = 0; var wt = wx[o];
                    for (int t = 0; t < wt.Length; t++) v += wt[t] * src[c][y * sw + fx[o] + t];
                    rows[c][y * ow + o] = v;
                }

        var outPx = new int[width * height];                          // vertical pass; the rest stays transparent
        int padX = (width - ow) / 2, padY = height - oh;              // bottom centred
        for (int o = 0; o < oh; o++)
            for (int x = 0; x < ow; x++)
            {
                var v = new double[4]; var wt = wy[o];
                for (int c = 0; c < 4; c++)
                    for (int t = 0; t < wt.Length; t++) v[c] += wt[t] * rows[c][(fy[o] + t) * ow + x];
                double a = Math.Max(0, Math.Min(1, v[3]));
                int ia = (int)Math.Round(a * 255), color = 0;
                if (ia > 0) color = (ia << 24) | (ToSrgb(v[0] / a) << 16) | (ToSrgb(v[1] / a) << 8) | ToSrgb(v[2] / a);
                outPx[(padY + o) * width + padX + (mirror ? ow - 1 - x : x)] = color;
            }

        using (var result = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            var data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++) Marshal.Copy(outPx, y * width, data.Scan0 + y * data.Stride, width);
            result.UnlockBits(data);
            result.Save(outPath, ImageFormat.Png);
        }

        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "drawn {0}x{1}{2} -> {3}x{4} in {5}x{6}", sw, sh, mirror ? ", mirrored" : "", ow, oh, width, height);
    }
}
'@

function Prepare([string]$characterId, [string]$folder, [string]$facing, [bool]$key) {
    if (-not $characterId.StartsWith('character.')) { throw "'$characterId' is not a character ID." }
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw "${characterId}: source folder $folder is missing." }
    $pictures = @(Get-ChildItem -LiteralPath $folder -Filter '*.png' -File | Sort-Object Name)
    if ($pictures.Count -eq 0) { throw "${characterId}: no PNG directly in $folder (pictures go to <folder>/<emotion>.png)." }
    $name = $characterId.Substring('character.'.Length)
    $out = Join-Path 'Game/Assets/Paramatrix/Art/Characters' (Join-Path $name $(if ($Kind -eq 'novel') { 'novel' } else { 'emotions' }))
    New-Item -ItemType Directory -Force $out | Out-Null
    foreach ($p in $pictures) {
        $target = [IO.Path]::GetFullPath((Join-Path $out $p.Name.ToLowerInvariant()))
        $line = [FitAvatar]::Run($p.FullName, $Width, $Height, $Pad.IsPresent, $facing -eq 'left', $key, $target)
        Write-Output "${characterId} $($p.BaseName): $line"
    }
}

if ($Id -or $Source) {
    if (-not ($Id -and $Source)) { throw '-Id and -Source go together.' }
    Prepare $Id $Source $Facing $KeyGreen.IsPresent
    exit 0
}

$data = Get-Content (Join-Path $PSScriptRoot 'characters.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$done = 0
foreach ($c in $data.characters) {
    if ($Only -and $c.id -ne $Only) { continue }
    $entry = if ($Kind -eq 'novel') { $c.novel } else { $c.avatars }
    if ($null -eq $entry) { continue }
    $facing = if ($entry.facing) { $entry.facing } else { 'right' }
    Prepare $c.id $entry.source $facing ($KeyGreen.IsPresent -or $entry.key -eq 'green')
    $done++
}
if ($done -eq 0) { throw "No character with '$Kind' pictures in characters.json matched." }
