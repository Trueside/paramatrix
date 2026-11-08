using System;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Walks a character along the floor line (T19, 2D side view): no obstacles, no path finding,
    /// so a walk to a reachable point always arrives.
    /// </summary>
    public sealed class Walker : MonoBehaviour
    {
        private const float ArrivalTolerance = 0.02f;

        [SerializeField]
        private float speed = 4f;

        private float? _target;
        private Action _arrived;

        public float Speed => speed;

        public bool IsWalkingToTarget => _target.HasValue;

        public float X => transform.position.x;

        /// <summary>Walks to <paramref name="x"/>; <paramref name="arrived"/> runs once on arrival unless <see cref="Stop"/> is called.</summary>
        public void WalkTo(float x, Action arrived)
        {
            _target = x;
            _arrived = arrived;
            Face(x - X);
            if (Mathf.Abs(x - X) <= ArrivalTolerance)
            {
                Arrive();
            }
        }

        /// <summary>Stops a target walk without calling its callback.</summary>
        public void Stop()
        {
            _target = null;
            _arrived = null;
        }

        /// <summary>Free movement by player input; clamps to the floor.</summary>
        public void Step(float direction, float minX, float maxX, float deltaTime)
        {
            if (Mathf.Approximately(direction, 0f))
            {
                return;
            }

            Face(direction);
            SetX(Mathf.Clamp(X + (Mathf.Sign(direction) * speed * deltaTime), minX, maxX));
        }

        public void PlaceAt(float x)
        {
            Stop();
            SetX(x);
        }

        private void Update()
        {
            if (!_target.HasValue)
            {
                return;
            }

            var next = Mathf.MoveTowards(X, _target.Value, speed * Time.deltaTime);
            SetX(next);
            if (Mathf.Abs(_target.Value - next) <= ArrivalTolerance)
            {
                Arrive();
            }
        }

        private void Arrive()
        {
            SetX(_target.Value);
            var callback = _arrived;
            _target = null;
            _arrived = null;
            callback?.Invoke();
        }

        private void SetX(float x)
        {
            var position = transform.position;
            transform.position = new Vector3(x, position.y, position.z);
        }

        private void Face(float direction)
        {
            if (Mathf.Abs(direction) < 0.0001f)
            {
                return;
            }

            // Flip the sprites only: mirroring the transform would also move child points (approach, bubble anchor).
            foreach (var sprite in GetComponentsInChildren<SpriteRenderer>())
            {
                sprite.flipX = direction < 0f;
            }
        }
    }
}
