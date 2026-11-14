using UnityEditor;
using UnityEngine;

namespace Paramatrix.Unity.Editor
{
    /// <summary>
    /// Every texture in the Art folder is a pixel-grid sprite (Visual Production Standard 0.3, §6.4: the master asset is
    /// one file pixel per grid pixel): <see cref="PixelGrid.PixelsPerUnit"/> pixels per unit, no smoothing, no lossy
    /// compression, no mipmaps, the pivot under the feet (<see cref="Pivot"/>). The rule is applied on import, so art dropped into the folder needs no manual setup;
    /// <see cref="Problem"/> is the same rule as a check, used by the project validator for any sprite in a scene.
    /// Avatars are the exception (<see cref="IsAvatar"/>): pictures in screen pixels, not on the grid (Р10).
    /// </summary>
    public sealed class PixelArtImport : AssetPostprocessor
    {
        public const string ArtFolder = "Assets/Paramatrix/Art/";

        public override uint GetVersion() => 4;

        /// <summary>Avatars (Р10): Characters/&lt;name&gt;/emotions/&lt;emotion&gt;.png, same as DemoSceneBuilder.AvatarsFolder.</summary>
        public const string AvatarsFolder = ArtFolder + "Characters/";

        /// <summary>Unity's largest texture size: a backdrop is never shrunk on import.</summary>
        private const int MaxSize = 16384;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtFolder))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            if (IsAvatar(assetPath))
            {
                ApplyAvatar(importer);
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            // One sprite = the whole picture. Multiple mode keeps the sprite rectangles saved in the .meta: a re-drawn,
            // bigger picture would be cut to its old size.
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.maxTextureSize = MaxSize;
            importer.spritePixelsPerUnit = PixelGrid.PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            // Pivot under the feet: a figure placed at y = 0 stands on the floor line whatever its height, so scenes do
            // not depend on sprite sizes (a re-made, shorter sprite used to float above the floor).
            importer.GetSourceTextureWidthAndHeight(out var width, out _);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = Pivot(width);
            importer.SetTextureSettings(settings);
        }

        /// <summary>
        /// The pivot of a sprite <paramref name="width"/> pixels wide: the bottom edge, at the left edge of the middle
        /// pixel column. On a whole number of pixels, so a figure at a whole-pixel position lies exactly on the grid.
        /// </summary>
        public static Vector2 Pivot(int width) => new Vector2((width / 2) / (float)width, 0f);

        /// <summary>Why a texture is not a pixel-grid sprite, or null if it is.</summary>
        public static string Problem(TextureImporter importer)
        {
            if (importer == null)
            {
                return "not a texture asset";
            }

            if (importer.textureType != TextureImporterType.Sprite)
            {
                return $"texture type is {importer.textureType}, not Sprite";
            }

            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            importer.GetSourceTextureWidthAndHeight(out var sourceWidth, out _);
            if (textureSettings.spriteAlignment != (int)SpriteAlignment.Custom || textureSettings.spritePivot != Pivot(sourceWidth))
            {
                return $"pivot is {textureSettings.spritePivot}, not under the feet {Pivot(sourceWidth)}";
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                return $"sprite mode is {importer.spriteImportMode}, not Single: saved sprite rectangles can cut the picture";
            }

            if (importer.maxTextureSize < MaxSize)
            {
                return $"max texture size is {importer.maxTextureSize}: a bigger picture would be shrunk";
            }

            if (!Mathf.Approximately(importer.spritePixelsPerUnit, PixelGrid.PixelsPerUnit))
            {
                return $"{importer.spritePixelsPerUnit} pixels per unit, the grid is {PixelGrid.PixelsPerUnit}";
            }

            if (importer.filterMode != FilterMode.Point)
            {
                return $"filter mode is {importer.filterMode}, pixel art needs Point";
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                return $"compression is {importer.textureCompression}, pixel art needs Uncompressed";
            }

            if (importer.mipmapEnabled)
            {
                return "mipmaps are on";
            }

            var standalone = importer.GetPlatformTextureSettings("Standalone"); // the only target platform (Windows)
            if (standalone.overridden && (standalone.textureCompression != TextureImporterCompression.Uncompressed
                                          || standalone.format != TextureImporterFormat.Automatic
                                          || standalone.maxTextureSize < MaxSize))
            {
                return "the Standalone platform override changes the texture format, compression or size";
            }

            return null;
        }

        /// <summary>
        /// A picture in screen pixels: an avatar (Characters/&lt;name&gt;/emotions/) or a novel picture
        /// (Characters/&lt;name&gt;/novel/, Р12). Both are imported the same way.
        /// </summary>
        public static bool IsAvatar(string path)
        {
            if (!path.StartsWith(AvatarsFolder))
            {
                return false;
            }

            var parts = path.Substring(AvatarsFolder.Length).Split('/');
            return parts.Length == 3 && (parts[1] == "emotions" || parts[1] == "novel");
        }

        /// <summary>
        /// Avatars are pictures in screen pixels (Р10): one texel is one Full HD pixel, the game scales them only with the
        /// screen resolution. Smoothing with mipmaps keeps fine lines whole on a screen smaller than Full HD; no lossy
        /// compression.
        /// </summary>
        private static void ApplyAvatar(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.maxTextureSize = MaxSize;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = true;
        }

        /// <summary>Why a texture is not imported as an avatar (<see cref="ApplyAvatar"/>), or null if it is.</summary>
        public static string AvatarProblem(TextureImporter importer)
        {
            if (importer == null)
            {
                return "not a texture asset";
            }

            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
            {
                return $"imported as {importer.textureType} / {importer.spriteImportMode}, not one sprite";
            }

            if (importer.filterMode != FilterMode.Trilinear || !importer.mipmapEnabled)
            {
                return $"filter {importer.filterMode}, mipmaps {(importer.mipmapEnabled ? "on" : "off")}: an avatar needs Trilinear with mipmaps";
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed || importer.maxTextureSize < MaxSize)
            {
                return "compressed or size-limited";
            }

            return null;
        }
    }
}
