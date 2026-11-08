using System;
using System.Collections.Generic;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Named animations a scene can play on this entity (<c>animation</c> action). Until real art exists a clip is a
    /// named duration; an unknown name is an error (Р0), never silently skipped.
    /// </summary>
    public sealed class EntityAnimations : MonoBehaviour
    {
        [Serializable]
        public struct Clip
        {
            public string name;

            [Min(0f)]
            public float duration;
        }

        [SerializeField]
        private List<Clip> clips = new List<Clip>();

        private float _remaining;
        private Action _done;

        public IReadOnlyList<Clip> Clips => clips;

        public bool IsPlaying => _done != null;

        public bool TryPlay(string clipName, Action done, out string error)
        {
            foreach (var clip in clips)
            {
                if (clip.name == clipName)
                {
                    if (_done != null)
                    {
                        error = "another animation is playing";
                        return false;
                    }

                    _remaining = clip.duration;
                    _done = done;
                    error = null;
                    return true;
                }
            }

            error = $"no animation '{clipName}' on '{name}'";
            return false;
        }

        /// <summary>Editor and tests: replaces the clip list.</summary>
        public void SetClips(IEnumerable<Clip> newClips) => clips = new List<Clip>(newClips);

        private void Update()
        {
            if (_done == null)
            {
                return;
            }

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                var done = _done;
                _done = null;
                done();
            }
        }
    }
}
