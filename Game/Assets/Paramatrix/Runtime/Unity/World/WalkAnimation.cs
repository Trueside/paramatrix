using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Frame-by-frame walk cycle (Visual Production Standard 0.3, В-27): while the character moves, its sprite steps
    /// through the walk frames; at rest it shows the standing frame.
    /// Frames advance by distance walked, not by time, so the feet do not slide: speeding up and slowing down
    /// (<see cref="Walker"/>) slow the cycle down with the body. Facing is the walker's (sprite flip).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(Walker))]
    public sealed class WalkAnimation : MonoBehaviour
    {
        [SerializeField]
        private Sprite standing;

        [SerializeField]
        private Sprite[] frames = new Sprite[0];

        /// <summary>Grid pixels walked per frame: at cruising speed (3 px a tick) a frame lasts 6 ticks, 10 frames a second.</summary>
        [SerializeField]
        private float pixelsPerFrame = 18f;

        private SpriteRenderer _renderer;
        private Walker _walker;
        private float _lastX;
        private float _distance;

        public bool IsWalking { get; private set; }

        public int Frame { get; private set; }

        public void SetFrames(Sprite standingSprite, Sprite[] walkFrames, float distancePerFrame)
        {
            standing = standingSprite;
            frames = walkFrames;
            pixelsPerFrame = distancePerFrame;
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _walker = GetComponent<Walker>();
            _lastX = transform.position.x;
        }

        private void LateUpdate()
        {
            var x = transform.position.x;
            var moved = Mathf.Abs(x - _lastX) * PixelGrid.PixelsPerUnit;
            _lastX = x;
            // The walker's state, not this frame's distance, says whether it walks: a frame without a movement tick
            // must not flash the standing pose. A jump across the screen is a placement, not a step.
            if (!_walker.IsMoving || moved > PixelGrid.Width)
            {
                IsWalking = false;
                _distance = 0f;
                Frame = 0;
                _renderer.sprite = standing;
                return;
            }

            if (!IsWalking)
            {
                IsWalking = true;
                _distance = 0f;
                Frame = 0;
            }

            _distance += moved;
            while (_distance >= pixelsPerFrame)
            {
                _distance -= pixelsPerFrame;
                Frame = (Frame + 1) % frames.Length;
            }

            _renderer.sprite = frames[Frame];
        }
    }
}
