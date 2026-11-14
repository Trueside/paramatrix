using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// A layer of a scrolling location drawn nearer or farther than the floor (В-19). Its children are placed as seen
    /// with the camera in the middle of the location's view; when the camera moves by d, the layer moves by
    /// d × <see cref="Follow"/>: 0 — on the floor plane (moves with the world), 0.5 — far behind, close to 1 — on the
    /// horizon; negative — in front of the floor plane (foreground passes faster than the world).
    /// The layer is moved in whole grid pixels. Clickable entities never live on a layer.
    /// </summary>
    public sealed class ParallaxLayer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Share of the camera's movement the layer follows: 0 floor plane, 0..1 background, < 0 foreground.")]
        private float follow;

        private float _authoredX;
        private bool _captured;

        public float Follow => follow;

        /// <summary>Why the layer cannot work, or null. A layer that follows the camera fully or more is not a layer.</summary>
        public string Problem => follow >= 1f ? $"parallax layer '{name}' follows the camera by {follow}; it must be below 1" : null;

        /// <summary>Places the layer for the camera at <paramref name="cameraX"/>; <paramref name="centerX"/> is the location's view centre.</summary>
        public void Apply(float cameraX, float centerX)
        {
            if (!_captured)
            {
                _authoredX = transform.position.x;
                _captured = true;
            }

            var x = _authoredX + ((cameraX - centerX) * follow);
            var position = transform.position;
            transform.position = new Vector3(Mathf.Round(x * PixelGrid.PixelsPerUnit) / PixelGrid.PixelsPerUnit, position.y, position.z);
        }
    }
}
