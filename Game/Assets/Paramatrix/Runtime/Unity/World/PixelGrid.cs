namespace Paramatrix.Unity
{
    /// <summary>
    /// The one pixel grid of the game world (Visual Production Standard 0.3, В-06, В-25): every world sprite is
    /// drawn on a 960×540 grid, which the camera scales to the screen — exactly ×2 on Full HD, by the largest whole
    /// factor plus a smooth remainder elsewhere, with bars on other aspect ratios (В-22).
    /// One grid pixel is <see cref="PixelsPerUnit"/>-th of a world unit, for every sprite; the frame is 20 × 11.25 units.
    /// </summary>
    public static class PixelGrid
    {
        public const int Width = 960;

        public const int Height = 540;

        public const int PixelsPerUnit = 48;

        /// <summary>World units spanned by <paramref name="pixels"/> grid pixels.</summary>
        public static float Units(float pixels) => pixels / PixelsPerUnit;

        /// <summary>Half the frame height in world units: the camera's orthographic size.</summary>
        public static float OrthographicSize => Units(Height) / 2f;
    }
}
