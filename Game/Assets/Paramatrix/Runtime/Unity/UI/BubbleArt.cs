using System;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Pixel-art pieces of comic bubbles (Р10), drawn in code: a 9-slice body with rounded pixel corners; under it a
    /// speech tail whose tip points down and to the left (mirrored for the other side), or, for a thought, a trail of two
    /// circles toward the thinker. One texel is one grid pixel, so the outline has the same pixel thickness as the world. Placeholder art
    /// until the bubbles are drawn by hand.
    /// </summary>
    public sealed class BubbleArt : IDisposable
    {
        /// <summary>Texels of the body that are not stretched on each side.</summary>
        public const int BodyBorder = 4;

        public const int TailWidth = 8;

        public const int TailHeight = 7;

        private const int BodySize = 12;

        public const int TrailWidth = 10;

        public const int TrailHeight = 12;

        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 Ink = new Color32(24, 22, 30, 255);
        private static readonly Color32 Paper = new Color32(255, 255, 255, 255);

        private readonly Texture2D _bodyTexture;
        private readonly Texture2D _tailTexture;
        private readonly Texture2D _trailTexture;

        public BubbleArt()
        {
            _bodyTexture = NewTexture(BodySize, BodySize, "Bubble body");
            _bodyTexture.SetPixels32(BodyPixels());
            _bodyTexture.Apply(false, true);

            _tailTexture = NewTexture(TailWidth, TailHeight, "Bubble tail");
            _tailTexture.SetPixels32(TailPixels());
            _tailTexture.Apply(false, true);

            // 100 texels per unit with the canvas default of 100 reference pixels per unit: 1 texel = 1 canvas unit;
            // the Image multiplier then makes it one grid pixel.
            Body = Sprite.Create(_bodyTexture, new Rect(0, 0, BodySize, BodySize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(BodyBorder, BodyBorder, BodyBorder, BodyBorder));
            Tail = Sprite.Create(_tailTexture, new Rect(0, 0, TailWidth, TailHeight), new Vector2(0f, 1f), 100f, 0, SpriteMeshType.FullRect);

            _trailTexture = NewTexture(TrailWidth, TrailHeight, "Thought trail");
            _trailTexture.SetPixels32(TrailPixels());
            _trailTexture.Apply(false, true);
            Trail = Sprite.Create(_trailTexture, new Rect(0, 0, TrailWidth, TrailHeight), new Vector2(0.5f, 1f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Two circles under a thought bubble, the smaller one lowest, toward the thinker's head.</summary>
        public Sprite Trail { get; }

        public Sprite Body { get; }

        /// <summary>Tip at the bottom left; the top row continues the body's fill.</summary>
        public Sprite Tail { get; }

        public static Color32 InkColor => Ink;

        public void Dispose()
        {
            UnityEngine.Object.Destroy(Body);
            UnityEngine.Object.Destroy(Tail);
            UnityEngine.Object.Destroy(_bodyTexture);
            UnityEngine.Object.Destroy(_tailTexture);
            UnityEngine.Object.Destroy(Trail);
            UnityEngine.Object.Destroy(_trailTexture);
        }

        private static Texture2D NewTexture(int width, int height, string name) =>
            new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

        /// <summary>Rows from the bottom (Unity's texture order). Corners are cut by two pixels: a rounded pixel corner.</summary>
        private static Color32[] BodyPixels()
        {
            var pixels = new Color32[BodySize * BodySize];
            for (var y = 0; y < BodySize; y++)
            {
                for (var x = 0; x < BodySize; x++)
                {
                    var edgeX = Math.Min(x, BodySize - 1 - x);
                    var edgeY = Math.Min(y, BodySize - 1 - y);
                    Color32 c;
                    if (edgeX + edgeY <= 1)
                    {
                        c = Clear; // the cut corner
                    }
                    else if (edgeX == 0 || edgeY == 0 || (edgeX == 1 && edgeY == 1))
                    {
                        c = Ink; // the outline, with one diagonal pixel at each corner
                    }
                    else
                    {
                        c = Paper;
                    }

                    pixels[(y * BodySize) + x] = c;
                }
            }

            return pixels;
        }

        /// <summary>
        /// A wedge narrowing from the top row (x 2..7) to the tip (x 0) at the bottom; its slanted sides are ink, the top
        /// row is paper so it merges with the body it overlaps.
        /// </summary>
        private static Color32[] TailPixels()
        {
            var pixels = new Color32[TailWidth * TailHeight];
            for (var row = 0; row < TailHeight; row++)
            {
                // row 0 = top of the picture = texture row TailHeight - 1
                var t = row / (float)(TailHeight - 1);
                var left = Mathf.RoundToInt(Mathf.Lerp(2f, 0f, t));
                var right = Mathf.RoundToInt(Mathf.Lerp(TailWidth - 1, 0f, t));
                var y = TailHeight - 1 - row;
                for (var x = 0; x < TailWidth; x++)
                {
                    pixels[(y * TailWidth) + x] = x < left || x > right ? Clear : (x == left || x == right ? Ink : Paper);
                }
            }

            return pixels;
        }

        /// <summary>Two circles: a bigger one under the bubble and a smaller one below it, toward the head.</summary>
        private static Color32[] TrailPixels()
        {
            bool Inside(int x, int y)
            {
                float px = x + 0.5f, py = y + 0.5f;
                bool Circle(float cx, float cy, float r) => ((px - cx) * (px - cx)) + ((py - cy) * (py - cy)) <= r * r;
                return Circle(5.5f, 8.5f, 3.3f) || Circle(2.8f, 2.2f, 1.9f); // apart: two separate circles
            }

            return Outlined(TrailWidth, TrailHeight, Inside);
        }

        /// <summary>Paper inside, ink on inside pixels next to the outside (4-neighbours; beyond the texture is outside).</summary>
        private static Color32[] Outlined(int width, int height, Func<int, int, bool> inside)
        {
            bool In(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && inside(x, y);

            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    pixels[(y * width) + x] = !In(x, y) ? Clear
                        : (!In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1)) ? Ink : Paper;
                }
            }

            return pixels;
        }
    }
}
