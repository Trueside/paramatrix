using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>Named place where the hero appears when entering the scene. Exactly one per scene is the default (Р9).</summary>
    public sealed class EntryPoint : MonoBehaviour
    {
        [SerializeField]
        private string entryName = "default";

        [SerializeField]
        private bool isDefault;

        [SerializeField]
        [Tooltip("Where the hero looks on arrival: away from the door he came through, into the room. Must be set.")]
        private Facing facing;

        public string EntryName => entryName;

        public bool IsDefault => isDefault;

        public Facing Facing => facing;
    }
}
