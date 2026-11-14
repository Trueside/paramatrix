using UnityEditor;
using UnityEngine;

namespace Paramatrix.Unity.Editor
{
    /// <summary>
    /// Hand assembly of location scenes on the pixel grid (scene kit document): moving objects in steps of one grid
    /// pixel, and putting stray objects back on whole grid pixels. The project validator reports any sprite that is
    /// off the grid, scaled or rotated.
    /// </summary>
    public static class PixelGridTools
    {
        /// <summary>One grid pixel in world units.</summary>
        public static float Step => 1f / PixelGrid.PixelsPerUnit;

        [MenuItem("Paramatrix/Pixel Grid Snapping")]
        public static void EnableSnapping()
        {
            EditorSnapSettings.move = new Vector3(Step, Step, 1f);
            EditorSnapSettings.gridSnapEnabled = true;
            Debug.Log("[Paramatrix] Scene view: moving snaps to whole grid pixels (1/48 of a unit).");
        }

        [MenuItem("Paramatrix/Snap Selection To Pixel Grid")]
        public static void SnapSelection()
        {
            foreach (var transform in Selection.transforms)
            {
                Undo.RecordObject(transform, "Snap to pixel grid");
                transform.position = Snap(transform.position);
            }
        }

        [MenuItem("Paramatrix/Snap Selection To Pixel Grid", true)]
        private static bool CanSnapSelection() => Selection.transforms.Length > 0;

        public static Vector3 Snap(Vector3 position) =>
            new Vector3(Mathf.Round(position.x * PixelGrid.PixelsPerUnit) * Step, Mathf.Round(position.y * PixelGrid.PixelsPerUnit) * Step, position.z);

        /// <summary>Whether a world coordinate lies on a whole grid pixel.</summary>
        public static bool OnGrid(float value) => Mathf.Abs((value * PixelGrid.PixelsPerUnit) - Mathf.Round(value * PixelGrid.PixelsPerUnit)) < 0.01f;
    }
}
