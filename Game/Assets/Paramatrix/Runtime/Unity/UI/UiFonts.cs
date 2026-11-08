using TMPro;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Font for all game text. A dynamic font asset over an installed Windows font (A14: Windows only): it reads the
    /// font from the system at runtime and embeds nothing in the project, so no font licence question arises.
    /// The final typeface is an art decision; replacing it means supplying another TMP font asset.
    /// </summary>
    public static class UiFonts
    {
        private static readonly string[] Candidates = { "Segoe UI", "Arial", "Tahoma" };

        public const string CyrillicProbe = "АаБбВвЁёЖжЩщЪъЫыЭэЮюЯя";

        /// <summary>Creates the font, or returns null with the reason. The font must render Cyrillic.</summary>
        public static TMP_FontAsset CreateSystemFont(out string error)
        {
            foreach (var family in Candidates)
            {
                var font = TMP_FontAsset.CreateFontAsset(family, "Regular");
                if (font != null && font.HasCharacters(CyrillicProbe, out _, true, true))
                {
                    font.name = family + " (system, dynamic)";
                    error = null;
                    return font;
                }
            }

            error = "no installed system font with Cyrillic glyphs was found (tried " + string.Join(", ", Candidates) + ")";
            return null;
        }
    }
}
