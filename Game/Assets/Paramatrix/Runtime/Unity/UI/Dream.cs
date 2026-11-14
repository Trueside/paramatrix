using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// The novel's dreamy backdrop (Р12) from a picture of the location: smaller and blurred (the eye is on the partner),
    /// a little more colourful and lighter, with a faint pink veil — "as in a dream". Deterministic, on the CPU, once per
    /// novel: no shaders, no post-processing.
    /// </summary>
    internal static class Dream
    {
        private const int Shrink = 4;          // 960x540 -> 240x135, shown stretched: soft by itself
        private const int BlurRadius = 2;      // box blur radius in shrunk pixels
        private const int BlurPasses = 3;      // three box passes ~ a gaussian
        private const float Saturation = 1.35f;
        private const float Lift = 1.06f;
        private const float VeilShare = 0.10f;
        private static readonly Vector3 Veil = new Vector3(1f, 0.82f, 0.95f);

        public static Texture2D Make(Color32[] pixels, int width, int height)
        {
            int w = width / Shrink, h = height / Shrink;
            var r = new float[w * h];
            var g = new float[w * h];
            var b = new float[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    float sr = 0f, sg = 0f, sb = 0f;
                    for (var dy = 0; dy < Shrink; dy++)
                    {
                        for (var dx = 0; dx < Shrink; dx++)
                        {
                            var c = pixels[((y * Shrink) + dy) * width + (x * Shrink) + dx];
                            sr += c.r;
                            sg += c.g;
                            sb += c.b;
                        }
                    }

                    var n = Shrink * Shrink * 255f;
                    r[y * w + x] = sr / n;
                    g[y * w + x] = sg / n;
                    b[y * w + x] = sb / n;
                }
            }

            for (var pass = 0; pass < BlurPasses; pass++)
            {
                Blur(r, w, h);
                Blur(g, w, h);
                Blur(b, w, h);
            }

            var result = new Color32[w * h];
            for (var i = 0; i < result.Length; i++)
            {
                var lum = (0.3f * r[i]) + (0.59f * g[i]) + (0.11f * b[i]);
                var c = new Vector3(lum + ((r[i] - lum) * Saturation), lum + ((g[i] - lum) * Saturation), lum + ((b[i] - lum) * Saturation)) * Lift;
                c = Vector3.Lerp(c, Veil, VeilShare);
                result[i] = new Color(Mathf.Clamp01(c.x), Mathf.Clamp01(c.y), Mathf.Clamp01(c.z), 1f);
            }

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "Novel dream",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(result);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>One box pass, horizontal then vertical, edges clamped.</summary>
        private static void Blur(float[] v, int w, int h)
        {
            var tmp = new float[v.Length];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var sum = 0f;
                    for (var k = -BlurRadius; k <= BlurRadius; k++)
                    {
                        sum += v[y * w + Mathf.Clamp(x + k, 0, w - 1)];
                    }

                    tmp[y * w + x] = sum / ((2 * BlurRadius) + 1);
                }
            }

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var sum = 0f;
                    for (var k = -BlurRadius; k <= BlurRadius; k++)
                    {
                        sum += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                    }

                    v[y * w + x] = sum / ((2 * BlurRadius) + 1);
                }
            }
        }
    }
}
