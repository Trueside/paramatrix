using System;
using System.Collections.Generic;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// The world camera of the Bootstrap scene. Vertically it puts the location's floor line (world y = 0) at the
    /// location's floor depth above the bottom of the screen. Horizontally (В-19): a location one screen wide is a fixed screen,
    /// a wider one scrolls with the hero, never showing past the location's view edges. The hero pushes the screen: the
    /// camera stands still while he walks in the middle and moves only to keep him <see cref="PushMargin"/> from a screen
    /// edge, so most of the time the picture does not move at all (a moving picture smears on the display). The camera
    /// stands on whole grid pixels, then moves the location's parallax layers. Runs after everything that moves figures.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(1000)]
    public sealed class WorldCamera : MonoBehaviour
    {
        private LocationRoot _root;
        private IReadOnlyList<ParallaxLayer> _layers = Array.Empty<ParallaxLayer>();
        private Transform _target;
        private float _x;

        /// <summary>How close to a screen edge the hero comes before he pushes the screen, grid pixels (3/8 of the screen).</summary>
        public const int PushMargin = PixelGrid.Width * 3 / 8;

        /// <summary>Half the screen width in world units.</summary>
        public static float HalfWidth => PixelGrid.Units(PixelGrid.Width) / 2f;

        public float X => transform.position.x;

        /// <summary>Called by the scene loader with the new location; the camera jumps to its place at once.</summary>
        public void EnterScene(LocationRoot root, IReadOnlyList<ParallaxLayer> layers, Transform target)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _x = _target.position.x; // a new location starts with the hero in the middle (or as near as its edges allow)
            Place();
        }

        private void LateUpdate()
        {
            // Unity's null: the location scene is being replaced; the camera keeps its place until the next one.
            if (_root != null && _target != null)
            {
                Place();
            }
        }

        private void Place()
        {
            // The hero pushes the screen only past the dead zone in the middle; then the camera keeps him at its edge.
            var reach = HalfWidth - PixelGrid.Units(PushMargin);
            var hero = _target.position.x;
            if (hero > _x + reach)
            {
                _x = hero - reach;
            }
            else if (hero < _x - reach)
            {
                _x = hero + reach;
            }

            _x = Mathf.Clamp(_x, _root.ViewMinX + HalfWidth, _root.ViewMaxX - HalfWidth);
            var x = Mathf.Round(_x * PixelGrid.PixelsPerUnit) / PixelGrid.PixelsPerUnit;
            var y = PixelGrid.Units((PixelGrid.Height / 2f) - _root.FloorDepth);
            transform.position = new Vector3(x, y, transform.position.z);
            foreach (var layer in _layers)
            {
                layer.Apply(x, _root.ViewCenterX);
            }
        }
    }
}
