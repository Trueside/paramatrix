using System;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Walks a character along the floor line (T19, 2D side view): no obstacles, no path finding,
    /// so a walk to a reachable point always arrives.
    /// <para>
    /// Movement runs in fixed ticks (<see cref="TicksPerSecond"/>, the game's frame rate) and in grid pixels: at
    /// cruising speed every tick moves the same whole number of pixels, so on screen every step is the same size.
    /// A walk speeds up from rest and slows down before its target (and when free walking stops).
    /// Ticks run on scaled time: a paused world (<see cref="WorldPause"/>) does not move.
    /// </para>
    /// </summary>
    public sealed class Walker : MonoBehaviour
    {
        public const int TicksPerSecond = 60;

        /// <summary>Cruising speed in grid pixels per tick: 180 grid pixels a second. A whole number: every step is the same size.</summary>
        public const float CruisePixelsPerTick = 3f;

        /// <summary>Ticks from rest to cruising speed when a walk starts: 24 (0.4 s), a gentle start.</summary>
        public const int StartTicks = 24;

        /// <summary>Ticks from cruising speed to rest when a walk ends: 9 (0.15 s), a short stop.</summary>
        public const int StopTicks = 9;

        /// <summary>Speed gained per tick while starting.</summary>
        public const float AccelerationPixelsPerTick = CruisePixelsPerTick / StartTicks;

        /// <summary>Speed lost per tick while stopping.</summary>
        public const float DecelerationPixelsPerTick = CruisePixelsPerTick / StopTicks;

        private const float TickSeconds = 1f / TicksPerSecond;

        [SerializeField]
        [Tooltip("Where the figure looks before it first moves or turns.")]
        private Facing startFacing = Facing.Right;

        private float? _target;          // grid pixels
        private Action _arrived;
        private float _pixelX;           // exact position in grid pixels; the transform shows it rounded to the grid
        private float _velocity;         // grid pixels per tick, >= 0, along _direction
        private float _direction = 1f;
        private float _accumulated;      // seconds not yet turned into ticks
        private int _drivenFrame = int.MinValue / 2;
        private float _driveDirection;
        private float _minX = float.NegativeInfinity;
        private float _maxX = float.PositiveInfinity;

        /// <summary>Cruising speed in world units per second.</summary>
        public float Speed => PixelGrid.Units(CruisePixelsPerTick * TicksPerSecond);

        public bool IsWalkingToTarget => _target.HasValue;

        /// <summary>Walking now: towards a target, or with speed left from free walking.</summary>
        public bool IsMoving => _target.HasValue || _velocity > 0f;

        public float X => transform.position.x;

        public Facing Facing => _direction < 0f ? Facing.Left : Facing.Right;

        /// <summary>Turns the figure where it stands. Turning while walking would break the walk, so it is an error.</summary>
        public void Face(Facing facing)
        {
            if (facing != Facing.Left && facing != Facing.Right)
            {
                throw new ArgumentOutOfRangeException(nameof(facing), facing, "Facing must be Left or Right.");
            }

            if (IsMoving)
            {
                throw new InvalidOperationException($"{name} cannot turn while walking.");
            }

            SetDirection((float)facing);
        }

        /// <summary>Turns to look at world X; a point straight above or below (closer than half a grid pixel) changes nothing.</summary>
        public void FaceTowards(float x)
        {
            var offset = x - X;
            if (Mathf.Abs(offset) >= PixelGrid.Units(0.5f))
            {
                Face(offset < 0f ? Facing.Left : Facing.Right);
            }
        }

        /// <summary>Walks to <paramref name="x"/>; <paramref name="arrived"/> runs once on arrival unless <see cref="Stop"/> is called.</summary>
        public void WalkTo(float x, Action arrived)
        {
            Sync();
            _target = x * PixelGrid.PixelsPerUnit;
            _arrived = arrived;
            var towards = _target.Value - _pixelX;
            if (Mathf.Abs(towards) < 0.5f)
            {
                Arrive();
                return;
            }

            if (Mathf.Sign(towards) != _direction)
            {
                _velocity = 0f; // turning round starts from rest
            }

            SetDirection(towards);
        }

        /// <summary>Stops a target walk without calling its callback. Free-walk momentum is kept.</summary>
        public void Stop()
        {
            _target = null;
            _arrived = null;
        }

        /// <summary>Stands still at once where the figure is (an interaction starts): no target, no momentum.</summary>
        public void Halt()
        {
            Stop();
            _velocity = 0f;
            _driveDirection = 0f;
            _accumulated = 0f;
            Show();
        }

        /// <summary>
        /// Free movement by player input for <paramref name="deltaTime"/> seconds, clamped to the floor
        /// (<paramref name="minX"/>..<paramref name="maxX"/>, world units). When input stops, the walker slows down.
        /// </summary>
        public void Step(float direction, float minX, float maxX, float deltaTime)
        {
            if (Mathf.Approximately(direction, 0f))
            {
                return;
            }

            Sync();
            _minX = minX * PixelGrid.PixelsPerUnit;
            _maxX = maxX * PixelGrid.PixelsPerUnit;
            if (Mathf.Sign(direction) != _direction)
            {
                _velocity = 0f;
            }

            SetDirection(direction);
            _drivenFrame = Time.frameCount;
            _driveDirection = Mathf.Sign(direction);
            Advance(deltaTime);
        }

        public void PlaceAt(float x)
        {
            Stop();
            _velocity = 0f;
            _accumulated = 0f;
            _pixelX = x * PixelGrid.PixelsPerUnit;
            Show();
        }

        private void Awake()
        {
            _pixelX = transform.position.x * PixelGrid.PixelsPerUnit;
            if (startFacing != Facing.Left && startFacing != Facing.Right)
            {
                throw new InvalidOperationException($"{name}: start facing is not set.");
            }

            SetDirection((float)startFacing);
        }

        private void Update()
        {
            // Input advances the walker itself; whether its Update runs before or after this one in a frame is not
            // defined, so free walking counts as held until a whole frame has passed without input.
            if (Time.frameCount - _drivenFrame <= 1)
            {
                return;
            }

            _driveDirection = 0f;
            if (_target.HasValue || _velocity > 0f)
            {
                Advance(Time.deltaTime);
            }
            else
            {
                _accumulated = 0f;
            }
        }

        private void Advance(float deltaTime)
        {
            // Whole ticks, rounded: at 60 frames a second every frame is exactly one tick despite timer jitter.
            _accumulated += deltaTime;
            var ticks = Mathf.RoundToInt(_accumulated / TickSeconds);
            _accumulated -= ticks * TickSeconds;
            for (var i = 0; i < ticks && (_target.HasValue || _velocity > 0f || _driveDirection != 0f); i++)
            {
                Tick();
            }

            Show();
        }

        private void Tick()
        {
            if (_target.HasValue)
            {
                var remaining = Mathf.Abs(_target.Value - _pixelX);
                // Speed that still lets the walk stop at the target with the stopping deceleration.
                var stoppable = Mathf.Sqrt(2f * DecelerationPixelsPerTick * remaining);
                _velocity = Mathf.Min(CruisePixelsPerTick, _velocity + AccelerationPixelsPerTick, Mathf.Max(stoppable, AccelerationPixelsPerTick));
                if (remaining <= _velocity)
                {
                    _pixelX = _target.Value;
                    _velocity = 0f;
                    Arrive();
                    return;
                }

                _pixelX += _direction * _velocity;
                if (_velocity == CruisePixelsPerTick)
                {
                    _pixelX = Mathf.Round(_pixelX); // even cruising steps, as in free walking below
                }

                return;
            }

            _velocity = _driveDirection != 0f
                ? Mathf.Min(CruisePixelsPerTick, _velocity + AccelerationPixelsPerTick)
                : Mathf.Max(0f, _velocity - DecelerationPixelsPerTick);
            _pixelX = Mathf.Clamp(_pixelX + (_direction * _velocity), _minX, _maxX);
            if (_velocity == 0f || _velocity == CruisePixelsPerTick)
            {
                // Speeding up and slowing down leave a fraction of a pixel (24 ticks of start cover 37.5 pixels). At cruise
                // a fraction would make whole steps show unevenly (2, 4, 2, 4 instead of 3, 3), and at rest the next walk
                // would start from it. The exact position becomes the one on screen: nothing visibly moves.
                _pixelX = Mathf.Round(_pixelX);
            }
            if (_pixelX <= _minX || _pixelX >= _maxX)
            {
                _velocity = 0f;
            }
        }

        private void Arrive()
        {
            // Arriving is standing: speed left from free walking must not keep the figure "moving" after the target.
            _pixelX = _target.Value;
            _velocity = 0f;
            Show();
            var callback = _arrived;
            _target = null;
            _arrived = null;
            callback?.Invoke();
        }

        /// <summary>Takes over a position set from outside (scene placement, editor) before moving.</summary>
        private void Sync()
        {
            var shown = Mathf.Round(_pixelX) / PixelGrid.PixelsPerUnit;
            if (!Mathf.Approximately(shown, transform.position.x))
            {
                _pixelX = transform.position.x * PixelGrid.PixelsPerUnit;
                _velocity = 0f;
            }
        }

        private void Show()
        {
            var position = transform.position;
            transform.position = new Vector3(Mathf.Round(_pixelX) / PixelGrid.PixelsPerUnit, position.y, position.z);
        }

        private void SetDirection(float direction)
        {
            if (Mathf.Abs(direction) < 0.0001f)
            {
                return;
            }

            _direction = Mathf.Sign(direction);
            // Flip the sprites only: mirroring the transform would also move child points (approach, bubble anchor).
            var flip = _direction < 0f; // all figure art is drawn looking right
            foreach (var sprite in GetComponentsInChildren<SpriteRenderer>())
            {
                sprite.flipX = flip;
            }
        }
    }
}
